using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace DesktopPet.Core;

public sealed class DeepSeekChatClient(HttpClient http)
{
    public async Task<string> ReplyAsync(ChatSettings settings, IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new ChatServiceException("请先在 AI 设置中填写 DeepSeek API 密钥。");
        if (string.IsNullOrWhiteSpace(settings.Model))
            throw new ChatServiceException("请在 AI 设置中填写模型名称。");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.deepseek.com/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey.Trim());
        request.Content = JsonContent.Create(new
        {
            model = settings.Model.Trim(),
            messages = new[] { new ChatMessage("system", settings.Persona) }.Concat(messages),
            thinking = new { type = "disabled" },
            stream = false,
            max_tokens = 1024
        });
        try
        {
            using var response = await http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new ChatServiceException(response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => "DeepSeek API 密钥无效，请在 AI 设置中检查。",
                    HttpStatusCode.PaymentRequired => "DeepSeek 账户余额不足，请到开放平台检查余额。",
                    HttpStatusCode.Forbidden => "此 DeepSeek 账户无权使用该接口。",
                    HttpStatusCode.TooManyRequests => "请求过于频繁，请稍后重试。",
                    HttpStatusCode.BadRequest or HttpStatusCode.NotFound => "请求配置不被接受，请检查 AI 设置中的模型名称。",
                    HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable => "DeepSeek 服务繁忙，请稍后重试。",
                    _ => $"DeepSeek 请求失败（HTTP {(int)response.StatusCode}），请稍后重试。"
                });
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false));
            if (!json.RootElement.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array ||
                choices.GetArrayLength() == 0 || !choices[0].TryGetProperty("message", out var message) ||
                !message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(content.GetString()))
                throw new ChatServiceException("DeepSeek 没有返回可用的回答，请重试。");
            return content.GetString()!.Trim();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ChatServiceException("等待回复超时，请检查网络后重试。");
        }
        catch (HttpRequestException)
        {
            throw new ChatServiceException("无法连接 DeepSeek，请检查网络后重试。");
        }
        catch (JsonException)
        {
            throw new ChatServiceException("DeepSeek 返回的数据无法读取，请重试。");
        }
    }
}

using System.Net;
using System.Text;
using System.Text.Json;
using DesktopPet.Core;

namespace DesktopPet.Core.Tests;

public sealed class DeepSeekChatTests
{
    private static ChatSettings Settings => new() { ApiKey = "test-local-key", Persona = "温柔地用中文回答" };

    [Fact]
    public async Task SendsAuthenticatedConversationAndReturnsFinalAnswer()
    {
        using var http = new HttpClient(new ResponseHandler(async (request, token) =>
        {
            Assert.Equal("https://api.deepseek.com/chat/completions", request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal(Settings.ApiKey, request.Headers.Authorization.Parameter);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var body = json.RootElement;
            Assert.Equal("deepseek-flash", body.GetProperty("model").GetString());
            Assert.Equal("disabled", body.GetProperty("thinking").GetProperty("type").GetString());
            Assert.False(body.GetProperty("stream").GetBoolean());
            Assert.Equal("system", body.GetProperty("messages")[0].GetProperty("role").GetString());
            Assert.Equal(Settings.Persona, body.GetProperty("messages")[0].GetProperty("content").GetString());
            Assert.Equal("你好", body.GetProperty("messages")[1].GetProperty("content").GetString());
            return JsonResponse("""{"choices":[{"message":{"content":"你好呀！","reasoning_content":"不应显示的推理"}}]}""");
        }));
        var reply = await new DeepSeekChatClient(http).ReplyAsync(Settings, [new("user", "你好")], default);
        Assert.Equal("你好呀！", reply);
    }

    [Theory]
    [InlineData(401, "密钥")]
    [InlineData(402, "余额")]
    [InlineData(429, "频繁")]
    [InlineData(503, "繁忙")]
    public async Task GivesUsefulErrorsWithoutEchoingServerSecrets(int status, string expected)
    {
        using var http = new HttpClient(new ResponseHandler((_, _) => Task.FromResult(
            new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("secret-response") })));
        var error = await Assert.ThrowsAsync<ChatServiceException>(() =>
            new DeepSeekChatClient(http).ReplyAsync(Settings, [new("user", "你好")], default));
        Assert.Contains(expected, error.Message);
        Assert.DoesNotContain("secret-response", error.Message);
    }

    [Theory]
    [InlineData("{\"choices\":[]}")]
    [InlineData("{\"choices\":[{\"message\":{\"content\":null}}]}")]
    [InlineData("not-json")]
    public async Task RejectsMissingOrMalformedReplies(string json)
    {
        using var http = new HttpClient(new ResponseHandler((_, _) => Task.FromResult(JsonResponse(json))));
        await Assert.ThrowsAsync<ChatServiceException>(() =>
            new DeepSeekChatClient(http).ReplyAsync(Settings, [new("user", "你好")], default));
    }

    [Fact]
    public async Task MissingKeyDoesNotMakeANetworkRequest()
    {
        using var http = new HttpClient(new ResponseHandler((_, _) => throw new Exception("Must not send")));
        var error = await Assert.ThrowsAsync<ChatServiceException>(() =>
            new DeepSeekChatClient(http).ReplyAsync(Settings with { ApiKey = " " }, [new("user", "你好")], default));
        Assert.Contains("设置", error.Message);
    }

    [Fact]
    public async Task CancellationReachesHttpRequest()
    {
        using var cancellation = new CancellationTokenSource();
        using var http = new HttpClient(new ResponseHandler(async (_, token) =>
        {
            cancellation.Cancel();
            await Task.Delay(Timeout.Infinite, token);
            return JsonResponse("{}");
        }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new DeepSeekChatClient(http).ReplyAsync(Settings, [new("user", "你好")], cancellation.Token));
    }

    [Fact]
    public async Task NextReplyIncludesEarlierSuccessfulTurns()
    {
        var requests = new List<string>();
        using var http = new HttpClient(new ResponseHandler(async (request, token) =>
        {
            requests.Add(await request.Content!.ReadAsStringAsync(token));
            return JsonResponse("""{"choices":[{"message":{"content":"我记住了"}}]}""");
        }));
        var session = new ChatSession(new DeepSeekChatClient(http));
        await session.SendAsync("我叫小明", Settings, default);
        await session.SendAsync("我叫什么", Settings, default);
        using var body = JsonDocument.Parse(requests[1]);
        var messages = body.RootElement.GetProperty("messages");
        Assert.Equal(4, messages.GetArrayLength());
        Assert.Equal("我叫小明", messages[1].GetProperty("content").GetString());
        Assert.Equal("assistant", messages[2].GetProperty("role").GetString());
        Assert.Equal("我叫什么", messages[3].GetProperty("content").GetString());
    }

    [Fact]
    public async Task FailedTurnDoesNotPolluteNextRequest()
    {
        var count = 0;
        using var http = new HttpClient(new ResponseHandler(async (request, token) =>
        {
            if (++count == 1) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Assert.Equal(2, body.RootElement.GetProperty("messages").GetArrayLength());
            return JsonResponse("""{"choices":[{"message":{"content":"好了"}}]}""");
        }));
        var session = new ChatSession(new DeepSeekChatClient(http));
        await Assert.ThrowsAsync<ChatServiceException>(() => session.SendAsync("失败请求", Settings, default));
        Assert.Empty(session.Messages);
        await session.SendAsync("重试", Settings, default);
        Assert.Equal(2, session.Messages.Count);
    }

    [Fact]
    public async Task KeepsTwentyCompleteTurnsAndClearStartsFresh()
    {
        using var http = new HttpClient(new ResponseHandler((_, _) => Task.FromResult(
            JsonResponse("""{"choices":[{"message":{"content":"回复"}}]}"""))));
        var session = new ChatSession(new DeepSeekChatClient(http));
        for (var i = 0; i < 25; i++) await session.SendAsync($"问题{i}", Settings, default);
        Assert.Equal(40, session.Messages.Count);
        Assert.Equal("问题5", session.Messages[0].Content);
        Assert.Equal("assistant", session.Messages[^1].Role);
        session.Clear();
        Assert.Empty(session.Messages);
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class ResponseHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => respond(request, cancellationToken);
    }
}

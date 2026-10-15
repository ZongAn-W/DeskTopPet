using System.Net;
using System.Text;
using System.Text.Json;
using DesktopPet.Core;

namespace DesktopPet.Core.Tests;

public sealed class EmptyReplyRecoveryTests
{
    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    [InlineData(3, 4)]
    public async Task RecoversEmptyRepliesAndPreservesMoodOnPlainFallback(int emptyCount, int expectedRequests)
    {
        var count = 0;
        var logs = new List<string>();
        var now = DateTimeOffset.UtcNow;
        var mood = EmotionState.Calm(now);
        using var http = new HttpClient(new Handler(async request =>
        {
            count++;
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var jsonMode = body.RootElement.TryGetProperty("response_format", out _);
            Assert.Equal(count <= 2, jsonMode);
            var context = body.RootElement.GetProperty("messages").ToString();
            if (!jsonMode) Assert.DoesNotContain("current_emotion", context);
            var answer = count <= emptyCount ? "" : jsonMode ? "{\"reply\":\"你好\",\"emotion\":\"Happy\",\"intensity\":30}" : "你好";
            return Response(JsonSerializer.Serialize(new { id = "req-123", choices = new[] { new { message = new { content = answer, reasoning_content = "private" }, finish_reason = "stop" } } }));
        }));
        var result = await new DeepSeekChatClient(http, logs.Add).ReplyWithEmotionAsync(new() { ApiKey = "secret" }, [new("user", "private question")], mood, now, default);
        Assert.Equal("你好", result.Text);
        Assert.Equal(expectedRequests, count);
        if (emptyCount >= 2) Assert.Equal(mood, result.Emotion);
        Assert.Equal(emptyCount, logs.Count);
        Assert.All(logs, log => { Assert.DoesNotContain("private", log); Assert.DoesNotContain("secret", log); Assert.Contains("req-123", log); });
    }

    [Theory]
    [InlineData("length", "截断")]
    [InlineData("content_filter", "过滤")]
    [InlineData("stop", "空回复")]
    public async Task PersistentFailureIsBoundedAndDoesNotCommitHistory(string finish, string expected)
    {
        var count = 0;
        using var http = new HttpClient(new Handler(_ =>
        {
            count++;
            return Task.FromResult(Response(JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = "" }, finish_reason = finish } } })));
        }));
        var session = new ChatSession(new DeepSeekChatClient(http), EmotionState.Calm(DateTimeOffset.UtcNow));
        var error = await Assert.ThrowsAsync<ChatServiceException>(() => session.SendAsync("hello", new() { ApiKey = "secret" }, default));
        Assert.Contains(expected, error.Message);
        Assert.Equal(finish == "stop" ? 4 : 1, count);
        Assert.Empty(session.Messages);
    }

    [Fact]
    public async Task CancellationDuringEmptyReplyPreventsRetry()
    {
        using var cancellation = new CancellationTokenSource();
        var count = 0;
        using var http = new HttpClient(new Handler(_ =>
        {
            count++;
            cancellation.Cancel();
            return Task.FromResult(Response("{\"choices\":[]}"));
        }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new DeepSeekChatClient(http).ReplyAsync(new() { ApiKey = "secret" }, [new("user", "hi")], cancellation.Token));
        Assert.Equal(1, count);
    }

    private static HttpResponseMessage Response(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => response(request);
    }
}

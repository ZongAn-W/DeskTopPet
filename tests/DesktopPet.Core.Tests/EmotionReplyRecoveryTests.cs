using System.Net;
using System.Text;
using System.Text.Json;
using DesktopPet.Core;

namespace DesktopPet.Core.Tests;

public sealed class EmotionReplyRecoveryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 10, 0, 0, TimeSpan.Zero);
    private static readonly EmotionState Initial = new() { Kind = EmotionKind.Sad, Intensity = 40, UpdatedAt = Now };
    private static ChatSettings Settings => new() { ApiKey = "recovery-test-key", Persona = "Answer helpfully in Chinese." };
    private const string CodeAnswer = "取出第一列：\n```c\nint first[matrixSize];\nfor (int i = 0; i < matrixSize; i++) {\n    first[i] = matrix[i][0];\n}\nprintf(\"first: %d\\n\", first[0]);\n```";

    [Theory]
    [InlineData("```c\nint first[matrixSize];\nfor (int i = 0; i < matrixSize; i++) first[i] = matrix[i][0];\nprintf(\"first: %d\\n\", first[0]);\n```")]
    [InlineData("```cpp\nint reply = 0;\n// emotion and intensity are ordinary code names.\n```\n\n然后进行二分查找。")]
    [InlineData("```c\n{\"reply\":\"literal source text\",\"emotion\":\"Happy\",\"intensity\":30}\n```")]
    [InlineData("```\nfirst[i] = matrix[i][0];\n```")]
    [InlineData("先取出第一列。\n\n```c\nfirst[i] = matrix[i][0];\n```\n\n接着把它传入 `searchInsert`。")]
    [InlineData("{\"first\":[1,3,5],\"explanation\":\"A JSON answer\"}")]
    [InlineData("[{\"reply\":\"An ordinary array item\",\"emotion\":\"Happy\",\"intensity\":30}]")]
    [InlineData("```json\n{\"first\":[1,3,5]}\n```")]
    [InlineData("```json\n{\"first\":[1,3,5]}\n```\n接着用这个数组进行二分查找。")]
    [InlineData("```\n{\"first\":[1,3,5]}\n```\n接着用这个数组进行二分查找。")]
    [InlineData("```json\n{\"first\":[1,3,5]}\n```\n\n```json\n{\"target\":3}\n```")]
    [InlineData("```json\n{\"first\":[1,3,5]}\n```\n\n```c\nint col = searchInsert(first, matrixSize, target);\n```")]
    [InlineData("```json\n[1,3,5]\n```")]
    [InlineData("```\n{\"first\":[1,3,5]}\n```")]
    [InlineData("```\n[1,3,5]\n```")]
    public async Task OrdinaryCodeMarkdownAndJsonPreserveCompleteAnswerWithoutRetry(string answer)
    {
        var requests = 0;
        using var http = new HttpClient(new Handler((_, _) =>
        {
            requests++;
            return Task.FromResult(Ok(" \n" + answer + "\n "));
        }));
        var session = CreateSession(http);

        Assert.Equal(answer, await session.SendAsync("怎么取出第一列？", Settings, default));
        Assert.Equal(1, requests);
        Assert.Equal(Initial, session.Emotion);
        Assert.Equal(answer, session.Messages[1].Content);
    }

    [Theory]
    [InlineData("json")]
    [InlineData("")]
    public async Task FullyEnclosingJsonFencesUnwrapOnlyTheEmotionEnvelope(string language)
    {
        var requests = 0;
        var envelope = Envelope(CodeAnswer, "Happy", 30);
        using var http = new HttpClient(new Handler((_, _) =>
        {
            requests++;
            return Task.FromResult(Ok($"```{language}\n{envelope}\n```"));
        }));
        var session = CreateSession(http);

        Assert.Equal(CodeAnswer, await session.SendAsync("帮我写 C 代码", Settings, default));
        Assert.Equal(1, requests);
        Assert.Equal(EmotionKind.Happy, session.Emotion.Kind);
        Assert.Equal(30, session.Emotion.Intensity);
        Assert.Equal(CodeAnswer, session.Messages[1].Content);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\"emotion\":\"Unknown\",\"intensity\":30")]
    [InlineData("\"emotion\":17,\"intensity\":30")]
    [InlineData("\"emotion\":\"Happy\"")]
    [InlineData("\"intensity\":30")]
    [InlineData("\"emotion\":\"Happy\",\"intensity\":\"30\"")]
    [InlineData("\"emotion\":\"Happy\",\"intensity\":30.5")]
    [InlineData("\"emotion\":\"Happy\",\"intensity\":-1")]
    [InlineData("\"emotion\":\"Happy\",\"intensity\":101")]
    [InlineData("\"emotion\":\"Calm\",\"intensity\":30")]
    public async Task UsableReplySurvivesMissingOrInvalidMetadataWithoutRetry(string metadata)
    {
        var requests = 0;
        var content = "{\"reply\":" + JsonSerializer.Serialize(CodeAnswer) + (metadata.Length == 0 ? "" : "," + metadata) + "}";
        using var http = new HttpClient(new Handler((_, _) =>
        {
            requests++;
            return Task.FromResult(Ok(content));
        }));
        var session = CreateSession(http);

        Assert.Equal(CodeAnswer, await session.SendAsync("帮我写 C 代码", Settings, default));
        Assert.Equal(1, requests);
        Assert.Equal(Initial, session.Emotion);
        Assert.Equal(2, session.Messages.Count);
        Assert.Equal(CodeAnswer, session.Messages[1].Content);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("{\"reply\":\"unterminated")]
    [InlineData("{\"emotion\":\"Happy\",\"intensity\":30}")]
    [InlineData("{\"reply\":\"\",\"emotion\":\"Happy\",\"intensity\":30}")]
    [InlineData("{\"reply\":\" \\n\\t\",\"emotion\":\"Happy\",\"intensity\":30}")]
    [InlineData("{\"reply\":\" \n\t\",\"emotion\":\"Happy\",\"intensity\":30}")]
    [InlineData("{\"reply\":null,\"emotion\":\"Happy\",\"intensity\":30}")]
    [InlineData("{\"reply\":42,\"emotion\":\"Happy\",\"intensity\":30}")]
    [InlineData("```json\n{\"reply\":\"unterminated\n```")]
    [InlineData("```json\n{\"reply\":\"not a whole-response envelope\",\"emotion\":\"Happy\",\"intensity\":30}\n```\nHere is extra commentary.")]
    [InlineData("```\n{\"reply\":\"not a whole-response envelope\",\"emotion\":\"Happy\",\"intensity\":30}\n```\nHere is extra commentary.")]
    public async Task InvalidEnvelopeRetriesOnceWithOriginalContextAndCommitsOnlyRecoveredReply(string invalid)
    {
        var requests = new List<string>();
        using var http = new HttpClient(new Handler(async (request, token) =>
        {
            requests.Add(await request.Content!.ReadAsStringAsync(token));
            return Ok(requests.Count switch
            {
                1 => Envelope("前一轮成功回答", "Sad", 40),
                2 => invalid,
                _ => Envelope(CodeAnswer, "Happy", 30)
            });
        }));
        var session = CreateSession(http);
        await session.SendAsync("上一个问题", Settings, default, "用户正在学习 C 语言。");
        const string question = "int searchInsert(int* nums, int numsSize, int target) {\n    printf(\"target=%d\\n\", target);\n}\n怎么把二维矩阵第一列取出来？";

        Assert.Equal(CodeAnswer, await session.SendAsync(question, Settings, default, "用户正在学习 C 语言。"));
        Assert.Equal(3, requests.Count);
        using var original = JsonDocument.Parse(requests[1]);
        using var retry = JsonDocument.Parse(requests[2]);
        AssertJsonRequest(original.RootElement);
        AssertJsonRequest(retry.RootElement);
        var originalMessages = ReadMessages(original.RootElement);
        var retryMessages = ReadMessages(retry.RootElement);
        Assert.Equal(question, originalMessages[^1].Content);
        Assert.Equal(originalMessages.Length + 1, retryMessages.Length);
        var correctionIndex = Array.FindIndex(retryMessages, message => !originalMessages.Contains(message));
        Assert.True(correctionIndex >= 0);
        Assert.Equal("system", retryMessages[correctionIndex].Role);
        Assert.Contains("JSON", retryMessages[correctionIndex].Content, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(originalMessages, retryMessages.Where((_, index) => index != correctionIndex).ToArray());
        Assert.Equal(4, session.Messages.Count);
        Assert.Equal(question, session.Messages[2].Content);
        Assert.Equal(CodeAnswer, session.Messages[3].Content);
        Assert.Equal(EmotionKind.Happy, session.Emotion.Kind);
        Assert.Equal(30, session.Emotion.Intensity);
    }

    [Fact]
    public async Task TwoInvalidEnvelopesFailWithoutCommittingChatOrEmotion()
    {
        var requests = 0;
        using var http = new HttpClient(new Handler((_, _) =>
        {
            requests++;
            return Task.FromResult(Ok("{\"emotion\":\"Angry\",\"intensity\":40}"));
        }));
        var session = CreateSession(http);

        var error = await Assert.ThrowsAsync<ChatServiceException>(() => session.SendAsync("问题", Settings, default));
        Assert.Contains("回复格式不完整", error.Message);
        Assert.Equal(2, requests);
        Assert.Empty(session.Messages);
        Assert.Equal(Initial, session.Emotion);
    }

    [Fact]
    public async Task CancellationAfterFirstResponsePreventsRetryAndCommit()
    {
        var requests = 0;
        using var cancellation = new CancellationTokenSource();
        using var http = new HttpClient(new Handler((_, _) =>
        {
            requests++;
            cancellation.Cancel();
            return Task.FromResult(Ok("{broken"));
        }));
        var session = CreateSession(http);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.SendAsync("问题", Settings, cancellation.Token));
        Assert.Equal(1, requests);
        Assert.Empty(session.Messages);
        Assert.Equal(Initial, session.Emotion);
    }

    [Fact]
    public async Task CancellationDuringRetryReachesHttpAndPreservesChatAndMood()
    {
        var requests = 0;
        var retryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        using var http = new HttpClient(new Handler(async (_, token) =>
        {
            if (++requests == 1) return Ok("{broken");
            retryStarted.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            return Ok(Envelope("不应提交", "Angry", 40));
        }));
        var session = CreateSession(http);
        var pending = session.SendAsync("问题", Settings, cancellation.Token);
        await retryStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(2, requests);
        Assert.Empty(session.Messages);
        Assert.Equal(Initial, session.Emotion);
    }

    [Fact]
    public async Task ClearDuringRetryRejectsLateRecoveredAnswerAndEmotion()
    {
        var requests = 0;
        var retryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new Handler(async (_, _) =>
        {
            if (++requests == 1) return Ok("{broken");
            retryStarted.SetResult();
            return await release.Task;
        }));
        var session = CreateSession(http);
        var pending = session.SendAsync("旧的问题", Settings, default);
        await retryStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        session.Clear();
        release.SetResult(Ok(Envelope("不应提交", "Angry", 40)));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(2, requests);
        Assert.Empty(session.Messages);
        Assert.Equal(Initial, session.Emotion);
    }

    [Fact]
    public async Task LengthLimitedResponseKeepsDistinctErrorAndDoesNotRetry()
    {
        var requests = 0;
        using var http = new HttpClient(new Handler((_, _) =>
        {
            requests++;
            return Task.FromResult(Ok("{\"reply\":\"unfinished", "length"));
        }));
        var session = CreateSession(http);

        var error = await Assert.ThrowsAsync<ChatServiceException>(() => session.SendAsync("问题", Settings, default));
        Assert.Contains("返回的内容不完整", error.Message);
        Assert.Equal(1, requests);
        Assert.Empty(session.Messages);
        Assert.Equal(Initial, session.Emotion);
    }

    [Fact]
    public async Task NetworkFailureDoesNotTriggerFormatRecovery()
    {
        var requests = 0;
        using var http = new HttpClient(new Handler((_, _) =>
        {
            requests++;
            throw new HttpRequestException("simulated connection failure");
        }));
        var session = CreateSession(http);

        var error = await Assert.ThrowsAsync<ChatServiceException>(() => session.SendAsync("问题", Settings, default));
        Assert.Contains("无法连接", error.Message);
        Assert.Equal(1, requests);
        Assert.Empty(session.Messages);
        Assert.Equal(Initial, session.Emotion);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task HttpFailureDoesNotTriggerFormatRecovery(HttpStatusCode status)
    {
        var requests = 0;
        using var http = new HttpClient(new Handler((_, _) =>
        {
            requests++;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("server failure") });
        }));
        var session = CreateSession(http);

        await Assert.ThrowsAsync<ChatServiceException>(() => session.SendAsync("问题", Settings, default));
        Assert.Equal(1, requests);
        Assert.Empty(session.Messages);
        Assert.Equal(Initial, session.Emotion);
    }

    private static ChatSession CreateSession(HttpClient http) => new(new DeepSeekChatClient(http), Initial, new FixedClock(Now));

    private static string Envelope(string reply, string emotion, int intensity) =>
        JsonSerializer.Serialize(new { reply, emotion, intensity });

    private static void AssertJsonRequest(JsonElement body)
    {
        Assert.Equal("json_object", body.GetProperty("response_format").GetProperty("type").GetString());
        Assert.Equal(3072, body.GetProperty("max_tokens").GetInt32());
    }

    private static ChatMessage[] ReadMessages(JsonElement body) => body.GetProperty("messages").EnumerateArray()
        .Select(message => new ChatMessage(message.GetProperty("role").GetString()!, message.GetProperty("content").GetString()!)).ToArray();

    private static HttpResponseMessage Ok(string content, string finishReason = "stop") => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content }, finish_reason = finishReason } }
        }), Encoding.UTF8, "application/json")
    };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => respond(request, token);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

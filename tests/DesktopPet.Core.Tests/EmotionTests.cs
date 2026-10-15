using System.Net;
using System.Text;
using System.Text.Json;
using DesktopPet.Core;

namespace DesktopPet.Core.Tests;

public sealed class EmotionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 10, 0, 0, TimeSpan.Zero);
    private static ChatSettings Settings => new() { ApiKey = "emotion-test-key", Persona = "Test persona" };

    [Fact]
    public async Task OneRequestChangesReplyAndEmotionWithGradualAccumulation()
    {
        var requests = 0;
        using var http = new HttpClient(new Handler(async (request, token) =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Assert.Equal("json_object", body.RootElement.GetProperty("response_format").GetProperty("type").GetString());
            var messages = body.RootElement.GetProperty("messages");
            Assert.Equal(Settings.Persona, messages[0].GetProperty("content").GetString());
            var instructions = messages[1].GetProperty("content").GetString()!;
            Assert.Contains("character's own reaction", instructions);
            Assert.Contains("Quoted insults", instructions);
            Assert.Contains("reply_styles", instructions);
            Assert.Contains(requests == 0 ? "\"intensity\":0" : "\"intensity\":40", instructions);
            requests++;
            return Ok("""{"reply":"That makes me happy!","emotion":"Happy","intensity":95}""");
        }));
        var session = new ChatSession(new DeepSeekChatClient(http), EmotionState.Calm(Now), new FixedClock(Now));
        Assert.Equal("That makes me happy!", await session.SendAsync("Thank you", Settings, default));
        Assert.Equal(EmotionKind.Happy, session.Emotion.Kind);
        Assert.Equal(40, session.Emotion.Intensity);
        await session.SendAsync("You helped a lot", Settings, default);
        Assert.Equal(2, requests);
        Assert.Equal(60, session.Emotion.Intensity);
        Assert.Equal(4, session.Messages.Count);
        Assert.All(session.Messages.Where(message => message.Role == "assistant"), message => Assert.Equal("That makes me happy!", message.Content));
        session.Clear();
        Assert.Empty(session.Messages);
        Assert.Equal(EmotionKind.Happy, session.Emotion.Kind);
    }

    [Theory]
    [InlineData("Calm", 0, EmotionKind.Calm)]
    [InlineData("Happy", 30, EmotionKind.Happy)]
    [InlineData("Sad", 30, EmotionKind.Sad)]
    [InlineData("Angry", 30, EmotionKind.Angry)]
    [InlineData("Shy", 30, EmotionKind.Shy)]
    [InlineData("Concerned", 30, EmotionKind.Concerned)]
    public async Task AllSupportedEmotionsReturnOnlyTheNaturalReply(string kind, int intensity, EmotionKind expected)
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Ok(JsonSerializer.Serialize(new
        {
            reply = "Natural response", emotion = kind, intensity
        })))));
        var result = await new DeepSeekChatClient(http).ReplyWithEmotionAsync(Settings, [new("user", "message")],
            EmotionState.Calm(Now), Now, default);
        Assert.Equal("Natural response", result.Text);
        Assert.Equal(expected, result.Emotion.Kind);
    }

    [Theory]
    [InlineData("{\"reply\":\"answer\",\"emotion\":\"Unknown\",\"intensity\":30}")]
    [InlineData("{\"reply\":\"answer\",\"emotion\":\"Happy\",\"intensity\":\"30\"}")]
    [InlineData("{\"reply\":\"answer\",\"emotion\":\"Happy\",\"intensity\":-1}")]
    [InlineData("{\"reply\":\"answer\",\"emotion\":\"Happy\",\"intensity\":101}")]
    [InlineData("{\"reply\":\"answer\",\"emotion\":\"Calm\",\"intensity\":30}")]
    [InlineData("{\"reply\":\"answer\"}")]
    [InlineData("{\"reply\":\"answer\",\"emotion\":null,\"intensity\":30}")]
    [InlineData("{\"reply\":\"answer\",\"emotion\":\"Happy\",\"intensity\":30.5}")]
    [InlineData("{\"reply\":\"answer\",\"emotion\":\"Happy\"}")]
    public async Task InvalidMetadataStillCommitsTheReplyAndRetainsEmotion(string response)
    {
        var requests = 0;
        using var http = new HttpClient(new Handler((_, _) =>
        {
            requests++;
            return Task.FromResult(Ok(response));
        }));
        var initial = new EmotionState { Kind = EmotionKind.Sad, Intensity = 40, UpdatedAt = Now };
        var session = new ChatSession(new DeepSeekChatClient(http), initial, new FixedClock(Now));
        Assert.Equal("answer", await session.SendAsync("message", Settings, default));
        Assert.Equal(1, requests);
        Assert.Equal(2, session.Messages.Count);
        Assert.Equal("answer", session.Messages[^1].Content);
        Assert.Equal(initial, session.Emotion);
    }

    [Theory]
    [InlineData("{\"reply\":\"\",\"emotion\":\"Happy\",\"intensity\":30}")]
    [InlineData("{\"reply\":\" \",\"emotion\":\"Happy\",\"intensity\":30}")]
    [InlineData("{\"reply\":null,\"emotion\":\"Happy\",\"intensity\":30}")]
    [InlineData("{\"reply\":42,\"emotion\":\"Happy\",\"intensity\":30}")]
    [InlineData("{\"emotion\":\"Happy\",\"intensity\":30}")]
    [InlineData("{broken")]
    public async Task UnreadableReplyDoesNotCommitChatOrEmotionAfterOneRetry(string response)
    {
        var requests = 0;
        using var http = new HttpClient(new Handler((_, _) =>
        {
            requests++;
            return Task.FromResult(Ok(response));
        }));
        var initial = new EmotionState { Kind = EmotionKind.Sad, Intensity = 40, UpdatedAt = Now };
        var session = new ChatSession(new DeepSeekChatClient(http), initial, new FixedClock(Now));
        await Assert.ThrowsAsync<ChatServiceException>(() => session.SendAsync("message", Settings, default));
        Assert.Equal(2, requests);
        Assert.Empty(session.Messages);
        Assert.Equal(initial, session.Emotion);
    }

    [Fact]
    public async Task ClearDuringReplyPreventsLateEmotionChange()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new Handler(async (_, _) =>
        {
            started.TrySetResult();
            return await release.Task;
        }));
        var session = new ChatSession(new DeepSeekChatClient(http), EmotionState.Calm(Now), new FixedClock(Now));
        var pending = session.SendAsync("old message", Settings, default);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        session.Clear();
        release.SetResult(Ok("""{"reply":"Late answer","emotion":"Angry","intensity":40}"""));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(EmotionKind.Calm, session.Emotion.Kind);
        Assert.Empty(session.Messages);
    }

    [Fact]
    public async Task PlainTextCompatibilityRetainsPreviousEmotion()
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Ok("Plain answer"))));
        var initial = new EmotionState { Kind = EmotionKind.Shy, Intensity = 30, UpdatedAt = Now };
        var session = new ChatSession(new DeepSeekChatClient(http), initial, new FixedClock(Now));
        Assert.Equal("Plain answer", await session.SendAsync("hello", Settings, default));
        Assert.Equal(initial, session.Emotion);
    }

    [Fact]
    public void RecoveryPreservesFractionalTimeAndEventuallyReturnsToCalm()
    {
        var emotion = new EmotionState { Kind = EmotionKind.Angry, Intensity = 80, UpdatedAt = Now };
        var afterFiveMinutes = emotion.At(Now.AddMinutes(5));
        Assert.Equal(78, afterFiveMinutes.Intensity);
        Assert.Equal(77, afterFiveMinutes.At(Now.AddMinutes(6)).Intensity);
        Assert.Equal(emotion.At(Now.AddMinutes(6)), afterFiveMinutes.At(Now.AddMinutes(6)));
        Assert.Equal(EmotionKind.Calm, emotion.At(Now.AddHours(3)).Kind);
    }

    [Fact]
    public void SwitchingEmotionsStartsMildlyAndFurtherChangesAreBounded()
    {
        var happy = new EmotionState { Kind = EmotionKind.Happy, Intensity = 90, UpdatedAt = Now };
        var angry = happy.Transition(EmotionKind.Angry, 100, Now);
        Assert.Equal(40, angry.Intensity);
        Assert.Equal(60, angry.Transition(EmotionKind.Angry, 100, Now).Intensity);
        Assert.Equal(20, angry.Transition(EmotionKind.Angry, 15, Now).Intensity);
    }

    [Fact]
    public void StoreRestoresEmotionAndAppliesRecoveryAcrossRestart()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"pet-emotion-{Guid.NewGuid():N}.json");
        var store = new EmotionStore(path);
        try
        {
            store.Save(new EmotionState { Kind = EmotionKind.Happy, Intensity = 40, UpdatedAt = Now });
            var restored = store.Load(Now.AddMinutes(10));
            Assert.Equal(EmotionKind.Happy, restored.Kind);
            Assert.Equal(35, restored.Intensity);
            Assert.Equal(EmotionKind.Calm, store.Load(Now.AddHours(4)).Kind);
            File.WriteAllText(path, "broken");
            Assert.Equal(EmotionKind.Calm, store.Load(Now).Kind);
            Assert.True(store.LoadFailed);
        }
        finally { File.Delete(path); }
    }

    private static HttpResponseMessage Ok(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new { choices = new[] { new { message = new { content } } } }),
            Encoding.UTF8, "application/json")
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

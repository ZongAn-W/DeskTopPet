using System.Text.Json;
using DesktopPet.Core;

namespace DesktopPet.Core.Tests;

public sealed class ChatSettingsTests
{
    [Fact]
    public void DefaultsUseBubbleFiveMessagesAndClickOutside()
    {
        var settings = new ChatSettings();

        Assert.Equal(ChatPresentationMode.Bubble, settings.DefaultPresentation);
        Assert.Equal(5, settings.BubbleMessageCount);
        Assert.Equal(BubbleDismissMode.ClickOutside, settings.BubbleDismiss);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void SupportedMessageCountsRoundTrip(int count)
    {
        var settings = new ChatSettings { BubbleMessageCount = count };
        var loaded = JsonSerializer.Deserialize<ChatSettings>(JsonSerializer.Serialize(settings))!.Normalize();

        Assert.Equal(count, loaded.BubbleMessageCount);
    }

    [Theory]
    [InlineData(ChatPresentationMode.Bubble, BubbleDismissMode.ClickOutside)]
    [InlineData(ChatPresentationMode.FullWindow, BubbleDismissMode.ClickOutsideOrIdle)]
    [InlineData(ChatPresentationMode.FullWindow, BubbleDismissMode.AfterReply)]
    public void PresentationAndDismissalValuesRoundTrip(ChatPresentationMode presentation, BubbleDismissMode dismiss)
    {
        var settings = new ChatSettings { DefaultPresentation = presentation, BubbleDismiss = dismiss };
        var loaded = JsonSerializer.Deserialize<ChatSettings>(JsonSerializer.Serialize(settings))!.Normalize();

        Assert.Equal(presentation, loaded.DefaultPresentation);
        Assert.Equal(dismiss, loaded.BubbleDismiss);
    }

    [Fact]
    public void NormalizeClampsMessageCountAndMapsInvalidEnums()
    {
        var settings = new ChatSettings
        {
            BubbleMessageCount = 99,
            DefaultPresentation = (ChatPresentationMode)123,
            BubbleDismiss = (BubbleDismissMode)123
        };

        var normalized = settings.Normalize();

        Assert.Equal(5, normalized.BubbleMessageCount);
        Assert.Equal(ChatPresentationMode.Bubble, normalized.DefaultPresentation);
        Assert.Equal(BubbleDismissMode.ClickOutside, normalized.BubbleDismiss);
    }

    [Fact]
    public void OldSerializedSettingsUseNewDefaults()
    {
        var oldJson = "{\"ApiKey\":\"key\",\"Model\":\"model\",\"Persona\":\"persona\"}";

        var loaded = JsonSerializer.Deserialize<ChatSettings>(oldJson)!.Normalize();

        Assert.Equal("key", loaded.ApiKey);
        Assert.Equal("model", loaded.Model);
        Assert.Equal("persona", loaded.Persona);
        Assert.Equal(ChatPresentationMode.Bubble, loaded.DefaultPresentation);
        Assert.Equal(5, loaded.BubbleMessageCount);
        Assert.Equal(BubbleDismissMode.ClickOutside, loaded.BubbleDismiss);
    }
}

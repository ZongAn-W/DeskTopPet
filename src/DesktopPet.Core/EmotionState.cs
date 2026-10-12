using System.Text.Json.Serialization;

namespace DesktopPet.Core;

public enum EmotionKind { Calm, Happy, Sad, Angry, Shy, Concerned }

public sealed record EmotionState
{
    public EmotionKind Kind { get; init; } = EmotionKind.Calm;
    public int Intensity { get; init; }
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;

    [JsonIgnore]
    public string Label => Kind switch
    {
        EmotionKind.Happy => "开心",
        EmotionKind.Sad => "低落",
        EmotionKind.Angry => "生气",
        EmotionKind.Shy => "害羞",
        EmotionKind.Concerned => "担心",
        _ => "平静"
    };

    [JsonIgnore]
    public string Color => Kind switch
    {
        EmotionKind.Happy => "#B78A35",
        EmotionKind.Sad => "#7488AE",
        EmotionKind.Angry => "#B86166",
        EmotionKind.Shy => "#AD739A",
        EmotionKind.Concerned => "#947F53",
        _ => "#6F8978"
    };

    public static EmotionState Calm(DateTimeOffset now) => new() { UpdatedAt = now };

    public EmotionState At(DateTimeOffset now)
    {
        if (!Enum.IsDefined(Kind) || Intensity is < 0 or > 100 || Kind == EmotionKind.Calm || Intensity < 15)
            return Calm(now);
        if (UpdatedAt > now) return this with { UpdatedAt = now };
        var steps = Math.Floor((now - UpdatedAt).TotalMinutes / 2);
        if (steps < 1) return this;
        var intensity = Intensity - (int)Math.Min(100, steps);
        if (intensity < 15) return Calm(now);
        // Keep the fractional interval so repeated reads do not postpone recovery.
        return this with { Intensity = intensity, UpdatedAt = UpdatedAt.AddMinutes(steps * 2) };
    }

    public EmotionState Transition(EmotionKind kind, int intensity, DateTimeOffset now)
    {
        if (!Enum.IsDefined(kind) || kind == EmotionKind.Calm || intensity < 15) return Calm(now);
        var previous = At(now);
        var strength = kind == previous.Kind
            ? Math.Clamp(intensity, Math.Max(15, previous.Intensity - 20), Math.Min(100, previous.Intensity + 20))
            : Math.Clamp(intensity, 15, 40);
        return new() { Kind = kind, Intensity = strength, UpdatedAt = now };
    }

    public static string ReplyStyle(EmotionKind kind) => kind switch
    {
        EmotionKind.Happy => "Bright, warm and lightly playful; show enjoyment with lively phrasing, without excessive exclamation marks.",
        EmotionKind.Sad => "Quiet, subdued and gentle; use shorter, softer sentences and measured warmth, without guilt-tripping or demanding comfort.",
        EmotionKind.Angry => "Restrained, direct and firm; express mild displeasure and clear boundaries respectfully, never insult, threaten or punish the user.",
        EmotionKind.Shy => "Slightly bashful, hesitant and tender; occasional small pauses, without forced romance or exaggerated stammering.",
        EmotionKind.Concerned => "Attentive, careful and supportive; take the user's difficulty seriously and offer grounded help, without panic or lecturing.",
        _ => "Relaxed, natural and warmly conversational; answer clearly without heightened emotional language."
    };
}

public sealed record EmotionalReply(string Text, EmotionState Emotion);

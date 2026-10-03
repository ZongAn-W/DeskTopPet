namespace DesktopPet;

public sealed record ChatEntry(string Speaker, string Text, string Background)
{
    // Speaker remains the localized display label used by the existing template.
    // Role gives alternate views an explicit ownership signal.
    public string Role { get; init; } = "";
}

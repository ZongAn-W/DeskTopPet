using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DesktopPet.Core;

/// <summary>One note as it was considered for the memory context injected into a reply.</summary>
public sealed record MemoryDigestEntry(string Section, string Text, string Reason, double Score);

/// <summary>What the last reply actually received, and why the other notes stayed out.</summary>
public sealed record MemoryDigestReport(
    DateTimeOffset GeneratedAt,
    string Query,
    int TotalNotes,
    int IncludedNotes,
    int Characters,
    IReadOnlyList<MemoryDigestEntry> Included,
    IReadOnlyList<MemoryDigestEntry> Skipped);

public sealed record MemoryDigestResult(string Text, MemoryDigestReport Report);

/// <summary>
/// Turns the editable Markdown document into the bounded context for one reply.
/// Lasting facts stay in every reply; one-off events earn their place by matching the current
/// message, by being recent, or by fitting the remaining character budget.
/// </summary>
public static class MemoryDigest
{
    public const int MaxCharacters = 1400;
    public const int StableCharacters = 600;
    public const string StableTopic = "关于她";
    public const string EventTopic = "一起经历的事";
    private const double WeakScore = 0.18;

    /// <summary>Sections treated as lasting facts: always remembered, never ranked away.</summary>
    public static IReadOnlySet<string> StableSections { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "关于她", "关于你", "身份与背景", "偏好", "喜好", "习惯", "约定", "计划与约定", "忌讳", "重要",
        "identity", "background", "preferences", "agreements", "plans"
    };

    private static readonly Regex DatePrefix = new(
        @"^(?:(\d{4})-(\d{1,2})-(\d{1,2})|(\d{4})年(\d{1,2})月(\d{1,2})日)", RegexOptions.CultureInvariant);
    private static readonly Regex CjkRun = new(@"[\u4e00-\u9fff]+", RegexOptions.CultureInvariant);
    private static readonly Regex WordRun = new(@"[A-Za-z0-9_]{2,}", RegexOptions.CultureInvariant);
    private static readonly char[] SeparatorChars = [' ', '\u3000', '：', ':', '，', ',', '-', '—', '·'];

    public static MemoryDigestResult Build(string document, string query, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(document);
        var notes = new List<Note>();
        if (!string.IsNullOrWhiteSpace(document))
        {
            foreach (var note in MemoryDocument.Parse(document).Notes)
            {
                var collapsed = Collapse(note.Text);
                if (collapsed.Length == 0) continue;
                notes.Add(Read(note.Section, collapsed));
            }
        }

        var nowDate = DateOnly.FromDateTime(now.LocalDateTime);
        var trimmedQuery = (query ?? "").Trim();
        var included = new List<Note>();
        var skipped = new List<Note>();

        if (notes.Count > 0)
        {
            var stable = notes.Where(note => note.Stable).ToList();
            var episodes = notes.Where(note => !note.Stable).ToList();
            var estimate = notes.Sum(note => Render(note, nowDate).Length + SectionName(note).Length + 4);
            if (estimate <= MaxCharacters)
            {
                foreach (var note in notes) { note.Reason = "all"; included.Add(note); }
            }
            else
            {
                SelectStable(stable, included, skipped, nowDate);
                Rank(episodes, trimmedQuery, nowDate);
                SelectEpisodes(episodes, included, skipped, nowDate);
            }
        }

        var text = Render(included, nowDate);
        var report = new MemoryDigestReport(
            GeneratedAt: now,
            Query: PreviewQuery(trimmedQuery),
            TotalNotes: notes.Count,
            IncludedNotes: included.Count,
            Characters: text.Length,
            Included: included.Select(ToEntry).ToList(),
            Skipped: skipped.Select(ToEntry).ToList());
        return new(text, report);
    }

    private static void SelectStable(List<Note> stable, List<Note> included, List<Note> skipped, DateOnly now)
    {
        var characters = 0;
        foreach (var note in stable)
        {
            var length = Render(note, now).Length + 1;
            if (characters + length > StableCharacters && included.Count > 0)
            {
                note.Reason = "stable-budget";
                skipped.Add(note);
                continue;
            }
            note.Reason = "stable";
            included.Add(note);
            characters += length;
        }
    }

    private static void SelectEpisodes(List<Note> episodes, List<Note> included, List<Note> skipped, DateOnly now)
    {
        var characters = included.Sum(note => Render(note, now).Length + 1);
        foreach (var note in episodes.OrderByDescending(note => note.Score).ThenBy(note => note.Section, StringComparer.Ordinal))
        {
            var length = Render(note, now).Length + 1;
            if (characters + length > MaxCharacters)
            {
                note.Reason = "budget";
                skipped.Add(note);
            }
            else if (note.Score < WeakScore)
            {
                note.Reason = "weak";
                skipped.Add(note);
            }
            else
            {
                note.Reason = note.Score >= WeakScore + 0.12 ? "relevant" : "recent";
                included.Add(note);
                characters += length;
            }
        }
    }

    private static void Rank(List<Note> episodes, string query, DateOnly now)
    {
        if (episodes.Count == 0) return;
        var queryTokens = Tokenize(query);
        var documentFrequency = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var note in episodes)
        {
            note.Tokens = Tokenize(note.Body);
            foreach (var token in note.Tokens) documentFrequency[token] = documentFrequency.GetValueOrDefault(token) + 1;
        }
        var phrase = query.Length >= 2 ? query : null;
        foreach (var note in episodes)
        {
            var evidence = 0.0;
            foreach (var token in queryTokens)
            {
                if (!note.Tokens.Contains(token)) continue;
                var weight = token.Length >= 3 ? 1.3 : 1.0;
                evidence += weight / (1 + documentFrequency.GetValueOrDefault(token));
            }
            if (phrase is not null && note.Body.Contains(phrase, StringComparison.Ordinal)) evidence += 2;
            var matching = evidence <= 0 ? 0 : evidence / (evidence + 0.8);
            var days = note.When is null ? 30 : Math.Max(0, now.DayNumber - note.When.Value.DayNumber);
            var recency = Math.Pow(2, -days / 30.0);
            note.Score = Math.Round(0.75 * matching + 0.25 * recency, 4);
        }
    }

    private static string Render(List<Note> included, DateOnly now)
    {
        var builder = new StringBuilder();
        foreach (var section in included.Select(SectionName).Distinct(StringComparer.Ordinal))
        {
            if (builder.Length > 0) builder.Append('\n');
            builder.Append("## ").Append(section).Append('\n');
            foreach (var note in included.Where(note => SectionName(note) == section))
                builder.Append(Render(note, now)).Append('\n');
        }
        return builder.ToString().TrimEnd();
    }

    private static string Render(Note note, DateOnly now)
    {
        var age = AgeLabel(note.When, now);
        return age is null ? "- " + note.Body : "- " + age + "：" + note.Body;
    }

    private static MemoryDigestEntry ToEntry(Note note) => new(note.Section, note.Body, note.Reason, note.Score);

    private static string SectionName(Note note) => note.Section.Length == 0 ? "未分类" : note.Section;

    private static Note Read(string section, string text)
    {
        DateOnly? when = null;
        var body = text;
        var match = DatePrefix.Match(text);
        if (match.Success && TryParseDate(match, out var date))
        {
            when = date;
            body = text[match.Length..].TrimStart(SeparatorChars).Trim();
        }
        return new Note(section, body, when, StableSections.Contains(section));
    }

    private static bool TryParseDate(Match match, out DateOnly date)
    {
        date = default;
        for (var group = 1; group <= 4; group += 3)
        {
            if (!match.Groups[group].Success) continue;
            return DateOnly.TryParseExact(
                $"{match.Groups[group].Value}-{int.Parse(match.Groups[group + 1].Value, CultureInfo.InvariantCulture):00}-{int.Parse(match.Groups[group + 2].Value, CultureInfo.InvariantCulture):00}",
                "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        }
        return false;
    }

    private static string? AgeLabel(DateOnly? when, DateOnly now)
    {
        if (when is null) return null;
        var days = now.DayNumber - when.Value.DayNumber;
        if (days < 0) return when.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (days == 0) return "今天";
        if (days == 1) return "昨天";
        if (when.Value.Year == now.Year)
            return days < 30 ? days + " 天前" : Math.Max(1, days / 30) + " 个月前";
        return when.Value.Year == now.Year - 1
            ? "去年 " + when.Value.ToString("MM-dd", CultureInfo.InvariantCulture)
            : when.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static HashSet<string> Tokenize(string text)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(text)) return tokens;
        foreach (Match run in CjkRun.Matches(text))
        {
            var value = run.Value;
            if (value.Length == 1) { tokens.Add(value); continue; }
            for (var index = 0; index < value.Length - 1; index++) tokens.Add(value.Substring(index, 2));
            if (value.Length <= 4) tokens.Add(value);
        }
        foreach (Match word in WordRun.Matches(text)) tokens.Add(word.Value.ToLowerInvariant());
        return tokens;
    }

    private static string Collapse(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            var value = line.Trim();
            if (value.Length == 0) continue;
            if (builder.Length > 0) builder.Append(' ');
            builder.Append(value);
        }
        return builder.ToString();
    }

    private static string PreviewQuery(string query) =>
        query.Length <= 40 ? query : query[..40] + "…";

    private sealed class Note(string section, string body, DateOnly? when, bool stable)
    {
        public string Section { get; } = section;
        public string Body { get; } = body;
        public DateOnly? When { get; } = when;
        public bool Stable { get; } = stable;
        public HashSet<string> Tokens { get; set; } = [];
        public double Score { get; set; }
        public string Reason { get; set; } = "";
    }
}

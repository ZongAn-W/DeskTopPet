using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace DesktopPet.Core;

public sealed record MemoryNote(int Id, string Section, string Text);

/// <summary>An editable view of Markdown notes which keeps the original source outside an edit.</summary>
public sealed class MemoryDocument
{
    private const string Header = "# Desktop Pet Memory";
    private static readonly Regex HeadingPattern = new(@"^ {0,3}(#{2,6})\s+(.+?)\s*#*\s*$");
    private static readonly Regex BulletPattern = new(@"^( {0,3}(?:[-+*]|\d+[.)])\s+)(.*)$");
    private readonly ReadOnlyCollection<NoteSpan> _spans;
    private readonly ReadOnlyCollection<SectionSpan> _sections;

    private MemoryDocument(string source, List<NoteSpan> spans, List<SectionSpan> sections)
    {
        Source = source;
        _spans = spans.AsReadOnly();
        _sections = sections.AsReadOnly();
        Notes = spans.Select(span => span.Note).ToList().AsReadOnly();
    }

    public string Source { get; }
    public IReadOnlyList<MemoryNote> Notes { get; }

    public static MemoryDocument Parse(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var lines = SplitLines(source);
        if (lines.Count == 0 || lines[0].Text.TrimEnd() != Header)
            throw new InvalidDataException("记忆文档必须以 # Desktop Pet Memory 开头。");

        var spans = new List<NoteSpan>();
        var sections = new List<SectionSpan>();
        var section = "";
        var sectionStart = lines[0].End;
        var fenced = false;
        var fence = '\0';
        for (var index = 1; index < lines.Count;)
        {
            var line = lines[index];
            var trimmed = line.Text.TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                if (!fenced) { fenced = true; fence = trimmed[0]; }
                else if (trimmed[0] == fence) fenced = false;
                index++;
                continue;
            }
            if (fenced || IsSeparator(line.Text)) { index++; continue; }
            var heading = HeadingPattern.Match(line.Text);
            if (heading.Success)
            {
                sections.Add(new(section, sectionStart, line.Start));
                section = heading.Groups[2].Value.Trim();
                sectionStart = line.End;
                index++;
                continue;
            }

            var bullet = BulletPattern.Match(line.Text);
            var prefix = bullet.Success ? bullet.Groups[1].Value : "";
            var text = bullet.Success ? bullet.Groups[2].Value : line.Text;
            var continuationIndent = prefix.Length;
            var endIndex = index;
            var parts = new List<string> { text };
            for (var next = index + 1; next < lines.Count; next++)
            {
                var candidate = lines[next].Text;
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    // A blank line belongs to a list item only when indented prose follows it.
                    if (bullet.Success && next + 1 < lines.Count && IsIndentedContinuation(lines[next + 1].Text, prefix.Length))
                    {
                        parts.Add("");
                        endIndex = next;
                        continue;
                    }
                    break;
                }
                if (HeadingPattern.IsMatch(candidate) || candidate.TrimStart().StartsWith("```", StringComparison.Ordinal) ||
                    candidate.TrimStart().StartsWith("~~~", StringComparison.Ordinal) || IsSeparator(candidate)) break;
                if (bullet.Success)
                {
                    if (!IsIndentedContinuation(candidate, prefix.Length) && BulletPattern.IsMatch(candidate)) break;
                    var indent = candidate.Length - candidate.TrimStart().Length;
                    if (parts.Count == 1) continuationIndent = indent;
                    parts.Add(candidate[Math.Min(continuationIndent, indent)..]);
                }
                else
                {
                    if (BulletPattern.IsMatch(candidate)) break;
                    parts.Add(candidate);
                }
                endIndex = next;
            }
            var note = new MemoryNote(spans.Count, section, string.Join("\n", parts));
            spans.Add(new(note, line.Start, lines[endIndex].ContentEnd, lines[endIndex].End, prefix, continuationIndent));
            index = endIndex + 1;
        }
        sections.Add(new(section, sectionStart, source.Length));
        return new(source, spans, sections);
    }

    public MemoryDocument ReplaceNote(int id, string text)
    {
        var span = FindNote(id);
        var parts = ValidateText(text);
        var newline = Newline;
        var replacement = span.Prefix + string.Join(newline, parts.Select((part, index) =>
            index == 0 || span.Prefix.Length == 0 || part.Length == 0 ? part : new string(' ', span.ContinuationIndent) + part));
        return Parse(Source[..span.Start] + replacement + Source[span.ContentEnd..]);
    }

    public MemoryDocument RemoveNote(int id)
    {
        var span = FindNote(id);
        return Parse(Source[..span.Start] + Source[span.End..]);
    }

    public MemoryDocument AddNote(string section, string text)
    {
        ArgumentNullException.ThrowIfNull(section);
        if (section.Contains('\r') || section.Contains('\n'))
            throw new ArgumentException("分类名称只能使用一行文字。", nameof(section));
        var parts = ValidateText(text);
        var newline = Newline;
        var note = "- " + string.Join(newline, parts.Select((part, index) => index == 0 || part.Length == 0 ? part : "  " + part)) + newline;
        var target = _sections.LastOrDefault(item => item.Section == section.Trim());
        if (target is null)
        {
            var prefix = EndsWithBlankLine(Source) ? "" : Source.EndsWith('\n') ? newline : newline + newline;
            return Parse(Source + prefix + "## " + section.Trim() + newline + newline + note);
        }
        var position = target.End;
        // Insert before blank separator lines, never before whitespace belonging to another note.
        var sectionLines = SplitLines(Source[target.Start..target.End]);
        var lastContentLine = sectionLines.LastOrDefault(line => !string.IsNullOrWhiteSpace(line.Text));
        position = lastContentLine is null ? target.Start : target.Start + lastContentLine.End;
        var before = Source[..position];
        var lead = before.EndsWith('\n') ? "" : newline;
        if (position == target.Start) lead += newline;
        return Parse(before + lead + note + Source[position..]);
    }

    private string Newline => Source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
    private NoteSpan FindNote(int id) => _spans.FirstOrDefault(span => span.Note.Id == id)
        ?? throw new ArgumentOutOfRangeException(nameof(id), "找不到这条记忆，请重新加载文档。");
    private static string[] ValidateText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("记忆内容不能为空。", nameof(text));
        var parts = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim().Split('\n');
        if (parts.Any(line => Regex.IsMatch(line, @"^ {0,3}#{1,6}\s") ||
            line.TrimStart().StartsWith("```", StringComparison.Ordinal) ||
            line.TrimStart().StartsWith("~~~", StringComparison.Ordinal) ||
            !string.IsNullOrWhiteSpace(line) && IsSeparator(line)))
            throw new ArgumentException("请使用普通文字填写记忆，不要在内容中加入 Markdown 标题、代码围栏、分隔线或注释。", nameof(text));
        return parts;
    }
    private static bool IsIndentedContinuation(string text, int minimumIndent) =>
        !string.IsNullOrWhiteSpace(text) && text.TakeWhile(char.IsWhiteSpace).Count() >= minimumIndent;
    private static bool IsSeparator(string text) => string.IsNullOrWhiteSpace(text) ||
        Regex.IsMatch(text.Trim(), @"^(?:---+|\*\*\*+|___+)$") || text.TrimStart().StartsWith("<!--", StringComparison.Ordinal);
    private static bool EndsWithBlankLine(string text) => text.EndsWith("\n\n", StringComparison.Ordinal) || text.EndsWith("\r\n\r\n", StringComparison.Ordinal);

    private static List<Line> SplitLines(string source)
    {
        var lines = new List<Line>();
        for (var start = 0; start < source.Length;)
        {
            var newline = source.IndexOf('\n', start);
            var end = newline < 0 ? source.Length : newline + 1;
            var contentEnd = newline < 0 ? end : newline;
            if (contentEnd > start && source[contentEnd - 1] == '\r') contentEnd--;
            lines.Add(new(start, contentEnd, end, source[start..contentEnd]));
            start = end;
        }
        return lines;
    }

    private sealed record Line(int Start, int ContentEnd, int End, string Text);
    private sealed record NoteSpan(MemoryNote Note, int Start, int ContentEnd, int End, string Prefix, int ContinuationIndent);
    private sealed record SectionSpan(string Section, int Start, int End);
}

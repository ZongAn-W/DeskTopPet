using DesktopPet.Core;

namespace DesktopPet.Core.Tests;

public sealed class MemoryDigestTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 4, 12, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void SmallDocumentIsInjectedWholeWithRenderedDates()
    {
        const string document = "# Desktop Pet Memory\n\n## 关于她\n\n- 她喜欢茶。\n\n## 一起经历的事\n\n- 2026-08-01：她去看了海。\n";
        var result = MemoryDigest.Build(document, "今天想喝什么？", Now);

        Assert.Contains("## 关于她", result.Text);
        Assert.Contains("- 她喜欢茶。", result.Text);
        Assert.Contains("- 3 天前：她去看了海。", result.Text);
        Assert.DoesNotContain("2026-08-01", result.Text);
        Assert.Equal(2, result.Report.TotalNotes);
        Assert.Equal(2, result.Report.IncludedNotes);
        Assert.Empty(result.Report.Skipped);
        Assert.All(result.Report.Included, entry => Assert.Equal("all", entry.Reason));
    }

    [Fact]
    public void EmptyDocumentProducesNoContext()
    {
        var result = MemoryDigest.Build("", "你好", Now);

        Assert.Equal("", result.Text);
        Assert.Equal(0, result.Report.TotalNotes);
        Assert.Empty(result.Report.Included);
        Assert.Empty(result.Report.Skipped);
    }

    [Fact]
    public void OverBudgetKeepsMatchingEventAndSkipsUnrelatedOldOnes()
    {
        var document = BuildBigDocument();
        var result = MemoryDigest.Build(document, "上次拍的照片还在吗？", Now);

        Assert.Contains("她去海边拍了照片", result.Text);
        var matching = Assert.Single(result.Report.Included, entry => entry.Text.Contains("她去海边拍了照片"));
        Assert.Equal("relevant", matching.Reason);
        Assert.Contains(result.Report.Included, entry => entry.Section == "关于她" && entry.Reason == "stable");
        Assert.Contains(result.Report.Skipped, entry => entry.Reason == "weak");
        Assert.True(result.Report.Characters <= MemoryDigest.MaxCharacters);
    }

    [Fact]
    public void ReadsChineseDatePrefixes()
    {
        const string document = "# Desktop Pet Memory\n\n## 一起经历的事\n\n- 2025年9月17日：她第一次坐飞机。\n";
        var result = MemoryDigest.Build(document, "坐飞机", Now);

        Assert.Contains("- 去年 09-17：她第一次坐飞机。", result.Text);
    }

    [Fact]
    public void UntimedNotesAreKeptWithoutPrefix()
    {
        const string document = "# Desktop Pet Memory\n\n## 一起经历的事\n\n- 她说过想学游泳。\n";
        var result = MemoryDigest.Build(document, "游泳", Now);

        Assert.Contains("- 她说过想学游泳。", result.Text);
        Assert.DoesNotContain("天前", result.Text);
    }

    private static string BuildBigDocument()
    {
        var lines = new List<string>
        {
            "# Desktop Pet Memory",
            "",
            "## 关于她",
            "",
            "- 她喜欢茶。",
            "",
            "## 一起经历的事",
            "",
            "- 2026-08-01：她去海边拍了照片。"
        };
        for (var index = 0; index < 30; index++)
        {
            lines.Add("");
            lines.Add($"- 2025-01-02：很久以前的琐事第 {index} 条，和今天这句话没有关系，只是把文档撑得比预算更长。");
        }
        lines.Add("");
        return string.Join("\n", lines);
    }
}

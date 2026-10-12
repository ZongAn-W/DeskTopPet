using DesktopPet.Core;

namespace DesktopPet.Core.Tests;

public sealed class MemoryDocumentTests
{
    [Fact]
    public void ParsesBulletContinuationsAndParagraphsWithoutChangingSource()
    {
        const string source = "# Desktop Pet Memory\r\n\r\n## 偏好\r\n\r\n* 喜欢茶。\r\n    不加糖。\r\n\r\n    周末会泡一杯。\r\n\r\n一段独立的记忆，\r\n保留换行。\r\n\r\n## 计划\r\n- 周五整理照片。\r\n";
        var document = MemoryDocument.Parse(source);
        Assert.Equal(source, document.Source);
        Assert.Equal(3, document.Notes.Count);
        Assert.Equal(new MemoryNote(0, "偏好", "喜欢茶。\n不加糖。\n\n周末会泡一杯。"), document.Notes[0]);
        Assert.Equal("一段独立的记忆，\n保留换行。", document.Notes[1].Text);
        Assert.Equal("计划", document.Notes[2].Section);
    }

    [Fact]
    public void ReplacesJustOneNoteAndKeepsBulletAndNewlineStyle()
    {
        const string source = "# Desktop Pet Memory\r\n\r\n## 偏好\r\n\r\n* 茶。\r\n    不加糖。\r\n\r\n- 保留原文  。\r\n\r\n## 计划\r\n准备去旅行。";
        var original = MemoryDocument.Parse(source);
        var edited = original.ReplaceNote(0, "咖啡。\n少糖。");
        Assert.Equal(source.Replace("* 茶。\r\n    不加糖。", "* 咖啡。\r\n    少糖。", StringComparison.Ordinal), edited.Source);
        Assert.Equal(source, original.Source);
    }

    [Fact]
    public void DeletesWholeMultilineNoteAndPreservesOtherSectionsAndFormatting()
    {
        const string source = "# Desktop Pet Memory\n\n## 偏好\n- 茶。\n  不加糖。\n\n## 计划\n\n去旅行。\n\n第二段保持原样。\n";
        var edited = MemoryDocument.Parse(source).RemoveNote(0);
        Assert.Equal(source.Replace("- 茶。\n  不加糖。\n", "", StringComparison.Ordinal), edited.Source);
        Assert.Equal(2, edited.Notes.Count);
        Assert.Equal("去旅行。", edited.Notes[0].Text);
    }

    [Fact]
    public void AddsToRequestedExistingSectionWithoutInventingCategoryOrChangingOtherSource()
    {
        const string source = "# Desktop Pet Memory\n\n## 偏好\n- 茶。\n\n## 计划\n- 去旅行。\n";
        var edited = MemoryDocument.Parse(source).AddNote("偏好", "喜欢散步。\n在晚上。");
        Assert.Equal(3, edited.Notes.Count);
        Assert.Equal("偏好", edited.Notes[1].Section);
        Assert.Equal("喜欢散步。\n在晚上。", edited.Notes[1].Text);
        Assert.EndsWith("## 计划\n- 去旅行。\n", edited.Source);
        Assert.Contains("- 茶。", edited.Source);
        Assert.DoesNotContain("日期", edited.Source);
    }

    [Fact]
    public void AddsUnnamedNoteToHeaderOnlyDocumentAndExplicitlyNamedNewSection()
    {
        var unnamed = MemoryDocument.Parse(ChatMemoryStore.EmptyDocument).AddNote("", "今天开始学习。");
        Assert.Equal("", Assert.Single(unnamed.Notes).Section);
        Assert.Equal("# Desktop Pet Memory\n\n- 今天开始学习。\n", unnamed.Source);
        var named = unnamed.AddNote("旅行", "想去海边。");
        Assert.Equal("旅行", named.Notes[1].Section);
        Assert.Contains("## 旅行\n\n- 想去海边。", named.Source);
    }

    [Fact]
    public void PreservesCodeBlocksAndCommentOutsideEditedNote()
    {
        const string source = "# Desktop Pet Memory\n\n<!-- keep metadata -->\n\n```text\n- not a memory\n```\n\n- 正式记忆。\n";
        var document = MemoryDocument.Parse(source);
        Assert.Single(document.Notes);
        Assert.Equal(source.Replace("- 正式记忆。", "- 修改的记忆。", StringComparison.Ordinal), document.ReplaceNote(0, "修改的记忆。").Source);
    }

    [Fact]
    public void SupportsMarkdownLazyContinuation()
    {
        var document = MemoryDocument.Parse("# Desktop Pet Memory\n\n- Long note\ncontinues here.\n- Next note\n");
        Assert.Equal(2, document.Notes.Count);
        Assert.Equal("Long note\ncontinues here.", document.Notes[0].Text);
    }

    [Fact]
    public void AddingNoteRetainsAnotherNotesTrailingWhitespaceAndSectionSeparators()
    {
        const string source = "# Desktop Pet Memory\r\n\r\n## 偏好\r\n* 茶。  \r\n\r\n\r\n## 计划\r\n休息。\r\n";
        var edited = MemoryDocument.Parse(source).AddNote("偏好", "咖啡。");
        Assert.Equal(source.Replace("* 茶。  \r\n", "* 茶。  \r\n- 咖啡。\r\n", StringComparison.Ordinal), edited.Source);
    }

    [Theory]
    [InlineData("")]
    [InlineData("# Something Else\n")]
    [InlineData("# Desktop Pet Memory Unexpected\n")]
    public void RejectsMissingOrChangedRequiredHeader(string source) =>
        Assert.Throws<InvalidDataException>(() => MemoryDocument.Parse(source));

    [Fact]
    public void RejectsEmptyEditsAndUnknownNotes()
    {
        var document = MemoryDocument.Parse("# Desktop Pet Memory\n\n- A note\n");
        Assert.Throws<ArgumentException>(() => document.ReplaceNote(0, "  "));
        Assert.Throws<ArgumentException>(() => document.AddNote("Category", "\n"));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.RemoveNote(5));
    }

    [Theory]
    [InlineData("喜欢茶。\n# 周末\n想去公园。")]
    [InlineData("喜欢茶。\n## 周末\n想去公园。")]
    [InlineData("记忆\n```text\nhidden\n```")]
    [InlineData("记忆\n<!-- hidden -->")]
    [InlineData("记忆\n---\nother")]
    public void StructuralMarkdownInNoteInputIsRejectedWithoutChangingSource(string text)
    {
        const string source = "# Desktop Pet Memory\n\n- Original\n";
        var document = MemoryDocument.Parse(source);
        Assert.Throws<ArgumentException>(() => document.ReplaceNote(0, text));
        Assert.Throws<ArgumentException>(() => document.AddNote("偏好", text));
        Assert.Equal(source, document.Source);
    }
}

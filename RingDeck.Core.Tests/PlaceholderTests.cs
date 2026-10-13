namespace RingDeck.Core.Tests;

public class PlaceholderTests
{
    static readonly DateTime Now = new(2026, 10, 13, 9, 5, 7);
    static string Run(string text, string? clip = "CLIP") => Placeholders.Expand(text, Now, () => clip);

    [Fact] public void PlainTextIsUnchanged() => Assert.Equal("hello", Run("hello"));
    [Fact] public void ExpandsFormattedDateAndTime() =>
        Assert.Equal("2026/10/13 09:05", Run("{date:yyyy/MM/dd} {time:HH:mm}"));
    [Fact] public void DefaultDateUsesShortDate() => Assert.Equal(Now.ToString("d"), Run("{date}"));
    [Fact] public void DefaultTimeUsesShortTime() => Assert.Equal(Now.ToString("t"), Run("{time}"));
    [Fact] public void DateTimeUsesGeneral() => Assert.Equal(Now.ToString("g"), Run("{datetime}"));
    [Fact] public void ExpandsClipboard() => Assert.Equal("a CLIP b", Run("a {clipboard} b"));
    [Fact] public void NullClipboardBecomesEmpty() => Assert.Equal("ab", Run("a{clipboard}b", null));
    [Fact] public void DoubleBraceIsLiteral() => Assert.Equal("{date}", Run("{{date}"));
    [Fact] public void UnknownPlaceholderIsKept() => Assert.Equal("{nope} x", Run("{nope} x"));
    [Fact] public void UnclosedBraceIsKept() => Assert.Equal("a {date", Run("a {date"));
    [Fact] public void NamesAreCaseInsensitive() => Assert.Equal("CLIP", Run("{Clipboard}"));

    [Fact]
    public void ClipboardContentIsNotExpandedAgain() =>
        Assert.Equal("{date} {{ }", Run("{clipboard}", "{date} {{ }"));
}

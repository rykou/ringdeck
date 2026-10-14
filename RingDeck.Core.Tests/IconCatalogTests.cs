namespace RingDeck.Core.Tests;

public class IconCatalogTests
{
    const string EmojiJson = """
        [{"u":"1f600","n":"grinning face","g":"Smileys & Emotion","k":["face","grin"],"c":"😀"},
         {"u":"1f680","n":"rocket","g":"Travel & Places","k":["space","launch"],"c":"🚀"},
         {"u":"1f44d","n":"thumbs up","g":"People & Body","k":["like","yes","+1"],"c":"👍"},
         {"u":"1f3e0","n":"house","g":"Travel & Places","k":["home"],"c":"🏠"}]
        """;
    const string IconsJson = """[{"n":"home","c":62000},{"n":"home more","c":62001},{"n":"rocket","c":62002},{"n":"mail","c":62003}]""";

    [Fact]
    public void ParsesBothCatalogs()
    {
        var emoji = IconCatalog.ParseEmoji(EmojiJson);
        Assert.Equal(4, emoji.Count);
        Assert.Equal(new IconEntry("emoji:1f680", "rocket", "Travel & Places", new[] { "space", "launch" }), emoji[1], new EntryComparer());
        var icons = IconCatalog.ParseIcons(IconsJson);
        Assert.Equal("icon:f230", icons[0].Id);
        Assert.Equal("home", icons[0].Name);
        Assert.Empty(IconCatalog.ParseEmoji("not json"));
        Assert.Empty(IconCatalog.ParseIcons("[1,2]"));
    }

    [Fact]
    public void SearchMatchesNamesAndKeywordsByWordStart()
    {
        var all = IconCatalog.ParseEmoji(EmojiJson).Concat(IconCatalog.ParseIcons(IconsJson)).ToList();
        Assert.Equal(new[] { "emoji:1f3e0", "icon:f230", "icon:f231" }, IconCatalog.Search(all, "hom", null, 10).Select(e => e.Id).OrderBy(x => x));
        Assert.Equal("emoji:1f44d", IconCatalog.Search(all, "LIKE", null, 10).Single().Id);
        Assert.Equal("emoji:1f44d", IconCatalog.Search(all, "thumbs u", null, 10).Single().Id);
        Assert.Empty(IconCatalog.Search(all, "ocket", null, 10));                   // word starts only, not anywhere
    }

    [Fact]
    public void NameMatchesRankAboveKeywordMatches()
    {
        var all = IconCatalog.ParseEmoji(EmojiJson).Concat(IconCatalog.ParseIcons(IconsJson)).ToList();
        var ids = IconCatalog.Search(all, "home", null, 10).Select(e => e.Id).ToList();
        Assert.Equal("icon:f230", ids[0]);                  // exact name first
        Assert.Equal("icon:f231", ids[1]);                  // then names starting with it
        Assert.Equal("emoji:1f3e0", ids[2]);                // then keyword matches
    }

    [Fact]
    public void EmptyQueryListsTheGroupInOrderAndLimitApplies()
    {
        var emoji = IconCatalog.ParseEmoji(EmojiJson);
        Assert.Equal(new[] { "emoji:1f680", "emoji:1f3e0" }, IconCatalog.Search(emoji, "  ", "Travel & Places", 10).Select(e => e.Id));
        Assert.Equal(2, IconCatalog.Search(emoji, "", null, 2).Count);
        Assert.Equal(new[] { "Smileys & Emotion", "Travel & Places", "People & Body" }, IconCatalog.Groups(emoji));
    }

    [Theory]
    [InlineData("emoji:1f600", "emoji", "1f600")]
    [InlineData("icon:f230", "icon", "f230")]
    [InlineData("glyph:★", "glyph", "★")]
    [InlineData("file:a.png", "file", "a.png")]
    [InlineData("", "", "")]
    [InlineData("nonsense", "", "")]
    public void IconStringsSplitIntoKindAndValue(string icon, string kind, string value) => Assert.Equal((kind, value), IconCatalog.Split(icon));

    [Fact]
    public void IconCodePointBecomesText() => Assert.Equal("", IconCatalog.IconText("icon:f230"));

    sealed class EntryComparer : IEqualityComparer<IconEntry>
    {
        public bool Equals(IconEntry? a, IconEntry? b) => a!.Id == b!.Id && a.Name == b.Name && a.Group == b.Group && a.Keywords.SequenceEqual(b.Keywords);
        public int GetHashCode(IconEntry e) => e.Id.GetHashCode();
    }
}

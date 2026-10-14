namespace RingDeck.Core.Tests;

public class ColorMathTests
{
    [Theory]
    [InlineData(0, 1, 1, 255, 0, 0)]
    [InlineData(120, 1, 1, 0, 255, 0)]
    [InlineData(240, 1, 1, 0, 0, 255)]
    [InlineData(60, 1, 1, 255, 255, 0)]
    [InlineData(0, 0, 1, 255, 255, 255)]
    [InlineData(0, 0, 0, 0, 0, 0)]
    [InlineData(200, 0.5, 0.5, 64, 106, 128)]
    [InlineData(360, 1, 1, 255, 0, 0)]
    public void HsvToRgb(double h, double s, double v, byte r, byte g, byte b) =>
        Assert.Equal((r, g, b), ColorMath.HsvToRgb(h, s, v));

    [Fact]
    public void RoundTripsEveryCornerOfTheCube()
    {
        foreach (var r in new byte[] { 0, 37, 128, 255 })
        foreach (var g in new byte[] { 0, 90, 200, 255 })
        foreach (var b in new byte[] { 0, 15, 160, 255 })
        {
            var (h, s, v) = ColorMath.RgbToHsv(r, g, b);
            Assert.InRange(h, 0, 360);
            Assert.InRange(s, 0, 1);
            Assert.InRange(v, 0, 1);
            Assert.Equal((r, g, b), ColorMath.HsvToRgb(h, s, v));
        }
    }

    [Theory]
    [InlineData("#4CC2FF", 0x4C, 0xC2, 0xFF)]
    [InlineData("4cc2ff", 0x4C, 0xC2, 0xFF)]
    [InlineData("#FF4CC2FF", 0x4C, 0xC2, 0xFF)]
    [InlineData(" #000 ", 0, 0, 0)]
    [InlineData("#abc", 0xAA, 0xBB, 0xCC)]
    public void ParsesHex(string hex, byte r, byte g, byte b)
    {
        Assert.True(ColorMath.TryParseHex(hex, out var rgb));
        Assert.Equal((r, g, b), rgb);
    }

    [Theory]
    [InlineData("")]
    [InlineData("#12")]
    [InlineData("#GGGGGG")]
    [InlineData("blue")]
    public void RejectsBadHex(string hex) => Assert.False(ColorMath.TryParseHex(hex, out _));

    [Fact]
    public void FormatsHex() => Assert.Equal("#4CC2FF", ColorMath.ToHex(0x4C, 0xC2, 0xFF));
}

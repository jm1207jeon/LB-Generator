// 흑백 변환 — 임계값·디더링·비트 채우기 (MSB 먼저) 성질 검사.
using LaPrint.Core.Zpl;
using SkiaSharp;
using Xunit;

namespace LaPrint.Core.Tests.Zpl;

public class MonochromeTests
{
    private static SKBitmap HalfBlack(SKColorType ct)
    {
        var bmp = new SKBitmap(new SKImageInfo(16, 4, ct, SKAlphaType.Premul));
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 16; x++)
                bmp.SetPixel(x, y, x < 8 ? SKColors.Black : SKColors.White);
        return bmp;
    }

    [Theory]
    [InlineData(SKColorType.Rgba8888)]
    [InlineData(SKColorType.Bgra8888)]
    public void NoDither_LeftBlackRightWhite(SKColorType ct)
    {
        using var bmp = HalfBlack(ct);
        var m = Monochrome.Convert(bmp, threshold: 160, dither: false);
        Assert.Equal(16, m.Width);
        Assert.Equal(4, m.Height);
        Assert.Equal(2, m.WidthBytes);
        Assert.Equal(8, m.Bytes.Length);
        for (int y = 0; y < 4; y++)
        {
            Assert.Equal(0xFF, m.Bytes[y * 2]);
            Assert.Equal(0x00, m.Bytes[y * 2 + 1]);
        }
    }

    [Fact]
    public void Dither_HalfBlackRightWhite_StillExact()
    {
        using var bmp = HalfBlack(SKColorType.Rgba8888);
        var m = Monochrome.Convert(bmp, dither: true);
        for (int y = 0; y < 4; y++)
        {
            Assert.Equal(0xFF, m.Bytes[y * 2]);
            Assert.Equal(0x00, m.Bytes[y * 2 + 1]);
        }
    }

    [Fact]
    public void Dither_Gray50_RoughlyHalfBlack()
    {
        using var bmp = new SKBitmap(new SKImageInfo(8, 8, SKColorType.Rgba8888, SKAlphaType.Premul));
        bmp.Erase(new SKColor(128, 128, 128));
        var m = Monochrome.Convert(bmp, threshold: 160, dither: true);
        int black = 0;
        foreach (var b in m.Bytes) black += System.Numerics.BitOperations.PopCount(b);
        Assert.InRange(black, 20, 44);
    }

    [Fact]
    public void NoDither_Gray50_AllBlackBelowThreshold()
    {
        using var bmp = new SKBitmap(new SKImageInfo(8, 2, SKColorType.Bgra8888, SKAlphaType.Premul));
        bmp.Erase(new SKColor(128, 128, 128));
        Assert.All(Monochrome.Convert(bmp, threshold: 160, dither: false).Bytes, b => Assert.Equal(0xFF, b));
        Assert.All(Monochrome.Convert(bmp, threshold: 100, dither: false).Bytes, b => Assert.Equal(0x00, b));
    }

    [Fact]
    public void Transparent_IsWhite()
    {
        using var bmp = new SKBitmap(new SKImageInfo(8, 1, SKColorType.Rgba8888, SKAlphaType.Premul));
        bmp.Erase(SKColors.Transparent);
        var m = Monochrome.Convert(bmp, dither: false);
        Assert.Equal(0x00, m.Bytes[0]);
    }

    [Fact]
    public void WidthNotMultipleOf8_PadsRow()
    {
        using var bmp = new SKBitmap(new SKImageInfo(11, 1, SKColorType.Rgba8888, SKAlphaType.Premul));
        bmp.Erase(SKColors.Black);
        var m = Monochrome.Convert(bmp, dither: false);
        Assert.Equal(2, m.WidthBytes);
        Assert.Equal(0xFF, m.Bytes[0]);
        Assert.Equal(0xE0, m.Bytes[1]);     // 3비트만 검정, 나머지 패딩 0
    }
}

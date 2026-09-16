// 이미지 후처리 — 불투명 JPG/회색 배경 PNG 는 가장자리만 투명해지고, 이미 투명한 PNG 는 그대로.
using LaPrint.Core.Imaging;
using SkiaSharp;
using Xunit;

namespace LaPrint.Core.Tests.Imaging;

public class ImageProcessorTests
{
    private static byte[] Img(string name) => File.ReadAllBytes(Fixtures.Path(Path.Combine("images", name)));

    private static (int Transparent, int Opaque) AlphaStats(SKBitmap b)
    {
        int t = 0, o = 0;
        var px = b.Bytes;
        for (var i = 3; i < px.Length; i += 4)
        {
            if (px[i] == 0) t++;
            else if (px[i] == 255) o++;
        }
        return (t, o);
    }

    private static void AssertCornersTransparent(SKBitmap b)
    {
        var w = b.Width;
        var h = b.Height;
        Assert.Equal(0, b.GetPixel(0, 0).Alpha);
        Assert.Equal(0, b.GetPixel(w - 1, 0).Alpha);
        Assert.Equal(0, b.GetPixel(0, h - 1).Alpha);
        Assert.Equal(0, b.GetPixel(w - 1, h - 1).Alpha);
    }

    [Fact]
    public void OpaqueJpg_BecomesTransparentAtEdges_KeepsInterior()
    {
        using var raw = SKBitmap.Decode(Img("BNHS.jpg"));
        Assert.Equal(255, raw.GetPixel(0, 0).Alpha);

        using var b = ImageProcessor.Process(Img("BNHS.jpg"));
        Assert.Equal(raw.Width, b.Width);
        Assert.Equal(raw.Height, b.Height);
        Assert.Equal(SKColorType.Rgba8888, b.ColorType);
        AssertCornersTransparent(b);

        var (t, o) = AlphaStats(b);
        Assert.True(t > 1000, $"투명 픽셀 {t}");
        Assert.True(o > 1000, $"불투명 픽셀 {o}");
        // 안쪽 어딘가는 알파 255 가 남아 있다
        var found = false;
        for (var y = b.Height / 4; y < b.Height * 3 / 4 && !found; y++)
            for (var x = b.Width / 4; x < b.Width * 3 / 4; x++)
                if (b.GetPixel(x, y).Alpha == 255) { found = true; break; }
        Assert.True(found);
    }

    [Fact]
    public void TransparentPng_IsReturnedUnchanged()
    {
        using var raw = ImageProcessor.Process(Img("BCG.png"), autoTransparent: false);
        using var b = ImageProcessor.Process(Img("BCG.png"));
        Assert.Equal(raw.Width, b.Width);
        Assert.Equal(raw.Height, b.Height);
        Assert.Equal(AlphaStats(raw), AlphaStats(b));
        Assert.Equal(raw.Bytes, b.Bytes);
        AssertCornersTransparent(b);
    }

    [Fact]
    public void GreyBackgroundPng_BecomesTransparentAtEdges()
    {
        const string name = "FAUNASTENT™ 1.png";
        using var raw = SKBitmap.Decode(Img(name));
        var c = raw.GetPixel(0, 0);
        Assert.Equal(255, c.Alpha);
        Assert.True(c.Red < 250 || c.Green < 250 || c.Blue < 250, "모서리가 흰색이 아니어야 회색 배경 검사가 된다");

        using var b = ImageProcessor.Process(Img(name));
        AssertCornersTransparent(b);
        var (t, o) = AlphaStats(b);
        Assert.True(t > 1000, $"투명 픽셀 {t}");
        Assert.True(o > 100, $"불투명 픽셀 {o}");
    }

    [Fact]
    public void AutoTransparentOff_KeepsOpaque()
    {
        using var b = ImageProcessor.Process(Img("F.jpg"), autoTransparent: false);
        Assert.Equal(255, b.GetPixel(0, 0).Alpha);
        var (t, _) = AlphaStats(b);
        Assert.Equal(0, t);
    }

    [Fact]
    public void DetectBgColor_QuantizedMode()
    {
        // 3×3, 가장자리 회색(200) · 가운데 검정
        var w = 3; var h = 3;
        var px = new byte[w * h * 4];
        for (var i = 0; i < w * h; i++) { px[i * 4] = 200; px[i * 4 + 1] = 200; px[i * 4 + 2] = 200; px[i * 4 + 3] = 255; }
        px[4 * 4] = 0; px[4 * 4 + 1] = 0; px[4 * 4 + 2] = 0;
        Assert.Equal((200 >> 4 << 4) + 8, ImageProcessor.DetectBgColor(px, w, h).R);
        ImageProcessor.RemoveBackground(px, w, h, 30);
        Assert.Equal(0, px[3]);          // 모서리 투명
        Assert.Equal(255, px[4 * 4 + 3]); // 가운데 검정은 남는다
    }

    [Fact]
    public void BadBytes_Throw()
    {
        var e = Assert.Throws<InvalidOperationException>(() => ImageProcessor.Process(new byte[] { 1, 2, 3 }));
        Assert.Equal("이미지 디코딩 실패", e.Message);
    }
}

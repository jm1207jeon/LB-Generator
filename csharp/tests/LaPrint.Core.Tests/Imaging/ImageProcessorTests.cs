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

    [Theory]
    [InlineData(SKEncodedOrigin.TopLeft, 3, 2, 0, 0)]        // 그대로
    [InlineData(SKEncodedOrigin.TopRight, 3, 2, 2, 0)]       // 좌우 반전 → 표식이 오른쪽 위
    [InlineData(SKEncodedOrigin.BottomRight, 3, 2, 2, 1)]    // 180° → 오른쪽 아래
    [InlineData(SKEncodedOrigin.BottomLeft, 3, 2, 0, 1)]     // 상하 반전 → 왼쪽 아래
    [InlineData(SKEncodedOrigin.LeftTop, 2, 3, 0, 0)]        // 전치 → 왼쪽 위 (크기 바뀜)
    [InlineData(SKEncodedOrigin.RightTop, 2, 3, 1, 0)]       // 시계 90° → 오른쪽 위
    [InlineData(SKEncodedOrigin.RightBottom, 2, 3, 1, 2)]    // 반대각 전치 → 오른쪽 아래
    [InlineData(SKEncodedOrigin.LeftBottom, 2, 3, 0, 2)]     // 반시계 90° → 왼쪽 아래
    public void ApplyOrientation_MovesTopLeftMarker_LikeChrome(SKEncodedOrigin origin, int w, int h, int mx, int my)
    {
        // 3×2 그림: 왼쪽 위 픽셀만 빨강, 나머지 흰색. 브라우저(drawImage)는 EXIF 방향을 적용해 그렸다.
        var src = new SKBitmap(new SKImageInfo(3, 2, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        src.Erase(SKColors.White);
        src.SetPixel(0, 0, SKColors.Red);
        var dst = ImageProcessor.ApplyOrientation(src, origin);
        Assert.Equal((w, h), (dst.Width, dst.Height));
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                Assert.Equal((x, y) == (mx, my) ? SKColors.Red : SKColors.White, dst.GetPixel(x, y));
    }

    [Fact]
    public void Process_HonoursExifOrientation()
    {
        // EXIF Orientation=6(시계 90°) 이 붙은 JPEG 은 바로 세워져야 한다
        using var bmp = new SKBitmap(new SKImageInfo(40, 20, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        bmp.Erase(SKColors.White);
        using (var c = new SKCanvas(bmp)) c.DrawRect(2, 2, 10, 10, new SKPaint { Color = SKColors.Black });
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Jpeg, 95);
        var jpg = WithExifOrientation(data.ToArray(), 6);

        using var codec = SKCodec.Create(new SKMemoryStream(jpg));
        Assert.Equal(SKEncodedOrigin.RightTop, codec!.EncodedOrigin);   // 태그가 붙었는지

        using var outp = ImageProcessor.Process(jpg, autoTransparent: false);
        Assert.Equal((20, 40), (outp.Width, outp.Height));            // 가로 40×20 → 세로 20×40
        // 왼쪽 위 검정 사각형이 시계 90° 회전하면 오른쪽 위로 간다
        Assert.True(outp.GetPixel(20 - 7, 7).Red < 80);
        Assert.True(outp.GetPixel(7, 7).Red > 200);
    }

    // JFIF 바로 뒤에 APP1 Exif(Orientation 만) 를 끼워 넣는다
    private static byte[] WithExifOrientation(byte[] jpeg, ushort orientation)
    {
        var tiff = new List<byte> { 0x4D, 0x4D, 0x00, 0x2A, 0x00, 0x00, 0x00, 0x08 };      // 빅엔디언 TIFF 머리
        tiff.AddRange(new byte[] { 0x00, 0x01 });                                           // IFD 항목 1개
        tiff.AddRange(new byte[] { 0x01, 0x12, 0x00, 0x03, 0x00, 0x00, 0x00, 0x01, (byte)(orientation >> 8), (byte)orientation, 0x00, 0x00 });
        tiff.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x00 });                               // 다음 IFD 없음
        var payload = new List<byte> { (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0 };
        payload.AddRange(tiff);
        var len = payload.Count + 2;
        var seg = new List<byte> { 0xFF, 0xE1, (byte)(len >> 8), (byte)len };
        seg.AddRange(payload);
        var outp = new List<byte>();
        outp.AddRange(jpeg.Take(2));      // SOI
        outp.AddRange(seg);
        outp.AddRange(jpeg.Skip(2));
        return outp.ToArray();
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

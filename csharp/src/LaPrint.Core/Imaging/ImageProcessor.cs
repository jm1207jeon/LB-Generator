// 이미지 후처리 — 알파가 없으면 가장자리 최빈색을 배경으로 보고 플러드필 투명화 (imaging.js processBlob).
//
// 규칙 (엑셀 VBA UpdateImagesKeepAspectRatio 대응 + 요구사항):
//  - 배경이 투명한 PNG → 그대로 사용
//  - JPG 또는 배경이 불투명한 PNG → 가장자리 색을 배경색으로 감지하여
//    가장자리에서 연결된(플러드필) 유사색 픽셀만 투명 처리 (제품 내부의 흰색 영역은 유지됨)
using System.Runtime.InteropServices;
using SkiaSharp;

namespace LaPrint.Core.Imaging;

/// <summary>그림 파일 바이트 → 처리된 비트맵 (RGBA 8888, 비승산 알파).</summary>
public static class ImageProcessor
{
    /// <summary>파일 바이트를 디코드하고, autoTransparent 이면 불투명 배경을 투명화한 새 비트맵을 돌려준다.</summary>
    public static SKBitmap Process(byte[] data, bool autoTransparent = true, int tolerance = 30)
    {
        ArgumentNullException.ThrowIfNull(data);
        var bmp = DecodeRgba(data);
        if (!autoTransparent) return bmp;

        var w = bmp.Width;
        var h = bmp.Height;
        if (w <= 0 || h <= 0) return bmp;

        // 브라우저의 getImageData 와 같은 비승산 RGBA 바이트 (복사본)
        var px = bmp.Bytes;
        if (HasTransparency(px)) return bmp;

        RemoveBackground(px, w, h, tolerance);
        var info = new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var result = new SKBitmap(info);
        Marshal.Copy(px, 0, result.GetPixels(), px.Length);
        bmp.Dispose();
        return result;
    }

    /* 어떤 형식이든 RGBA8888 · 비승산 알파로 디코드한다 */
    private static SKBitmap DecodeRgba(byte[] data)
    {
        using var stream = new SKMemoryStream(data);
        using var codec = SKCodec.Create(stream);
        if (codec is null) throw new InvalidOperationException("이미지 디코딩 실패");
        var info = codec.Info.WithColorType(SKColorType.Rgba8888).WithAlphaType(SKAlphaType.Unpremul);
        var bmp = SKBitmap.Decode(codec, info);
        if (bmp is null) throw new InvalidOperationException("이미지 디코딩 실패");
        return bmp;
    }

    /// <summary>알파 &lt; 250 인 픽셀이 17개 이상이면 이미 투명 배경으로 간주한다.</summary>
    public static bool HasTransparency(ReadOnlySpan<byte> rgba)
    {
        var count = 0;
        for (var i = 3; i < rgba.Length; i += 4)
        {
            if (rgba[i] < 250) { count++; if (count > 16) return true; }
        }
        return false;
    }

    /// <summary>가장자리 픽셀에서 배경색 후보(16단위 양자화 최빈색)를 구한다.</summary>
    public static (int R, int G, int B) DetectBgColor(byte[] rgba, int w, int h)
    {
        ArgumentNullException.ThrowIfNull(rgba);
        // 삽입 순서를 지켜야 동률일 때 JS Map 과 같은 색이 뽑힌다
        var order = new List<int>();
        var freq = new Dictionary<int, int>();
        void Push(int x, int y)
        {
            var i = (y * w + x) * 4;
            var key = ((rgba[i] >> 4) << 16) | ((rgba[i + 1] >> 4) << 8) | (rgba[i + 2] >> 4);
            if (freq.TryGetValue(key, out var n)) freq[key] = n + 1;
            else { freq[key] = 1; order.Add(key); }
        }
        for (var x = 0; x < w; x += Math.Max(1, w >> 6)) { Push(x, 0); Push(x, h - 1); }
        for (var y = 0; y < h; y += Math.Max(1, h >> 6)) { Push(0, y); Push(w - 1, y); }

        var best = -1;
        var bestN = -1;
        foreach (var k in order)
        {
            var n = freq[k];
            if (n > bestN) { bestN = n; best = k; }
        }
        if (best < 0) return (8, 8, 8);
        return ((((best >> 16) & 0xF) << 4) + 8, (((best >> 8) & 0xF) << 4) + 8, ((best & 0xF) << 4) + 8);
    }

    /// <summary>가장자리에서 시작하는 BFS 플러드필로 배경(유사색) 픽셀의 알파를 0 으로 만든다. 제자리 수정.</summary>
    public static void RemoveBackground(byte[] rgba, int w, int h, int tolerance)
    {
        ArgumentNullException.ThrowIfNull(rgba);
        if (w <= 0 || h <= 0) return;
        var (br, bg, bb) = DetectBgColor(rgba, w, h);
        var tol2 = tolerance * tolerance * 3;
        var visited = new byte[w * h];
        var queue = new int[w * h];
        int qh = 0, qt = 0;

        bool IsBg(int p)
        {
            var i = p * 4;
            var dr = rgba[i] - br;
            var dg = rgba[i + 1] - bg;
            var db = rgba[i + 2] - bb;
            return dr * dr + dg * dg + db * db <= tol2;
        }
        void TryPush(int p)
        {
            if (visited[p] == 0 && IsBg(p)) { visited[p] = 1; queue[qt++] = p; }
        }

        for (var x = 0; x < w; x++) { TryPush(x); TryPush((h - 1) * w + x); }
        for (var y = 0; y < h; y++) { TryPush(y * w); TryPush(y * w + w - 1); }

        while (qh < qt)
        {
            var p = queue[qh++];
            rgba[p * 4 + 3] = 0;
            var x = p % w;
            var y = p / w;
            if (x > 0) TryPush(p - 1);
            if (x < w - 1) TryPush(p + 1);
            if (y > 0) TryPush(p - w);
            if (y < h - 1) TryPush(p + w);
        }
    }
}

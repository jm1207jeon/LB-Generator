// 흑백 변환 — 회색조 0.299R+0.587G+0.114B, 알파는 흰색 합성, Floyd–Steinberg 디더링 (zpl.js toMonochrome).
using SkiaSharp;

namespace LaPrint.Core.Zpl;

/// <summary>1비트 비트맵. 행마다 WidthBytes 바이트, 1 = 검정.</summary>
public sealed record MonoBitmap(byte[] Bytes, int WidthBytes, int Width, int Height);

/// <summary>비트맵 → 1비트 변환.</summary>
public static class Monochrome
{
    /// <summary>비트맵을 1비트 흑백으로. 1 = 검정(인쇄). dither 켬이면 Floyd–Steinberg 로 회색조를 살린다.</summary>
    public static MonoBitmap Convert(SKBitmap bmp, int threshold = 160, bool dither = true)
    {
        ArgumentNullException.ThrowIfNull(bmp);
        int w = bmp.Width, h = bmp.Height;

        // 그레이스케일 (흰 배경 위에 합성된 상태를 가정). 브라우저판 Float32Array 와 같이 float 로 누적한다.
        var gray = new float[w * h];
        ReadGray(bmp, gray);

        if (dither)
        {
            // Floyd–Steinberg — 제품 사진의 회색조를 살린다
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int p = y * w + x;
                    double old = gray[p];
                    double nv = old < threshold ? 0 : 255;
                    gray[p] = (float)nv;
                    double err = old - nv;
                    if (x + 1 < w) gray[p + 1] = (float)(gray[p + 1] + err * 7 / 16);
                    if (y + 1 < h)
                    {
                        if (x > 0) gray[p + w - 1] = (float)(gray[p + w - 1] + err * 3 / 16);
                        gray[p + w] = (float)(gray[p + w] + err * 5 / 16);
                        if (x + 1 < w) gray[p + w + 1] = (float)(gray[p + w + 1] + err * 1 / 16);
                    }
                }
            }
        }

        int widthBytes = (w + 7) / 8;
        var bytes = new byte[widthBytes * h];
        for (int y = 0; y < h; y++)
        {
            int rowOff = y * widthBytes;
            for (int x = 0; x < w; x++)
            {
                float v = gray[y * w + x];
                bool black = dither ? (v < 128) : (v < threshold);
                if (black) bytes[rowOff + (x >> 3)] |= (byte)(0x80 >> (x & 7));
            }
        }
        return new MonoBitmap(bytes, widthBytes, w, h);
    }

    /// <summary>ColorType 에 따라 픽셀을 읽어 흰색 합성 회색조로 채운다.</summary>
    private static void ReadGray(SKBitmap bmp, float[] gray)
    {
        int w = bmp.Width, h = bmp.Height;
        var ct = bmp.ColorType;
        bool premul = bmp.AlphaType == SKAlphaType.Premul;

        if ((ct == SKColorType.Rgba8888 || ct == SKColorType.Bgra8888) && bmp.RowBytes >= w * 4)
        {
            ReadOnlySpan<byte> px = bmp.GetPixelSpan();
            int rOff = ct == SKColorType.Rgba8888 ? 0 : 2;
            int bOff = ct == SKColorType.Rgba8888 ? 2 : 0;
            int rowBytes = bmp.RowBytes;
            for (int y = 0; y < h; y++)
            {
                int row = y * rowBytes;
                for (int x = 0; x < w; x++)
                {
                    int i = row + x * 4;
                    gray[y * w + x] = Lum(px[i + rOff], px[i + 1], px[i + bOff], px[i + 3], premul);
                }
            }
            return;
        }

        // 그 밖의 형식은 GetPixel 로 (느리지만 정확). Skia 가 비프리멀 값으로 돌려준다.
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var c = bmp.GetPixel(x, y);
                gray[y * w + x] = Lum(c.Red, c.Green, c.Blue, c.Alpha, false);
            }
        }
    }

    /// <summary>회색조 = 0.299R+0.587G+0.114B 를 알파로 흰색 위에 합성. 프리멀티플라이 픽셀은 이미 알파가 곱해져 있다.</summary>
    private static float Lum(byte r, byte g, byte b, byte alpha, bool premul)
    {
        double a = alpha / 255.0;
        double lum = 0.299 * r + 0.587 * g + 0.114 * b;
        return (float)((premul ? lum : lum * a) + 255 * (1 - a));      // 투명은 흰색으로
    }
}

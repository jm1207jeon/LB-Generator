// 흑백 변환 — 회색조 0.299R+0.587G+0.114B, 알파는 흰색 합성, Floyd–Steinberg 디더링 (zpl.js).
using SkiaSharp;

namespace LaPrint.Core.Zpl;

/// <summary>1비트 비트맵. 행마다 WidthBytes 바이트, 1 = 검정.</summary>
public sealed record MonoBitmap(byte[] Bytes, int WidthBytes, int Width, int Height);

/// <summary>비트맵 → 1비트 변환.</summary>
public static class Monochrome
{
    public static MonoBitmap Convert(SKBitmap bmp, int threshold = 160, bool dither = true)
        => throw new NotImplementedException("Monochrome.Convert — 아직 구현되지 않았습니다");
}

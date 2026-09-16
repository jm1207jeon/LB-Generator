// 조판 엔진 — text.js 를 가장 정밀하게 옮긴다 (tokenize · 금칙 · CJK · autoShrink 이분탐색 · 정렬).
using LaPrint.Core.Model;
using SkiaSharp;

namespace LaPrint.Core.Typography;

/// <summary>텍스트 조판과 그리기. 조판은 mm 로 한 번만 하고 그리기만 배율을 곱한다.</summary>
public static class TextLayoutEngine
{
    /// <summary>pt → mm.</summary>
    public const double Pt2Mm = 25.4 / 72;

    /// <summary>측정 기준 배율 (px/mm).</summary>
    public const double Ref = 20;

    public static TextLayout Layout(TextObject o, string text)
        => throw new NotImplementedException("TextLayoutEngine.Layout — 아직 구현되지 않았습니다");

    public static TextLayout Draw(SKCanvas c, TextObject o, string text, double scale, double ox, double oy)
        => throw new NotImplementedException("TextLayoutEngine.Draw — 아직 구현되지 않았습니다");

    public static (double WMm, double HMm, bool OverflowX, bool OverflowY, double SizePt, bool Shrunk, int Lines) Bounds(TextObject o, string text)
        => throw new NotImplementedException("TextLayoutEngine.Bounds — 아직 구현되지 않았습니다");

    /// <summary>조판 캐시를 비운다 (글꼴·객체 속성이 바뀐 뒤).</summary>
    public static void Invalidate()
        => throw new NotImplementedException("TextLayoutEngine.Invalidate — 아직 구현되지 않았습니다");
}

// 원판에서 떼어내기 — A3 세트 서식에서 라벨 하나의 영역만 잘라 새 서식을 만든다 (extract.js).
using LaPrint.Core.Model;
using SkiaSharp;

namespace LaPrint.Core.Export;

/// <summary>원판 서식에서 영역 안의 객체와 배경을 떼어낸다.</summary>
public static class SheetExtractor
{
    /// <summary>객체가 영역 안에 완전히 들어가는지 (tol mm 허용).</summary>
    public static bool Inside(LabelObject o, SKRect r, double tol = 0.8)
        => throw new NotImplementedException("SheetExtractor.Inside — 아직 구현되지 않았습니다");

    /// <summary>객체가 영역과 일부라도 겹치는지.</summary>
    public static bool Overlaps(LabelObject o, SKRect r)
        => throw new NotImplementedException("SheetExtractor.Overlaps — 아직 구현되지 않았습니다");

    public static (LabelTemplate Tpl, int Taken, int Partial, int Dropped) Extract(LabelTemplate sheet, SKRect rectMm, bool includePartial, bool keepBackground)
        => throw new NotImplementedException("SheetExtractor.Extract — 아직 구현되지 않았습니다");

    public static (LabelTemplate, int, int, int) ExtractPreset(LabelTemplate sheet, LabelPreset preset, bool keepBackground)
        => throw new NotImplementedException("SheetExtractor.ExtractPreset — 아직 구현되지 않았습니다");

    /// <summary>원판 배경에서 영역만큼 잘라낸 비트맵. 배경이 없으면 null.</summary>
    public static SKBitmap? CropBackground(SKBitmap bg, LabelSize sheet, SKRect rectMm)
        => throw new NotImplementedException("SheetExtractor.CropBackground — 아직 구현되지 않았습니다");

    /// <summary>프리셋별로 몇 개가 들어가고 몇 개가 걸치는지 미리보기.</summary>
    public static IReadOnlyList<(LabelPreset, int N, int Partial)> Preview(LabelTemplate sheet)
        => throw new NotImplementedException("SheetExtractor.Preview — 아직 구현되지 않았습니다");
}

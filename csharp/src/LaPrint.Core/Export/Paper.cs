// 라벨 규격 · 용지 배치(면付) — 기본 단위는 라벨 실물 크기, 용지는 출력 시점에만 개입 (paper.js).
using LaPrint.Core.Model;
using SkiaSharp;

namespace LaPrint.Core.Export;

/// <summary>용지 규격(mm). W/H 가 0 이면 라벨 실물 크기 또는 사용자 지정.</summary>
public sealed record PaperSpec(string Id, string Name, double W, double H);

/// <summary>라벨 규격. Src 는 A3 원판에서의 위치(떼어내기용).</summary>
public sealed record LabelPreset(string Id, string Name, double W, double H, (double X, double Y)? Src);

/// <summary>용지 배치 옵션 — 설정(layout)과 같은 구조.</summary>
public sealed class LayoutOptions
{
    /// <summary>label | A4 | A3 | … | custom.</summary>
    public string Paper { get; set; } = "label";
    public double CustomW { get; set; } = 210;
    public double CustomH { get; set; } = 297;
    /// <summary>auto | portrait | landscape.</summary>
    public string Orientation { get; set; } = "auto";
    public double MarginMm { get; set; } = 8;
    public double GapX { get; set; } = 3;
    public double GapY { get; set; } = 3;
    /// <summary>center | start.</summary>
    public string Align { get; set; } = "center";
    public bool CropMarks { get; set; }
    public bool Outline { get; set; }
    /// <summary>fill | single.</summary>
    public string Repeat { get; set; } = "fill";
}

/// <summary>배치 계산 결과. Direct 는 용지 없이 라벨 실물 크기로 출력.</summary>
public sealed record ImpositionPlan(double PaperW, double PaperH, int Cols, int Rows, int PerPage, double OriginX, double OriginY,
                                    double StepX, double StepY, bool Fits, bool Direct, string Reason);

/// <summary>용지·라벨 규격 표와 배치 계산.</summary>
public static class Paper
{
    public static IReadOnlyList<PaperSpec> Papers { get; } = new[]
    {
        new PaperSpec("label", "라벨 실물 크기 (용지 없이)", 0, 0),
        new PaperSpec("A4", "A4 (210 × 297)", 210, 297),
        new PaperSpec("A3", "A3 (297 × 420)", 297, 420),
        new PaperSpec("A5", "A5 (148 × 210)", 148, 210),
        new PaperSpec("B4", "B4 (257 × 364)", 257, 364),
        new PaperSpec("B5", "B5 (182 × 257)", 182, 257),
        new PaperSpec("Letter", "Letter (216 × 279)", 215.9, 279.4),
        new PaperSpec("Legal", "Legal (216 × 356)", 215.9, 355.6),
        new PaperSpec("custom", "사용자 지정…", 0, 0),
    };

    /// <summary>이 제품군의 A3 원판 안에 배치된 실제 라벨들 (배경 서식에서 실측).</summary>
    public static IReadOnlyList<LabelPreset> ProductLabels { get; } = new[]
    {
        new LabelPreset("PSL-001", "PSL-001 파우치 라벨", 173.8, 26.3, (59.5, 20.0)),
        new LabelPreset("BOX-PN", "제품 라벨 (PN 포함)", 173.8, 29.8, (59.5, 46.3)),
        new LabelPreset("BOX-TAB", "제품 라벨 (탭형)", 173.8, 28.9, (59.5, 76.1)),
        new LabelPreset("SPEC", "SPECIFICATION 규격표", 173.8, 92.6, (59.5, 105.0)),
        new LabelPreset("PML-001", "PML-001 UDI 라벨", 173.8, 22.9, (59.5, 197.6)),
        new LabelPreset("SMALL-1", "소형 라벨 (좌상)", 86.8, 26.5, (59.5, 220.5)),
        new LabelPreset("SMALL-2", "소형 라벨 (우상)", 87.0, 26.5, (146.3, 220.5)),
        new LabelPreset("SMALL-3", "소형 라벨 (좌하)", 86.8, 23.5, (59.5, 247.0)),
        new LabelPreset("SMALL-4", "소형 라벨 (우하)", 87.0, 23.5, (146.3, 247.0)),
        new LabelPreset("ICL-001-L", "ICL-001 환자카드 (좌)", 86.8, 47.5, (59.5, 270.5)),
        new LabelPreset("ICL-001-R", "ICL-001 환자카드 (우)", 87.0, 47.5, (146.3, 270.5)),
        new LabelPreset("PMFL-001", "PMFL-001 대형 라벨", 173.8, 75.0, (59.5, 318.0)),
    };

    /// <summary>감열 라벨 프린터에서 흔한 일반 규격.</summary>
    public static IReadOnlyList<LabelPreset> CommonLabels { get; } = new[]
    {
        new LabelPreset("C-100x150", "100 × 150 (4″×6″ 배송)", 100, 150, null),
        new LabelPreset("C-100x70", "100 × 70", 100, 70, null),
        new LabelPreset("C-100x50", "100 × 50", 100, 50, null),
        new LabelPreset("C-90x40", "90 × 40", 90, 40, null),
        new LabelPreset("C-70x40", "70 × 40", 70, 40, null),
        new LabelPreset("C-60x40", "60 × 40", 60, 40, null),
        new LabelPreset("C-50x30", "50 × 30", 50, 30, null),
        new LabelPreset("C-40x20", "40 × 20", 40, 20, null),
        new LabelPreset("C-30x15", "30 × 15", 30, 15, null),
    };

    /// <summary>A3 라벨 세트 원판 전체.</summary>
    public static LabelPreset Sheet { get; } = new("SHEET-A3", "A3 라벨 세트 원판 (전체)", 297, 420, null);

    public static ImpositionPlan Plan(LabelSize label, LayoutOptions o)
        => throw new NotImplementedException("Paper.Plan — 아직 구현되지 않았습니다");

    /// <summary>i 번째 칸의 좌상단 (mm).</summary>
    public static (double X, double Y) SlotAt(ImpositionPlan p, int i)
        => throw new NotImplementedException("Paper.SlotAt — 아직 구현되지 않았습니다");

    /// <summary>배치 요약 문구 (예: "A4 세로 · 2×3 = 6장/쪽").</summary>
    public static string Describe(LabelSize l, LayoutOptions o)
        => throw new NotImplementedException("Paper.Describe — 아직 구현되지 않았습니다");

    /// <summary>현재 치수와 일치하는 프리셋 (0.3mm 허용). 없으면 null.</summary>
    public static LabelPreset? MatchPreset(double w, double h)
        => throw new NotImplementedException("Paper.MatchPreset — 아직 구현되지 않았습니다");

    public static void DrawCropMarks(SKCanvas c, ImpositionPlan p, int i, LabelSize l, double pxPerMm)
        => throw new NotImplementedException("Paper.DrawCropMarks — 아직 구현되지 않았습니다");
}

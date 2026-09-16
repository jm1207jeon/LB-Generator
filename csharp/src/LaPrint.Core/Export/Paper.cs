// 라벨 규격 · 용지 배치(면付) — 기본 단위는 라벨 실물 크기, 용지는 출력 시점에만 개입 (paper.js).
using System.Globalization;
using LaPrint.Core.Model;
using SkiaSharp;

namespace LaPrint.Core.Export;

/// <summary>용지 규격(mm). W/H 가 0 이면 라벨 실물 크기 또는 사용자 지정.</summary>
public sealed record PaperSpec(string Id, string Name, double W, double H);

/// <summary>라벨 규격. Src 는 A3 원판에서의 위치(떼어내기용).</summary>
public sealed record LabelPreset(string Id, string Name, double W, double H, (double X, double Y)? Src);

/// <summary>용지 배치 옵션 — 설정(layout)과 같은 구조 (paper.js DEFAULT_LAYOUT).</summary>
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
    /// <summary>center | topleft.</summary>
    public string Align { get; set; } = "center";
    public bool CropMarks { get; set; }
    public bool Outline { get; set; }
    /// <summary>fill(한 페이지를 같은 라벨로 채움) | one(1장만).</summary>
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

    /// <summary>원판 크기 (mm) — paper.js SHEET.</summary>
    public static (double W, double H) SheetSize => (297, 420);

    /// <summary>프리셋 그룹 (이 제품 라벨 · 일반 규격 · 원판) — paper.js allLabelPresets.</summary>
    public static IReadOnlyList<(string Group, IReadOnlyList<LabelPreset> Items)> AllLabelPresets() => new[]
    {
        ("이 제품 라벨", ProductLabels),
        ("일반 규격", CommonLabels),
        ("원판", (IReadOnlyList<LabelPreset>)new[] { Sheet }),
    };

    /// <summary>id 로 용지 찾기. 모르면 첫 번째(라벨 실물 크기).</summary>
    public static PaperSpec PaperById(string? id)
    {
        foreach (var p in Papers) if (p.Id == id) return p;
        return Papers[0];
    }

    /// <summary>id 로 라벨 프리셋 찾기. 없으면 null.</summary>
    public static LabelPreset? LabelById(string? id)
    {
        foreach (var l in AllPresets()) if (l.Id == id) return l;
        return null;
    }

    /// <summary>용지에 라벨을 몇 개 앉힐 수 있는지 계산한다 (paper.js plan).</summary>
    public static ImpositionPlan Plan(LabelSize label, LayoutOptions o)
    {
        ArgumentNullException.ThrowIfNull(label);
        o ??= new LayoutOptions();
        var paper = o.Paper ?? "label";
        var p = PaperById(paper);
        if (paper == "label" || (p.W == 0 && paper != "custom"))
        {
            return new ImpositionPlan(label.W, label.H, 1, 1, 1, 0, 0, label.W, label.H, true, true, "");
        }
        var pw = paper == "custom" ? Num(o.CustomW, 210) : p.W;
        var ph = paper == "custom" ? Num(o.CustomH, 297) : p.H;

        // 방향 자동: 라벨이 더 많이 들어가는 쪽
        if (o.Orientation == "landscape" || (o.Orientation == "auto" && CountFit(label, ph, pw, o) > CountFit(label, pw, ph, o)))
        {
            (pw, ph) = (ph, pw);
        }
        var m = Math.Max(0, Num(o.MarginMm, 0));
        var gx = Math.Max(0, Num(o.GapX, 0));
        var gy = Math.Max(0, Num(o.GapY, 0));
        var availW = pw - m * 2;
        var availH = ph - m * 2;

        var cols = Math.Max(0, FloorInt((availW + gx) / (label.W + gx)));
        var rows = Math.Max(0, FloorInt((availH + gy) / (label.H + gy)));
        var fits = cols > 0 && rows > 0;

        var usedW = cols * label.W + (cols - 1) * gx;
        var usedH = rows * label.H + (rows - 1) * gy;
        double ox = m, oy = m;
        if (o.Align == "center" && fits)
        {
            ox = (pw - usedW) / 2;
            oy = (ph - usedH) / 2;
        }
        var reason = fits ? "" : $"라벨({Js(label.W)}×{Js(label.H)}mm)이 여백을 뺀 인쇄 영역({Fixed0(availW)}×{Fixed0(availH)}mm)보다 큽니다.";
        return new ImpositionPlan(pw, ph, cols, rows, cols * rows, ox, oy, label.W + gx, label.H + gy, fits, false, reason);
    }

    /// <summary>주어진 용지 방향에 몇 장 들어가는지 (paper.js countFit).</summary>
    public static int CountFit(LabelSize label, double pw, double ph, LayoutOptions o)
    {
        ArgumentNullException.ThrowIfNull(label);
        o ??= new LayoutOptions();
        var m = Math.Max(0, Num(o.MarginMm, 0));
        var gx = Math.Max(0, Num(o.GapX, 0));
        var gy = Math.Max(0, Num(o.GapY, 0));
        var c = FloorInt((pw - m * 2 + gx) / (label.W + gx));
        var r = FloorInt((ph - m * 2 + gy) / (label.H + gy));
        return Math.Max(0, c) * Math.Max(0, r);
    }

    /// <summary>i 번째 칸의 좌상단 (mm).</summary>
    public static (double X, double Y) SlotAt(ImpositionPlan p, int i)
    {
        ArgumentNullException.ThrowIfNull(p);
        var cols = Math.Max(1, p.Cols);
        var c = i % cols;
        var r = i / cols;
        return (p.OriginX + c * p.StepX, p.OriginY + r * p.StepY);
    }

    /// <summary>사람이 읽을 요약 (paper.js describe).</summary>
    public static string Describe(LabelSize l, LayoutOptions o)
    {
        ArgumentNullException.ThrowIfNull(l);
        var pl = Plan(l, o);
        if (pl.Direct) return $"라벨 실물 크기 {Js(l.W)}×{Js(l.H)}mm 로 1장씩 출력";
        if (!pl.Fits) return $"❌ {pl.Reason}";
        return $"{Fixed0(pl.PaperW)}×{Fixed0(pl.PaperH)}mm 용지에 {pl.Cols}열 × {pl.Rows}행 = 한 장에 {pl.PerPage}개";
    }

    /// <summary>현재 치수와 일치하는 프리셋 (0.3mm 허용). 없으면 null.</summary>
    public static LabelPreset? MatchPreset(double w, double h)
    {
        foreach (var l in AllPresets())
            if (Math.Abs(l.W - w) < 0.3 && Math.Abs(l.H - h) < 0.3) return l;
        return null;
    }

    /// <summary>재단선 그리기 — 칸 네 모서리 바깥쪽에 3mm 선 (paper.js drawCropMarks). 캔버스 좌표는 pxPerMm 배율.</summary>
    public static void DrawCropMarks(SKCanvas c, ImpositionPlan p, int i, LabelSize l, double pxPerMm)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(p);
        ArgumentNullException.ThrowIfNull(l);
        var (sx, sy) = SlotAt(p, i);
        const double L = 3;                              // 재단선 길이 mm
        using var paint = new SKPaint
        {
            Color = SKColors.Black,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = (float)Math.Max(0.5, 0.2 * pxPerMm),
            IsAntialias = true,
        };
        var pts = new (double X, double Y, int Dx, int Dy)[]
        {
            (sx, sy, -1, 0), (sx, sy, 0, -1),
            (sx + l.W, sy, 1, 0), (sx + l.W, sy, 0, -1),
            (sx, sy + l.H, -1, 0), (sx, sy + l.H, 0, 1),
            (sx + l.W, sy + l.H, 1, 0), (sx + l.W, sy + l.H, 0, 1),
        };
        c.Save();
        foreach (var (x, y, dx, dy) in pts)
        {
            c.DrawLine(
                (float)((x + dx * 0.8) * pxPerMm), (float)((y + dy * 0.8) * pxPerMm),
                (float)((x + dx * (0.8 + L)) * pxPerMm), (float)((y + dy * (0.8 + L)) * pxPerMm), paint);
        }
        c.Restore();
    }

    /* ---------------- 내부 ---------------- */

    private static IEnumerable<LabelPreset> AllPresets()
        => ProductLabels.Concat(CommonLabels).Append(Sheet);

    // JS `Number(x) || fallback` — 0 · NaN 은 대체값
    private static double Num(double v, double fallback) => double.IsNaN(v) || v == 0 ? fallback : v;

    // JS Math.floor 결과를 int 로 (무한대·NaN 은 0)
    private static int FloorInt(double v)
    {
        if (double.IsNaN(v) || double.IsInfinity(v)) return 0;
        var f = Math.Floor(v);
        return f >= int.MaxValue ? int.MaxValue : f <= int.MinValue ? int.MinValue : (int)f;
    }

    // JS 템플릿 문자열의 숫자 표기 (최단 왕복 표현: 75 → "75", 173.8 → "173.8")
    private static string Js(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    // JS toFixed(0) — 소수점 .5 는 0 에서 먼 쪽으로
    private static string Fixed0(double v) => Math.Round(v, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);
}

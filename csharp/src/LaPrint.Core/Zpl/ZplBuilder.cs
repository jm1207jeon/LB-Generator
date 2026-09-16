// ZPL 문서 조립 — ^XA … ^XZ, 농도·속도·매체 모드·원점, 점검 문구, 테스트 라벨 (zpl.js build · DIAGNOSTIC · sanityCheck).
using System.Globalization;
using LaPrint.Core.Data;
using LaPrint.Core.Model;

namespace LaPrint.Core.Zpl;

/// <summary>ZPL 생성 옵션 — 설정(printer)과 같은 구조.</summary>
public sealed class ZplOptions
{
    public int Dpi { get; set; } = 203;
    /// <summary>인쇄 농도 -30~30. null 이면 프린터 설정 유지.</summary>
    public int? Darkness { get; set; }
    /// <summary>인치/초. null 이면 프린터 설정 유지.</summary>
    public int? Speed { get; set; }
    public int Quantity { get; set; } = 1;
    /// <summary>T 티어오프 | P 필오프 | C 커터. null 이면 유지.</summary>
    public string? MediaMode { get; set; }
    public int HomeX { get; set; }
    public int HomeY { get; set; }
    /// <summary>180도 회전 출력.</summary>
    public bool Invert { get; set; }
    public int Threshold { get; set; } = 160;
    public bool Dither { get; set; } = true;
    public bool Compress { get; set; } = true;
    public int? LabelWidthDots { get; set; }
    public int? LabelLenDots { get; set; }
}

/// <summary>ZPL 문자열 조립과 점검.</summary>
public static class ZplBuilder
{
    /// <summary>프린터 상태/설정을 확인할 수 있는 표준 ZPL 조각들 (zpl.js DIAGNOSTIC).</summary>
    private const string DiagConfig = "^XA^HH^XZ";           // 설정 라벨을 호스트로
    private const string DiagPrintConfig = "~WC";            // 설정 라벨 인쇄
    private const string DiagCalibrate = "~JC";              // 미디어 캘리브레이션
    private const string DiagStatus = "~HS";                 // 상태 질의
    private const string DiagTestLabel =
        "^XA^CI28^FO40,40^A0N,40,40^FDLB Generator TEST^FS" +
        "^FO40,100^BXN,6,200^FD(01)08806367087911(10)TEST0001^FS" +
        "^FO40,320^A0N,28,28^FDZPL OK^FS^PQ1^XZ";

    /// <summary>1비트 비트맵 → ZPL 문자열. 줄은 \n 으로 잇는다.</summary>
    public static string Build(MonoBitmap m, ZplOptions o)
    {
        ArgumentNullException.ThrowIfNull(m);
        o ??= new ZplOptions();
        string gfa = ZplCompress.ToGfa(m, o.Compress);

        // JS 의 `o.labelWidthDots || mono.width` — 0 도 자동으로 본다
        int pw = o.LabelWidthDots is > 0 ? o.LabelWidthDots.Value : m.Width;
        int ll = o.LabelLenDots is > 0 ? o.LabelLenDots.Value : m.Height;

        var lines = new List<string>
        {
            "^XA",
            "^CI28",                                    // UTF-8 (텍스트 필드를 쓸 경우 대비)
            $"^PW{pw}",
            $"^LL{ll}",
            $"^LH{o.HomeX},{o.HomeY}",
        };
        if (o.Invert) lines.Add("^POI");
        if (o.Darkness != null) lines.Add($"^MD{o.Darkness.Value}");
        if (o.Speed != null) lines.Add($"^PR{o.Speed.Value}");
        if (!string.IsNullOrEmpty(o.MediaMode)) lines.Add($"^MM{o.MediaMode}");
        lines.Add($"^FO0,0{gfa}^FS");
        lines.Add($"^PQ{Math.Max(1, o.Quantity)}");
        lines.Add("^XZ");
        return string.Join("\n", lines);
    }

    /// <summary>라벨(mm) → 프린터 도트. JS Math.round 와 같이 .5 는 위로.</summary>
    public static int MmToDots(double mm, int dpi)
        => (int)Math.Floor(mm / 25.4 * dpi + 0.5);

    /// <summary>전송 전 점검 — 라벨 크기·도트 수·프린터 해상도가 맞는지.</summary>
    public static IReadOnlyList<Issue> SanityCheck(string zpl, int widthDots, int heightDots, int dpi, LabelSize label)
    {
        var issues = new List<Issue>();
        double mb = (zpl?.Length ?? 0) / 1048576.0;
        if (mb > 4)
        {
            issues.Add(new Issue("warn", "ZPL_SIZE",
                $"ZPL 크기가 {mb.ToString("F1", CultureInfo.InvariantCulture)}MB입니다. 프린터 메모리를 넘거나 전송이 느릴 수 있습니다. 해상도를 낮추거나 라벨을 나누세요."));
        }
        if (label != null && label.W > 110)
        {
            issues.Add(new Issue("warn", "ZPL_LABEL_WIDTH",
                $"라벨 폭이 {label.W.ToString(CultureInfo.InvariantCulture)}mm입니다. 일반 데스크톱 제브라 모델의 최대 인쇄 폭(약 104mm)을 넘습니다."));
        }
        if (widthDots > 1400 && dpi == 203)
        {
            issues.Add(new Issue("info", "ZPL_WIDE", "폭이 넓습니다. 산업용 모델(ZT 계열)인지 확인하세요."));
        }
        return issues;
    }

    /// <summary>테스트 라벨 (텍스트 + GS1 DataMatrix). 브라우저판과 같이 해상도와 무관한 고정 도트 좌표.</summary>
    public static string TestLabel(int dpi) => DiagTestLabel;

    /// <summary>프린터 설정 라벨 출력 명령 (~WC).</summary>
    public static string PrintConfig() => DiagPrintConfig;

    /// <summary>매체 보정 명령 (~JC).</summary>
    public static string Calibrate() => DiagCalibrate;

    /// <summary>설정 라벨을 호스트로 되돌려 받는 명령 (^HH).</summary>
    public static string HostConfig() => DiagConfig;

    /// <summary>상태 질의 명령 (~HS).</summary>
    public static string HostStatus() => DiagStatus;
}

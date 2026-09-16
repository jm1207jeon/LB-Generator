// ZPL 문서 조립 — ^XA … ^XZ, 농도·속도·매체 모드·원점, 점검 문구, 테스트 라벨.
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
    public static string Build(MonoBitmap m, ZplOptions o)
        => throw new NotImplementedException("ZplBuilder.Build — 아직 구현되지 않았습니다");

    public static int MmToDots(double mm, int dpi)
        => throw new NotImplementedException("ZplBuilder.MmToDots — 아직 구현되지 않았습니다");

    /// <summary>전송 전 점검 — 라벨 크기·도트 수·프린터 해상도가 맞는지.</summary>
    public static IReadOnlyList<Issue> SanityCheck(string zpl, int widthDots, int heightDots, int dpi, LabelSize label)
        => throw new NotImplementedException("ZplBuilder.SanityCheck — 아직 구현되지 않았습니다");

    public static string TestLabel(int dpi)
        => throw new NotImplementedException("ZplBuilder.TestLabel — 아직 구현되지 않았습니다");

    /// <summary>프린터 설정 라벨 출력 명령 (~WC).</summary>
    public static string PrintConfig()
        => throw new NotImplementedException("ZplBuilder.PrintConfig — 아직 구현되지 않았습니다");

    /// <summary>매체 보정 명령 (~JC).</summary>
    public static string Calibrate()
        => throw new NotImplementedException("ZplBuilder.Calibrate — 아직 구현되지 않았습니다");
}

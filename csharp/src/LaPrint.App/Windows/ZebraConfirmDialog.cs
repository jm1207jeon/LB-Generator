// ZEBRA 전송 확인 — 프린터·해상도·도트 크기·전송 경로·점검 결과를 적고 묻는다. 기본 버튼은 취소 (app.js confirmZebra).
using System.Windows;
using LaPrint.Core.Data;
using LaPrint.Core.Storage;

namespace LaPrint.App.Windows;

/// <summary>전송할 ZPL 작업 요약.</summary>
public sealed record ZebraJobInfo(int Dpi, int WidthDots, int HeightDots, double LabelW, double LabelH, string Transport, int ZplLength);

/// <summary>ZEBRA 전송 확인 대화상자.</summary>
public sealed class ZebraConfirmDialog : Window
{
    private readonly ZebraJobInfo _info;
    private readonly IReadOnlyList<Issue> _issues;
    private readonly int _jobCount;
    private readonly PrinterSettings _printer;

    private ZebraConfirmDialog(Window owner, ZebraJobInfo info, IReadOnlyList<Issue> issues, int jobCount, PrinterSettings printer)
    {
        Owner = owner;
        _info = info; _issues = issues; _jobCount = jobCount; _printer = printer;
        Title = "ZEBRA 프린터로 보내기";
        Width = 500;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Build();
    }

    /// <summary>보내면 true. "{jobCount}건을 ZEBRA 프린터로 보냅니다."</summary>
    public static Task<bool> ShowAsync(Window owner, ZebraJobInfo info, IReadOnlyList<Issue> issues, int jobCount, PrinterSettings printer)
        => throw new NotImplementedException("ZebraConfirmDialog.ShowAsync — TODO wave");

    /// <summary>머리 + 표(프린터/전송/해상도/라벨 크기/도트/농도·속도) + 점검 목록 + [취소](기본)/[보내기].</summary>
    private void Build() { /* TODO wave */ }
}

// 출력 확인 — 개수·저장 위치·점검 결과를 적고 물음표로 끝난다. 기본 버튼은 취소 (app.js confirmDialog, DESIGN §4-3).
using System.Windows;
using LaPrint.Core.Export;

namespace LaPrint.App.Windows;

/// <summary>PDF 출력 확인 대화상자 (1장 / 큐).</summary>
public sealed class PrintConfirmDialog : Window
{
    private readonly IReadOnlyList<ExportJob> _jobs;
    private readonly PreflightResult _pf;
    private readonly ExportOptions _options;
    private readonly bool _queueMode;
    private readonly int _total;
    private readonly int _skipped;

    private PrintConfirmDialog(Window owner, IReadOnlyList<ExportJob> jobs, PreflightResult pf, ExportOptions options,
                               bool queueMode, int total, int skipped)
    {
        Owner = owner;
        _jobs = jobs; _pf = pf; _options = options; _queueMode = queueMode; _total = total; _skipped = skipped;
        Title = queueMode ? "큐 출력 확인" : "출력 확인";
        Width = 500;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Build();
    }

    /// <summary>출력을 진행하면 true. queueMode 면 "큐 {n}행 · 총 {m}장을 출력합니다. 오류 행 {k}건은 건너뜁니다. 출력할까요?"</summary>
    public static Task<bool> ShowAsync(Window owner, IReadOnlyList<ExportJob> jobs, PreflightResult pf, ExportOptions options,
                                       bool queueMode = false, int total = 0, int skipped = 0)
        => throw new NotImplementedException("PrintConfirmDialog.ShowAsync — TODO wave");

    /// <summary>머리(개수) + 필드 요약(품목·LOT·SN·제조일·유효일·UDI) + 경고 목록 + 저장 폴더/파일명 + [취소](기본)/[출력].</summary>
    private void Build() { /* TODO wave */ }
}

// 출력 확인 — 개수·저장 위치·점검 결과를 적고 물음표로 끝난다. 기본 버튼은 취소 (app.js confirmDialog, DESIGN §4-3).
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using LaPrint.Core.Data;
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

    private static Brush B(string key) => (Brush)Application.Current.FindResource(key);
    private static Style? S(string key) => Application.Current.TryFindResource(key) as Style;

    private PrintConfirmDialog(Window owner, IReadOnlyList<ExportJob> jobs, PreflightResult pf, ExportOptions options,
                               bool queueMode, int total, int skipped)
    {
        Owner = owner;
        _jobs = jobs; _pf = pf; _options = options; _queueMode = queueMode; _total = total; _skipped = skipped;
        Title = queueMode ? "연속 출력 확인" : "출력 확인";
        Width = 500;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = B("PaperBrush");
        Build();
    }

    /// <summary>출력을 진행하면 true. queueMode 면 "큐 {n}행 · 총 {m}장을 출력합니다. 오류 행 {k}건은 건너뜁니다. 출력할까요?"</summary>
    public static Task<bool> ShowAsync(Window owner, IReadOnlyList<ExportJob> jobs, PreflightResult pf, ExportOptions options,
                                       bool queueMode = false, int total = 0, int skipped = 0)
    {
        var w = new PrintConfirmDialog(owner, jobs, pf, options, queueMode, total, skipped);
        return Task.FromResult(w.ShowDialog() == true);
    }

    /// <summary>머리(개수) + 필드 요약(품목·LOT·SN·제조일·유효일·UDI) + 경고 목록 + 저장 폴더/파일명 + [취소](기본)/[출력].</summary>
    private void Build()
    {
        var body = new StackPanel { Margin = new Thickness(18, 16, 18, 14) };
        var f = _jobs.Count > 0 ? _jobs[0].Fields : new Fields();
        var copies = _jobs.Count > 0 ? Math.Max(1, _jobs[0].Copies) : 1;

        // 머리 — 개수를 굵게
        var head = new TextBlock { FontSize = 14, Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap };
        if (_queueMode)
        {
            head.Inlines.Add(new System.Windows.Documents.Run($"큐 {_jobs.Count}행 · 총 {_total}장") { FontWeight = FontWeights.Bold });
            head.Inlines.Add("을 출력합니다.");
        }
        else
        {
            head.Inlines.Add(new System.Windows.Documents.Run($"이 라벨 {copies}장") { FontWeight = FontWeights.Bold });
            head.Inlines.Add("을 출력합니다.");
        }
        body.Children.Add(head);
        if (_queueMode && _skipped > 0)
            body.Children.Add(new TextBlock { Text = $"오류가 있는 {_skipped}행은 건너뜁니다.", Foreground = B("FailBrush"), FontSize = 12, Margin = new Thickness(0, 0, 0, 6) });

        // 핵심 값을 크게 다시 보여준다 (의료기기 라벨 오류 방지)
        var cells = _queueMode
            ? new[] { ("첫 품목", f.Get("ITEM")), ("규격", f.Get("REF")), ("LOT", f.Get("LOT")), ("제조일", f.Get("MFG")), ("유효일", f.Get("EXP")) }
            : new[]
            {
                ("품목번호", f.Get("ITEM")), ("규격 REF", f.Get("REF")), ("LOT", f.Get("LOT")), ("SN", f.Get("SN")),
                ("제조일", f.Get("MFG")), ("유효일", f.Get("EXP")), ("매수", copies + "장"),
            };
        var grid = new UniformGrid { Columns = 2 };
        foreach (var (k, v) in cells) grid.Children.Add(Cell(k, v));
        body.Children.Add(grid);
        body.Children.Add(Cell("UDI", f.Get("UDI_FULL"), mono: true));

        // 경고 (최대 6개)
        var warns = _pf.Warnings.Take(6).ToList();
        if (warns.Count > 0)
        {
            var ul = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            foreach (var w in warns)
                ul.Children.Add(new TextBlock { Text = "⚠ " + w.Msg, Foreground = B("WarnBrush"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 1) });
            body.Children.Add(ul);
        }

        // 저장 위치 — 어디에 어떤 이름으로 저장되는지
        var save = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        save.Children.Add(Hint("저장 폴더: " + (string.IsNullOrWhiteSpace(_options.OutDir) ? "(다운로드)" : _options.OutDir)));
        save.Children.Add(Hint("파일명 규칙: " + _options.Pattern + (_queueMode ? (_options.Mode == "merged" ? " · 한 PDF에 여러 쪽" : " · 라벨마다 개별 PDF") : "")));
        if (_options.Conflict == "overwrite") save.Children.Add(Hint("같은 이름이 있으면 덮어씁니다."));
        body.Children.Add(save);

        body.Children.Add(new TextBlock
        {
            Text = "출력된 라벨은 되돌릴 수 없습니다. 값을 확인했으면 출력할까요?",
            FontSize = 13, Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap, Foreground = B("InkBrush"),
        });

        var ok = new Button { Content = _queueMode ? $"{_total}장 출력" : "출력", MinWidth = 96, Style = S("PrimaryButton") };
        var cancel = new Button { Content = "취소", MinWidth = 84, IsDefault = true, IsCancel = true };
        ok.Click += (_, _) => { DialogResult = true; Close(); };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        body.Children.Add(buttons);
        Content = body;
        Loaded += (_, _) => cancel.Focus();
    }

    private static Border Cell(string k, string v, bool mono = false) => new()
    {
        Background = B("GroundBrush"), BorderBrush = B("Line2Brush"), BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 0, 4, 4),
        Child = new StackPanel
        {
            Children =
            {
                new TextBlock { Text = k, FontSize = 10.5, Foreground = B("Ink3Brush") },
                new TextBlock
                {
                    Text = string.IsNullOrEmpty(v) ? "—" : v, FontSize = mono ? 12 : 15, FontWeight = FontWeights.Bold,
                    FontFamily = new FontFamily(mono ? "Consolas" : "Segoe UI"), TextWrapping = TextWrapping.Wrap,
                },
            },
        },
    };

    private static TextBlock Hint(string t) => new() { Text = t, FontSize = 11, Foreground = B("Ink3Brush"), TextWrapping = TextWrapping.Wrap };
}

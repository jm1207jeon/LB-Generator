// ZEBRA 전송 확인 — 프린터·해상도·도트 크기·전송 경로·점검 결과를 적고 묻는다. 기본 버튼은 취소 (app.js confirmZebra).
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using LaPrint.App.Services;
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

    private static Brush B(string key) => (Brush)Application.Current.FindResource(key);
    private static Style? S(string key) => Application.Current.TryFindResource(key) as Style;

    private ZebraConfirmDialog(Window owner, ZebraJobInfo info, IReadOnlyList<Issue> issues, int jobCount, PrinterSettings printer)
    {
        Owner = owner;
        _info = info; _issues = issues; _jobCount = jobCount; _printer = printer;
        Title = "ZEBRA 출력 확인";
        Width = 500;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = B("PaperBrush");
        Build();
    }

    /// <summary>보내면 true. "{jobCount}건을 ZEBRA 프린터로 보냅니다."</summary>
    public static Task<bool> ShowAsync(Window owner, ZebraJobInfo info, IReadOnlyList<Issue> issues, int jobCount, PrinterSettings printer)
    {
        var w = new ZebraConfirmDialog(owner, info, issues, jobCount, printer);
        return Task.FromResult(w.ShowDialog() == true);
    }

    /// <summary>머리 + 표(프린터/전송/해상도/라벨 크기/도트/농도·속도) + 점검 목록 + [취소](기본)/[보내기].</summary>
    private void Build()
    {
        var body = new StackPanel { Margin = new Thickness(18, 16, 18, 14) };
        var head = new TextBlock { FontSize = 13.5, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
        head.Inlines.Add(new System.Windows.Documents.Run($"{_jobCount}건") { FontWeight = FontWeights.Bold });
        head.Inlines.Add("을 ZEBRA 프린터로 보냅니다.");
        body.Children.Add(head);

        var darkness = _printer.Darkness is { } d ? d.ToString() : "프린터 설정";
        var speed = _printer.Speed is { } s ? $"{s} in/s" : "프린터 설정";
        long bytes = _info.ZplLength;   // ZPL 은 ASCII 이므로 글자 수 = 바이트 수
        var cells = new[]
        {
            ("프린터", _info.Transport), ("전송 방법", PrinterService.MethodName(_printer.Method)),
            ("프린터 해상도", $"{_info.Dpi} dpi"), ("라벨 크기", $"{_info.LabelW}×{_info.LabelH} mm"),
            ("도트", $"{_info.WidthDots} × {_info.HeightDots}"), ("전송량", PrinterService.FmtBytes(bytes)),
            ("인쇄 농도", darkness), ("인쇄 속도", speed),
        };
        var grid = new UniformGrid { Columns = 2 };
        foreach (var (k, v) in cells) grid.Children.Add(Cell(k, v));
        body.Children.Add(grid);

        if (_issues.Count > 0)
        {
            var ul = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            foreach (var i in _issues)
            {
                var err = i.Level == "error";
                ul.Children.Add(new TextBlock
                {
                    Text = (err ? "✕ " : "⚠ ") + i.Msg, Foreground = B(err ? "FailBrush" : "WarnBrush"),
                    FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 1),
                });
            }
            body.Children.Add(ul);
        }

        body.Children.Add(new TextBlock
        {
            Text = "전송 방법: " + PrinterService.MethodName(_printer.Method) + " (설정 › ZEBRA 프린터에서 변경)",
            FontSize = 11, Foreground = B("Ink3Brush"), Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap,
        });
        body.Children.Add(new TextBlock
        {
            Text = "프린터로 보낸 라벨은 되돌릴 수 없습니다. 보낼까요?",
            FontSize = 13, Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap,
        });

        var ok = new Button { Content = $"{_jobCount}건 보내기", MinWidth = 110, Style = S("PrimaryButton") };
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

    private static Border Cell(string k, string v) => new()
    {
        Background = B("GroundBrush"), BorderBrush = B("Line2Brush"), BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 0, 4, 4),
        Child = new StackPanel
        {
            Children =
            {
                new TextBlock { Text = k, FontSize = 10.5, Foreground = B("Ink3Brush") },
                new TextBlock { Text = string.IsNullOrEmpty(v) ? "—" : v, FontSize = 13, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap },
            },
        },
    };
}

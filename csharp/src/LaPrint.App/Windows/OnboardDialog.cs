// 처음 설정 안내 — 라벨DB 지정 · 이미지 폴더 · 저장 폴더 · 샘플 데이터 단계 (app.js showOnboard).
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LaPrint.App.Windows;

/// <summary>처음 시작 안내. 단계마다 완료 표시와 바로가기 동작.</summary>
public sealed class OnboardDialog : Window
{
    private readonly bool _dbDone;
    private readonly bool _imgDone;
    private readonly bool _outDone;
    private string? _result;

    private static Brush B(string key) => (Brush)Application.Current.FindResource(key);
    private static Style? S(string key) => Application.Current.TryFindResource(key) as Style;

    private OnboardDialog(Window owner, bool dbDone, bool imgDone, bool outDone)
    {
        Owner = owner;
        _dbDone = dbDone; _imgDone = imgDone; _outDone = outDone;
        Title = "처음 설정";
        Width = 480;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = B("PaperBrush");
        Build();
    }

    /// <summary>사용자가 고른 동작 키: "db" | "img" | "out" | "sample" | "never"(다시 보지 않기) | null(닫음).</summary>
    public static Task<string?> ShowAsync(Window owner, bool dbDone, bool imgDone, bool outDone)
    {
        var w = new OnboardDialog(owner, dbDone, imgDone, outDone);
        w.ShowDialog();
        return Task.FromResult(w._result);
    }

    /// <summary>단계 목록(제목·설명·완료 ✓·바로가기 버튼) + [나중에].</summary>
    private void Build()
    {
        var body = new StackPanel { Margin = new Thickness(20, 18, 20, 16) };
        body.Children.Add(new TextBlock { Text = "처음 설정", FontSize = 18, FontWeight = FontWeights.Bold });
        body.Children.Add(new TextBlock
        {
            Text = "아래 3가지만 지정하면 바로 쓸 수 있습니다. 한 번 지정하면 계속 유지됩니다.",
            FontSize = 12, Foreground = B("Ink3Brush"), Margin = new Thickness(0, 4, 0, 12), TextWrapping = TextWrapping.Wrap,
        });

        var steps = new[]
        {
            ("라벨DB 지정", "DB 파일이 있는 폴더와 파일명을 고릅니다.", _dbDone, "db"),
            ("이미지 폴더 지정", "제품 그림이 있는 폴더를 고릅니다. (네트워크 드라이브 가능)", _imgDone, "img"),
            ("PDF 저장 폴더 지정", "선택 사항 — 지정하지 않으면 다운로드 폴더로 저장됩니다.", _outDone, "out"),
        };
        var n = 1;
        foreach (var (t, d, done, act) in steps)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var mark = new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(11), VerticalAlignment = VerticalAlignment.Top,
                Background = done ? B("PassBrush") : B("AccentSoftBrush"),
                Child = new TextBlock
                {
                    Text = done ? "✓" : n.ToString(), FontSize = 12, FontWeight = FontWeights.Bold,
                    Foreground = done ? Brushes.White : B("AccentDeepBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                },
            };
            row.Children.Add(mark);
            var txt = new StackPanel { Margin = new Thickness(6, 0, 10, 0) };
            txt.Children.Add(new TextBlock { Text = t, FontWeight = FontWeights.SemiBold, FontSize = 14, Foreground = done ? B("Ink3Brush") : B("InkBrush") });
            txt.Children.Add(new TextBlock { Text = d, FontSize = 11.5, Foreground = B("Ink3Brush"), TextWrapping = TextWrapping.Wrap });
            Grid.SetColumn(txt, 1);
            row.Children.Add(txt);
            var b = new Button { Content = done ? "변경" : "지정", MinWidth = 64, VerticalAlignment = VerticalAlignment.Center, Style = S(done ? "SmallGhostButton" : "SmallButton") };
            var key = act;
            b.Click += (_, _) => { _result = key; DialogResult = true; Close(); };
            Grid.SetColumn(b, 2);
            row.Children.Add(b);
            body.Children.Add(row);
            n++;
        }

        var foot = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var sample = new Button { Content = "샘플 데이터로 체험", MinWidth = 130, Style = S("PrimaryButton") };
        sample.Click += (_, _) => { _result = "sample"; DialogResult = true; Close(); };
        DockPanel.SetDock(sample, Dock.Right);
        foot.Children.Add(sample);
        var later = new Button { Content = "나중에", MinWidth = 76, IsCancel = true, IsDefault = true, Style = S("GhostButton") };
        later.Click += (_, _) => { _result = null; DialogResult = false; Close(); };
        var never = new Button { Content = "다시 보지 않기", MinWidth = 100, Margin = new Thickness(6, 2, 2, 2) };
        never.Click += (_, _) => { _result = "never"; DialogResult = true; Close(); };
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(later);
        left.Children.Add(never);
        foot.Children.Add(left);
        body.Children.Add(foot);
        Content = body;
        Loaded += (_, _) => later.Focus();
    }
}

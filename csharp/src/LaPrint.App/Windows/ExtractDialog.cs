// 원판에서 떼어내기 — A3 세트 서식에서 라벨 규격 하나를 골라 독립 서식으로 만든다 (app.js openExtractDialog).
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using LaPrint.Core.Export;
using LaPrint.Core.Model;
using LaPrint.Core.Storage;

namespace LaPrint.App.Windows;

/// <summary>사용자가 고른 떼어내기 조건.</summary>
public sealed record ExtractChoice(LabelPreset Preset, bool IncludePartial, bool KeepBackground);

/// <summary>원판에서 떼어내기 대화상자. 프리셋별 (들어가는 객체 N · 걸치는 객체) 미리보기 표.</summary>
public sealed class ExtractDialog : Window
{
    private readonly LabelTemplate _sheet;
    private LabelPreset? _chosen;
    private readonly List<Border> _cards = new();
    private CheckBox _keep = null!;
    private CheckBox _partial = null!;
    private TextBlock _pickHint = null!;

    private static Brush B(string key) => (Brush)Application.Current.FindResource(key);
    private static Style? S(string key) => Application.Current.TryFindResource(key) as Style;

    private ExtractDialog(Window owner, LabelTemplate sheet)
    {
        Owner = owner;
        _sheet = sheet;
        Title = "원판에서 라벨 떼어내기";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = B("PaperBrush");
        Build();
    }

    /// <summary>선택 결과. 취소면 null. (호출자가 SheetExtractor.ExtractPreset 로 실제 떼어낸다)</summary>
    public static Task<ExtractChoice?> ShowAsync(Window owner, LabelTemplate sheet)
    {
        var w = new ExtractDialog(owner, sheet);
        var ok = w.ShowDialog() == true && w._chosen is not null;
        return Task.FromResult(ok ? new ExtractChoice(w._chosen!, w._partial.IsChecked == true, w._keep.IsChecked == true) : null);
    }

    /// <summary>SheetExtractor.Preview(sheet) 표 + 옵션(걸치는 객체 포함 · 배경 유지) + [떼어내기]/[취소].</summary>
    private void Build()
    {
        var body = new StackPanel { Margin = new Thickness(18, 16, 18, 14) };
        body.Children.Add(new TextBlock
        {
            Text = "A3 원판에서 라벨 한 장만 떼어내 그 라벨의 실제 크기를 가진 독립 서식으로 만듭니다. 배경 서식 그림도 그 부분만 잘라 옵니다.",
            FontSize = 11, Foreground = B("Ink3Brush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8),
        });

        // 프리셋별 (들어가는 객체 · 걸치는 객체) — Preview 가 실패해도 규격은 고를 수 있게 한다
        var items = new List<(LabelPreset Preset, int N, int Partial)>();
        string? previewError = null;
        try
        {
            foreach (var (p, n, partial) in SheetExtractor.Preview(_sheet)) items.Add((p, n, partial));
        }
        catch (Exception ex)
        {
            AppLog.Warn("떼어내기 미리보기 실패: " + ex.Message);
            previewError = ex.Message;
            items.Clear();
        }
        if (items.Count == 0) foreach (var p in Paper.ProductLabels) items.Add((p, -1, 0));

        var list = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 2) };
        foreach (var (preset, n, partial) in items)
        {
            var card = Card(preset, n, partial);
            _cards.Add(card);
            list.Children.Add(card);
        }
        body.Children.Add(new ScrollViewer { Content = list, MaxHeight = 380, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        if (previewError is not null)
            body.Children.Add(new TextBlock
            {
                Text = "객체 수를 미리 셀 수 없습니다: " + previewError, FontSize = 11, Foreground = B("WarnBrush"),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0),
            });

        _keep = new CheckBox { Content = "배경 서식 그림도 잘라서 가져오기", IsChecked = true, Margin = new Thickness(0, 8, 0, 0) };
        body.Children.Add(_keep);
        _partial = new CheckBox { Content = "라벨 경계에 걸친 객체도 함께 가져오기", IsChecked = false, Margin = new Thickness(0, 2, 0, 0),
                                  ToolTip = "라벨 영역 밖으로 일부가 나간 객체까지 가져옵니다. 보통은 끄는 편이 안전합니다." };
        body.Children.Add(_partial);

        body.Children.Add(new Border
        {
            Background = B("WarnFillBrush"), BorderBrush = B("WarnLineBrush"), BorderThickness = new Thickness(3, 1, 1, 1),
            CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 8, 0, 0),
            Child = new TextBlock
            {
                Text = "지금 서식은 교체됩니다. 되돌리려면 Ctrl+Z 를 누르세요.", FontSize = 12, Foreground = B("WarnBrush"), TextWrapping = TextWrapping.Wrap,
            },
        });
        _pickHint = new TextBlock { Text = "떼어낼 라벨을 하나 고르세요.", FontSize = 12, Foreground = B("FailBrush"), Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
        body.Children.Add(_pickHint);

        var ok = new Button { Content = "떼어내기", MinWidth = 96, Style = S("PrimaryButton") };
        var cancel = new Button { Content = "취소", MinWidth = 84, IsCancel = true, IsDefault = true };
        ok.Click += (_, _) =>
        {
            if (_chosen is null) { _pickHint.Visibility = Visibility.Visible; return; }
            DialogResult = true;
            Close();
        };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        body.Children.Add(buttons);
        Content = body;
        Loaded += (_, _) => cancel.Focus();
    }

    /// <summary>라벨 규격 카드 (이름 · 크기 · 객체 수) — css .pick-item.</summary>
    private Border Card(LabelPreset preset, int n, int partial)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var name = new TextBlock { Text = preset.Name, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = B("InkBrush"), TextTrimming = TextTrimming.CharacterEllipsis };
        var size = new TextBlock { Text = $"{preset.W} × {preset.H} mm", FontSize = 11, Foreground = B("Ink3Brush"), FontFamily = new FontFamily("Consolas"), Margin = new Thickness(8, 0, 0, 0) };
        var count = new TextBlock
        {
            Text = n < 0 ? "객체 수 미확인" : $"{n}개 객체" + (partial > 0 ? $" (+걸친 것 {partial})" : ""),
            FontSize = 11, Foreground = B("Ink3Brush"), Margin = new Thickness(0, 1, 0, 0),
        };
        Grid.SetColumn(name, 0); Grid.SetRow(name, 0);
        Grid.SetColumn(size, 1); Grid.SetRow(size, 0);
        Grid.SetColumn(count, 0); Grid.SetRow(count, 1); Grid.SetColumnSpan(count, 2);
        g.Children.Add(name); g.Children.Add(size); g.Children.Add(count);
        var card = new Border
        {
            Child = g, Tag = preset, Cursor = Cursors.Hand, Background = B("GroundBrush"), BorderBrush = B("LineBrush"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(9, 7, 9, 7), Margin = new Thickness(3),
        };
        card.MouseLeftButtonUp += (_, e) => { e.Handled = true; Choose(preset); };
        card.MouseEnter += (_, _) => { if (!ReferenceEquals(_chosen, preset)) card.BorderBrush = B("AccentBrush"); };
        card.MouseLeave += (_, _) => { if (!ReferenceEquals(_chosen, preset)) card.BorderBrush = B("LineBrush"); };
        return card;
    }

    private void Choose(LabelPreset preset)
    {
        _chosen = preset;
        _pickHint.Visibility = Visibility.Collapsed;
        foreach (var c in _cards)
        {
            var on = ReferenceEquals(c.Tag, preset);
            c.Background = on ? B("AccentSoftBrush") : B("GroundBrush");
            c.BorderBrush = on ? B("AccentBrush") : B("LineBrush");
            c.BorderThickness = new Thickness(on ? 2 : 1);
            c.Padding = on ? new Thickness(8, 6, 8, 6) : new Thickness(9, 7, 9, 7);
        }
    }
}

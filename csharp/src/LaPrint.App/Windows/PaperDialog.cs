// 용지 배치 대화상자 — 용지·방향·여백·간격·정렬·재단선·외곽선·반복, 배치 미리보기 (app.js openPaperDialog).
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LaPrint.Core.Export;
using LaPrint.Core.Model;
using LaPrint.Core.Storage;

namespace LaPrint.App.Windows;

/// <summary>용지 배치 설정. 적용하면 opt 가 바뀐 채 true.</summary>
public sealed class PaperDialog : Window
{
    private readonly LabelSize _label;
    private readonly LayoutOptions _opt;

    private ComboBox _selPaper = null!;
    private TextBox _customW = null!;
    private TextBox _customH = null!;
    private StackPanel _customWrap = null!;
    private ComboBox _selOrient = null!;
    private TextBox _margin = null!;
    private TextBox _gapX = null!;
    private TextBox _gapY = null!;
    private ComboBox _selAlign = null!;
    private ComboBox _selRepeat = null!;
    private CheckBox _cropMarks = null!;
    private CheckBox _outline = null!;
    private Border _out = null!;
    private TextBlock _outText = null!;
    private bool _building = true;

    private static Brush B(string key) => (Brush)Application.Current.FindResource(key);
    private static Style? S(string key) => Application.Current.TryFindResource(key) as Style;

    private PaperDialog(Window owner, LabelSize label, LayoutOptions opt)
    {
        Owner = owner;
        _label = label;
        _opt = opt;
        Title = "용지 배치";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = B("PaperBrush");
        Build();
    }

    /// <summary>대화상자를 띄운다. 적용이면 true (opt 갱신됨), 취소면 false.</summary>
    public static Task<bool> ShowAsync(Window owner, LabelSize label, LayoutOptions opt)
    {
        var w = new PaperDialog(owner, label, opt);
        return Task.FromResult(w.ShowDialog() == true);
    }

    /// <summary>폼(용지 콤보·사용자 지정 W/H·방향·여백·간격·정렬·재단선·외곽선·반복) + 미리보기(Paper.Plan/Describe).</summary>
    private void Build()
    {
        var body = new StackPanel { Margin = new Thickness(18, 16, 18, 14) };
        body.Children.Add(new TextBlock
        {
            Text = "기본은 라벨 실물 크기로 1장씩 출력합니다. 용지를 고르면 같은 라벨을 한 장에 여러 개 앉혀(면付) 인쇄합니다.",
            FontSize = 11, Foreground = B("Ink3Brush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8),
        });

        var form = new Grid();
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _selPaper = new ComboBox { MinWidth = 260, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var p in Paper.Papers) _selPaper.Items.Add(new ComboBoxItem { Content = p.Name, Tag = p.Id });
        MainWindow.SelectByTag(_selPaper, _opt.Paper);
        if (_selPaper.SelectedItem is null) MainWindow.SelectByTag(_selPaper, "label");
        AddRow(form, "용지", _selPaper);

        _customW = NumBox(_opt.CustomW);
        _customH = NumBox(_opt.CustomH);
        _customWrap = new StackPanel { Orientation = Orientation.Horizontal };
        _customWrap.Children.Add(_customW);
        _customWrap.Children.Add(Mini("×"));
        _customWrap.Children.Add(_customH);
        _customWrap.Children.Add(Mini("mm"));
        AddRow(form, "사용자 지정 크기", _customWrap);

        _selOrient = Select(new[] { ("auto", "자동 (많이 들어가는 쪽)"), ("portrait", "세로"), ("landscape", "가로") }, _opt.Orientation, "auto");
        AddRow(form, "방향", _selOrient);

        _margin = NumBox(_opt.MarginMm);
        AddRow(form, "가장자리 여백 (mm)", _margin);

        _gapX = NumBox(_opt.GapX);
        _gapY = NumBox(_opt.GapY);
        var gwrap = new StackPanel { Orientation = Orientation.Horizontal };
        gwrap.Children.Add(Mini("가로"));
        gwrap.Children.Add(_gapX);
        gwrap.Children.Add(Mini("세로"));
        gwrap.Children.Add(_gapY);
        gwrap.Children.Add(Mini("mm"));
        AddRow(form, "라벨 사이 간격", gwrap);

        _selAlign = Select(new[] { ("center", "용지 가운데"), ("start", "왼쪽 위부터") }, _opt.Align == "topleft" ? "start" : _opt.Align, "center");
        AddRow(form, "배치 기준", _selAlign);

        _selRepeat = Select(new[] { ("fill", "한 장을 같은 라벨로 가득 채움"), ("single", "한 장에 1개만") }, _opt.Repeat == "one" ? "single" : _opt.Repeat, "fill");
        _selRepeat.ToolTip = "큐(연속 출력)는 언제나 서로 다른 라벨로 칸을 채웁니다.";
        AddRow(form, "단일 출력 시 반복", _selRepeat, "큐(연속 출력)는 언제나 서로 다른 라벨로 칸을 채웁니다.");

        _cropMarks = new CheckBox { IsChecked = _opt.CropMarks, HorizontalAlignment = HorizontalAlignment.Left };
        AddRow(form, "재단선 표시", _cropMarks);
        _outline = new CheckBox { IsChecked = _opt.Outline, HorizontalAlignment = HorizontalAlignment.Left };
        AddRow(form, "라벨 테두리선 표시", _outline);
        body.Children.Add(form);

        _outText = new TextBlock { FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
        _out = new Border
        {
            BorderThickness = new Thickness(3, 1, 1, 1), CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(0, 8, 0, 0), Child = _outText,
        };
        body.Children.Add(_out);

        // 값이 바뀌면 바로 미리보기
        foreach (var cb in new[] { _selPaper, _selOrient, _selAlign, _selRepeat }) cb.SelectionChanged += (_, _) => Refresh();
        foreach (var tb in new[] { _customW, _customH, _margin, _gapX, _gapY }) tb.TextChanged += (_, _) => Refresh();
        foreach (var ck in new[] { _cropMarks, _outline }) { ck.Checked += (_, _) => Refresh(); ck.Unchecked += (_, _) => Refresh(); }

        var reset = new Button { Content = "라벨 실물 크기로", MinWidth = 120, Style = S("GhostButton"), ToolTip = "용지 없이 라벨 실물 크기로 1장씩 출력" };
        var apply = new Button { Content = "적용", MinWidth = 84, Style = S("PrimaryButton") };
        var cancel = new Button { Content = "취소", MinWidth = 84, IsCancel = true, IsDefault = true };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        reset.Click += (_, _) =>
        {
            CopyInto(new LayoutOptions(), _opt);
            DialogResult = true;
            Close();
        };
        apply.Click += (_, _) =>
        {
            CopyInto(Read(), _opt);
            DialogResult = true;
            Close();
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        buttons.Children.Add(cancel);
        buttons.Children.Add(reset);
        buttons.Children.Add(apply);
        body.Children.Add(buttons);
        Content = body;
        _building = false;
        Refresh();
        Loaded += (_, _) => cancel.Focus();
    }

    /// <summary>입력값을 읽어 LayoutOptions 로.</summary>
    private LayoutOptions Read() => new()
    {
        Paper = MainWindow.TagOf(_selPaper) ?? "label",
        CustomW = Num(_customW, 210, 10, 2000),
        CustomH = Num(_customH, 297, 10, 2000),
        Orientation = MainWindow.TagOf(_selOrient) ?? "auto",
        MarginMm = Num(_margin, 0, 0, 60),
        GapX = Num(_gapX, 0, 0, 50),
        GapY = Num(_gapY, 0, 0, 50),
        Align = MainWindow.TagOf(_selAlign) ?? "center",
        Repeat = MainWindow.TagOf(_selRepeat) ?? "fill",
        CropMarks = _cropMarks.IsChecked == true,
        Outline = _outline.IsChecked == true,
    };

    /// <summary>입력값으로 미리보기 문구를 갱신하고, 라벨 실물 크기면 나머지 칸을 잠근다.</summary>
    private void Refresh()
    {
        if (_building) return;
        var o = Read();
        var direct = o.Paper == "label";
        var custom = o.Paper == "custom";
        _customWrap.Opacity = custom ? 1 : .4;
        _customW.IsEnabled = custom;
        _customH.IsEnabled = custom;
        foreach (var e in new Control[] { _selOrient, _margin, _gapX, _gapY, _selAlign, _selRepeat, _cropMarks, _outline }) e.IsEnabled = !direct;

        var w = Math.Round(_label.W * 10) / 10;
        var h = Math.Round(_label.H * 10) / 10;
        string txt;
        try { txt = Paper.Describe(_label, o); }
        catch (Exception ex)
        {
            AppLog.Warn("배치 설명 실패: " + ex.Message);
            txt = direct ? $"라벨 실물 크기 {_label.W}×{_label.H}mm 로 1장씩 출력" : "배치 계산을 할 수 없습니다: " + ex.Message;
        }
        _outText.Text = $"라벨 {w}×{h}mm — {txt}";
        var level = txt.StartsWith("❌", StringComparison.Ordinal) ? "err" : direct ? "" : "info";
        var (bg, fg, line) = level switch
        {
            "err" => ("FailFillBrush", "FailBrush", "FailBrush"),
            "info" => ("AccentSoftBrush", "InkBrush", "AccentBrush"),
            _ => ("PaperBrush", "Ink2Brush", "LineBrush"),
        };
        _out.Background = B(bg);
        _out.BorderBrush = B(line);
        _outText.Foreground = B(fg);
    }

    private static void CopyInto(LayoutOptions from, LayoutOptions to)
    {
        to.Paper = from.Paper; to.CustomW = from.CustomW; to.CustomH = from.CustomH; to.Orientation = from.Orientation;
        to.MarginMm = from.MarginMm; to.GapX = from.GapX; to.GapY = from.GapY; to.Align = from.Align;
        to.CropMarks = from.CropMarks; to.Outline = from.Outline; to.Repeat = from.Repeat;
    }

    /* ---- 폼 도우미 ---- */

    private static void AddRow(Grid form, string label, FrameworkElement ctl, string? tip = null)
    {
        var r = form.RowDefinitions.Count;
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var l = new TextBlock
        {
            Text = label, FontSize = 12, Foreground = B("Ink2Brush"), VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0), ToolTip = tip,
        };
        Grid.SetRow(l, r); Grid.SetColumn(l, 0);
        Grid.SetRow(ctl, r); Grid.SetColumn(ctl, 1);
        ctl.Margin = new Thickness(0, 3, 0, 3);
        if (ctl is Control c) c.VerticalAlignment = VerticalAlignment.Center;
        form.Children.Add(l);
        form.Children.Add(ctl);
    }

    private static ComboBox Select(IEnumerable<(string Tag, string Text)> items, string value, string fallback)
    {
        var cb = new ComboBox { MinWidth = 260, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var (tag, text) in items) cb.Items.Add(new ComboBoxItem { Content = text, Tag = tag });
        MainWindow.SelectByTag(cb, value);
        if (cb.SelectedItem is null) MainWindow.SelectByTag(cb, fallback);
        return cb;
    }

    private static TextBox NumBox(double v) => new()
    {
        Text = v.ToString(CultureInfo.InvariantCulture), Width = 86, FontFamily = new FontFamily("Consolas"), FontSize = 13,
        TextAlignment = TextAlignment.Right, HorizontalAlignment = HorizontalAlignment.Left,
    };

    private static TextBlock Mini(string t) => new()
    {
        Text = t, FontSize = 12, Foreground = B("Ink3Brush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0),
    };

    private static double Num(TextBox tb, double fallback, double min, double max)
    {
        if (!double.TryParse(tb.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || double.IsNaN(v)) return fallback;
        return Math.Max(min, Math.Min(max, v));
    }
}

// 데이터 매칭 편집기 — 라벨DB 를 표로 펼쳐 놓고 항목별 열을 지정(map 모드)하거나 열 하나를 고른다(pick 모드) (js/mapper.js).
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LaPrint.App.Services;
using LaPrint.Core.Data;

namespace LaPrint.App.Windows;

/// <summary>편집기가 보여줄 라벨DB 원본. Rows 는 표본(앞 N행)이어도 된다.</summary>
public sealed record MapperSource(IReadOnlyList<DbRow> Rows, DbRow? Header, int ColCount, string KeyCol, string Sheet, DbRow? SampleRow);

/// <summary>데이터 매칭 편집기 창. 별도 창(1100×720), 좌 항목 / 우 열 그리드.</summary>
public partial class MapperWindow : Window
{
    private const int PreviewRows = 6;
    private const int MaxCols = 200;
    private const double RowH = 24;
    private const string KeyField = "__KEY__";

    private static readonly Regex NotLetters = new("[^A-Z]", RegexOptions.Compiled);

    /// <summary>"map" | "pick".</summary>
    private readonly string _mode;
    private readonly MapperSource? _src;
    private readonly FieldMap? _map;
    /// <summary>편집 중 초안 매핑 (적용 전).</summary>
    private Dictionary<string, string> Draft { get; set; } = new();
    private string DraftKey { get; set; } = "H";
    private string? SelectedField { get; set; }
    /// <summary>pick 모드 결과.</summary>
    private (string Col, string Value)? Picked { get; set; }

    /// <summary>항목 행 (Border 행 · 열 입력칸 · 표본 값).</summary>
    private readonly Dictionary<string, (Border Row, TextBox Col, TextBlock Val)> _fieldRows = new();
    /// <summary>열 문자 → 그 열의 모든 셀 (머리글 포함).</summary>
    private readonly Dictionary<string, List<Border>> _cellsByCol = new();
    /// <summary>열 문자 → 열 문자 머리글 셀.</summary>
    private readonly Dictionary<string, Border> _colHeads = new();
    private readonly Dictionary<Border, (Brush Bg, Brush Fg)> _cellBase = new();
    private bool _syncingKey;

    private static Brush B(string key) => (Brush)Application.Current.FindResource(key);

    private MapperWindow(Window owner, string mode, MapperSource? src, FieldMap? map, string? hint)
    {
        InitializeComponent();
        Owner = owner;
        _mode = mode;
        _src = src;
        _map = map;
        DraftKey = NormCol(src?.KeyCol ?? map?.KeyCol ?? "H", "H");
        if (map is not null) Draft = new Dictionary<string, string>(map.Cols);

        if (mode == "pick")
        {
            Title = "DB 열 고르기";
            fieldsPane.Visibility = Visibility.Collapsed;
            fieldsCol.Width = new GridLength(0);
            chosenBox.Visibility = Visibility.Visible;
            btnOk.Content = "이 열 사용";
            btnReset.Visibility = Visibility.Collapsed;
            hintText.Text = hint ?? "표에서 열을 클릭하면 그 열이 지정됩니다.";
        }
        else
        {
            var pf = map is not null ? Profiles.Get(map.Profile).Name : "";
            Title = string.IsNullOrEmpty(pf) ? "데이터 매칭 편집기" : $"데이터 매칭 편집기 — {pf}";
            if (!HasSource) hintText.Text = "라벨DB를 먼저 불러오면 실제 값을 보면서 지정할 수 있습니다.";
            BuildFields();
        }
        BuildGrid();
        footText.Text = HasSource
            ? $"시트 \"{_src!.Sheet}\" · {_src.ColCount}개 열 · {_src.Rows.Count:N0}행"
            : "";
        Loaded += (_, _) =>
        {
            if (_mode == "map") SelectField(KeyField);
            else PaintGrid();
        };
    }

    private bool HasSource => _src is not null && _src.Rows.Count > 0;

    /// <summary>매칭 편집기. 적용했으면 true (map 이 바뀌어 있다).</summary>
    public static Task<bool> OpenEditorAsync(Window owner, MapperSource? src, FieldMap map, DbRow? sampleRow = null)
    {
        var w = new MapperWindow(owner, "map", src is null ? null : src with { SampleRow = sampleRow ?? src.SampleRow ?? src.Rows.FirstOrDefault() }, map, null);
        return Task.FromResult(w.ShowDialog() == true);
    }

    /// <summary>열 하나 고르기. 취소면 null. Value 는 표본 행의 그 열 값.</summary>
    public static Task<(string Col, string Value)?> PickColumnAsync(Window owner, MapperSource? src, string? hint = null, DbRow? sampleRow = null)
    {
        var w = new MapperWindow(owner, "pick", src is null ? null : src with { SampleRow = sampleRow ?? src.SampleRow ?? src.Rows.FirstOrDefault() }, null, hint);
        return Task.FromResult(w.ShowDialog() == true ? w.Picked : null);
    }

    /* ---------------- 열 그리드 ---------------- */

    /// <summary>오른쪽 표를 만든다 (열 문자 머리글, 앞 N행) — mapper.js buildGrid.</summary>
    private void BuildGrid()
    {
        grid.Children.Clear();
        grid.RowDefinitions.Clear();
        grid.ColumnDefinitions.Clear();
        gridLabels.Children.Clear();
        gridLabels.RowDefinitions.Clear();
        _cellsByCol.Clear();
        _colHeads.Clear();
        _cellBase.Clear();
        if (!HasSource)
        {
            gridEmpty.Visibility = Visibility.Visible;
            gridScroll.Visibility = Visibility.Collapsed;
            gridLabels.Visibility = Visibility.Collapsed;
            return;
        }
        gridEmpty.Visibility = Visibility.Collapsed;
        gridScroll.Visibility = Visibility.Visible;
        gridLabels.Visibility = Visibility.Visible;
        var src = _src!;
        var n = Math.Min(src.ColCount > 0 ? src.ColCount : 55, MaxCols);

        // 데이터 행 — 현재 선택 품목을 맨 위에
        var sample = new List<DbRow>();
        if (src.SampleRow is not null) sample.Add(src.SampleRow);
        foreach (var r in src.Rows)
        {
            if (sample.Count >= PreviewRows) break;
            if (!ReferenceEquals(r, src.SampleRow)) sample.Add(r);
        }
        var rowCount = 2 + sample.Count;
        for (var i = 0; i < rowCount; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(RowH) });
            gridLabels.RowDefinitions.Add(new RowDefinition { Height = new GridLength(RowH) });
        }
        for (var c = 0; c < n; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // 고정 첫 열
        gridLabels.Children.Add(RowHead("열", 0, true));
        gridLabels.Children.Add(RowHead("머리글", 1, true));
        for (var ri = 0; ri < sample.Count; ri++)
        {
            var isSample = ri == 0 && src.SampleRow is not null;
            gridLabels.Children.Add(RowHead(isSample ? "선택 품목" : $"예시 {ri + (src.SampleRow is not null ? 0 : 1)}", ri + 2, false));
        }

        for (var c = 0; c < n; c++)
        {
            var cn = ColumnName.FromIndex(c);
            var list = new List<Border>();
            _cellsByCol[cn] = list;

            // 1행: 열 문자
            var head = Cell(cn, cn, B("Paper2Brush"), B("Ink2Brush"), bold: true, mono: true, center: true);
            head.ToolTip = $"{cn}열 — 클릭하면 지정합니다";
            Grid.SetRow(head, 0); Grid.SetColumn(head, c);
            grid.Children.Add(head);
            list.Add(head);
            _colHeads[cn] = head;

            // 2행: DB 머리글
            var ht = src.Header?.Get(cn) ?? "";
            var hd = Cell(cn, ht, B("PaperBrush"), B("Ink2Brush"), bold: false, mono: false, center: false, small: true);
            hd.ToolTip = ht.Length > 0 ? ht : $"{cn}열 (머리글 없음)";
            Grid.SetRow(hd, 1); Grid.SetColumn(hd, c);
            grid.Children.Add(hd);
            list.Add(hd);

            // 3행~: 표본 값
            for (var ri = 0; ri < sample.Count; ri++)
            {
                var isSample = ri == 0 && src.SampleRow is not null;
                var v = sample[ri].Get(cn);
                var td = Cell(cn, v, isSample ? B("AccentSoftBrush") : B("GroundBrush"), isSample ? B("InkBrush") : B("Ink2Brush"),
                              bold: false, mono: false, center: false);
                if (isSample) ((TextBlock)td.Child).FontWeight = FontWeights.Medium;
                if (v.Length > 0) td.ToolTip = v;
                Grid.SetRow(td, ri + 2); Grid.SetColumn(td, c);
                grid.Children.Add(td);
                list.Add(td);
            }
        }
    }

    private Border RowHead(string text, int row, bool top)
    {
        var b = new Border
        {
            Background = B("Paper2Brush"), BorderBrush = B("LineBrush"), BorderThickness = new Thickness(0, 0, 1, 1),
            Padding = new Thickness(6, 0, 6, 0), MinWidth = 64,
            Child = new TextBlock
            {
                Text = text, FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = B("Ink3Brush"),
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Grid.SetRow(b, row);
        return b;
    }

    /// <summary>표 셀 하나 — 클릭하면 그 열을 지정한다.</summary>
    private Border Cell(string col, string text, Brush bg, Brush fg, bool bold, bool mono, bool center, bool small = false)
    {
        var tb = new TextBlock
        {
            Text = text, FontSize = small ? 10.5 : 11, Foreground = fg, TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            TextAlignment = center ? TextAlignment.Center : TextAlignment.Left, MaxWidth = 160,
        };
        if (mono) tb.FontFamily = new FontFamily("Consolas");
        var b = new Border
        {
            Background = bg, BorderBrush = B("Line2Brush"), BorderThickness = new Thickness(0, 0, 1, 1),
            Padding = new Thickness(6, 0, 6, 0), MinWidth = 40, Tag = col, Cursor = Cursors.Hand, Child = tb,
        };
        b.MouseLeftButtonUp += OnGridClick;
        _cellBase[b] = (bg, fg);
        return b;
    }

    /// <summary>지정된 열·선택 열을 색으로 표시 — mapper.js paintGrid.</summary>
    private void PaintGrid()
    {
        if (_cellsByCol.Count == 0) return;
        var used = new Dictionary<string, List<string>>();
        foreach (var kv in Draft)
        {
            if (string.IsNullOrEmpty(kv.Value)) continue;
            if (!used.TryGetValue(kv.Value, out var l)) used[kv.Value] = l = new List<string>();
            l.Add(kv.Key);
        }
        var cur = _mode == "pick" ? Picked?.Col
                : SelectedField is null ? null
                : SelectedField == KeyField ? DraftKey
                : Draft.TryGetValue(SelectedField, out var dc) ? dc : null;
        if (string.IsNullOrEmpty(cur)) cur = null;

        foreach (var (col, cells) in _cellsByCol)
        {
            var isUsed = used.ContainsKey(col);
            var isCur = cur == col;
            var isKey = col == DraftKey;
            foreach (var cell in cells)
            {
                var (bg, fg) = _cellBase[cell];
                var tb = (TextBlock)cell.Child;
                if (isCur)
                {
                    cell.Background = B("AccentBrush");
                    tb.Foreground = Brushes.White;
                    cell.BorderBrush = B("AccentDeepBrush");
                }
                else if (isUsed)
                {
                    cell.Background = B("WarnFillBrush");
                    tb.Foreground = fg;
                    cell.BorderBrush = B("Line2Brush");
                }
                else
                {
                    cell.Background = bg;
                    tb.Foreground = fg;
                    cell.BorderBrush = B("Line2Brush");
                }
                cell.BorderThickness = new Thickness(0, isKey ? 2 : 0, 1, 1);
                if (isKey) cell.BorderBrush = B("PassBrush");
            }
            if (_colHeads.TryGetValue(col, out var head))
            {
                var names = used.TryGetValue(col, out var f) ? string.Join(", ", f.Select(k => FieldMap.Labels.TryGetValue(k, out var lb) ? lb : k)) : $"{col}열";
                head.ToolTip = (isKey ? "품목번호 조회 키\n" : "") + names;
            }
        }
        // 현재 열로 가로 스크롤
        if (cur is not null && _colHeads.TryGetValue(cur, out var target)) ScrollToColumn(target);
    }

    private void ScrollToColumn(Border head)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                if (!head.IsLoaded || !grid.IsLoaded) return;
                var x = head.TransformToAncestor(grid).Transform(new Point(0, 0)).X;
                var left = gridScroll.HorizontalOffset;
                var vw = gridScroll.ViewportWidth;
                if (x >= left && x + head.ActualWidth <= left + vw) return;
                gridScroll.ScrollToHorizontalOffset(Math.Max(0, x - vw / 2 + head.ActualWidth / 2));
            }
            catch (InvalidOperationException) { /* 아직 배치 전 */ }
        }), DispatcherPriority.Loaded);
    }

    /// <summary>선택한 항목(또는 pick 결과)에 열을 지정 — mapper.js assign.</summary>
    private void Assign(string colName)
    {
        if (_mode == "pick")
        {
            var value = _src?.SampleRow?.Get(colName) ?? "";
            Picked = (colName, value);
            var hd = _src?.Header?.Get(colName) ?? "";
            chosenText.Inlines.Clear();
            chosenText.Inlines.Add(new System.Windows.Documents.Run($"{colName}열") { FontWeight = FontWeights.Bold });
            chosenText.Inlines.Add((hd.Length > 0 ? $" — {hd}" : "") + (value.Length > 0 ? $"  ▸ {value}" : "  ▸ (값 없음)"));
            chosenText.Foreground = B("InkBrush");
            chosenBox.BorderBrush = B("AccentBrush");
            chosenBox.Background = B("AccentSoftBrush");
            PaintGrid();
            return;
        }
        if (SelectedField is null)
        {
            Toast("왼쪽에서 항목을 먼저 고르세요.", ToastLevel.Warn);
            return;
        }
        if (SelectedField == KeyField) DraftKey = colName;
        else Draft[SelectedField] = colName;
        RefreshFields();
        PaintGrid();
    }

    /* ---------------- 필드 목록 ---------------- */

    /// <summary>왼쪽 항목 목록을 그룹별로 만든다 — mapper.js buildFields.</summary>
    private void BuildFields()
    {
        fieldsList.Items.Clear();
        _fieldRows.Clear();
        _syncingKey = true;
        inpKeyCol.Text = DraftKey;
        _syncingKey = false;
        foreach (var (group, keys) in FieldMap.Groups)
        {
            var gd = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
            gd.Children.Add(new TextBlock
            {
                Text = group, FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = B("Ink3Brush"),
                Background = B("PaperBrush"), Padding = new Thickness(7, 4, 7, 4), Margin = new Thickness(0, 0, 0, 2),
            });
            foreach (var k in keys) gd.Children.Add(FieldRow(k, FieldMap.Labels.TryGetValue(k, out var lb) ? lb : k));
            fieldsList.Items.Add(gd);
        }
        RefreshFields();
    }

    /// <summary>항목 한 줄: 이름 · 열 입력칸(3자) · 표본 값 — mapper.js fieldRow.</summary>
    private Border FieldRow(string key, string label)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
        var nm = new TextBlock { Text = label, FontSize = 11.5, Foreground = B("InkBrush"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = label };
        var col = new TextBox
        {
            Width = 46, MaxLength = 3, FontFamily = new FontFamily("Consolas"), FontSize = 11.5, FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center, CharacterCasing = CharacterCasing.Upper, Margin = new Thickness(3, 0, 3, 0), Padding = new Thickness(1, 2, 1, 2),
            Text = Draft.TryGetValue(key, out var v0) ? v0 : "",
        };
        var val = new TextBlock { FontSize = 11, Foreground = B("Ink2Brush"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(6, 0, 0, 0) };
        Grid.SetColumn(nm, 0); Grid.SetColumn(col, 1); Grid.SetColumn(val, 2);
        g.Children.Add(nm); g.Children.Add(col); g.Children.Add(val);
        var row = new Border { Child = g, CornerRadius = new CornerRadius(5), Padding = new Thickness(6, 3, 6, 3), BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent, Cursor = Cursors.Hand, Tag = key };
        _fieldRows[key] = (row, col, val);

        col.TextChanged += (_, _) =>
        {
            var v = NotLetters.Replace((col.Text ?? "").Trim().ToUpperInvariant(), "");
            if (v != col.Text) { var caret = col.SelectionStart; col.Text = v; col.SelectionStart = Math.Min(caret, v.Length); return; }
            Draft[key] = v;
            UpdateRowValue(key);
            PaintGrid();
        };
        col.GotKeyboardFocus += (_, _) => SelectField(key);
        row.MouseLeftButtonUp += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && MainWindow.FindAncestor<TextBox>(d) == col) return;
            SelectField(key);
            col.Focus();
        };
        row.MouseEnter += (_, _) => { if (SelectedField != key) row.Background = B("PaperBrush"); };
        row.MouseLeave += (_, _) => { if (SelectedField != key) row.Background = Brushes.Transparent; };
        UpdateRowValue(key);
        return row;
    }

    /// <summary>항목의 표본 값 문구 — mapper.js updateRowValue.</summary>
    private void UpdateRowValue(string key)
    {
        var isKey = key == KeyField;
        var c = isKey ? DraftKey : (Draft.TryGetValue(key, out var d) ? d : "");
        var val = isKey ? keyColSample : (_fieldRows.TryGetValue(key, out var fr) ? fr.Val : null);
        if (val is null) return;
        if (string.IsNullOrEmpty(c))
        {
            val.Text = "(사용 안 함)";
            val.Foreground = B("Ink3Brush");
            val.FontStyle = FontStyles.Italic;
            val.ToolTip = null;
            return;
        }
        var v = _src?.SampleRow?.Get(c) ?? "";
        val.Text = v.Length > 0 ? v : "(값 없음)";
        val.Foreground = v.Length > 0 ? B("Ink2Brush") : B("Ink3Brush");
        val.FontStyle = v.Length > 0 ? FontStyles.Normal : FontStyles.Italic;
        val.ToolTip = v.Length > 0 ? v : null;
    }

    private void SelectField(string key)
    {
        SelectedField = key;
        foreach (var (k, fr) in _fieldRows)
        {
            var sel = k == key;
            fr.Row.Background = sel ? B("AccentSoftBrush") : Brushes.Transparent;
            fr.Row.BorderBrush = sel ? B("AccentBrush") : Brushes.Transparent;
        }
        var keySel = key == KeyField;
        keyRow.Background = keySel ? B("AccentSoftBrush") : Brushes.Transparent;
        keyRow.BorderBrush = keySel ? B("AccentBrush") : Brushes.Transparent;
        PaintGrid();
    }

    /// <summary>항목 행의 표본 값을 갱신 — mapper.js refreshFields.</summary>
    private void RefreshFields()
    {
        foreach (var (k, fr) in _fieldRows)
        {
            var want = Draft.TryGetValue(k, out var d) ? d : "";
            if (fr.Col.Text != want) fr.Col.Text = want;
            UpdateRowValue(k);
        }
        if (inpKeyCol.Text != DraftKey)
        {
            _syncingKey = true;
            inpKeyCol.Text = DraftKey;
            _syncingKey = false;
        }
        UpdateRowValue(KeyField);
    }

    /// <summary>초안을 FieldMap 에 적용 — mapper.js persist. (프로필별 설정 저장은 호출자가 Map.Diff()/KeyCol 로 한다.)</summary>
    private void Persist()
    {
        if (_map is null) return;
        _map.Apply(Draft);
        _map.KeyCol = NormCol(DraftKey, "H");
    }

    private static string NormCol(string? v, string fallback)
    {
        var s = NotLetters.Replace((v ?? "").Trim().ToUpperInvariant(), "");
        return s.Length == 0 ? fallback : s;
    }

    private void Toast(string msg, ToastLevel level)
    {
        Window? w = this;
        while (w is not null && w is not MainWindow) w = w.Owner;
        Dialogs.Toast(w ?? Owner, msg, level);
    }

    /* ---------------- 이벤트 ---------------- */

    /// <summary>표 셀 클릭 → 그 열을 지정.</summary>
    private void OnGridClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border { Tag: string col } && col.Length > 0)
        {
            e.Handled = true;
            Assign(col);
        }
    }

    private void OnKeyColChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingKey) return;
        var v = NotLetters.Replace((inpKeyCol.Text ?? "").Trim().ToUpperInvariant(), "");
        if (v != inpKeyCol.Text)
        {
            var caret = inpKeyCol.SelectionStart;
            _syncingKey = true;
            inpKeyCol.Text = v;
            _syncingKey = false;
            inpKeyCol.SelectionStart = Math.Min(caret, v.Length);
        }
        DraftKey = v.Length > 0 ? v : "H";
        UpdateRowValue(KeyField);
        PaintGrid();
    }

    private void OnKeyColFocus(object sender, KeyboardFocusChangedEventArgs e) => SelectField(KeyField);

    private void OnKeyRowClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && MainWindow.FindAncestor<TextBox>(d) == inpKeyCol) return;
        SelectField(KeyField);
        inpKeyCol.Focus();
    }

    /// <summary>프로필 기본값으로 되돌리고 바로 적용한다 (mapper.js 'reset').</summary>
    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        if (_map is null) { DialogResult = false; Close(); return; }
        _map.Reset();
        _map.KeyCol = "H";
        Toast("열 매칭을 기본값으로 되돌렸습니다.", ToastLevel.Ok);
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        if (_mode == "pick")
        {
            if (Picked is null)
            {
                chosenText.Text = "표에서 열을 먼저 클릭하세요.";
                chosenText.Foreground = B("WarnBrush");
                chosenBox.BorderBrush = B("WarnLineBrush");
                chosenBox.Background = B("WarnFillBrush");
                return;
            }
            DialogResult = true;
            Close();
            return;
        }
        Persist();
        Toast("열 매칭을 저장했습니다.", ToastLevel.Ok);
        DialogResult = true;
        Close();
    }
}

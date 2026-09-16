// 설정 창 — 왼쪽 탐색(10 페이지) + 오른쪽 페이지. 설정은 바꾸는 즉시 AppSettings 에 반영되고 600ms 뒤(또는 닫을 때) 저장한다
// (app.js openSettings/buildSettingsPages/buildMappingPage/buildLayoutPage + 설정 컨트롤 헬퍼).
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using LaPrint.App.Services;
using LaPrint.Core.Data;
using LaPrint.Core.Export;
using LaPrint.Core.Storage;
using LaPrint.Core.Zpl;

namespace LaPrint.App.Windows;

/// <summary>설정 창. 1000×820 CenterOwner, 작업 표시줄에 나타나지 않는다.</summary>
public partial class SettingsWindow : Window
{
    /// <summary>탐색 페이지 (app.js PAGES 와 같은 키·문구).</summary>
    public static readonly IReadOnlyList<(string Key, string Title)> Pages = new (string, string)[]
    {
        ("paths", "폴더 경로"), ("mapping", "데이터 매칭"), ("layout", "라벨 · 용지"),
        ("output", "출력"), ("printer", "ZEBRA 프린터"), ("imaging", "이미지"),
        ("validation", "검증 규칙"), ("ui", "화면"), ("history", "출력 이력"), ("about", "정보"),
    };

    /// <summary>탐색 목록 항목.</summary>
    public sealed record NavItem(string Key, string Title);

    /// <summary>출력 이력 표의 한 줄.</summary>
    public sealed record HistoryRowView(string At, string Item, string Lot, string Sn, int Copies, string Result, bool Ok);

    private readonly AppSettings _settings;
    private readonly SettingsStore _store;
    private readonly HistoryStore? _history;
    private readonly Dictionary<string, FrameworkElement> _pages = new();
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private bool _needSave;

    public SettingsWindow(Window owner, AppSettings settings, SettingsStore store, HistoryStore? history, string? page)
    {
        InitializeComponent();
        Owner = owner;
        _settings = settings;
        _store = store;
        _history = history;
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveNow(); };
        Closing += (_, _) => { _saveTimer.Stop(); SaveNow(); };
        nav.ItemsSource = Pages.Select(p => new NavItem(p.Key, p.Title)).ToList();
        nav.SelectedValue = Pages.Any(p => p.Key == page) ? page : "paths";
    }

    /// <summary>설정 창을 띄운다. 닫힐 때 저장했으면 true.</summary>
    public static Task<bool> ShowAsync(Window owner, AppSettings settings, SettingsStore store, HistoryStore? history = null, string? page = null)
    {
        var w = new SettingsWindow(owner, settings, store, history, page);
        return Task.FromResult(w.ShowDialog() == true);
    }

    /// <summary>바뀐 값이 있었는지 (닫을 때 저장 여부).</summary>
    public bool Dirty { get; private set; }

    /// <summary>메인 창 (설정이 메인 창의 동작을 부를 때). 다른 창이 열었으면 null.</summary>
    private MainWindow? Main => Owner as MainWindow;

    /* ================= 헬퍼 (app.js settingRow/selectCtl/textCtl/numCtl/checkCtl) ================= */

    private static Brush B(string key) => (Brush)Application.Current.FindResource(key);
    private static Style? S(string key) => Application.Current.TryFindResource(key) as Style;

    private static TextBlock H3(string text) => new() { Text = text, FontSize = 18, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 10) };

    private static TextBlock Hint(string text, string? brush = null, Thickness? margin = null) => new()
    {
        Text = text, FontSize = 11, Foreground = B(brush ?? "Ink3Brush"), TextWrapping = TextWrapping.Wrap,
        Margin = margin ?? new Thickness(0, 0, 0, 10),
    };

    private static HeaderedContentControl GBox(string legend, UIElement content)
        => new() { Style = S("GBox"), Header = legend, Content = content, Margin = new Thickness(0, 0, 0, 10) };

    private static Border Callout(string text, string level = "")
    {
        var (bg, fg, line) = level switch
        {
            "err" => ("FailFillBrush", "FailBrush", "FailBrush"),
            "warn" => ("WarnFillBrush", "WarnBrush", "WarnLineBrush"),
            "info" => ("AccentSoftBrush", "AccentDeepBrush", "AccentBrush"),
            _ => ("PaperBrush", "InkBrush", "LineBrush"),
        };
        return new Border
        {
            Background = B(bg), BorderBrush = B(line), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 6, 10, 6),
            Child = new TextBlock { Text = text, Foreground = B(fg), FontSize = 12.5, TextWrapping = TextWrapping.Wrap },
        };
    }

    private static void SetCallout(Border b, string text, string level = "")
    {
        var n = Callout(text, level);
        b.Background = n.Background; b.BorderBrush = n.BorderBrush;
        b.Child = n.Child;
    }

    private static StackPanel Row(params UIElement[] items)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        foreach (var it in items) p.Children.Add(it);
        return p;
    }

    private static Button Btn(string text, string style = "SmallButton") => new() { Content = text, Style = S(style) };

    /// <summary>설정 항목 한 줄 (라벨 + 컨트롤) — app.js settingRow.</summary>
    private static FrameworkElement SettingRow(string label, FrameworkElement ctl)
    {
        var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var l = new TextBlock { Text = label, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        g.Children.Add(l);
        Grid.SetColumn(ctl, 1);
        ctl.HorizontalAlignment = HorizontalAlignment.Left;
        ctl.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(ctl);
        return g;
    }

    private ComboBox SelectCtl<T>(T current, IEnumerable<(T Value, string Text)> opts, Action<T> set, Action? after = null) where T : notnull
    {
        var cb = new ComboBox { MinWidth = 200 };
        foreach (var (v, t) in opts) cb.Items.Add(new ComboBoxItem { Content = t, Tag = v });
        foreach (var it in cb.Items)
            if (it is ComboBoxItem ci && Equals(ci.Tag, current)) { cb.SelectedItem = ci; break; }
        cb.SelectionChanged += (_, _) =>
        {
            if (cb.SelectedItem is ComboBoxItem ci && ci.Tag is T v) { set(v); MarkDirty(); after?.Invoke(); }
        };
        return cb;
    }

    private TextBox TextCtl(string current, string placeholder, Action<string> set, double width = 260)
    {
        var tb = new TextBox { Text = current, Width = width, ToolTip = placeholder.Length > 0 ? placeholder : null };
        tb.TextChanged += (_, _) => { set(tb.Text); MarkDirty(); };
        return tb;
    }

    /// <summary>숫자 칸. nullable 이면 비워 둘 수 있다 (프린터 농도·속도처럼 '프린터 설정 유지').</summary>
    private TextBox NumCtl(double? current, double min, double max, Action<double?> set, Action? after = null, bool nullable = false)
    {
        var tb = new TextBox { Width = 80, Text = current is { } c ? c.ToString(CultureInfo.InvariantCulture) : "" };
        void Commit()
        {
            var s = tb.Text.Trim();
            if (s.Length == 0)
            {
                if (!nullable) { tb.Text = (current ?? min).ToString(CultureInfo.InvariantCulture); return; }
                set(null); MarkDirty(); after?.Invoke(); return;
            }
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) { tb.Text = current is { } c2 ? c2.ToString(CultureInfo.InvariantCulture) : ""; return; }
            v = Math.Clamp(v, min, max);
            tb.Text = v.ToString(CultureInfo.InvariantCulture);
            current = v;
            set(v); MarkDirty(); after?.Invoke();
        }
        tb.LostFocus += (_, _) => Commit();
        tb.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) { Commit(); e.Handled = true; } };
        return tb;
    }

    private CheckBox CheckCtl(bool current, Action<bool> set, Action? after = null, string content = "")
    {
        var c = new CheckBox { IsChecked = current, Content = content.Length > 0 ? content : null, VerticalAlignment = VerticalAlignment.Center };
        c.Checked += (_, _) => { set(true); MarkDirty(); after?.Invoke(); };
        c.Unchecked += (_, _) => { set(false); MarkDirty(); after?.Invoke(); };
        return c;
    }

    private void MarkDirty()
    {
        Dirty = true;
        _needSave = true;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>바뀐 것이 있으면 지금 저장 (SettingsStore.Changed 로 메인 창이 곧바로 반영한다).</summary>
    private void SaveNow()
    {
        if (!_needSave) return;
        _needSave = false;
        try { _store.Save(_settings); }
        catch (Exception ex)
        {
            AppLog.Error("설정 저장 실패", ex);
            Dialogs.Toast(this, "설정 저장 실패: " + ex.Message, ToastLevel.Err);
        }
    }

    private void Toast(string msg, ToastLevel level = ToastLevel.Info) => Dialogs.Toast(Owner, msg, level);

    /* ================= 페이지 ================= */

    /// <summary>페이지 하나를 만든다 (app.js buildSettingsPages 의 해당 부분).</summary>
    private FrameworkElement BuildPage(string key) => key switch
    {
        "paths" => BuildPathsPage(),
        "mapping" => BuildMappingPage(),
        "layout" => BuildLayoutPage(),
        "output" => BuildOutputPage(),
        "printer" => BuildPrinterPage(),
        "imaging" => BuildImagingPage(),
        "validation" => BuildValidationPage(),
        "ui" => BuildUiPage(),
        "history" => BuildHistoryPage(),
        "about" => BuildAboutPage(),
        _ => new TextBlock { Text = "없는 페이지: " + key },
    };

    /// <summary>키로 페이지를 보인다 (한 번 만든 페이지는 캐시).</summary>
    private void SelectPage(string key)
    {
        if (!_pages.TryGetValue(key, out var page))
        {
            try { page = BuildPage(key); }
            catch (Exception ex)
            {
                AppLog.Error("설정 페이지 만들기 실패: " + key, ex);
                page = new TextBlock { Text = "페이지를 열지 못했습니다: " + ex.Message, Foreground = B("FailBrush"), TextWrapping = TextWrapping.Wrap };
            }
            _pages[key] = page;
        }
        pageHost.Content = page;
    }

    /// <summary>만들어 둔 페이지를 버리고 다시 그린다 (초기화·가져오기 뒤).</summary>
    private void RebuildPages()
    {
        _pages.Clear();
        SelectPage(nav.SelectedValue as string ?? "paths");
    }

    private void OnNavSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (nav.SelectedValue is string key) SelectPage(key);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        _saveTimer.Stop();
        SaveNow();
        DialogResult = Dirty;
        Close();
    }

    /* --- 폴더 경로 --- */

    private FrameworkElement BuildPathsPage()
    {
        var p = new StackPanel();
        p.Children.Add(H3("폴더 경로"));
        p.Children.Add(Hint("네트워크 폴더는 \\\\서버\\공유 경로를 그대로 쓰거나, Windows에서 드라이브 문자로 연결(예: Z:)한 뒤 그 드라이브를 골라도 됩니다."));

        /* 라벨DB */
        var g1 = new StackPanel();
        ComboBox fsel = new() { MinWidth = 260 }, bsel = new() { MinWidth = 260 };
        // 폴더 목록은 네트워크 공유에서 오래 걸릴 수 있다 — 스레드 풀에서 읽고 결과가 최신 폴더일 때만 반영
        async void RefreshDbFileSelect()
        {
            var dir = _settings.Paths.DbDir;
            List<string>? files = null;
            if (!string.IsNullOrWhiteSpace(dir))
            {
                foreach (var sel in new[] { fsel, bsel })
                {
                    sel.Items.Clear();
                    sel.Items.Add(new ComboBoxItem { Content = "(폴더를 읽는 중…)", Tag = "" });
                    sel.SelectedIndex = 0;
                    sel.IsEnabled = false;
                }
                try
                {
                    files = await Task.Run(() => !Directory.Exists(dir) ? null
                        : Directory.EnumerateFiles(dir)
                            .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".xlsx" or ".xlsm" or ".xls" or ".csv")
                            .Select(f => Path.GetFileName(f)).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList());
                }
                catch (Exception ex) { AppLog.Warn("DB 폴더 목록 읽기 실패: " + ex.Message); }
                if (dir != _settings.Paths.DbDir) return;      // 읽는 동안 폴더가 바뀌었다 — 새 호출이 채운다
            }
            foreach (var (sel, cur) in new[] { (fsel, _settings.Paths.DbFileName), (bsel, _settings.Paths.DbFileNameBsc) })
            {
                sel.Items.Clear();
                if (files is null)
                {
                    sel.Items.Add(new ComboBoxItem { Content = string.IsNullOrWhiteSpace(dir) ? "(폴더를 먼저 지정하세요)" : "(폴더를 찾을 수 없습니다)", Tag = "" });
                    sel.SelectedIndex = 0;
                    sel.IsEnabled = false;
                    continue;
                }
                sel.IsEnabled = true;
                sel.Items.Add(new ComboBoxItem { Content = "(선택 안 함)", Tag = "" });
                foreach (var f in files) sel.Items.Add(new ComboBoxItem { Content = f, Tag = f });
                if (!string.IsNullOrEmpty(cur) && !files.Contains(cur)) sel.Items.Add(new ComboBoxItem { Content = cur + " (없음)", Tag = cur });
                MainWindow.SelectByTag(sel, cur ?? "");
                if (sel.SelectedItem is null) sel.SelectedIndex = 0;
            }
        }
        g1.Children.Add(PathRow("라벨DB 폴더", "지정되지 않음", () => _settings.Paths.DbDir, v => _settings.Paths.DbDir = v, RefreshDbFileSelect));

        var freload = Btn("지금 읽기");
        var breload = Btn("지금 읽기");
        var fl = new TextBlock { Text = "일반 DB", Width = 86, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
        var bl = new TextBlock
        {
            Text = "BSC 일본 DB", Width = 86, VerticalAlignment = VerticalAlignment.Center, FontSize = 12,
            ToolTip = "BSC 출고(일본)용 라벨DB. 작업 입력의 [출고 구분] 을 BSC로 바꾸면 이 파일을 씁니다.",
        };
        g1.Children.Add(Row(fl, fsel, freload));
        g1.Children.Add(Row(bl, bsel, breload));
        g1.Children.Add(Hint("BSC DB는 AL열부터 열 구성이 달라 열 매칭을 따로 둡니다 (설정 › 데이터 매칭).", margin: new Thickness(92, 4, 0, 0)));
        fsel.SelectionChanged += (_, _) => { if (fsel.IsEnabled && MainWindow.TagOf(fsel) is { } v && v != _settings.Paths.DbFileName) { _settings.Paths.DbFileName = v; _settings.Paths.DbLastModified = null; MarkDirty(); } };
        bsel.SelectionChanged += (_, _) => { if (bsel.IsEnabled && MainWindow.TagOf(bsel) is { } v && v != _settings.Paths.DbFileNameBsc) { _settings.Paths.DbFileNameBsc = v; _settings.Paths.DbLastModifiedBsc = null; MarkDirty(); } };
        freload.Click += (_, _) => RunMain(m => m.SettingsReloadDbAsync("general", this));
        breload.Click += (_, _) => RunMain(m => m.SettingsReloadDbAsync("bsc", this));
        RefreshDbFileSelect();

        var auto = CheckCtl(_settings.Paths.DbAutoLoad, v => _settings.Paths.DbAutoLoad = v, content: "프로그램을 열 때 자동으로 읽기 (파일이 바뀌었으면 다시 읽음)");
        auto.Margin = new Thickness(0, 8, 0, 0);
        g1.Children.Add(auto);

        var mb = Btn("파일에서 직접 불러오기…");
        mb.Click += (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = "라벨DB 파일", Filter = "엑셀·CSV (*.xlsx;*.xlsm;*.xls;*.csv)|*.xlsx;*.xlsm;*.xls;*.csv|모든 파일 (*.*)|*.*" };
            if (!string.IsNullOrWhiteSpace(_settings.Paths.DbDir) && Directory.Exists(_settings.Paths.DbDir)) dlg.InitialDirectory = _settings.Paths.DbDir;
            if (dlg.ShowDialog(this) != true) return;
            RunMain(m => m.SettingsLoadDbFileAsync(dlg.FileName));
        };
        var sb = Btn("샘플 DB 불러오기", "SmallGhostButton");
        sb.Click += (_, _) => RunMain(m => m.SettingsLoadSampleAsync());
        g1.Children.Add(Row(mb, sb));
        p.Children.Add(GBox("라벨DB", g1));

        /* 그 밖의 폴더 */
        var g2 = new StackPanel();
        g2.Children.Add(PathRow("이미지 폴더", "지정되지 않음 — 제품 그림이 표시되지 않습니다", () => _settings.Paths.ImgDir, v => _settings.Paths.ImgDir = v,
            () => Main?.SettingsImagesChanged()));
        g2.Children.Add(PathRow("PDF 저장 폴더", "지정되지 않음 — 다운로드 폴더로 저장됩니다", () => _settings.Paths.OutDir, v => _settings.Paths.OutDir = v, null));
        var chk = Btn("이미지 폴더 점검", "SmallGhostButton");
        chk.Click += (_, _) => CheckImageFolder();
        g2.Children.Add(Row(chk));
        p.Children.Add(GBox("그 밖의 폴더", g2));
        return p;
    }

    /// <summary>폴더 한 줄: 점 · 이름/경로 · [폴더 선택…] [해제] (app.js mkPath).</summary>
    private FrameworkElement PathRow(string label, string desc, Func<string> get, Action<string> set, Action? after)
    {
        var g = new Grid { Margin = new Thickness(0, 2, 0, 6) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var dot = new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(dot);
        var info = new StackPanel { Margin = new Thickness(4, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        var nm = new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, FontSize = 13 };
        var pv = new TextBlock { FontSize = 11.5, Foreground = B("Ink3Brush"), TextTrimming = TextTrimming.CharacterEllipsis };
        info.Children.Add(nm); info.Children.Add(pv);
        Grid.SetColumn(info, 1);
        g.Children.Add(info);
        var pick = Btn("폴더 선택…");
        Grid.SetColumn(pick, 2);
        g.Children.Add(pick);
        var clr = Btn("해제", "SmallGhostButton");
        Grid.SetColumn(clr, 3);
        g.Children.Add(clr);

        async void Sync()
        {
            var v = get();
            var has = !string.IsNullOrWhiteSpace(v);
            pv.Text = has ? v : desc;
            pv.ToolTip = has ? v : null;
            pv.Foreground = B("Ink3Brush");
            dot.Fill = B("Ink3Brush");
            clr.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
            if (!has) return;
            // 끊긴 네트워크 공유의 Directory.Exists 는 SMB 시간 초과만큼 걸린다 — 창이 멎지 않게 뒤에서 확인
            bool exists;
            try { exists = await Task.Run(() => Directory.Exists(v)); }
            catch { exists = false; }
            if (get() != v) return;
            dot.Fill = exists ? B("PassBrush") : B("WarnLineBrush");
            pv.Foreground = exists ? B("Ink3Brush") : B("WarnBrush");
            if (!exists) pv.Text = v + " — 지금은 찾을 수 없습니다 (네트워크 연결 확인)";
        }
        pick.Click += (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = label, Multiselect = false };
            var cur = get();
            if (!string.IsNullOrWhiteSpace(cur) && Directory.Exists(cur)) dlg.InitialDirectory = cur;
            if (dlg.ShowDialog(this) != true) return;
            set(dlg.FolderName);
            MarkDirty();
            Toast($"{label}: {Path.GetFileName(dlg.FolderName.TrimEnd('\\', '/'))}", ToastLevel.Ok);
            after?.Invoke();
            Sync();
        };
        clr.Click += (_, _) => { set(""); MarkDirty(); after?.Invoke(); Sync(); };
        Sync();
        return g;
    }

    /// <summary>[이미지 폴더 점검] — 폴더의 그림 수 · DB 가 참조하는 파일 수 · 없는 파일 목록.</summary>
    private void CheckImageFolder()
    {
        var r = Main?.CheckImageFolder();
        if (r is null) { Toast("이미지 폴더가 없거나 권한이 없습니다.", ToastLevel.Warn); return; }
        var (inFolder, needed, missing) = r.Value;
        var body = new StackPanel();
        var head = new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap };
        head.Inlines.Add("폴더의 이미지 ");
        head.Inlines.Add(new System.Windows.Documents.Run(inFolder.ToString("N0")) { FontWeight = FontWeights.Bold });
        head.Inlines.Add("개 · DB가 참조하는 파일 ");
        head.Inlines.Add(new System.Windows.Documents.Run(needed.ToString("N0")) { FontWeight = FontWeights.Bold });
        head.Inlines.Add("개");
        body.Children.Add(head);
        if (missing.Count > 0)
        {
            body.Children.Add(new TextBlock { Text = $"없는 파일 {missing.Count}개", Foreground = B("FailBrush"), FontSize = 12, Margin = new Thickness(0, 8, 0, 4) });
            body.Children.Add(new Border
            {
                Background = B("PaperBrush"), CornerRadius = new CornerRadius(6), Padding = new Thickness(8), MaxHeight = 220,
                Child = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = new TextBlock { Text = string.Join("\n", missing.Take(200)), FontFamily = new FontFamily("Consolas"), FontSize = 11, TextWrapping = TextWrapping.Wrap },
                },
            });
        }
        else body.Children.Add(new TextBlock { Text = "DB가 참조하는 파일이 모두 있습니다.", Foreground = B("PassBrush"), FontSize = 12, Margin = new Thickness(0, 8, 0, 0) });
        Dialogs.Info(this, "이미지 폴더 점검", body);
    }

    /* --- 데이터 매칭 --- */

    private FrameworkElement BuildMappingPage()
    {
        var p = new StackPanel();
        p.Children.Add(H3("데이터 매칭"));
        var note = new TextBlock { FontSize = 11, Foreground = B("Ink3Brush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        note.Inlines.Add("라벨DB의 ");
        note.Inlines.Add(new System.Windows.Documents.Run("어느 열") { FontWeight = FontWeights.Bold });
        note.Inlines.Add("을 어느 항목으로 읽을지 정합니다. 엑셀 서식이 바뀌어 열이 밀렸을 때 여기서 맞추면 프로그램 전체가 그 열을 씁니다. ");
        note.Inlines.Add(new System.Windows.Documents.Run("출고 구분마다 따로") { FontWeight = FontWeights.Bold });
        note.Inlines.Add(" 저장됩니다.");
        p.Children.Add(note);

        var main = Main;
        var map = main?.Map ?? new FieldMap(_settings.Data.Profile);
        var profileName = Profiles.Get(main?.ProfileKey ?? _settings.Data.Profile).Name;

        var g = new StackPanel();
        var sum = Callout("", "info");
        g.Children.Add(sum);
        var tbl = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        g.Children.Add(tbl);

        var auditBox = new StackPanel();
        void PaintAudit()
        {
            auditBox.Children.Clear();
            var rows = main?.DbRowsOrEmpty ?? Array.Empty<DbRow>();
            if (rows.Count == 0) { auditBox.Children.Add(Hint("라벨DB를 먼저 불러오세요.")); return; }
            IReadOnlyList<MappingAudit> list;
            try { list = MappingAuditor.Audit(rows, map); }
            catch (Exception ex) { auditBox.Children.Add(Hint("점검 실패: " + ex.Message, "FailBrush")); return; }
            var used = list.Count(a => a.Level != "off");
            var bad = list.Where(a => a.Level is "error" or "warn").ToList();
            auditBox.Children.Add(Callout(bad.Count > 0
                ? $"확인이 필요한 항목 {bad.Count}개 (전체 {used}개 중)"
                : $"지정한 {used}개 항목 모두 값이 잘 들어 있습니다.", bad.Count > 0 ? "warn" : "info"));
            foreach (var a in list)
            {
                if (a.Level is "ok" or "off") continue;
                var row = new Grid { Margin = new Thickness(0, 4, 0, 0) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var fg = B(a.Level == "error" ? "FailBrush" : "WarnBrush");
                var c0 = new TextBlock { Text = a.Label, FontWeight = FontWeights.SemiBold, FontSize = 12, Foreground = fg };
                var c1 = new TextBlock { Text = a.Col + "열", FontFamily = new FontFamily("Consolas"), FontSize = 12 };
                var c2 = new TextBlock { Text = a.Note, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = fg };
                var c3 = new TextBlock { Text = a.Sample.Length > 0 ? "예: " + a.Sample : "", FontSize = 11, Foreground = B("Ink3Brush"), TextTrimming = TextTrimming.CharacterEllipsis };
                Grid.SetColumn(c1, 1); Grid.SetColumn(c2, 2); Grid.SetColumn(c3, 3);
                row.Children.Add(c0); row.Children.Add(c1); row.Children.Add(c2); row.Children.Add(c3);
                auditBox.Children.Add(row);
            }
        }

        void Paint()
        {
            var diff = map.Diff();
            var keyChanged = map.KeyCol != "H";
            var n = diff.Count;
            SetCallout(sum, (n > 0 || keyChanged)
                ? $"기본값과 다르게 지정된 항목 {n}개{(keyChanged ? $" · 조회 키 {map.KeyCol}열" : "")}"
                : $"모두 기본값입니다 (조회 키 {map.KeyCol}열).", (n > 0 || keyChanged) ? "warn" : "info");
            tbl.Children.Clear();
            var baseCols = map.BaseCols();
            var rows = new List<(string Label, string Cur, string Def)> { ("품목번호 (조회 키)", map.KeyCol, "H") };
            foreach (var kv in diff) rows.Add((FieldMap.Labels.TryGetValue(kv.Key, out var l) ? l : kv.Key, kv.Value, baseCols.GetValueOrDefault(kv.Key) ?? "—"));
            var shown = 0;
            foreach (var (label, cur, def) in rows)
            {
                if (cur == def) continue;
                var line = new TextBlock { FontSize = 12.5, Margin = new Thickness(0, 2, 0, 2) };
                line.Inlines.Add(new System.Windows.Documents.Run(label + "   ") { FontWeight = FontWeights.SemiBold });
                line.Inlines.Add(new System.Windows.Documents.Run(cur.Length > 0 ? cur : "—") { FontWeight = FontWeights.Bold });
                line.Inlines.Add("열 ");
                line.Inlines.Add(new System.Windows.Documents.Run($"(기본 {def}열)") { FontSize = 11, Foreground = B("Ink3Brush") });
                tbl.Children.Add(line);
                shown++;
            }
            if (shown == 0) tbl.Children.Add(Hint("바꾼 항목이 없습니다.", margin: new Thickness(0)));
        }
        Paint();
        PaintAudit();

        var open = new Button { Content = "데이터 매칭 편집기 열기…", Style = S("PrimaryButton") };
        open.Click += (_, _) => RunMain(async m => { await m.SettingsOpenMapperAsync(this); Paint(); PaintAudit(); });
        var reset = Btn("기본값으로", "SmallGhostButton");
        reset.Click += (_, _) => RunMain(async m =>
        {
            var ok = await Dialogs.Confirm(this, "열 매칭을 모두 기본값으로 되돌릴까요?", "기본값으로", "되돌리기", "취소");
            if (!ok) return;
            m.SettingsResetMapping();
            Paint(); PaintAudit();
            Toast("기본값으로 되돌렸습니다.", ToastLevel.Ok);
        });
        var r = Row(open, reset);
        r.Margin = new Thickness(0, 10, 0, 0);
        g.Children.Add(r);
        p.Children.Add(GBox($"현재 매칭 — {profileName}", g));

        var g2 = new StackPanel();
        g2.Children.Add(Hint("라벨DB 머리글은 예전 양식이 남아 실제 값과 다른 경우가 있습니다. 아래는 머리글이 아니라 실제 값을 표본으로 확인한 결과입니다.", margin: new Thickness(0, 0, 0, 6)));
        g2.Children.Add(auditBox);
        p.Children.Add(GBox("열 매칭 점검", g2));
        return p;
    }

    /* --- 라벨 · 용지 --- */

    private static IEnumerable<(string Group, IReadOnlyList<LabelPreset> Items)> AllLabelPresets() => new[]
    {
        ("제품 라벨 (A3 원판)", Paper.ProductLabels),
        ("일반 규격", Paper.CommonLabels),
        ("원판", (IReadOnlyList<LabelPreset>)new[] { Paper.Sheet }),
    };

    private static LabelPreset? LabelById(string? id)
        => Paper.ProductLabels.Concat(Paper.CommonLabels).Append(Paper.Sheet).FirstOrDefault(p => p.Id == id);

    private FrameworkElement BuildLayoutPage()
    {
        var p = new StackPanel();
        p.Children.Add(H3("라벨 · 용지"));
        var note = new TextBlock { FontSize = 11, Foreground = B("Ink3Brush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        note.Inlines.Add("출력 크기의 기본은 ");
        note.Inlines.Add(new System.Windows.Documents.Run("라벨 실물 크기") { FontWeight = FontWeights.Bold });
        note.Inlines.Add("입니다. A4·A3 용지는 같은 라벨을 한 장에 여러 개 앉혀 찍고 싶을 때만 고르세요.");
        p.Children.Add(note);
        var main = Main;
        var label = main?.Template.Label ?? new Core.Model.LabelSize { W = 173.8, H = 75 };

        /* 현재 라벨 */
        var g1 = new StackPanel();
        var cur = Callout("");
        g1.Children.Add(cur);
        var sel = new ComboBox { MinWidth = 320 };
        foreach (var (group, items) in AllLabelPresets())
        {
            sel.Items.Add(new ComboBoxItem { Content = group, IsEnabled = false, FontWeight = FontWeights.Bold, FontSize = 11, Foreground = B("Ink3Brush") });
            foreach (var it in items) sel.Items.Add(new ComboBoxItem { Content = $"{it.Name} — {it.W}×{it.H}mm", Tag = it.Id });
        }
        sel.Items.Add(new ComboBoxItem { Content = "사용자 지정", Tag = "" });
        void PaintCur()
        {
            LabelPreset? m = null;
            try { m = Paper.MatchPreset(label.W, label.H); } catch (Exception) { }
            SetCallout(cur, $"{Math.Round(label.W * 10) / 10} × {Math.Round(label.H * 10) / 10} mm" + (m is not null ? $" — {m.Name}" : " — 사용자 지정"));
            MainWindow.SelectByTag(sel, m?.Id ?? "");
        }
        PaintCur();
        var apply = Btn("이 크기로");
        apply.Click += (_, _) =>
        {
            var it = LabelById(MainWindow.TagOf(sel));
            if (it is null) { Toast("규격을 고르세요.", ToastLevel.Warn); return; }
            main?.SettingsApplyLabelSize(it);
            PaintCur();
            Toast($"라벨 크기를 {it.W}×{it.H}mm 로 맞췄습니다.", ToastLevel.Ok);
        };
        g1.Children.Add(Row(sel, apply));
        p.Children.Add(GBox("현재 라벨 크기", g1));

        /* 시작 서식 */
        var g0 = new StackPanel();
        var presetOpts = Paper.ProductLabels.Select(it => (it.Id, $"{it.Name} — {it.W}×{it.H}mm")).Append(("SHEET-A3", "A3 라벨 세트 원판 (297×420mm)"));
        var selT = SelectCtl(_settings.Template.Preset, presetOpts, v => _settings.Template.Preset = v);
        selT.MinWidth = 320;
        selT.SelectionChanged += (_, _) => { if (selT.SelectedItem is ComboBoxItem ci) main?.SetStatus($"시작 서식을 바꿨습니다 — {ci.Content}"); };
        g0.Children.Add(selT);
        g0.Children.Add(Hint("저장된 서식이 없을 때(또는 [서식 되돌리기] 를 눌렀을 때) 적용됩니다.", margin: new Thickness(0, 6, 0, 0)));
        p.Children.Add(GBox("시작할 때 쓸 기본 서식", g0));

        /* 용지 배치 */
        var g2 = new StackPanel();
        var desc = Callout("");
        g2.Children.Add(desc);
        void PaintL()
        {
            var t = main?.DescribeLayout() ?? "";
            SetCallout(desc, t, t.StartsWith("❌") ? "err" : _settings.Layout.Paper == "label" ? "" : "info");
        }
        PaintL();
        var openP = new Button { Content = "용지 배치 설정…" };
        openP.Click += (_, _) => RunMain(async m => { await m.SettingsOpenPaperDialogAsync(this); PaintL(); PaintCur(); });
        var resetP = Btn("라벨 실물 크기로", "SmallGhostButton");
        resetP.Click += (_, _) => { main?.SettingsResetLayout(); PaintL(); };
        g2.Children.Add(Row(openP, resetP));
        p.Children.Add(GBox("용지 배치 (면付)", g2));

        /* 원판에서 떼기 */
        var g3 = new StackPanel();
        g3.Children.Add(Hint("원판(297×420mm) 서식에서 라벨 한 장만 잘라 독립 서식으로 만듭니다.", margin: new Thickness(0)));
        var b3 = Btn("떼어내기…");
        b3.Click += (_, _) => RunMain(async m => { await m.SettingsOpenExtractAsync(this); PaintCur(); PaintL(); });
        var r3 = Row(b3);
        r3.Margin = new Thickness(0, 8, 0, 0);
        g3.Children.Add(r3);
        p.Children.Add(GBox("A3 원판에서 라벨 떼어내기", g3));
        return p;
    }

    /* --- 출력 --- */

    private FrameworkElement BuildOutputPage()
    {
        var p = new StackPanel();
        var o = _settings.Output;
        p.Children.Add(H3("출력"));
        var dpiWarn = Hint("", margin: new Thickness(0, 2, 0, 10));
        void UpdDpiWarn()
        {
            var label = Main?.Template.Label;
            if (label is null) { dpiWarn.Text = ""; return; }
            var mp = (label.W / 25.4 * o.Dpi) * (label.H / 25.4 * o.Dpi) / 1e6;
            dpiWarn.Text = $"현재 용지({label.W}×{label.H}mm)에서 약 {mp:0}백만 화소로 만들어집니다.";
            dpiWarn.Foreground = B(mp > 60 ? "WarnBrush" : "Ink3Brush");
        }
        p.Children.Add(SettingRow("해상도 (DPI)", SelectCtl(o.Dpi, new[] { (200, "200 — 초안"), (300, "300 — 권장 (라벨 인쇄 표준)"), (400, "400"), (600, "600 — 고정밀") },
            v => o.Dpi = v, () => { UpdDpiWarn(); Main?.SettingsUiChanged(); })));
        p.Children.Add(SettingRow("래스터 형식", SelectCtl(o.RasterFormat, new[]
        {
            ("auto", "자동 — 작은 라벨은 무손실, 큰 라벨은 고품질"), ("png", "항상 무손실 (PNG, 느림)"), ("jpeg", "항상 고품질 (JPEG, 빠름)"),
        }, v => o.RasterFormat = v)));
        p.Children.Add(Hint("A3처럼 큰 라벨을 무손실로 만들면 한 장에 3초 이상 걸립니다. 자동으로 두면 큰 라벨만 고품질 JPEG로 만들어 약 10배 빨라집니다. (실측: 이 서식의 바코드 9종이 두 방식 모두에서 정상 판독되었습니다.)", margin: new Thickness(0, 2, 0, 10)));
        UpdDpiWarn();
        p.Children.Add(dpiWarn);
        p.Children.Add(SettingRow("출력 방식", SelectCtl(o.Mode, new[] { ("separate", "라벨마다 개별 PDF"), ("merged", "한 PDF에 여러 쪽") }, v => o.Mode = v, () => Main?.SettingsUiChanged())));
        p.Children.Add(SettingRow("파일명 규칙", TextCtl(o.Pattern, "{ITEM}_{LOT}_{DATE}", v => o.Pattern = v)));
        p.Children.Add(Hint("쓸 수 있는 항목: {ITEM} {REF} {LOT} {SN} {MFG} {EXP} {EXP6} {PRODUCT} {DATE} {TIME} {COPY} · 날짜 형식 지정: {EXP:YYMMDD}", margin: new Thickness(0, 2, 0, 10)));
        p.Children.Add(SettingRow("같은 이름이 있을 때", SelectCtl(o.Conflict, new[] { ("increment", "번호를 붙여 새 파일로"), ("overwrite", "덮어쓰기") }, v => o.Conflict = v)));
        p.Children.Add(SettingRow("배경 서식 포함", CheckCtl(o.IncludeBg, v => o.IncludeBg = v)));
        p.Children.Add(SettingRow("출력 전 확인 대화상자", CheckCtl(o.ConfirmBeforeExport, v => o.ConfirmBeforeExport = v)));
        return p;
    }

    /* --- ZEBRA 프린터 --- */

    private FrameworkElement BuildPrinterPage()
    {
        var p = new StackPanel();
        var P = _settings.Printer;
        p.Children.Add(H3("ZEBRA 프린터"));
        var note = new TextBlock { FontSize = 11, Foreground = B("Ink3Brush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        note.Inlines.Add("라벨을 프린터 해상도의 흑백 그림으로 바꿔 ZPL 그래픽 명령(^GFA)으로 보냅니다. 화면·PDF와 ");
        note.Inlines.Add(new System.Windows.Documents.Run("완전히 같은 그림") { FontWeight = FontWeights.Bold });
        note.Inlines.Add("이 찍히며, 프린터에 한글 글꼴을 올릴 필요가 없습니다.");
        p.Children.Add(note);

        /* 연결 */
        var gp1 = new StackPanel();
        var connBox = Hint("", margin: new Thickness(0, 6, 0, 6));
        var connPanel = new StackPanel();
        void RefreshConn()
        {
            connPanel.Children.Clear();
            switch (P.Method)
            {
                case "tcp":
                {
                    connBox.Text = "프린터의 IP 주소(또는 호스트 이름)와 포트를 적습니다. 제브라 프린터의 원시 포트는 보통 9100 입니다.";
                    connBox.Foreground = B("Ink3Brush");
                    var host = TextCtl(P.Host, "예: 192.168.0.50", v => P.Host = v.Trim(), 200);
                    var port = NumCtl(P.Port > 0 ? P.Port : 9100, 1, 65535, v => P.Port = (int)(v ?? 9100));
                    connPanel.Children.Add(SettingRow("프린터 주소", host));
                    connPanel.Children.Add(SettingRow("포트", port));
                    break;
                }
                case "file":
                    connBox.Text = "출력할 때 .zpl 파일로 저장합니다. 기존 라벨 출력 시스템이나 프린터 스풀러에 그대로 넘길 수 있습니다.";
                    connBox.Foreground = B("Ink3Brush");
                    break;
                default:
                {
                    var printers = PrinterService.InstalledPrinters();
                    if (printers.Count == 0)
                    {
                        connBox.Text = "설치된 프린터를 찾지 못했습니다. Windows [프린터 및 스캐너]에서 Zebra 프린터(드라이버 또는 일반/텍스트 전용)를 추가한 뒤 [다시 찾기]를 누르세요.";
                        connBox.Foreground = B("WarnBrush");
                    }
                    else
                    {
                        connBox.Text = $"프린터 {printers.Count}대를 찾았습니다. ZPL 을 RAW 로 보내므로 제브라 드라이버가 아니어도 됩니다.";
                        connBox.Foreground = B("PassBrush");
                    }
                    var opts = printers.Select(n => (n, n)).ToList();
                    if (!string.IsNullOrEmpty(P.PrinterName) && !printers.Contains(P.PrinterName)) opts.Add((P.PrinterName, P.PrinterName + " (지금은 없음)"));
                    var devSel = SelectCtl(P.PrinterName, opts, v => P.PrinterName = v);
                    devSel.MinWidth = 320;
                    if (devSel.SelectedItem is null && printers.Count > 0)
                    {
                        // 아직 고른 적이 없으면 첫 프린터를 기본으로 (브라우저판 printers[0])
                        var zebra = printers.FirstOrDefault(n => n.Contains("zebra", StringComparison.OrdinalIgnoreCase) || n.Contains("ZDesigner", StringComparison.OrdinalIgnoreCase)) ?? printers[0];
                        MainWindow.SelectByTag(devSel, zebra);
                    }
                    var again = Btn("다시 찾기");
                    again.Click += (_, _) => RefreshConn();
                    connPanel.Children.Add(SettingRow("프린터", Row(devSel, again)));
                    break;
                }
            }
        }
        gp1.Children.Add(SettingRow("전송 방법", SelectCtl(P.Method, new[]
        {
            ("spooler", "Windows 프린터 (스풀러 · USB 포함)"), ("tcp", "네트워크 프린터 (TCP 9100)"), ("file", ".zpl 파일로 저장"),
        }, v => P.Method = v, RefreshConn)));
        gp1.Children.Add(connBox);
        gp1.Children.Add(connPanel);
        RefreshConn();
        p.Children.Add(GBox("연결", gp1));

        /* 인쇄 설정 */
        var gp2 = new StackPanel();
        gp2.Children.Add(SettingRow("프린터 해상도", SelectCtl(P.Dpi, new[]
        {
            (203, "203 dpi (8 dots/mm) — 일반 데스크톱"), (300, "300 dpi (12 dots/mm)"), (600, "600 dpi (24 dots/mm)"),
        }, v => P.Dpi = v)));
        gp2.Children.Add(SettingRow("인쇄 농도 (-30~30)", NumCtl(P.Darkness, -30, 30, v => P.Darkness = v is null ? null : (int)v, nullable: true)));
        gp2.Children.Add(SettingRow("인쇄 속도 (인치/초)", NumCtl(P.Speed, 1, 14, v => P.Speed = v is null ? null : (int)v, nullable: true)));
        gp2.Children.Add(SettingRow("용지 처리", SelectCtl(P.MediaMode ?? "", new[]
        {
            ("", "프린터 설정 유지"), ("T", "티어오프"), ("P", "필오프"), ("C", "커터"),
        }, v => P.MediaMode = v.Length == 0 ? null : v)));
        gp2.Children.Add(SettingRow("180도 회전", CheckCtl(P.Invert, v => P.Invert = v)));
        gp2.Children.Add(Hint("농도와 속도를 비워 두면 프린터에 저장된 값을 그대로 씁니다. 바코드가 흐리면 농도를 올리고 속도를 낮추세요.", margin: new Thickness(0, 4, 0, 0)));
        p.Children.Add(GBox("인쇄 설정", gp2));

        /* 흑백 변환 */
        var gp3 = new StackPanel();
        gp3.Children.Add(SettingRow("기준값 (0~255)", NumCtl(P.Threshold, 0, 255, v => P.Threshold = (int)(v ?? 160))));
        gp3.Children.Add(SettingRow("디더링 (사진 회색조 표현)", CheckCtl(P.Dither, v => P.Dither = v)));
        gp3.Children.Add(Hint("제브라 프린터는 검정 아니면 흰색만 찍습니다. 디더링을 켜면 회색을 점 밀도로 표현하고, 끄면 기준값보다 어두운 곳만 검정으로 찍습니다. 바코드와 글자는 어느 쪽이든 또렷합니다.", margin: new Thickness(0, 4, 0, 0)));
        p.Children.Add(GBox("흑백 변환", gp3));

        /* 진단 */
        var gp4 = new StackPanel();
        var diagRow = new WrapPanel();
        foreach (var (label, zpl) in new[]
        {
            ("테스트 라벨", ZplBuilder.TestLabel(P.Dpi)), ("설정 라벨 인쇄", ZplBuilder.PrintConfig()), ("용지 캘리브레이션", ZplBuilder.Calibrate()),
        })
        {
            var bq = Btn(label);
            bq.Click += async (_, _) =>
            {
                SaveNow();
                try
                {
                    await PrinterService.SendRawAsync(P, zpl, label, this, CancellationToken.None);
                    Toast(label + " 전송 완료", ToastLevel.Ok);
                    Main?.SetStatus("프린터로 " + label + " 전송");
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    AppLog.Error("진단 전송 실패", ex);
                    Toast(ex.Message, ToastLevel.Err);
                    Main?.SetStatus("전송 실패: " + ex.Message, StatusLevel.Error);
                }
            };
            diagRow.Children.Add(bq);
        }
        gp4.Children.Add(diagRow);
        gp4.Children.Add(Hint("테스트 라벨에는 DataMatrix가 포함되어 있어 스캐너로 판독해 인쇄 품질을 확인할 수 있습니다.", margin: new Thickness(0, 6, 0, 0)));
        p.Children.Add(GBox("진단", gp4));
        return p;
    }

    /* --- 이미지 --- */

    private FrameworkElement BuildImagingPage()
    {
        var p = new StackPanel();
        var im = _settings.Imaging;
        p.Children.Add(H3("이미지"));
        p.Children.Add(SettingRow("배경 자동 투명화", CheckCtl(im.AutoTransparent, v => im.AutoTransparent = v, () => Main?.SettingsImagesChanged())));
        p.Children.Add(Hint("JPG나 배경이 불투명한 PNG는 가장자리에서 이어진 배경색만 자동으로 지웁니다. 제품 안쪽의 흰색은 남습니다.", margin: new Thickness(0, 2, 0, 10)));
        p.Children.Add(SettingRow("배경 인식 허용치", NumCtl(im.Tolerance, 0, 120, v => im.Tolerance = (int)(v ?? 30), () => Main?.SettingsImagesChanged())));
        return p;
    }

    /* --- 검증 규칙 --- */

    private FrameworkElement BuildValidationPage()
    {
        var p = new StackPanel();
        var v = _settings.Validation;
        p.Children.Add(H3("검증 규칙"));
        p.Children.Add(Hint("끄면 해당 항목을 검사하지 않습니다. 의료기기 라벨 특성상 켜 두기를 권장합니다."));
        var rows = new (string Label, bool Cur, Action<bool> Set)[]
        {
            ("품목번호 필수 (오류)", v.RequireItem, x => v.RequireItem = x), ("LOT 필수 (오류)", v.RequireLot, x => v.RequireLot = x),
            ("제조일 필수 (오류)", v.RequireMfg, x => v.RequireMfg = x), ("SN 필수 (오류)", v.RequireSn, x => v.RequireSn = x),
            ("GTIN 체크디짓 검증 (오류)", v.CheckGtin, x => v.CheckGtin = x), ("유효일이 제조일보다 빠른지 (오류)", v.CheckExpOrder, x => v.CheckExpOrder = x),
            ("유효일이 이미 지났는지 (경고)", v.CheckExpPast, x => v.CheckExpPast = x), ("이미지 누락 (경고)", v.WarnMissingImage, x => v.WarnMissingImage = x),
            ("치환되지 않은 항목 (경고)", v.WarnUnresolved, x => v.WarnUnresolved = x), ("텍스트 넘침 (경고)", v.WarnOverflow, x => v.WarnOverflow = x),
            ("라벨 밖 객체 (경고)", v.WarnOutOfBounds, x => v.WarnOutOfBounds = x),
        };
        foreach (var (label, cur, set) in rows) p.Children.Add(SettingRow(label, CheckCtl(cur, set, () => Main?.SettingsUiChanged())));
        return p;
    }

    /* --- 화면 --- */

    private FrameworkElement BuildUiPage()
    {
        var p = new StackPanel();
        var u = _settings.Ui;
        Action after = () => Main?.SettingsUiChanged();
        p.Children.Add(H3("화면"));
        p.Children.Add(SettingRow("눈금자", CheckCtl(u.ShowRulers, v => u.ShowRulers = v, after)));
        p.Children.Add(SettingRow("격자", CheckCtl(u.ShowGrid, v => u.ShowGrid = v, after)));
        p.Children.Add(SettingRow("격자 간격 (mm)", NumCtl(u.GridMm, 0.5, 50, v => u.GridMm = v ?? 5, after)));
        p.Children.Add(SettingRow("맞춤 안내선 (스냅)", CheckCtl(u.Snap, v => u.Snap = v, after)));
        p.Children.Add(SettingRow("스냅 민감도 (px)", NumCtl(u.SnapPx, 1, 24, v => u.SnapPx = (int)(v ?? 6), after)));
        p.Children.Add(SettingRow("링크 보기", SelectCtl(u.LinkMode == "off" ? "off" : "on", new[]
        {
            ("on", "켬 — 객체를 하나 고르면 같은 값을 쓰는 곳이 함께 표시됩니다"), ("off", "끔"),
        }, v => u.LinkMode = v, after)));
        var ob = Btn("처음 설정 안내 다시 보기");
        ob.Margin = new Thickness(2, 12, 2, 2);
        ob.HorizontalAlignment = HorizontalAlignment.Left;
        ob.Click += (_, _) =>
        {
            var m = Main;
            if (m is null) return;
            u.OnboardingDone = false;
            MarkDirty();
            OnCloseClick(ob, new RoutedEventArgs());
            // 설정 창의 ShowDialog 가 끝난 뒤(바깥 메시지 루프에서) 안내를 띄운다
            m.Dispatcher.BeginInvoke(DispatcherPriority.Background, () => m.Run(m.SettingsShowOnboardAsync));
        };
        p.Children.Add(ob);
        return p;
    }

    /* --- 출력 이력 --- */

    private FrameworkElement BuildHistoryPage()
    {
        var p = new StackPanel();
        p.Children.Add(H3("출력 이력"));
        var hbox = Hint("불러오는 중…");
        p.Children.Add(hbox);

        var dg = new DataGrid
        {
            AutoGenerateColumns = false, HeadersVisibility = DataGridHeadersVisibility.Column, RowHeaderWidth = 0,
            RowHeight = 24, FontSize = 12, IsReadOnly = true, CanUserAddRows = false, MaxHeight = 420, Margin = new Thickness(0, 10, 0, 0),
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, HorizontalGridLinesBrush = B("GridLineBrush"),
            SelectionMode = DataGridSelectionMode.Single,
        };
        void Col(string header, string path, double width, bool star = false)
        {
            dg.Columns.Add(new DataGridTextColumn
            {
                Header = header, Binding = new Binding(path),
                Width = star ? new DataGridLength(1, DataGridLengthUnitType.Star) : new DataGridLength(width),
            });
        }
        Col("출력일시", nameof(HistoryRowView.At), 140);
        Col("품목번호", nameof(HistoryRowView.Item), 100);
        Col("LOT", nameof(HistoryRowView.Lot), 100);
        Col("SN", nameof(HistoryRowView.Sn), 70);
        Col("매수", nameof(HistoryRowView.Copies), 50);
        Col("결과", nameof(HistoryRowView.Result), 0, star: true);
        var rowStyle = new Style(typeof(DataGridRow));
        var failTrigger = new DataTrigger { Binding = new Binding(nameof(HistoryRowView.Ok)), Value = false };
        failTrigger.Setters.Add(new Setter(ForegroundProperty, B("FailBrush")));
        rowStyle.Triggers.Add(failTrigger);
        dg.RowStyle = rowStyle;

        void LoadHist()
        {
            var rows = _history?.List(200) ?? Array.Empty<HistoryEntry>();
            hbox.Text = $"최근 {rows.Count}건 (최대 200건 표시)";
            dg.ItemsSource = rows.Select(r => new HistoryRowView(
                r.At.ToString("yyyy-MM-dd HH:mm:ss"), r.Item, r.Lot, r.Sn, r.Copies,
                r.Ok ? (string.IsNullOrEmpty(r.FileName) ? "완료" : r.FileName) : "실패: " + (r.Error ?? ""), r.Ok)).ToList();
        }

        var hcsv = Btn("CSV로 내보내기");
        hcsv.Click += (_, _) =>
        {
            var rows = _history?.List(5000) ?? Array.Empty<HistoryEntry>();
            if (rows.Count == 0) { Toast("이력이 없습니다.", ToastLevel.Warn); return; }
            var dlg = new Microsoft.Win32.SaveFileDialog { Title = "출력 이력 내보내기", FileName = $"출력이력_{DateTime.Now:yyyy-MM-dd}.csv", DefaultExt = ".csv", Filter = "CSV (*.csv)|*.csv" };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                File.WriteAllText(dlg.FileName, HistoryStore.ToCsv(rows), new System.Text.UTF8Encoding(false));
                Toast($"이력을 내보냈습니다: {Path.GetFileName(dlg.FileName)}", ToastLevel.Ok);
            }
            catch (Exception ex) { Toast("내보내기 실패: " + ex.Message, ToastLevel.Err); }
        };
        var hopen = Btn("폴더 열기", "SmallGhostButton");
        hopen.ToolTip = AppPaths.Root;
        hopen.Click += (_, _) => Dialogs.OpenFolder(this, AppPaths.Root);
        var hclr = Btn("이력 지우기", "SmallDangerButton");
        hclr.Margin = new Thickness(24, 2, 2, 2);
        hclr.Click += async (_, _) =>
        {
            var ok = await Dialogs.Confirm(this, "출력 이력을 모두 지울까요?", "이력 삭제", "지우기", "취소", danger: true);
            if (!ok) return;
            try
            {
                if (File.Exists(AppPaths.HistoryFile)) File.Delete(AppPaths.HistoryFile);
                Toast("이력을 지웠습니다.", ToastLevel.Ok);
                LoadHist();
            }
            catch (Exception ex) { Toast("이력 삭제 실패: " + ex.Message, ToastLevel.Err); }
        };
        p.Children.Add(Row(hcsv, hopen, hclr));
        p.Children.Add(dg);
        LoadHist();
        return p;
    }

    /* --- 정보 --- */

    private FrameworkElement BuildAboutPage()
    {
        var p = new StackPanel();
        p.Children.Add(H3("정보"));
        var ver = typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3) ?? "3";
        p.Children.Add(Hint($"LaPrint v{ver} — 의료기기 라벨 조판 · PDF / ZEBRA 출력\n모든 데이터는 이 PC에만 저장되며 외부로 전송되지 않습니다.\n\n설정·서식·이력 폴더: {AppPaths.Root}\n로그 파일: {AppLog.FilePath}"));

        var logs = Btn("로그 폴더 열기");
        logs.Click += (_, _) => Dialogs.OpenFolder(this, AppPaths.Root);
        var exp = Btn("설정 내보내기");
        exp.Click += (_, _) =>
        {
            var dlg = new Microsoft.Win32.SaveFileDialog { Title = "설정 내보내기", FileName = "laprint-settings.json", DefaultExt = ".json", Filter = "JSON (*.json)|*.json" };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                File.WriteAllText(dlg.FileName, JsonSerializer.Serialize(_settings, StorageJson.Indented), new System.Text.UTF8Encoding(false));
                Toast("설정을 내보냈습니다.", ToastLevel.Ok);
            }
            catch (Exception ex) { Toast("내보내기 실패: " + ex.Message, ToastLevel.Err); }
        };
        var imp = Btn("설정 가져오기");
        imp.Click += (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = "설정 가져오기", Filter = "JSON (*.json)|*.json|모든 파일 (*.*)|*.*" };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(dlg.FileName), StorageJson.Indented)
                        ?? throw new InvalidDataException("설정 파일이 아닙니다.");
                _settings.Paths = s.Paths ?? new PathsSettings();
                _settings.Output = s.Output ?? new OutputSettings();
                _settings.Imaging = s.Imaging ?? new ImagingSettings();
                _settings.TextDefaults = s.TextDefaults ?? new TextDefaults();
                _settings.BarcodeDefaults = s.BarcodeDefaults ?? new BarcodeDefaults();
                _settings.Printer = s.Printer ?? new PrinterSettings();
                _settings.Ui = s.Ui ?? new UiSettings();
                _settings.Validation = s.Validation ?? new ValidationRules();
                _settings.Template = s.Template ?? new TemplateSettings();
                _settings.Layout = s.Layout ?? new LayoutOptions();
                _settings.Data = s.Data ?? new DataSettings();
                MarkDirty();
                SaveNow();
                Main?.SettingsUiChanged();
                RebuildPages();
                Toast("설정을 가져왔습니다.", ToastLevel.Ok);
            }
            catch (Exception ex) { Toast("가져오기 실패: " + ex.Message, ToastLevel.Err); }
        };
        var rst = Btn("설정 초기화", "SmallDangerButton");
        rst.Margin = new Thickness(24, 2, 2, 2);
        rst.Click += async (_, _) =>
        {
            var ok = await Dialogs.Confirm(this, "모든 설정을 기본값으로 되돌릴까요?", "설정 초기화", "초기화", "취소", danger: true,
                detail: "폴더 지정과 서식, 출력 이력은 그대로 유지됩니다.");
            if (!ok) return;
            _saveTimer.Stop();
            _needSave = false;
            Main?.SettingsResetAll();
            Dirty = true;
            RebuildPages();
            Toast("설정을 초기화했습니다.", ToastLevel.Ok);
        };
        var r = Row(logs, exp, imp, rst);
        r.Margin = new Thickness(0, 12, 0, 0);
        p.Children.Add(r);
        return p;
    }

    /// <summary>메인 창의 비동기 동작을 부른다. 설정 값이 먼저 저장되어야 하는 동작이므로 저장부터 한다.</summary>
    private void RunMain(Func<MainWindow, Task> action)
    {
        var m = Main;
        if (m is null) { Toast("메인 창에서만 쓸 수 있는 기능입니다.", ToastLevel.Warn); return; }
        _saveTimer.Stop();
        SaveNow();
        m.Run(async () =>
        {
            try { await action(m); }
            catch (Exception ex)
            {
                AppLog.Error("설정 동작 실패", ex);
                Toast("실패: " + ex.Message, ToastLevel.Err);
            }
        });
    }
}

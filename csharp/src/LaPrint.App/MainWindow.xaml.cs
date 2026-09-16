// 메인 창 — 관심사별 partial 파일(Job · Stage · Inspector · Queue · Print · Settings)로 나뉜다.
// 이 파일은 공유 상태 · 조립(생성자) · 상태줄 · 자동 저장 · 갱신 파이프라인 · 복원 · 단축키 같은 "조율" 만 맡는다.
// 실제 화면 동작은 partial 파일의 메서드가 맡는다 (골격 단계에서는 stub).
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LaPrint.App.Controls;
using LaPrint.App.Services;
using LaPrint.Core.Batch;
using LaPrint.Core.Data;
using LaPrint.Core.Export;
using LaPrint.Core.Imaging;
using LaPrint.Core.Model;
using LaPrint.Core.Render;
using LaPrint.Core.Storage;

namespace LaPrint.App;

/// <summary>상태줄 수준 — Info 검정 / Warn #9A5B00 굵게 / Error #B01E1E 굵게 (DESIGN.md §5).</summary>
public enum StatusLevel { Info, Warn, Error }

/// <summary>LaPrint 메인 창.</summary>
public partial class MainWindow : Window
{
    /* ================= 공유 상태 (모든 partial 이 쓴다) ================= */

    internal AppSettings Settings { get; set; }
    internal readonly SettingsStore SettingsStore = new();
    internal readonly TemplateStore Templates = new();
    internal readonly SessionStore Session = new();
    internal readonly HistoryStore History = new();
    internal readonly DbCache DbCache = new();

    /// <summary>편집 중인 서식 (Control.Template 을 가린다 — 이 창에서 Template 은 언제나 라벨 서식이다).</summary>
    internal new LabelTemplate Template = new() { Label = new LabelSize { W = 173.8, H = 26.3 } };
    internal FieldMap Map { get; set; }
    internal LabelDb? Db { get; set; }
    internal LabelIndex? Index { get; set; }
    internal Fields Fields { get; set; } = new();
    internal DbRow? Row { get; set; }
    internal readonly QueueEngine Queue = new();
    internal ImageStore? Images { get; set; }
    internal readonly PdfExporter Exporter = new();
    internal EditorCanvas Editor => stage;

    internal bool Locked { get; set; } = true;
    /// <summary>잠금 해제 상태에서 바뀐 레이아웃 = '검증되지 않은 서식' (출력 전 점검 경고).</summary>
    internal bool LayoutDirty { get; set; }
    internal bool Preview { get; set; }
    /// <summary>"general" | "bsc".</summary>
    internal string ProfileKey { get; set; } = "general";

    /// <summary>작업 입력 (JobInputs 는 불변 레코드 — 바꿀 때는 with 식).</summary>
    internal JobInputs Inputs { get; set; } = new("", "", "", "");
    internal int Copies { get; set; } = 1;
    /// <summary>큐 행을 편집 중이면 그 행 id, 아니면 null (jobOrigin "새 작업").</summary>
    internal string? ActiveQueueId { get; set; }
    internal string TemplateName { get; set; } = "";
    /// <summary>불러온 라벨DB 파일명 (칩 툴팁). 없으면 null.</summary>
    internal string? DbFileName { get; set; }
    internal readonly List<string> DbIssues = new();
    /// <summary>세션에서 복원한 큐 (행 수, 저장 시각) — NoticeRestoredQueue 가 알린다.</summary>
    internal (int N, DateTime? At)? RestoredQueue { get; set; }
    internal PreflightResult? LastPreflight { get; set; }

    /// <summary>현재 작업 서식이 저장되는 파일 (store.js 'template' 에 해당). 이름 있는 서식은 Templates 에.</summary>
    internal static readonly string CurrentTemplateFile = Path.Combine(AppPaths.Root, "current_template.json");

    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromMilliseconds(90) };
    /// <summary>저장되지 않은 변경이 있는지 (상태줄 '변경됨').</summary>
    internal bool Dirty { get; private set; }

    /* ================= 조립 ================= */

    public MainWindow()
    {
        InitializeComponent();

        // 설정 — 읽기 실패해도 기본값으로 시작한다
        try { Settings = SettingsStore.Load(); }
        catch (Exception ex)
        {
            AppLog.Warn("설정을 읽지 못해 기본값으로 시작합니다: " + ex.Message);
            Settings = new AppSettings();
        }

        // 사용자가 지정한 DB 열 매칭을 먼저 적용해야 이후 계산이 맞는다 (app.js restore 첫 부분)
        ProfileKey = Profiles.Get(Settings.Data.Profile).Key;
        Map = new FieldMap(ProfileKey);
        ApplyProfileToMap();

        Images = string.IsNullOrWhiteSpace(Settings.Paths.ImgDir) ? null : new ImageStore(Settings.Paths.ImgDir);

        // 편집기 조립
        Editor.Template = Template;
        Editor.Context = new RenderContext
        {
            ResolveText = ResolveText,
            Objects = Template.Objects,
            ImageOf = ImageOf,
            Dpi = Settings.Output.Dpi,
        };
        Editor.SelectionChanged += OnEditorSelectionChanged;
        Editor.ModelChanged += OnEditorModelChanged;
        Editor.ViewChanged += OnEditorViewChanged;
        Editor.HoverChanged += OnEditorHoverChanged;

        Queue.Changed += OnQueueChanged;
        SettingsStore.Changed += OnSettingsChanged;

        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveNow(); };
        _refreshTimer.Tick += (_, _) => { _refreshTimer.Stop(); Refresh(); };

        brandVer.Text = "v" + (typeof(MainWindow).Assembly.GetName().Version?.ToString(1) ?? "3");

        InitJob();
        InitPrint();
        InitStage();
        InitInspector();
        InitQueue();

        Loaded += OnLoaded;
        Closing += OnClosing;
        PreviewKeyDown += OnWindowPreviewKeyDown;
    }

    /// <summary>설정의 프로필별 매칭(fieldMap · keyCol)을 Map 에 적용한다.</summary>
    internal void ApplyProfileToMap()
    {
        if (Map.Profile != ProfileKey) Map.SetProfile(ProfileKey);
        Settings.Data.Profiles.TryGetValue(ProfileKey, out var ps);
        Map.Apply(ps?.FieldMap);
        Map.KeyCol = string.IsNullOrWhiteSpace(ps?.KeyCol) ? "H" : ps!.KeyCol;
    }

    /// <summary>플레이스홀더 치환기 — 편집기·바코드·미리보기가 공유한다.</summary>
    internal string ResolveText(string text) => Placeholders.Resolve(text ?? "", Fields, Row);

    /// <summary>이미지 객체 id → 슬롯에 읽혀 있는 비트맵.</summary>
    internal SkiaSharp.SKBitmap? ImageOf(string id) => Editor.ById(id) is ImageObject im ? im.Bitmap : null;

    /* ================= 상태줄 ================= */

    /// <summary>"[HH:mm:ss] 메시지". Warn·Error 는 굵게 + 로그. 툴팁 = 전체 문장.</summary>
    internal void SetStatus(string msg, StatusLevel level = StatusLevel.Info)
    {
        statusMsg.Text = $"[{DateTime.Now:HH:mm:ss}] {msg}";
        statusMsg.Foreground = level switch
        {
            StatusLevel.Error => Res<Brush>("ErrorStatusBrush"),
            StatusLevel.Warn => Res<Brush>("WarnBrush"),
            _ => Res<Brush>("InkBrush"),
        };
        statusMsg.FontWeight = level == StatusLevel.Info ? FontWeights.Normal : FontWeights.Bold;
        statusMsg.ToolTip = statusMsg.Text;
        if (level == StatusLevel.Warn) AppLog.Warn(msg);
        else if (level == StatusLevel.Error) AppLog.Error(msg);
    }

    /// <summary>토스트 (ui.js toast).</summary>
    internal void Toast(string msg, ToastLevel level = ToastLevel.Info) => Dialogs.Toast(this, msg, level);

    /* ================= 갱신 파이프라인 (app.js recompute/refresh) ================= */

    /// <summary>입력 + 색인 → 행 · 필드 재계산, 편집기 문맥 갱신.</summary>
    internal void Recompute()
    {
        Row = Index is not null && Index.ByRef.TryGetValue((Inputs.Item ?? "").Trim(), out var r) ? r : null;
        Fields = FieldComputer.Compute(Row, Inputs, Map);
        if (Inputs.ExpAuto) Inputs = Inputs with { Exp = Fields.TryGetValue("EXP", out var exp) ? exp : "" };
        Editor.Context.Fields = Fields;
        Editor.Context.Row = Row;
        Editor.Context.Objects = Template.Objects;
    }

    /// <summary>전체 갱신: 재계산 → 참조표 → 캔버스 → (이미지 슬롯) → 점검 → 인스펙터 → 출력 버튼 → 저장 예약.</summary>
    internal void Refresh(bool skipImages = false)
    {
        Recompute();
        RenderRefTable();
        Editor.Invalidate();
        if (!skipImages) Run(() => LoadSlotImagesAsync(false));
        RenderChecks();
        RefreshInspector();
        UpdatePrintButton();
        ScheduleSave();
    }

    /// <summary>타이핑 중 갱신을 90ms 로 모은다.</summary>
    internal void RefreshSoon()
    {
        _refreshTimer.Stop();
        _refreshTimer.Start();
    }

    /* ================= 자동 저장 (600ms 디바운스) ================= */

    /// <summary>'변경됨' 표시 후 600ms 뒤에 작업 서식 + 세션을 저장한다.</summary>
    internal void ScheduleSave()
    {
        Dirty = true;
        statusSaved.Text = "변경됨";
        statusSaved.Foreground = Res<Brush>("WarnBrush");
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>지금 저장한다 (닫을 때도). 실패는 상태줄 표시 + 로그, 예외는 삼킨다.</summary>
    internal void SaveNow()
    {
        try
        {
            Template.Name = TemplateName;
            Directory.CreateDirectory(AppPaths.Root);
            var tmp = CurrentTemplateFile + ".tmp";
            File.WriteAllText(tmp, TemplateJson.Save(Template));
            File.Move(tmp, CurrentTemplateFile, overwrite: true);
            Session.Save(new SessionState
            {
                Inputs = Inputs,
                Queue = Queue.Rows.ToList(),
                Locked = Locked,
                SavedAt = DateTime.Now,
            });
            Dirty = false;
            statusSaved.Text = $"저장됨 {DateTime.Now:HH:mm}";
            statusSaved.Foreground = Res<Brush>("InkBrush");
        }
        catch (Exception ex)
        {
            AppLog.Warn("자동 저장 실패: " + ex.Message);
            statusSaved.Text = "저장 실패";
            statusSaved.Foreground = Res<Brush>("ErrorStatusBrush");
            statusSaved.ToolTip = ex.Message;
        }
    }

    /* ================= 복원 (app.js restore) ================= */

    private async Task RestoreAsync()
    {
        ApplyUiSettings();

        // 작업 서식
        var restored = false;
        try
        {
            if (File.Exists(CurrentTemplateFile))
            {
                var t = TemplateJson.Load(File.ReadAllText(CurrentTemplateFile));
                if (t.Objects.Count > 0)
                {
                    InstallTemplate(t.Label, t.Objects, t.Name);
                    restored = true;
                }
            }
        }
        catch (Exception ex) { AppLog.Warn("작업 서식을 읽지 못했습니다: " + ex.Message); }
        if (!restored)
        {
            try { await ApplyDefaultTemplateAsync(false); }
            catch (Exception ex) { AppLog.Warn("기본 서식 적용 실패: " + ex.Message); }
        }
        for (var i = 0; i < Template.Objects.Count; i++) TemplateJson.Normalize(Template.Objects[i], i);
        Editor.Template = Template;
        Editor.Context.Objects = Template.Objects;

        // 세션
        try
        {
            var ses = Session.Load();
            if (ses is not null)
            {
                if (ses.Inputs is not null) Inputs = ses.Inputs;
                if (ses.Queue.Count > 0)
                {
                    foreach (var r in ses.Queue) if (r.Status == "running") r.Status = "pending";
                    Queue.AddMany(ses.Queue);
                    RestoredQueue = (ses.Queue.Count, ses.SavedAt == default ? null : ses.SavedAt);
                }
                Locked = ses.Locked;
            }
        }
        catch (Exception ex) { AppLog.Warn("세션을 읽지 못했습니다: " + ex.Message); }
        if (string.IsNullOrEmpty(Inputs.Mfg)) Inputs = Inputs with { Mfg = DateTime.Today.ToString("yyyy-MM-dd") };

        SyncInputsToUi();
        SyncProfileUi();
        SetLocked(Locked, silent: true);
        UpdateChips();

        try { await AutoLoadDbAsync(); }
        catch (Exception ex) { SetStatus("라벨DB 자동 로딩 실패: " + ex.Message, StatusLevel.Warn); }
        try { Refresh(); }
        catch (Exception ex) { SetStatus("갱신 실패: " + ex.Message, StatusLevel.Error); }
        Editor.ZoomFit();
        await CheckReconnectAsync();
        await MaybeOnboardAsync();
    }

    /// <summary>설정(ui · output.target · output.mode · layout)을 편집기·툴바·콤보에 반영 (app.js applyUiSettings).</summary>
    internal void ApplyUiSettings()
    {
        var ui = Settings.Ui;
        Editor.ShowRulers = ui.ShowRulers;
        Editor.ShowGrid = ui.ShowGrid;
        Editor.GridMm = ui.GridMm > 0 ? ui.GridMm : 5;
        Editor.SnapEnabled = ui.Snap;
        Editor.SnapPx = ui.SnapPx > 0 ? ui.SnapPx : 6;
        Editor.LinkMode = ui.LinkMode == "off" ? "off" : "on";
        Editor.Context.Dpi = Settings.Output.Dpi;
        btnLink.IsChecked = Editor.LinkMode == "on";
        btnSnap.IsChecked = ui.Snap;
        btnGrid.IsChecked = ui.ShowGrid;
        btnRuler.IsChecked = ui.ShowRulers;
        SelectByTag(selTarget, Settings.Output.Target);
        SelectByTag(selQueueMode, Settings.Output.Mode);
        SyncPaperUi();
        Editor.Invalidate();
    }

    /* ================= 창 수명 ================= */

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try { await RefreshTemplateListAsync(); }
        catch (Exception ex) { AppLog.Warn("서식 목록 읽기 실패: " + ex.Message); }
        try { await RestoreAsync(); }
        catch (Exception ex)
        {
            AppLog.Error("복원 실패", ex);
            SetStatus("복원 실패: " + ex.Message, StatusLevel.Error);
        }
        RenderQueue();
        NoticeRestoredQueue();
        SetStatus("준비됨");
        inpItem.Focus();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (IsPrinting)
        {
            var ok = Dialogs.Confirm(this,
                "큐 출력이 진행 중입니다. 지금 닫으면 출력이 중단됩니다.\n\n그래도 닫을까요?",
                "출력 진행 중", "닫기", "취소", danger: true).GetAwaiter().GetResult();
            if (!ok) { e.Cancel = true; return; }
            try { Queue.Cancel(); } catch (Exception ex) { AppLog.Warn("큐 중지 실패: " + ex.Message); }
            try { _zebraCts?.Cancel(); } catch (Exception ex) { AppLog.Warn("ZEBRA 전송 중지 실패: " + ex.Message); }
        }
        _saveTimer.Stop();
        SaveNow();
    }

    /* ================= 단축키 (Ctrl 조합만 — 스캐너 타이핑 충돌 방지, DESIGN.md §4-8) ================= */

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mod = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var inField = Keyboard.FocusedElement is TextBoxBase or ComboBox or DatePicker or PasswordBox
                      || (Keyboard.FocusedElement is DependencyObject d && FindAncestor<ComboBox>(d) is not null);

        if (mod && key == Key.Enter) { e.Handled = true; AddCurrentToQueue(); return; }
        if (mod && key == Key.P) { e.Handled = true; Run(shift ? DoPrintQueueAsync : DoPrintSingleAsync); return; }
        if (mod && key == Key.OemComma) { e.Handled = true; if (!IsPrinting) Run(() => OpenSettingsAsync(null)); return; }
        if (key == Key.F1) { e.Handled = true; ShowHelp(); return; }
        if (key == Key.F5) { e.Handled = true; TogglePreview(); return; }
        if (mod && key == Key.L) { e.Handled = true; SetLocked(!Locked); return; }
        if (mod && key == Key.S) { e.Handled = true; Run(SaveTemplateAsync); return; }
        if (mod && (key == Key.Down || key == Key.Up)) { e.Handled = true; OpenDrawer(key == Key.Down); return; }
        if (mod && key == Key.B && !inField) { e.Handled = true; chkBold.IsChecked = chkBold.IsChecked != true; OnBoldClick(chkBold, new RoutedEventArgs()); return; }
        if (mod && key == Key.I && !inField) { e.Handled = true; chkItalic.IsChecked = chkItalic.IsChecked != true; OnItalicClick(chkItalic, new RoutedEventArgs()); return; }
        if (mod && key == Key.V && !inField && !IsPrinting)
        {
            // 큐 영역에 붙여넣기 — 표(탭/줄바꿈)일 때만 (출력 중에는 큐를 바꾸지 않는다)
            string? txt = null;
            try { txt = Clipboard.ContainsText() ? Clipboard.GetText() : null; } catch { }
            if (!string.IsNullOrEmpty(txt) && (txt.Contains('\t') || txt.Contains('\n')))
            {
                e.Handled = true;
                Run(() => PasteToQueueAsync(txt));
            }
            return;
        }
        if (inField) return;
        if (Locked)
        {
            // 잠금 상태에서도 보기 관련 키는 살려 둔다
            if (mod && key == Key.D0) { e.Handled = true; Editor.ZoomFit(); }
            else if (mod && key == Key.D1) { e.Handled = true; Editor.ZoomTo(100); }
            return;
        }
        if (mod && key == Key.D0) { e.Handled = true; Editor.ZoomFit(); return; }
        if (mod && key == Key.Z) { e.Handled = true; if (shift) Editor.Redo(); else Editor.Undo(); return; }
        if (mod && key == Key.Y) { e.Handled = true; Editor.Redo(); return; }
        if (Editor.HandleKey(e)) e.Handled = true;
    }

    /* ================= 공용 도우미 ================= */

    /// <summary>비동기 동작을 시작하고 예외는 상태줄 + 로그로 보낸다 (fire-and-forget 대신).</summary>
    internal async void Run(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex)
        {
            AppLog.Error("작업 실패", ex);
            SetStatus("실패: " + ex.Message, StatusLevel.Error);
        }
    }

    internal T Res<T>(string key) => (T)FindResource(key);

    /// <summary>Tag 값으로 ComboBoxItem 을 고른다. 없으면 그대로.</summary>
    internal static void SelectByTag(ComboBox cb, string? tag)
    {
        foreach (var it in cb.Items)
            if (it is ComboBoxItem ci && (ci.Tag as string) == tag) { cb.SelectedItem = ci; return; }
    }

    /// <summary>선택된 ComboBoxItem 의 Tag (문자열). 없으면 null.</summary>
    internal static string? TagOf(ComboBox cb) => (cb.SelectedItem as ComboBoxItem)?.Tag as string;

    /// <summary>텍스트 상자의 숫자. 못 읽으면 fallback.</summary>
    internal static double Num(TextBox tb, double fallback)
        => double.TryParse(tb.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;

    internal static T? FindAncestor<T>(DependencyObject d) where T : DependencyObject
    {
        while (d is not null)
        {
            if (d is T t) return t;
            d = VisualTreeHelper.GetParent(d) ?? (d as FrameworkElement)?.Parent!;
        }
        return null;
    }
}

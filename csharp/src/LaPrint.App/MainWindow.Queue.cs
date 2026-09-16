// 연속 작업 큐 드로어 — 붙여넣기 · 파일 · SN 연번 · 표 · 진행률 · 일시정지/중지
// (app.js: renderQueue/updateQueueStatusCells/renderQueueFull, openDrawer, pasteToQueue, addCurrentToQueue, expandSnDialog,
//  revalidateQueue, noticeRestoredQueue, loadRowToInputs, bind 큐부).
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LaPrint.App.Services;
using LaPrint.Core.Batch;
using LaPrint.Core.Data;
using LaPrint.Core.Storage;

namespace LaPrint.App;

/// <summary>queueGrid 의 한 행 (QueueRow 를 표시용으로 편 것). Level = pending|running|done|error|warn|skipped.</summary>
public sealed class QueueRowView : INotifyPropertyChanged
{
    private int _no;
    private string _id = "";
    private string _item = "";
    private string _lot = "";
    private string _sn = "";
    private string _mfg = "";
    private int _months;
    private string _exp = "";
    private int _copies;
    private string _statusText = "";
    private string _message = "";
    private string _level = "pending";
    private string _tip = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public int No { get => _no; set => Set(ref _no, value); }
    public string Id { get => _id; set => Set(ref _id, value); }
    public string Item { get => _item; set => Set(ref _item, value); }
    public string Lot { get => _lot; set => Set(ref _lot, value); }
    public string Sn { get => _sn; set => Set(ref _sn, value); }
    public string Mfg { get => _mfg; set => Set(ref _mfg, value); }
    public int Months { get => _months; set => Set(ref _months, value); }
    public string Exp { get => _exp; set => Set(ref _exp, value); }
    public int Copies { get => _copies; set => Set(ref _copies, value); }
    /// <summary>대기 · 처리중 · 완료 · 오류 · 건너뜀.</summary>
    public string StatusText { get => _statusText; set => Set(ref _statusText, value); }
    /// <summary>오류 문구 또는 파일명.</summary>
    public string Message { get => _message; set => Set(ref _message, value); }
    public string Level { get => _level; set => Set(ref _level, value); }
    /// <summary>메시지 칸 툴팁 (모든 이슈 문구).</summary>
    public string Tip { get => _tip; set => Set(ref _tip, value); }
    public QueueRow? Row { get; set; }
}

public partial class MainWindow
{
    /// <summary>queueGrid 에 바인딩되는 표시용 행 목록.</summary>
    internal readonly ObservableCollection<QueueRowView> QueueRows = new();

    /// <summary>선택된 큐 행 id (체크/다중 선택).</summary>
    internal readonly HashSet<string> SelectedQueueRows = new();

    /// <summary>Queue.Changed 가 연달아 와도 그리기는 한 번만 (app.js queueRenderPending).</summary>
    private bool _queueRenderQueued;
    /// <summary>RenderQueue 가 QueueRows/선택을 바꾸는 동안 SelectionChanged 가 되돌아 들어오지 않게.</summary>
    private bool _syncingQueueSelection;
    /// <summary>ValidateAll 이 백그라운드에서 도는 중 (큰 큐).</summary>
    private bool _queueValidating;
    /// <summary>이 행 수를 넘으면 검증을 스레드 풀에서 돌린다.</summary>
    private const int BigQueue = 150;

    private static readonly LabelIndex EmptyIndex = new();

    /// <summary>붙여넣기 미리보기 표의 한 줄.</summary>
    public sealed record PasteRowView(int No, string Item, string Lot, string Sn, string Mfg, int Copies, string Check, bool Found);

    private static readonly IReadOnlyDictionary<string, string> MappingNames = new Dictionary<string, string>
    {
        ["item"] = "품목번호", ["lot"] = "LOT", ["sn"] = "SN", ["mfg"] = "제조일", ["exp"] = "유효일", ["months"] = "개월", ["copies"] = "매수",
    };

    /// <summary>queueGrid.ItemsSource = QueueRows 등 초기화.</summary>
    private void InitQueue()
    {
        queueGrid.ItemsSource = QueueRows;
        queueGrid.IsReadOnly = false;
        queueGrid.CanUserSortColumns = false;
        queueGrid.CanUserAddRows = false;
        queueGrid.CanUserDeleteRows = false;
        foreach (var col in queueGrid.Columns)
        {
            var h = col.Header as string ?? "";
            col.IsReadOnly = h is "#" or "상태" or "메시지 / 파일명";
            if (h == "메시지 / 파일명")
            {
                var cs = new Style(typeof(DataGridCell), queueGrid.CellStyle);
                cs.Setters.Add(new Setter(ToolTipProperty, new Binding("Tip")));
                col.CellStyle = cs;
            }
        }
        queueGrid.BeginningEdit += OnQueueBeginningEdit;
        queueGrid.CellEditEnding += OnQueueCellEditEnding;

        var menu = new ContextMenu();
        var load = new MenuItem { Header = "입력으로 불러오기" };
        load.Click += (_, _) => { var v = queueGrid.SelectedItem as QueueRowView; if (v?.Row is not null) LoadRowToInputs(v.Row); };
        var dup = new MenuItem { Header = "복제" };
        dup.Click += OnQueueDupClick;
        var del = new MenuItem { Header = "행 삭제" };
        del.Click += OnQueueDelClick;
        var reset = new MenuItem { Header = "상태 초기화 (완료·오류 → 대기)" };
        reset.Click += (_, _) => { if (Queue.Running) return; Queue.ResetStatus(); RevalidateQueue(); SetStatus("큐 상태를 초기화했습니다."); };
        menu.Items.Add(load);
        menu.Items.Add(dup);
        menu.Items.Add(del);
        menu.Items.Add(new Separator());
        menu.Items.Add(reset);
        queueGrid.ContextMenu = menu;
        OpenDrawer(false);
    }

    /* ================= 그리기 ================= */

    /// <summary>큐 전체를 다시 그린다 — QueueRows 재구성, queueCount/cntDone/cntErr, queueEmpty/queueGrid, 진행률·일시정지/중지 버튼, UpdatePrintButton (app.js renderQueue/renderQueueFull).</summary>
    private void RenderQueue()
    {
        // 출력 중에는 표를 새로 만들지 않는다. 행이 매번 새로 생기면 선택·포커스가 튀고 검증이 반복 실행되어 UI가 멎는다.
        if (Queue.Running) { UpdateQueueStatusCells(); return; }
        RenderQueueFull();
    }

    /// <summary>출력 중 상태 열만 갱신 (app.js updateQueueStatusCells).</summary>
    private void UpdateQueueStatusCells()
    {
        var byId = new Dictionary<string, QueueRow>();
        foreach (var r in Queue.Rows.ToList()) byId[r.Id] = r;
        foreach (var v in QueueRows)
        {
            if (!byId.TryGetValue(v.Id, out var r)) continue;
            v.Row = r;
            v.StatusText = StatusLabel(r.Status);
            v.Message = MessageOf(r);
            v.Level = LevelOf(r);
        }
        UpdateQueueCounts();
        var (index, total) = Queue.Progress;
        queueProgress.Visibility = Visibility.Visible;
        queueProgress.Value = total > 0 ? Math.Min(100, index * 100.0 / total) : 0;
        queueStatus.Text = $"{index} / {total}";
        btnQueuePause.Visibility = Visibility.Visible;
        btnQueuePause.Content = Queue.Paused ? "계속" : "일시정지";
        btnQueueCancel.Visibility = Visibility.Visible;
        queueTools.IsEnabled = false;
        btnQueueRun.IsEnabled = false;
    }

    private void RenderQueueFull()
    {
        var rows = Queue.Rows.ToList();
        var n = rows.Count;
        queueEmpty.Visibility = n > 0 ? Visibility.Collapsed : Visibility.Visible;
        queueGrid.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
        queueCount.Text = $"{n}건";
        UpdateQueueCounts();
        drawerHint.Visibility = n > 0 || drawerBody.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

        // 같은 행 순서면 제자리에서 값만 바꾼다 (선택·스크롤 유지). 아니면 다시 만든다.
        var same = QueueRows.Count == n;
        if (same)
            for (var i = 0; i < n; i++) if (QueueRows[i].Id != rows[i].Id) { same = false; break; }

        _syncingQueueSelection = true;
        try
        {
            if (same)
            {
                for (var i = 0; i < n; i++) FillView(QueueRows[i], rows[i], i);
            }
            else
            {
                QueueRows.Clear();
                for (var i = 0; i < n; i++)
                {
                    var v = new QueueRowView();
                    FillView(v, rows[i], i);
                    QueueRows.Add(v);
                }
                SelectedQueueRows.RemoveWhere(id => !rows.Any(r => r.Id == id));
                queueGrid.SelectedItems.Clear();
                foreach (var v in QueueRows) if (SelectedQueueRows.Contains(v.Id)) queueGrid.SelectedItems.Add(v);
            }
        }
        finally { _syncingQueueSelection = false; }

        // 진행률·실행 버튼
        queueProgress.Visibility = Visibility.Collapsed;
        queueProgress.Value = 0;
        queueStatus.Text = _queueValidating ? "검증 중…" : "";
        btnQueuePause.Visibility = Visibility.Collapsed;
        btnQueueCancel.Visibility = Visibility.Collapsed;
        queueTools.IsEnabled = !_queueValidating && !IsPrinting;
        var verb = Settings.Output.Target == "zebra" ? "ZEBRA 출력" : "출력";
        btnQueueRun.Content = $"큐 {Queue.TotalLabels}장 {verb}";
        btnQueueRun.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
        btnQueueRun.IsEnabled = n > 0 && !_queueValidating;
        btnQueueRun.Style = n > 0 ? Res<Style>("PrimaryButton") : Res<Style>("SmallButton");
        UpdatePrintButton();
    }

    private void UpdateQueueCounts()
    {
        var done = 0; var err = 0;
        foreach (var r in Queue.Rows.ToList())
        {
            if (r.Status == "done") done++;
            else if (r.Status == "error") err++;
        }
        cntDone.Text = $"{done}완료";
        cntDone.Visibility = done > 0 ? Visibility.Visible : Visibility.Collapsed;
        cntErr.Text = $"{err}오류";
        cntErr.Visibility = err > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>QueueRow → 표시용 행.</summary>
    private void FillView(QueueRowView v, QueueRow r, int index)
    {
        v.Row = r;
        v.No = index + 1;
        v.Id = r.Id;
        v.Item = r.Item ?? "";
        v.Lot = r.Lot ?? "";
        v.Sn = r.Sn ?? "";
        v.Mfg = r.Mfg ?? "";
        v.Months = r.Months > 0 ? r.Months : 36;
        v.Exp = r.ExpAuto ? ExpOf(r) : (r.Exp ?? "");
        v.Copies = Math.Max(1, r.Copies);
        v.StatusText = StatusLabel(r.Status);
        v.Message = MessageOf(r);
        v.Level = LevelOf(r);
        v.Tip = string.Join("\n", r.Issues.Select(i => i.Msg));
    }

    /// <summary>자동 계산 유효일 — 검증된 필드가 있으면 그 값, 없으면 지금 계산.</summary>
    private string ExpOf(QueueRow r)
    {
        if (r.Fields is not null) return r.Fields.Get("EXP");
        try
        {
            DbRow? row = null;
            Index?.ByRef.TryGetValue((r.Item ?? "").Trim(), out row);
            return FieldComputer.Compute(row, r.ToInputs(), Map).Get("EXP");
        }
        catch (Exception) { return ""; }
    }

    private static string StatusLabel(string status)
        => QueueRow.StatusLabels.TryGetValue(status ?? "", out var s) ? s : QueueRow.StatusLabels["pending"];

    private static string MessageOf(QueueRow r)
    {
        if (r.Status == "done" && !string.IsNullOrEmpty(r.FileName)) return r.FileName;
        if (!string.IsNullOrEmpty(r.Error)) return r.Error;
        var warns = r.Issues.Where(x => x.Level == "warn").ToList();
        if (warns.Count > 0) return $"⚠ {warns[0].Msg}" + (warns.Count > 1 ? $" 외 {warns.Count - 1}" : "");
        return "";
    }

    private static string LevelOf(QueueRow r)
    {
        if (r.Status is "error" or "done" or "running" or "skipped") return r.Status;
        return r.Issues.Any(x => x.Level == "warn") ? "warn" : "pending";
    }

    /// <summary>드로어 펼치기/접기 — drawerBody 표시, drawerCaret 회전, drawerHint (app.js openDrawer).</summary>
    private void OpenDrawer(bool open)
    {
        drawerBody.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        drawerCaret.Text = open ? "▼" : "▶";
        drawerToggle.IsChecked = open;
        drawerHint.Visibility = open || Queue.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        Editor.Invalidate();
    }

    /* ================= 추가 ================= */

    /// <summary>표 텍스트(탭/쉼표)를 큐에 — text 가 null 이면 입력 대화상자 → QueueEngine.ParseTable → 미리보기 확인 → AddMany → RevalidateQueue → OpenDrawer(true) (app.js pasteToQueue).</summary>
    private async Task PasteToQueueAsync(string? text = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            string? clip = null;
            try { clip = Clipboard.ContainsText() ? Clipboard.GetText() : null; } catch (Exception) { clip = null; }
            // 클립보드에 표(탭/줄바꿈)가 있으면 바로 쓰고, 아니면 붙여넣을 칸을 띄운다
            if (!string.IsNullOrWhiteSpace(clip) && (clip.Contains('\t') || clip.Contains('\n'))) text = clip;
            else
            {
                text = ShowPasteInputDialog(clip ?? "");
                if (string.IsNullOrWhiteSpace(text)) return;
            }
        }

        var defaults = new QueueRow { Mfg = Inputs.Mfg ?? "", Months = Inputs.Months > 0 ? Inputs.Months : 36 };
        var r = QueueEngine.ParseTable(text, defaults);
        if (r.Error is not null) { Toast(r.Error, ToastLevel.Err); return; }

        if (!ShowPastePreviewDialog(r.Rows, r.Mapping, r.HeaderDetected)) return;
        Queue.AddMany(r.Rows);
        await RevalidateQueueAsync();
        OpenDrawer(true);
        Toast($"{r.Rows.Count}행을 큐에 추가했습니다.", ToastLevel.Ok);
        SetStatus($"붙여넣기로 {r.Rows.Count}행 추가 — 총 {Queue.Count}행");
    }

    /// <summary>붙여넣을 표를 받는 대화상자. 취소면 null.</summary>
    private string? ShowPasteInputDialog(string initial)
    {
        var w = NewDialog("엑셀 붙여넣기", 560);
        var body = new StackPanel { Margin = new Thickness(18, 16, 18, 14) };
        body.Children.Add(new TextBlock
        {
            Text = "엑셀에서 복사한 표를 여기에 붙여넣으세요. 첫 줄이 머리글이면 자동으로 인식합니다.",
            Style = Res<Style>("Hint"), Margin = new Thickness(0, 0, 0, 6),
        });
        var ta = new TextBox
        {
            AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas"), FontSize = 13, MinHeight = 170, MaxHeight = 320, Text = initial,
            ToolTip = "품목번호\tLOT\tSN\t제조일\n16-0401\t26041086\t1\t2026-06-01",
        };
        body.Children.Add(ta);
        var ok = new Button { Content = "큐에 추가", MinWidth = 96, Style = Res<Style>("PrimaryButton") };
        var cancel = new Button { Content = "취소", MinWidth = 84, IsCancel = true };
        ok.Click += (_, _) => { w.DialogResult = true; w.Close(); };
        cancel.Click += (_, _) => { w.DialogResult = false; w.Close(); };
        body.Children.Add(ButtonRow(cancel, ok));
        w.Content = body;
        w.Loaded += (_, _) => { ta.Focus(); ta.SelectAll(); };
        return w.ShowDialog() == true ? ta.Text : null;
    }

    /// <summary>인식된 행 미리보기 — 머리글 감지·매핑, 품목 존재 여부. 추가하면 true.</summary>
    private bool ShowPastePreviewDialog(List<QueueRow> rows, Dictionary<string, int> mapping, bool headerDetected)
    {
        var w = NewDialog("붙여넣기 확인", 760);
        var body = new StackPanel { Margin = new Thickness(18, 16, 18, 14) };
        var mapped = string.Join(", ", mapping.Keys.Select(k => MappingNames.TryGetValue(k, out var nm) ? nm : k));
        body.Children.Add(new TextBlock
        {
            Text = $"{rows.Count}행 인식{(headerDetected ? " (첫 줄을 머리글로 인식)" : "")} · 매핑: {mapped}",
            Style = Res<Style>("Hint"), Margin = new Thickness(0, 0, 0, 8),
        });

        var list = new List<PasteRowView>();
        var shown = Math.Min(rows.Count, 60);
        for (var i = 0; i < shown; i++)
        {
            var x = rows[i];
            var found = Index is not null && Index.ByRef.ContainsKey((x.Item ?? "").Trim());
            list.Add(new PasteRowView(i + 1, x.Item ?? "", x.Lot ?? "", x.Sn ?? "", x.Mfg ?? "", x.Copies, found ? "✓" : "품목 없음", found));
        }
        var grid = new DataGrid { ItemsSource = list, IsReadOnly = true, MaxHeight = 300, SelectionMode = DataGridSelectionMode.Single };
        grid.Columns.Add(new DataGridTextColumn { Header = "#", Binding = new Binding("No"), Width = 34 });
        grid.Columns.Add(new DataGridTextColumn { Header = "품목번호", Binding = new Binding("Item"), Width = 120, FontFamily = new FontFamily("Consolas") });
        grid.Columns.Add(new DataGridTextColumn { Header = "LOT", Binding = new Binding("Lot"), Width = 130, FontFamily = new FontFamily("Consolas") });
        grid.Columns.Add(new DataGridTextColumn { Header = "SN", Binding = new Binding("Sn"), Width = 80, FontFamily = new FontFamily("Consolas") });
        grid.Columns.Add(new DataGridTextColumn { Header = "제조일", Binding = new Binding("Mfg"), Width = 110 });
        grid.Columns.Add(new DataGridTextColumn { Header = "매수", Binding = new Binding("Copies"), Width = 56 });
        grid.Columns.Add(new DataGridTextColumn { Header = "확인", Binding = new Binding("Check"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        var rs = new Style(typeof(DataGridRow));
        var trig = new DataTrigger { Binding = new Binding("Found"), Value = false };
        trig.Setters.Add(new Setter(BackgroundProperty, Res<Brush>("FailFillBrush")));
        rs.Triggers.Add(trig);
        grid.RowStyle = rs;
        body.Children.Add(grid);
        if (rows.Count > shown)
            body.Children.Add(new TextBlock { Text = $"… 외 {rows.Count - shown}행", Style = Res<Style>("Hint"), Margin = new Thickness(0, 6, 0, 0) });
        var missing = list.Count(x => !x.Found);
        if (missing > 0)
            body.Children.Add(new TextBlock
            {
                Text = Index is null ? "라벨DB가 없어 품목번호를 확인하지 못했습니다. 추가한 뒤 점검에서 오류로 표시됩니다." : $"품목번호가 라벨DB에 없는 행 {missing}건은 오류로 표시됩니다.",
                Foreground = Res<Brush>("WarnBrush"), FontSize = 12, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap,
            });

        var ok = new Button { Content = $"{rows.Count}행 추가", MinWidth = 96, Style = Res<Style>("PrimaryButton") };
        var cancel = new Button { Content = "취소", MinWidth = 84, IsCancel = true, IsDefault = true };
        ok.Click += (_, _) => { w.DialogResult = true; w.Close(); };
        cancel.Click += (_, _) => { w.DialogResult = false; w.Close(); };
        body.Children.Add(ButtonRow(cancel, ok));
        w.Content = body;
        w.Loaded += (_, _) => cancel.Focus();
        return w.ShowDialog() == true;
    }

    /// <summary>Ctrl+Enter / btnQueueAdd — 품목번호 없으면 안내, 여러 줄 LOT 은 행 여러 개, lot/sn 비움, OpenDrawer(true), 상태줄("큐에 N행 추가 — 총 M행 / K장") (app.js addCurrentToQueue).</summary>
    private void AddCurrentToQueue()
    {
        if (Queue.Running) return;
        if (string.IsNullOrWhiteSpace(Inputs.Item)) { Toast("품목번호를 먼저 입력하세요.", ToastLevel.Warn); inpItem.Focus(); return; }
        // 여러 줄 LOT 지원
        var lots = (Inputs.Lot ?? "").Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        if (lots.Count <= 1) lots = new List<string> { Inputs.Lot ?? "" };
        var inp = Inputs;
        var copies = Math.Max(1, Copies);
        var list = lots.Select(lot => new QueueRow
        {
            Item = inp.Item, Lot = lot, Sn = inp.Sn ?? "", Mfg = inp.Mfg ?? "",
            Months = inp.Months, ExpAuto = inp.ExpAuto, Exp = inp.Exp ?? "", Copies = copies,
        }).ToList();
        Queue.AddMany(list);
        ActiveQueueId = null;
        jobOrigin.Text = "새 작업";
        Inputs = Inputs with { Lot = "", Sn = "" };
        SyncInputsToUi();
        RevalidateQueue();
        OpenDrawer(true);
        Refresh();
        inpLot.Focus();
        SetStatus($"큐에 {list.Count}행 추가 — 총 {Queue.Count}행 / {Queue.TotalLabels}장");
    }

    /// <summary>파일 대화상자(csv/tsv/txt/xlsx/xls) → QueueEngine.ParseFileAsync → AddMany (app.js fileQueue.onchange).</summary>
    private async Task QueueFromFileAsync()
    {
        if (Queue.Running) return;
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "작업 큐 파일 불러오기",
            Filter = "표 파일 (*.csv;*.tsv;*.txt;*.xlsx;*.xlsm;*.xls)|*.csv;*.tsv;*.txt;*.xlsx;*.xlsm;*.xls|모든 파일 (*.*)|*.*",
        };
        if (dlg.ShowDialog(this) != true) return;
        var path = dlg.FileName;
        var name = Path.GetFileName(path);
        try
        {
            SetStatus($"파일을 읽는 중: {name}");
            var defaults = new QueueRow { Mfg = Inputs.Mfg ?? "", Months = Inputs.Months > 0 ? Inputs.Months : 36 };
            var r = await QueueEngine.ParseFileAsync(path, defaults);
            if (r.Error is not null) { Toast(r.Error, ToastLevel.Err); SetStatus($"불러오기 실패: {r.Error}", StatusLevel.Warn); return; }
            Queue.AddMany(r.Rows);
            await RevalidateQueueAsync();
            OpenDrawer(true);
            Toast($"{r.Rows.Count}행을 큐에 추가했습니다.", ToastLevel.Ok);
            SetStatus($"파일에서 {r.Rows.Count}행을 불러왔습니다: {name}");
        }
        catch (Exception ex)
        {
            AppLog.Error("큐 파일 불러오기 실패", ex);
            Toast("불러오기 실패: " + ex.Message, ToastLevel.Err);
            SetStatus("불러오기 실패: " + ex.Message, StatusLevel.Error);
        }
    }

    /// <summary>선택 행 1개를 SN 범위로 펼친다 (app.js expandSnDialog).</summary>
    private Task ExpandSerialAsync()
    {
        if (Queue.Running) return Task.CompletedTask;
        if (SelectedQueueRows.Count != 1)
        {
            Toast("SN 연번으로 펼칠 행을 하나만 선택하세요.", ToastLevel.Warn);
            return Task.CompletedTask;
        }
        var id = SelectedQueueRows.First();
        var r = Queue.Rows.FirstOrDefault(x => x.Id == id);
        if (r is null) { Toast("행을 찾을 수 없습니다.", ToastLevel.Err); return Task.CompletedTask; }

        var w = NewDialog("SN 연번 전개", 460);
        var body = new StackPanel { Margin = new Thickness(18, 16, 18, 14) };
        body.Children.Add(new TextBlock
        {
            Text = $"선택한 행({r.Item} / LOT {(string.IsNullOrEmpty(r.Lot) ? "—" : r.Lot)})을 SN 범위만큼 여러 행으로 펼칩니다.",
            Style = Res<Style>("Hint"), Margin = new Thickness(0, 0, 0, 8),
        });
        var snNum = int.TryParse((r.Sn ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s0) ? s0 : 1;
        var pad0 = (r.Sn ?? "").Trim().Length > 0 && (r.Sn ?? "").Trim().All(char.IsDigit) ? (r.Sn ?? "").Trim().Length : 0;
        var from = NumBox(snNum.ToString(CultureInfo.InvariantCulture));
        var to = NumBox((snNum + 9).ToString(CultureInfo.InvariantCulture));
        var pad = NumBox(pad0 > 0 ? pad0.ToString(CultureInfo.InvariantCulture) : "");
        pad.ToolTip = "비우면 시작 SN 의 자릿수를 씁니다";
        var form = new Grid();
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddFormRow(form, "시작 SN", from);
        AddFormRow(form, "끝 SN", to);
        AddFormRow(form, "자릿수 (0 채움)", pad);
        body.Children.Add(form);
        var ok = new Button { Content = "펼치기", MinWidth = 96, Style = Res<Style>("PrimaryButton") };
        var cancel = new Button { Content = "취소", MinWidth = 84, IsCancel = true };
        ok.Click += (_, _) => { w.DialogResult = true; w.Close(); };
        cancel.Click += (_, _) => { w.DialogResult = false; w.Close(); };
        body.Children.Add(ButtonRow(cancel, ok));
        w.Content = body;
        w.Loaded += (_, _) => { from.Focus(); from.SelectAll(); };
        if (w.ShowDialog() != true) return Task.CompletedTask;

        if (!int.TryParse(from.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var a)
            || !int.TryParse(to.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var b))
        {
            Toast("SN 시작/끝은 숫자여야 합니다.", ToastLevel.Err);
            return Task.CompletedTask;
        }
        var width = int.TryParse(pad.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) && p > 0 ? p : 0;
        int made;
        try { made = Queue.ExpandSerial(r, a, b, width).Count(); }
        catch (ArgumentException ex) { Toast(ex.Message, ToastLevel.Err); return Task.CompletedTask; }
        SelectedQueueRows.Clear();
        RevalidateQueue();
        Toast($"{made}행으로 펼쳤습니다.", ToastLevel.Ok);
        return Task.CompletedTask;
    }

    /* ================= 검증 ================= */

    /// <summary>Queue.ValidateAll → 행별 Issues/Status → RenderQueue (app.js revalidateQueue). 동기 — 출력 직전에 결과를 바로 읽는다.</summary>
    private void RevalidateQueue()
    {
        if (_queueValidating) return;
        if (Queue.Count == 0) { RenderQueue(); return; }
        try { ValidateQueueCore(); }
        catch (Exception ex)
        {
            AppLog.Warn("큐 검증 실패: " + ex.Message);
            SetStatus("큐 검증 실패: " + ex.Message, StatusLevel.Warn);
        }
        RenderQueue();
    }

    /// <summary>큰 큐(붙여넣기·파일)는 스레드 풀에서 검증하고 UI 는 Dispatcher 로 갱신한다.</summary>
    private async Task RevalidateQueueAsync()
    {
        if (Queue.Count <= BigQueue || _queueValidating) { RevalidateQueue(); return; }
        _queueValidating = true;
        RenderQueue();
        SetStatus($"큐 {Queue.Count}행을 검증하는 중…");
        try { await Task.Run(ValidateQueueCore); }
        catch (Exception ex)
        {
            AppLog.Warn("큐 검증 실패: " + ex.Message);
            SetStatus("큐 검증 실패: " + ex.Message, StatusLevel.Warn);
        }
        finally { _queueValidating = false; }
        RenderQueue();
        UpdatePrintButton();
    }

    private void ValidateQueueCore()
    {
        var dpi = Settings.Output.Dpi > 0 ? Settings.Output.Dpi : 300;
        Queue.ValidateAll(Index ?? EmptyIndex, Map, Template, Settings.Validation, dpi,
            r => MakeRenderContext(r.Fields ?? new Fields(), r.Row, forExport: true));
    }

    /// <summary>RestoredQueue 가 있으면 토스트/상태줄로 알린다 (app.js noticeRestoredQueue).</summary>
    private void NoticeRestoredQueue()
    {
        var r = RestoredQueue;
        if (r is null || r.Value.N == 0) return;
        RestoredQueue = null;
        var pend = Queue.Rows.Count(x => x.Status != "done");
        if (pend == 0) return;
        var when = r.Value.At;
        var old = when is not null && DateTime.Now - when.Value > TimeSpan.FromHours(6);
        var ago = when?.ToString("yyyy-MM-dd HH:mm") ?? "이전 작업";
        OpenDrawer(true);
        SetStatus($"이전 작업 큐 {pend}행이 남아 있습니다 ({ago}). 출력 전에 확인하세요.", StatusLevel.Warn);
        Dialogs.Toast(this, $"이전 작업 큐 {pend}행이 남아 있습니다.", old ? ToastLevel.Warn : ToastLevel.Info, 6000);
        if (old)
        {
            Run(async () =>
            {
                var keep = await Dialogs.Confirm(this, $"{ago}에 저장된 작업 큐 {pend}행이 남아 있습니다.\n계속 이어서 쓸까요?",
                    "이전 작업 큐 확인", "이어서 쓰기", "큐 비우기");
                if (!keep)
                {
                    Queue.Clear();
                    SelectedQueueRows.Clear();
                    ActiveQueueId = null;
                    RevalidateQueue();
                    SetStatus("이전 작업 큐를 비웠습니다.");
                }
            });
        }
    }

    /// <summary>큐 행을 작업 입력으로 불러온다 — ActiveQueueId, jobOrigin("큐 #n") (app.js loadRowToInputs).</summary>
    private void LoadRowToInputs(QueueRow r)
    {
        if (Queue.Running) return;
        Inputs = r.ToInputs();
        Copies = Math.Max(1, r.Copies);
        ActiveQueueId = r.Id;
        SyncInputsToUi();
        var idx = Queue.Rows.ToList().FindIndex(x => x.Id == r.Id);
        jobOrigin.Text = $"큐 {idx + 1}행 편집 중";
        Refresh();
    }

    /// <summary>Queue.Changed — RenderQueue, 출력 중이 아니면 ScheduleSave (UI 스레드로 마샬링).</summary>
    private void OnQueueChanged()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(OnQueueChanged)); return; }
        if (_queueRenderQueued) return;
        _queueRenderQueued = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _queueRenderQueued = false;
            try
            {
                RenderQueue();
                if (!Queue.Running) ScheduleSave();
            }
            catch (Exception ex)
            {
                AppLog.Error("큐 그리기 실패", ex);
                SetStatus("큐 그리기 실패: " + ex.Message, StatusLevel.Error);
            }
        }), DispatcherPriority.Background);
    }

    /* ================= 셀 편집 ================= */

    /// <summary>출력 중에는 편집 금지.</summary>
    private void OnQueueBeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
    {
        if (IsPrinting || _queueValidating) e.Cancel = true;
    }

    /// <summary>편집이 끝난 셀 값을 Queue.Update 로 반영하고 다시 검증한다 (app.js cell onchange).</summary>
    private void OnQueueCellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit) return;
        if (e.Row.Item is not QueueRowView v || e.EditingElement is not TextBox tb) return;
        var header = e.Column.Header as string ?? "";
        var text = (tb.Text ?? "").Trim();
        var id = v.Id;
        Action<QueueRow> edit;
        switch (header)
        {
            case "품목번호": edit = r => r.Item = text; break;
            case "LOT": edit = r => r.Lot = text; break;
            case "SN": edit = r => r.Sn = text; break;
            case "제조일":
                text = QueueEngine.NormDate(text);
                tb.Text = text;
                edit = r => r.Mfg = text;
                break;
            case "개월":
            {
                var m = int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : 36;
                tb.Text = m.ToString(CultureInfo.InvariantCulture);
                edit = r => { r.Months = m; r.ExpAuto = true; };
                break;
            }
            case "유효일":
                text = QueueEngine.NormDate(text);
                tb.Text = text;
                // 비우면 제조일+개월로 자동 계산, 적으면 그 날짜로 고정
                edit = r => { if (text.Length == 0) { r.ExpAuto = true; r.Exp = ""; } else { r.Exp = text; r.ExpAuto = false; } };
                break;
            case "매수":
            {
                var c = int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : 1;
                tb.Text = c.ToString(CultureInfo.InvariantCulture);
                edit = r => r.Copies = c;
                break;
            }
            default: return;
        }
        // 커밋이 끝난 뒤에 큐를 고쳐야 표를 다시 만들어도 편집기와 충돌하지 않는다
        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                Queue.Update(id, edit);
                RevalidateQueue();
            }
            catch (Exception ex)
            {
                AppLog.Error("큐 행 편집 실패", ex);
                SetStatus("큐 행 편집 실패: " + ex.Message, StatusLevel.Error);
            }
        }), DispatcherPriority.Background);
    }

    /* ---- 핸들러 ---- */

    private void OnDrawerToggleClick(object sender, RoutedEventArgs e) => OpenDrawer(drawerBody.Visibility != Visibility.Visible);
    private void OnQueueAddClick(object sender, RoutedEventArgs e) => AddCurrentToQueue();
    private void OnQueuePasteClick(object sender, RoutedEventArgs e) => Run(() => PasteToQueueAsync());
    private void OnQueueFileClick(object sender, RoutedEventArgs e) => Run(QueueFromFileAsync);

    /// <summary>빈 행 — 현재 item/mfg/months 로 Queue.Add.</summary>
    private void OnQueueAddRowClick(object sender, RoutedEventArgs e)
    {
        if (Queue.Running) return;
        Queue.Add(new QueueRow { Item = Inputs.Item ?? "", Mfg = Inputs.Mfg ?? "", Months = Inputs.Months > 0 ? Inputs.Months : 36 });
        RevalidateQueue();
        OpenDrawer(true);
    }

    private void OnQueueSerialClick(object sender, RoutedEventArgs e) => Run(ExpandSerialAsync);

    /// <summary>선택 행을 바로 아래에 복제 (batch.js duplicate). 선택 없으면 "복제할 행을 선택하세요."</summary>
    private void OnQueueDupClick(object sender, RoutedEventArgs e)
    {
        if (Queue.Running) return;
        if (SelectedQueueRows.Count == 0) { Toast("복제할 행을 선택하세요.", ToastLevel.Warn); return; }
        var next = new List<QueueRow>();
        var made = 0;
        foreach (var r in Queue.Rows.ToList())
        {
            next.Add(r);
            if (SelectedQueueRows.Contains(r.Id)) { next.Add(r.CloneInputs()); made++; }
        }
        Queue.Clear();
        Queue.AddMany(next);
        RevalidateQueue();
        SetStatus($"{made}행을 복제했습니다 — 총 {Queue.Count}행");
    }

    /// <summary>선택 행 삭제. 선택 없으면 "삭제할 행을 선택하세요."</summary>
    private void OnQueueDelClick(object sender, RoutedEventArgs e)
    {
        if (Queue.Running) return;
        if (SelectedQueueRows.Count == 0) { Toast("삭제할 행을 선택하세요.", ToastLevel.Warn); return; }
        foreach (var id in SelectedQueueRows.ToList())
        {
            if (ActiveQueueId == id) { ActiveQueueId = null; jobOrigin.Text = "새 작업"; }
            Queue.Remove(id);
        }
        SelectedQueueRows.Clear();
        RevalidateQueue();
    }

    /// <summary>완료 행 비우기 (batch.js removeCompleted).</summary>
    private void OnQueueClearDoneClick(object sender, RoutedEventArgs e)
    {
        if (Queue.Running) return;
        foreach (var r in Queue.Rows.Where(x => x.Status == "done").ToList())
        {
            if (ActiveQueueId == r.Id) { ActiveQueueId = null; jobOrigin.Text = "새 작업"; }
            SelectedQueueRows.Remove(r.Id);
            Queue.Remove(r.Id);
        }
        RevalidateQueue();
    }

    /// <summary>전체 비우기 — 확인("큐의 N행을 모두 지울까요?", danger, "비우기") 후 Queue.Clear.</summary>
    private void OnQueueClearClick(object sender, RoutedEventArgs e)
    {
        if (Queue.Running || Queue.Count == 0) return;
        Run(async () =>
        {
            var ok = await Dialogs.Confirm(this, $"큐의 {Queue.Count}행을 모두 지울까요?", "큐 비우기", "비우기", "취소", danger: true);
            if (!ok) return;
            Queue.Clear();
            SelectedQueueRows.Clear();
            ActiveQueueId = null;
            jobOrigin.Text = "새 작업";
            RevalidateQueue();
        });
    }

    /// <summary>selQueueMode → Settings.Output.Mode 저장.</summary>
    private void OnQueueModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        var v = TagOf(selQueueMode);
        if (string.IsNullOrEmpty(v) || v == Settings.Output.Mode) return;
        Settings.Output.Mode = v;
        try { SettingsStore.Save(Settings); }
        catch (Exception ex) { AppLog.Warn("설정 저장 실패: " + ex.Message); }
    }

    /// <summary>CSV 저장 — 비었으면 "큐가 비어 있습니다.", SaveFileDialog "작업큐_{yyyy-MM-dd}.csv".</summary>
    private void OnQueueExportClick(object sender, RoutedEventArgs e)
    {
        if (Queue.Count == 0) { Toast("큐가 비어 있습니다.", ToastLevel.Warn); return; }
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "큐를 CSV로 저장", FileName = $"작업큐_{DateTime.Now:yyyy-MM-dd}.csv", DefaultExt = ".csv",
            Filter = "CSV (*.csv)|*.csv|모든 파일 (*.*)|*.*",
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            // ToCsv 가 BOM 을 앞에 붙이므로 인코더는 BOM 없이 쓴다
            File.WriteAllText(dlg.FileName, Queue.ToCsv(), new UTF8Encoding(false));
            Toast("CSV로 저장했습니다.", ToastLevel.Ok);
            SetStatus($"큐 {Queue.Count}행을 CSV로 저장했습니다: {Path.GetFileName(dlg.FileName)}");
        }
        catch (Exception ex)
        {
            AppLog.Error("큐 CSV 저장 실패", ex);
            SetStatus("CSV 저장 실패: " + ex.Message, StatusLevel.Error);
            Toast("CSV 저장 실패: " + ex.Message, ToastLevel.Err);
        }
    }

    /// <summary>드로어의 [큐 N장 출력] — 왼쪽 [큐 N장 출력] 과 같은 명령 (모드 전환이 아니다).</summary>
    private void OnQueueRunClick(object sender, RoutedEventArgs e) => Run(DoPrintQueueAsync);

    private void OnQueuePauseClick(object sender, RoutedEventArgs e)
    {
        if (!Queue.Running) return;
        if (Queue.Paused) { Queue.Resume(); SetStatus("출력을 계속합니다."); }
        else { Queue.Pause(); SetStatus("출력을 일시정지했습니다. [계속] 을 누르면 이어서 출력합니다.", StatusLevel.Warn); }
        RenderQueue();
    }

    private void OnQueueCancelClick(object sender, RoutedEventArgs e)
    {
        if (!Queue.Running) return;
        Queue.Cancel();
        SetStatus("출력 중지를 요청했습니다…", StatusLevel.Warn);
    }

    /// <summary>행 더블클릭 → LoadRowToInputs.</summary>
    private void OnQueueRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Queue.Running) return;
        var row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject ?? queueGrid);
        if (row?.Item is not QueueRowView v || v.Row is null) return;
        // 두 번째 클릭이 셀 편집을 열었을 수 있다 — 편집은 접고 행을 불러온다
        queueGrid.CancelEdit(DataGridEditingUnit.Row);
        e.Handled = true;
        if (ActiveQueueId == v.Id) return;
        LoadRowToInputs(v.Row);
    }

    /// <summary>SelectedQueueRows 동기화.</summary>
    private void OnQueueSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingQueueSelection) return;
        SelectedQueueRows.Clear();
        foreach (var it in queueGrid.SelectedItems) if (it is QueueRowView v) SelectedQueueRows.Add(v.Id);
    }

    /* ---- 대화상자 도우미 ---- */

    private Window NewDialog(string title, double width) => new()
    {
        Title = title, Width = width, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
        WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
        Background = Res<Brush>("PaperBrush"), Owner = this,
    };

    private static StackPanel ButtonRow(params UIElement[] buttons)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        foreach (var b in buttons) p.Children.Add(b);
        return p;
    }

    private static TextBox NumBox(string text) => new()
    {
        Text = text, Width = 110, FontFamily = new FontFamily("Consolas"), FontSize = 14, HorizontalAlignment = HorizontalAlignment.Left,
    };

    private static void AddFormRow(Grid form, string label, FrameworkElement ctl)
    {
        var r = form.RowDefinitions.Count;
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var l = new TextBlock { Text = label, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        Grid.SetRow(l, r); Grid.SetColumn(l, 0);
        Grid.SetRow(ctl, r); Grid.SetColumn(ctl, 1);
        form.Children.Add(l);
        form.Children.Add(ctl);
    }
}

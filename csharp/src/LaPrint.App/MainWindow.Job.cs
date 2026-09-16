// 작업 입력 · 라벨DB · DB 참조값 · 출력 전 점검 · 칩 (app.js: bind 입력부, setDbProfile, loadDbFromFile, applyDb, autoLoadDb,
// showItemPop/pickItem, renderRefTable, renderChecks, updatePrintButton, updateChips, loadSampleData, noticeDbQuality).
using System.Collections;
using System.IO;
using System.Resources;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LaPrint.App.Services;
using LaPrint.Core.Data;
using LaPrint.Core.Export;
using LaPrint.Core.Imaging;
using LaPrint.Core.Storage;

namespace LaPrint.App;

/// <summary>DB 참조값 표의 한 줄 (tblRef ItemTemplate: Label · Value · Empty).</summary>
public sealed record RefRow(string Label, string Value, bool Empty);

/// <summary>출력 전 점검의 한 줄 (checks ItemTemplate: Icon ✓/⚠/✕ · Msg · Level error|warn|ok|info).</summary>
public sealed record CheckRow(string Icon, string Msg, string Level, string? ObjId = null);

public partial class MainWindow
{
    /// <summary>SyncInputsToUi 가 컨트롤을 채우는 동안 TextChanged 가 되돌아 들어오지 않게.</summary>
    private bool _syncingInputs;
    private bool _syncingProfile;
    /// <summary>품목 검색 팝업의 키보드 선택 위치 (app.js popIndex).</summary>
    private int _popIndex = -1;
    private readonly DispatcherTimer _popHideTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    /// <summary>'샘플 데이터로 체험' 이 풀어 놓은 그림 폴더 — 이미지 폴더가 지정되지 않았을 때만 쓴다 (app.js readImageSmart).</summary>
    private string? _sampleImgDir;

    /// <summary>DB 참조값 표 정의 (app.js REF_ROWS).</summary>
    private static readonly (string Label, string? Key, Func<Fields, string>? Fn)[] RefRows =
    {
        ("제품명", "PRODUCT", null), ("상단 문구", "MDR", null), ("규격 REF", "REF", null), ("GTIN", "GTIN", null),
        ("스텐트", null, f => string.Join(" × ", new[] { f.Get("STENT_OD"), f.Get("STENT_LEN") }.Where(s => s.Length > 0))),
        ("딜리버리", null, f => string.Join(" ", new[] { f.Get("DD_FR"), f.Get("DD_MM").Length > 0 ? $"({f.Get("DD_MM")})" : "", f.Get("DD_LEN") }.Where(s => s.Length > 0))),
        ("가이드와이어", null, f => string.Join(" ", new[] { f.Get("GW_INCH"), f.Get("GW_MM").Length > 0 ? $"({f.Get("GW_MM")})" : "" }.Where(s => s.Length > 0))),
        ("Cover", "COVER", null), ("LIFE TIME", "LIFETIME", null),
    };

    /// <summary>selProfile 채우기(Profiles.All) 등 초기화.</summary>
    private void InitJob()
    {
        _syncingProfile = true;
        selProfile.Items.Clear();
        foreach (var p in Profiles.All)
            selProfile.Items.Add(new ComboBoxItem { Content = p.Name, Tag = p.Key, ToolTip = p.Desc });
        _syncingProfile = false;
        _popHideTimer.Tick += (_, _) => { _popHideTimer.Stop(); HideItemPop(); };
        inpItem.ToolTip = "예: 16-0401";
    }

    /// <summary>Inputs/Copies → 입력 컨트롤 (app.js syncInputsToUi). 끝에 SyncLabelSizeUi().</summary>
    private void SyncInputsToUi()
    {
        _syncingInputs = true;
        try
        {
            inpItem.Text = Inputs.Item ?? "";
            inpLot.Text = Inputs.Lot ?? "";
            inpSn.Text = Inputs.Sn ?? "";
            inpMfg.SelectedDate = FieldComputer.ParseIso(Inputs.Mfg);
            inpMonths.Text = (Inputs.Months > 0 ? Inputs.Months : 36).ToString();
            chkExpAuto.IsChecked = Inputs.ExpAuto;
            inpExp.IsEnabled = !Inputs.ExpAuto;
            inpExp.SelectedDate = FieldComputer.ParseIso(Inputs.Exp);
            inpCopies.Text = Math.Max(1, Copies).ToString();
        }
        finally { _syncingInputs = false; }
        SyncLabelSizeUi();
    }

    /// <summary>selProfile 선택 · BSC 배지(bscBadge) · 콤보 주황 테두리 (app.js syncProfileUi, DESIGN §4-2).</summary>
    private void SyncProfileUi()
    {
        _syncingProfile = true;
        try { SelectByTag(selProfile, ProfileKey); }
        finally { _syncingProfile = false; }
        var bsc = ProfileKey == "bsc";
        bscBadge.Visibility = bsc ? Visibility.Visible : Visibility.Collapsed;
        selProfile.BorderBrush = bsc ? Res<Brush>("OrangeBrush") : Res<Brush>("LineBrush");
        selProfile.BorderThickness = new Thickness(bsc ? 2 : 1);
    }

    /// <summary>입력 컨트롤 → JobInputs (Copies 는 필드에).</summary>
    private JobInputs ReadInputs()
    {
        var months = (int)Num(inpMonths, 36);
        if (months <= 0) months = 36;
        var expAuto = chkExpAuto.IsChecked != false;
        var copies = (int)Num(inpCopies, 1);
        Copies = Math.Max(1, copies);
        var exp = expAuto ? Inputs.Exp : FieldComputer.FmtIso(inpExp.SelectedDate);
        return new JobInputs(
            inpItem.Text.Trim(), inpLot.Text.Trim(), inpSn.Text.Trim(),
            FieldComputer.FmtIso(inpMfg.SelectedDate), months, expAuto, exp ?? "");
    }

    /* ================= 품목 검색 ================= */

    /// <summary>품목번호 검색 팝업(itemPop/itemPopList) — Index.Search(inpItem.Text).</summary>
    private void ShowItemPop()
    {
        _popHideTimer.Stop();
        var q = inpItem.Text.Trim();
        itemPopList.Items.Clear();
        if (Index is null)
        {
            itemPopList.Items.Add(PopNote("라벨DB를 먼저 불러오세요.\n상단 ⚙ 설정에서 DB 폴더를 지정합니다."));
            itemPop.IsOpen = true;
            return;
        }
        var list = Index.Search(q, 40);
        if (list.Count == 0)
        {
            itemPopList.Items.Add(PopNote($"\"{q}\"에 해당하는 품목이 없습니다."));
            itemPop.IsOpen = true;
            return;
        }
        for (var i = 0; i < list.Count; i++)
        {
            var it = list[i];
            var sub = string.Join(" · ", new[] { it.Ref, it.Name }.Where(s => !string.IsNullOrEmpty(s)));
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = it.Key, FontFamily = new FontFamily("Consolas"), FontWeight = FontWeights.Bold, FontSize = 13 });
            panel.Children.Add(new TextBlock { Text = sub.Length > 0 ? sub : "(정보 없음)", FontSize = 11, Foreground = Res<Brush>("Ink3Brush"), TextTrimming = TextTrimming.CharacterEllipsis });
            itemPopList.Items.Add(new ListBoxItem { Content = panel, Tag = it.Key, Padding = new Thickness(8, 4, 8, 4) });
        }
        if (_popIndex >= 0 && _popIndex < itemPopList.Items.Count) itemPopList.SelectedIndex = _popIndex;
        itemPop.IsOpen = true;
    }

    private static ListBoxItem PopNote(string text) => new()
    {
        Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12 },
        IsEnabled = false, IsHitTestVisible = false, Padding = new Thickness(10, 8, 10, 8),
    };

    private void HideItemPop()
    {
        _popHideTimer.Stop();
        itemPop.IsOpen = false;
        _popIndex = -1;
    }

    /// <summary>검색 결과에서 품목을 고른다 → Inputs.Item, HideItemPop, Refresh, inpLot 포커스.</summary>
    private void PickItem(string key)
    {
        _syncingInputs = true;
        try { inpItem.Text = key; }
        finally { _syncingInputs = false; }
        Inputs = Inputs with { Item = key };
        HideItemPop();
        Refresh();
        inpLot.Focus();
        inpLot.SelectAll();
        var found = Index is not null && Index.ByRef.ContainsKey(key);
        SetStatus(found ? $"{key} 조회 완료 · {Fields.Get("PRODUCT")}" : $"{key} — 라벨DB에 없는 품목입니다",
            found ? StatusLevel.Info : StatusLevel.Warn);
    }

    /* ================= DB 참조값 표 · 점검 · 출력 버튼 · 칩 ================= */

    /// <summary>tblRef (RefRow 목록) + udiFull.</summary>
    private void RenderRefTable()
    {
        var rows = new List<RefRow>(RefRows.Length);
        foreach (var (label, key, fn) in RefRows)
        {
            var v = fn is not null ? fn(Fields) : Fields.Get(key!);
            rows.Add(new RefRow(label, v.Length > 0 ? v : "—", v.Length == 0));
        }
        tblRef.ItemsSource = rows;
        var udi = Fields.Get("UDI_FULL");
        udiFull.Text = udi.Length > 0 ? udi : "—";
        // 자동 계산된 유효일을 입력칸에도 비춘다 (app.js onInput 의 setTimeout 부분)
        if (Inputs.ExpAuto)
        {
            _syncingInputs = true;
            try { inpExp.SelectedDate = Fields.ExpDate ?? FieldComputer.ParseIso(Inputs.Exp); }
            finally { _syncingInputs = false; }
        }
    }

    /// <summary>Preflight.Run → LastPreflight → checks(CheckRow) · checkSummary. 잠금 해제 변경(LayoutDirty)이면 '검증되지 않은 서식' 경고.</summary>
    private void RenderChecks()
    {
        PreflightResult pf;
        try
        {
            Editor.Context.Objects = Template.Objects;
            pf = Preflight.Run(Template, Editor.Context, Settings.Validation, Settings.Output.Dpi);
        }
        catch (Exception ex)
        {
            AppLog.Warn("출력 전 점검 실패: " + ex.Message);
            var err = new Issue("error", "PREFLIGHT_FAIL", "출력 전 점검 실패: " + ex.Message);
            pf = new PreflightResult(new[] { err }, new[] { err }, Array.Empty<Issue>(), Array.Empty<Issue>());
        }
        LastPreflight = pf;

        // 객체별 이슈를 편집기(점선 테두리)에 넘긴다
        Editor.Issues.Clear();
        foreach (var i in pf.All)
        {
            if (string.IsNullOrEmpty(i.ObjId)) continue;
            Editor.Issues[i.ObjId] = Editor.Issues.TryGetValue(i.ObjId, out var prev) ? prev + "\n" + i.Msg : i.Msg;
        }

        var grouped = new Dictionary<string, (string Level, string Msg, string? ObjId, int N)>();
        var order = new List<string>();
        foreach (var i in pf.All)
        {
            if (i.Level != "error" && i.Level != "warn") continue;
            var k = i.Level + "|" + i.Code;
            if (!grouped.TryGetValue(k, out var g)) { g = (i.Level, i.Msg, i.ObjId, 0); order.Add(k); }
            grouped[k] = (g.Level, g.Msg, g.ObjId, g.N + 1);
        }
        var items = order.Select(k => grouped[k]).OrderBy(g => g.Level == "error" ? 0 : 1).ToList();

        var rows = new List<CheckRow>();
        if (items.Count == 0)
            rows.Add(new CheckRow("✓", "모든 점검을 통과했습니다.", "ok"));
        else
        {
            foreach (var it in items.Take(9))
                rows.Add(new CheckRow(it.Level == "error" ? "✕" : "⚠", it.Msg + (it.N > 1 ? $" ({it.N}건)" : ""), it.Level == "error" ? "error" : "warn", it.ObjId));
            if (items.Count > 9) rows.Add(new CheckRow("…", $"그 외 {items.Count - 9}종", "info"));
        }
        // 잠금 해제 상태에서 레이아웃을 건드렸다면 경고를 하나 더 얹는다
        if (LayoutDirty)
            rows.Insert(0, new CheckRow("⚠", "레이아웃이 수정되었습니다. 검증된 서식이 아닙니다 — 서식으로 저장하거나 되돌린 뒤 출력하세요.", "warn"));
        checks.ItemsSource = rows;

        var errN = pf.Errors.Count;
        var warnN = pf.Warnings.Count + (LayoutDirty ? 1 : 0);
        checkSummary.Text = errN > 0 ? $"오류 {errN}" : (warnN > 0 ? $"경고 {warnN}" : "통과");
        checkSummary.Foreground = errN > 0 ? Res<Brush>("FailBrush") : (warnN > 0 ? Res<Brush>("WarnBrush") : Res<Brush>("PassBrush"));
        Editor.Invalidate();
    }

    /// <summary>
    /// 출력 컨트롤 갱신.
    /// ★ 안전 설계: [이 라벨 1장]과 [큐 N장]은 언제나 서로 다른 버튼이다 (DESIGN §4-1).
    ///   Ctrl+P = 지금 입력한 값으로 1장 · Ctrl+Shift+P = 큐 전체.
    /// </summary>
    private void UpdatePrintButton()
    {
        var n = Queue.Count;
        var total = Queue.TotalLabels;
        var running = Queue.Running;
        var isZebra = Settings.Output.Target == "zebra";
        var verb = isZebra ? "ZEBRA 출력" : "출력";
        var blocked = (LastPreflight?.Errors.Count ?? 0) > 0;

        btnPrint.Content = $"이 라벨 1장 {verb}";
        btnPrint.IsEnabled = !blocked && !running;
        btnPrintQueue.Visibility = n == 0 ? Visibility.Collapsed : Visibility.Visible;
        btnPrintQueue.Content = $"큐 {total}장 {verb}";
        btnPrintQueue.IsEnabled = !running;
        btnPrintQueue.Style = n > 0 ? Res<Style>("PrimaryButton") : null;
        btnPrint.Style = n == 0 ? Res<Style>("PrimaryButton") : null;

        if (running) { printHint.Text = "출력 중…"; printHint.Foreground = Res<Brush>("Ink3Brush"); }
        else if (blocked) { printHint.Text = "오류를 해결해야 1장 출력을 할 수 있습니다"; printHint.Foreground = Res<Brush>("FailBrush"); }
        else if (n > 0) { printHint.Text = $"큐 {n}행 · 총 {total}장 대기 중"; printHint.Foreground = Res<Brush>("Ink3Brush"); }
        else
        {
            var dir = DirName(Settings.Paths.OutDir);
            printHint.Text = dir is not null ? $"→ {dir}" : "저장 폴더 미지정 → 다운로드";
            printHint.Foreground = Res<Brush>("Ink3Brush");
        }
    }

    /// <summary>폴더 경로의 표시 이름 (마지막 폴더 이름). 비었으면 null.</summary>
    private static string? DirName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var p = path.Trim().TrimEnd('\\', '/');
        var name = Path.GetFileName(p);
        return string.IsNullOrEmpty(name) ? p : name;
    }

    /// <summary>저장 폴더 — 지정되지 않았으면 사용자 다운로드 폴더 (브라우저판의 '다운로드' 에 해당).</summary>
    internal string OutDirOrDownloads()
    {
        if (!string.IsNullOrWhiteSpace(Settings.Paths.OutDir)) return Settings.Paths.OutDir;
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    /// <summary>chipDb/chipImg/chipOut 점 색·문구·툴팁 (app.js updateChips).</summary>
    private void UpdateChips()
    {
        var pass = Res<Brush>("PassBrush");
        var warn = Res<Brush>("WarnLineBrush");
        var bad = Res<Brush>("DangerBrush");
        var none = Res<Brush>("Ink3Brush");
        var profileName = Profiles.Get(ProfileKey).Name;

        if (Db is not null)
        {
            var usable = Index?.ByRef.Count ?? Db.Rows.Count;
            chipDbDot.Fill = DbIssues.Count > 0 ? warn : pass;
            chipDbText.Text = $"{profileName} · {usable:N0}" + (DbIssues.Count > 0 ? " ⚠" : "");
            chipDb.ToolTip = $"{DbFileName}\n시트: {Db.Sheet}\n읽은 행 {Db.Rows.Count:N0} · 쓸 수 있는 품목 {usable:N0}"
                             + (DbIssues.Count > 0 ? "\n\n확인 필요:\n· " + string.Join("\n· ", DbIssues) : "");
        }
        else
        {
            chipDbDot.Fill = bad;
            chipDbText.Text = "DB 없음";
            chipDb.ToolTip = "라벨DB가 없습니다. 클릭해 설정에서 지정하세요.";
        }

        var img = DirName(Settings.Paths.ImgDir);
        chipImgDot.Fill = img is not null ? pass : warn;
        chipImgText.Text = img ?? "이미지 폴더";
        chipImg.ToolTip = img is not null ? $"이미지 폴더: {Settings.Paths.ImgDir}" : "이미지 폴더가 지정되지 않았습니다.";

        var outd = DirName(Settings.Paths.OutDir);
        if (outd is not null)
        {
            chipOutDot.Fill = pass;
            chipOutText.Text = outd;
            chipOut.ToolTip = $"저장 폴더: {Settings.Paths.OutDir}";
        }
        else
        {
            chipOutDot.Fill = none;
            chipOutText.Text = "저장 폴더 (다운로드)";
            chipOut.ToolTip = "저장 폴더가 지정되지 않았습니다.";
        }
    }

    /* ================= 라벨DB ================= */

    /// <summary>출고 구분 변경 — ProfileKey, Settings.Data.Profile, ApplyProfileToMap, DB 재로딩, RevalidateQueue (app.js setDbProfile).</summary>
    private async Task SetDbProfileAsync(string key, bool silent = false)
    {
        ProfileKey = Profiles.Get(key).Key;
        Settings.Data.Profile = ProfileKey;
        try { SettingsStore.Save(Settings); }
        catch (Exception ex) { AppLog.Warn("설정 저장 실패: " + ex.Message); }
        ApplyProfileToMap();
        Db = null; Index = null; DbFileName = null; DbIssues.Clear();
        UpdateChips();
        try { await AutoLoadDbAsync(); }
        catch (Exception ex) { SetStatus("라벨DB 자동 로딩 실패: " + ex.Message, StatusLevel.Warn); }
        Refresh();
        RevalidateQueue();
        SyncProfileUi();
        if (!silent)
        {
            var name = Profiles.Get(ProfileKey).Name;
            SetStatus(Db is not null
                ? $"출고 구분: {name} — {DbFileName}"
                : $"출고 구분: {name} — 이 구분의 라벨DB가 아직 지정되지 않았습니다.",
                Db is not null ? StatusLevel.Info : StatusLevel.Warn);
        }
    }

    /// <summary>파일에서 라벨DB 읽기 (DbCache 우선) → ApplyDb. 성공이면 true (app.js loadDbFromFile).</summary>
    private Task<bool> LoadDbFromFileAsync(string path, bool silent = false) => LoadDbFromFileAsync(path, silent, remember: true);

    /// <summary>remember 면 설정의 마지막 수정 시각을 갱신한다 (샘플 DB 는 갱신하지 않는다).</summary>
    private async Task<bool> LoadDbFromFileAsync(string path, bool silent, bool remember)
    {
        var name = Path.GetFileName(path);
        try
        {
            SetStatus($"DB 읽는 중… {name}");
            var profile = ProfileKey;
            var keyCol = Map.KeyCol;
            var cacheKey = remember ? profile : profile + "-sample";
            var lastWrite = File.GetLastWriteTime(path);
            // 12,000행 파싱은 UI 스레드를 멈추므로 뒤에서 한다 (같은 수정 시각이면 캐시)
            var db = await Task.Run(() =>
            {
                var cached = DbCache.Get(cacheKey, lastWrite);
                if (cached is not null) return cached;
                LabelDb parsed;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    parsed = LabelDbLoader.Load(fs, name, keyCol);
                DbCache.Put(cacheKey, parsed, lastWrite);
                return parsed;
            });
            ApplyDb(db, name);
            if (remember)
            {
                if (profile == "bsc") Settings.Paths.DbLastModifiedBsc = lastWrite;
                else Settings.Paths.DbLastModified = lastWrite;
                try { SettingsStore.Save(Settings); } catch (Exception ex) { AppLog.Warn("설정 저장 실패: " + ex.Message); }
            }
            if (!silent) Toast($"라벨DB 로딩 완료 — {db.Rows.Count:N0}개 품목", ToastLevel.Ok);
            SetStatus($"라벨DB 로딩 완료: {name} · 시트 \"{db.Sheet}\" · {db.Rows.Count:N0}개 품목");
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error("DB 로딩 실패: " + path, ex);
            SetStatus("DB 로딩 실패: " + ex.Message, StatusLevel.Error);
            Toast("DB 로딩 실패: " + ex.Message, ToastLevel.Err);
            return false;
        }
    }

    /// <summary>Db/Index/DbFileName/DbIssues 갱신 → NoticeDbQuality → UpdateChips → RevalidateQueue → Refresh (app.js applyDb).</summary>
    private void ApplyDb(LabelDb db, string fileName)
    {
        Db = db;
        DbFileName = fileName;
        Index = LabelIndex.Build(db.Rows, Map);
        NoticeDbQuality();
        UpdateChips();
        RevalidateQueue();
        Refresh();
    }

    /// <summary>설정의 dbDir/dbFileName(프로필별)로 시작 시 자동 로딩 (app.js autoLoadDb).</summary>
    private async Task AutoLoadDbAsync()
    {
        if (!Settings.Paths.DbAutoLoad) return;
        var bsc = ProfileKey == "bsc";
        var fileName = bsc ? Settings.Paths.DbFileNameBsc : Settings.Paths.DbFileName;
        var stored = bsc ? Settings.Paths.DbLastModifiedBsc : Settings.Paths.DbLastModified;
        var path = string.IsNullOrWhiteSpace(Settings.Paths.DbDir) || string.IsNullOrWhiteSpace(fileName)
            ? null : Path.Combine(Settings.Paths.DbDir, fileName);

        if (path is not null && File.Exists(path))
        {
            var lastWrite = File.GetLastWriteTime(path);
            var changed = stored is null || Math.Abs((stored.Value - lastWrite).TotalSeconds) >= 1;
            if (!changed && Db is not null) { SetStatus("라벨DB 변경 없음 — 캐시 사용"); return; }
            var hadStamp = stored is not null;
            var ok = await LoadDbFromFileAsync(path, silent: !changed);
            if (ok && changed && hadStamp && Db is not null) Toast("라벨DB가 갱신되어 다시 읽었습니다.", ToastLevel.Ok);
            return;
        }
        // 파일이 없어도(네트워크 끊김 등) 저장된 사본이 있으면 그것으로 시작한다
        if (stored is not null && !string.IsNullOrWhiteSpace(fileName))
        {
            var cached = await Task.Run(() => DbCache.Get(ProfileKey, stored.Value));
            if (cached is not null)
            {
                ApplyDb(cached, fileName);
                SetStatus($"라벨DB (저장된 사본): {fileName} · {cached.Rows.Count:N0}개 품목");
            }
        }
    }

    /// <summary>동봉 샘플 DB(샘플_라벨DB.xlsx)·이미지로 시작 (app.js loadSampleData).</summary>
    private async Task LoadSampleDataAsync()
    {
        try
        {
            SetStatus("샘플 데이터를 불러오는 중…");
            var dir = await Task.Run(ExtractSampleAssets);
            var dbPath = Path.Combine(dir, SampleDbName);
            if (!File.Exists(dbPath)) throw new FileNotFoundException("샘플 파일을 찾을 수 없습니다.");
            // 이미지 폴더가 지정돼 있으면 그 폴더만 본다 — 같은 이름의 다른 제품 그림이 찍히면 안 된다
            _sampleImgDir = Path.Combine(dir, "images");
            if (string.IsNullOrWhiteSpace(Settings.Paths.ImgDir)) RebuildImageStore();
            var ok = await LoadDbFromFileAsync(dbPath, silent: false, remember: false);
            if (!ok) return;
            Inputs = Inputs with { Item = "16-0401", Lot = "26041086", Sn = "1", Mfg = DateTime.Today.ToString("yyyy-MM-dd") };
            SyncInputsToUi();
            Refresh();
            await LoadSlotImagesAsync(true);
            Toast("샘플 데이터를 불러왔습니다. 실제 DB와 이미지 폴더는 ⚙ 설정에서 지정하세요.", ToastLevel.Ok);
        }
        catch (Exception ex)
        {
            AppLog.Error("샘플 데이터 로딩 실패", ex);
            Toast("샘플 데이터를 불러오지 못했습니다: " + ex.Message, ToastLevel.Err);
        }
    }

    private const string SampleDbName = "샘플_라벨DB.xlsx";

    /// <summary>앱 리소스(Assets/sample/**)를 %APPDATA%\LaPrint\sample\ 로 풀어 놓고 그 폴더를 돌려준다.</summary>
    private static string ExtractSampleAssets()
    {
        var dir = Path.Combine(AppPaths.Root, "sample");
        Directory.CreateDirectory(Path.Combine(dir, "images"));
        var asm = typeof(MainWindow).Assembly;
        var resName = asm.GetName().Name + ".g.resources";
        using var stream = asm.GetManifestResourceStream(resName)
                           ?? throw new FileNotFoundException("동봉된 샘플 리소스를 찾을 수 없습니다.");
        using var reader = new ResourceReader(stream);
        var n = 0;
        foreach (DictionaryEntry e in reader)
        {
            var key = e.Key as string ?? "";
            if (!key.StartsWith("assets/sample/", StringComparison.OrdinalIgnoreCase)) continue;
            var rel = Uri.UnescapeDataString(key["assets/sample/".Length..]);
            // 리소스 키는 소문자로 저장되므로 원래 이름을 아는 파일은 그 이름으로 되돌린다
            var target = rel.Equals(SampleDbName, StringComparison.OrdinalIgnoreCase) ? SampleDbName : rel;
            var path = Path.Combine(dir, target.Replace('/', Path.DirectorySeparatorChar));
            if (e.Value is not Stream src) continue;
            if (File.Exists(path) && new FileInfo(path).Length == src.Length) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var dst = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            src.CopyTo(dst);
            n++;
        }
        AppLog.Info($"샘플 데이터 {n}개 파일을 풀었습니다: {dir}");
        return dir;
    }

    /// <summary>이미지 폴더 설정(또는 샘플 폴더)으로 ImageStore 를 다시 만든다.</summary>
    internal void RebuildImageStore()
    {
        var folder = !string.IsNullOrWhiteSpace(Settings.Paths.ImgDir) ? Settings.Paths.ImgDir : _sampleImgDir;
        if (folder is null) { Images = null; return; }
        if (Images is not null && string.Equals(Images.Folder, folder, StringComparison.OrdinalIgnoreCase))
        {
            try { Images.Invalidate(); } catch (Exception ex) { AppLog.Warn("이미지 캐시 비우기 실패: " + ex.Message); }
            return;
        }
        Images = new ImageStore(folder);
    }

    /// <summary>
    /// 라벨DB 자체의 품질 문제를 알린다. 실제 DB에는 품목번호가 '0' 인 빈 행이 수백 줄, 같은 품목번호가 여러 번
    /// 나오는 행이 섞여 있다. 조용히 넘기면 엉뚱한 제품 정보가 라벨에 찍힌다 (app.js noticeDbQuality).
    /// </summary>
    private void NoticeDbQuality()
    {
        DbIssues.Clear();
        if (Index is null) return;
        if (Index.Skipped > 0) DbIssues.Add($"품목번호가 비었거나 '0' 인 행 {Index.Skipped:N0}줄을 건너뛰었습니다");
        if (Index.Dups.Count > 0)
        {
            var top = string.Join(", ", Index.Dups.Take(3).Select(d => $"{d.Key}({d.Count}번)"));
            DbIssues.Add($"품목번호가 중복된 항목 {Index.Dups.Count}종 — {top}{(Index.Dups.Count > 3 ? " 외" : "")}. 먼저 나온 행을 씁니다");
        }
        if (DbIssues.Count > 0)
        {
            SetStatus($"라벨DB 확인 필요 — {string.Join(" · ", DbIssues)}", StatusLevel.Warn);
            Dialogs.Toast(this, $"라벨DB에 확인할 점이 {DbIssues.Count}가지 있습니다. 상태줄을 보세요.", ToastLevel.Warn, 6000);
        }
    }

    /* ---- 입력 핸들러 ---- */

    /// <summary>selProfile — 큐가 있으면 확인("출고 구분을 … 으로 바꾸면 큐에 쌓인 N행을 다시 검증합니다. 계속할까요?") 후 SetDbProfileAsync.</summary>
    private void OnProfileChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingProfile || !IsLoaded) return;
        var v = TagOf(selProfile);
        if (string.IsNullOrEmpty(v) || v == ProfileKey) return;
        Run(async () =>
        {
            var def = Profiles.Get(v);
            if (Queue.Count > 0)
            {
                var ok = await Dialogs.Confirm(this,
                    $"출고 구분을 \"{def.Name}\" 으로 바꾸면 큐에 쌓인 {Queue.Count}행을 다시 검증합니다. 계속할까요?",
                    "출고 구분 변경", "바꾸기", "취소");
                if (!ok) { SyncProfileUi(); return; }
            }
            await SetDbProfileAsync(v);
        });
    }

    /// <summary>inpItem 입력 — Inputs.Item, ShowItemPop, RefreshSoon.</summary>
    private void OnItemTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingInputs) return;
        Inputs = Inputs with { Item = inpItem.Text.Trim() };
        _popIndex = -1;
        if (inpItem.IsKeyboardFocusWithin) ShowItemPop();
        RefreshSoon();
        if (ActiveQueueId is not null) PushInputsToQueueRow();
    }

    private void OnItemGotFocus(object sender, KeyboardFocusChangedEventArgs e) => ShowItemPop();

    private void OnItemLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _popHideTimer.Stop();
        _popHideTimer.Start();
    }

    /// <summary>inpItem ↑↓ Enter Esc — 팝업 탐색/선택/닫기.</summary>
    private void OnItemKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;   // Ctrl 조합은 창 단축키
        var opts = itemPop.IsOpen ? itemPopList.Items.OfType<ListBoxItem>().Where(i => i.IsEnabled).ToList() : new List<ListBoxItem>();
        if (e.Key is Key.Down or Key.Up)
        {
            if (opts.Count == 0) return;
            e.Handled = true;
            _popIndex = Math.Max(0, Math.Min(opts.Count - 1, _popIndex + (e.Key == Key.Down ? 1 : -1)));
            itemPopList.SelectedItem = opts[_popIndex];
            itemPopList.ScrollIntoView(opts[_popIndex]);
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            if (opts.Count > 0 && _popIndex >= 0 && _popIndex < opts.Count) PickItem((string)opts[_popIndex].Tag);
            else if (opts.Count == 1) PickItem((string)opts[0].Tag);
            else { HideItemPop(); inpLot.Focus(); }
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            HideItemPop();
        }
    }

    /// <summary>itemPopList 클릭 → PickItem.</summary>
    private void OnItemPopPick(object sender, MouseButtonEventArgs e)
    {
        var item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject ?? itemPopList)
                   ?? itemPopList.SelectedItem as ListBoxItem;
        if (item?.Tag is string key && item.IsEnabled)
        {
            e.Handled = true;
            PickItem(key);
        }
    }

    /// <summary>inpLot/inpSn/inpMfg/inpMonths/chkExpAuto/inpExp/inpCopies — Inputs 갱신, inpExp 활성, RefreshSoon, 큐 행 편집 중이면 Queue.Update + RevalidateQueue.</summary>
    private void OnInputChanged(object sender, RoutedEventArgs e)
    {
        if (_syncingInputs || !IsLoaded) return;
        Inputs = ReadInputs();
        inpExp.IsEnabled = !Inputs.ExpAuto;
        RefreshSoon();
        if (ActiveQueueId is not null) PushInputsToQueueRow();
    }

    /// <summary>큐 행을 편집 중이면 그 행에도 반영하고 다시 검증한다.</summary>
    private void PushInputsToQueueRow()
    {
        var id = ActiveQueueId;
        if (id is null) return;
        var inp = Inputs;
        var copies = Copies;
        Queue.Update(id, r =>
        {
            r.Item = inp.Item; r.Lot = inp.Lot; r.Sn = inp.Sn; r.Mfg = inp.Mfg;
            r.Months = inp.Months; r.ExpAuto = inp.ExpAuto; r.Exp = inp.Exp; r.Copies = copies;
        });
        RevalidateQueue();
    }

    /// <summary>inpLot Enter → inpMfg 포커스.</summary>
    private void OnLotKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            inpMfg.Focus();
        }
    }

    private void OnMapDataClick(object sender, RoutedEventArgs e) => Run(OpenDataMapperAsync);

    /// <summary>btnClearJob — lot/sn 비움, copies 1, ActiveQueueId null, jobOrigin "새 작업", SyncInputsToUi, Refresh, inpLot 포커스.</summary>
    private void OnClearJobClick(object sender, RoutedEventArgs e)
    {
        Inputs = Inputs with { Lot = "", Sn = "" };
        Copies = 1;
        ActiveQueueId = null;
        jobOrigin.Text = "새 작업";
        SyncInputsToUi();
        Refresh();
        inpLot.Focus();
    }
}

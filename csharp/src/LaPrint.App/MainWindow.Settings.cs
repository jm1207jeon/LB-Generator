// 설정 창 · 데이터 매칭 편집기 · 처음 설정 안내 · 도움말 (app.js openSettings/openDataMapper/maybeOnboard/showHelp/checkReconnect).
// 설정 창이 메인 창의 동작(지금 읽기 · 샘플 DB · 매칭 편집기 · 라벨 크기 · 용지 배치 · 떼어내기 …)을 부를 수 있도록
// internal 도우미(Settings* 접두)를 여기에 둔다.
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LaPrint.App.Services;
using LaPrint.App.Windows;
using LaPrint.Core.Data;
using LaPrint.Core.Export;
using LaPrint.Core.Model;
using LaPrint.Core.Storage;

namespace LaPrint.App;

public partial class MainWindow
{
    private bool _applyingSettings;

    /// <summary>SettingsWindow.ShowAsync(this, Settings, SettingsStore, History, page) → ApplyUiSettings, ApplyProfileToMap, Images 재생성, UpdateChips, Refresh (app.js openSettings).</summary>
    private async Task OpenSettingsAsync(string? page = null)
    {
        var dbBefore = (Settings.Paths.DbDir, Settings.Paths.DbFileName, Settings.Paths.DbFileNameBsc);
        await SettingsWindow.ShowAsync(this, Settings, SettingsStore, History, page);
        ApplyUiSettings();
        ApplyProfileToMap();
        RebuildImageStore();
        Editor.Context.Background = BgBitmap();
        Editor.Context.IncludeBg = Settings.Output.IncludeBg && Template.Label.BgInclude;
        UpdateChips();
        // 설정에서 DB 폴더·파일을 새로 골랐으면 (브라우저판의 [지금 읽기] 를 누르지 않았어도) 바로 읽어 준다
        var dbAfter = (Settings.Paths.DbDir, Settings.Paths.DbFileName, Settings.Paths.DbFileNameBsc);
        if (Settings.Paths.DbAutoLoad && (Db is null || dbBefore != dbAfter))
        {
            try { await AutoLoadDbAsync(); }
            catch (Exception ex) { SetStatus("라벨DB 자동 로딩 실패: " + ex.Message, StatusLevel.Warn); }
        }
        Refresh();
        await LoadSlotImagesAsync(true);
    }

    /// <summary>MapperWindow.OpenEditorAsync(this, MapperSource(Db…), Map, Row) → 적용이면 Settings.Data.Profiles[ProfileKey] 갱신·저장, 색인 재구축, Refresh (app.js openDataMapper).</summary>
    private Task OpenDataMapperAsync() => OpenDataMapperAsync(this);

    /// <summary>owner 를 지정하는 판 — 설정 창 위에서 열 때.</summary>
    private async Task OpenDataMapperAsync(Window owner)
    {
        if (Db is null || Db.Rows.Count == 0)
        {
            var go = await Dialogs.Confirm(owner, "라벨DB를 아직 불러오지 않았습니다. 샘플 데이터를 불러올까요?",
                "데이터 매칭", "샘플 불러오기", "그냥 열기");
            if (go) await LoadSampleDataAsync();
        }
        var src = MapperSourceOrNull();
        var ok = await MapperWindow.OpenEditorAsync(owner, src, Map, Row);
        if (!ok) return;
        Settings.Data.Profiles[ProfileKey] = new ProfileSettings { FieldMap = Map.Diff(), KeyCol = Map.KeyCol };
        try { SettingsStore.Save(Settings); }
        catch (Exception ex) { AppLog.Warn("설정 저장 실패: " + ex.Message); }
        if (Db is not null && Db.Rows.Count > 0) Index = LabelIndex.Build(Db.Rows, Map);
        Refresh();
        RenderRefTable();
        RevalidateQueue();
        SetStatus($"열 매칭을 적용했습니다 — 조회 키 {Map.KeyCol}열");
    }

    /// <summary>데이터 매칭 편집기·열 고르기에 넘길 DB 원본. DB 가 없으면 null.</summary>
    internal MapperSource? MapperSourceOrNull()
    {
        if (Db is null) return null;
        return new MapperSource(Db.Rows, Db.Header, Db.ColCount > 0 ? Db.ColCount : 55, Map.KeyCol, Db.Sheet, Row ?? Db.Rows.FirstOrDefault());
    }

    /// <summary>Settings.Ui.OnboardingDone 이 아니면 OnboardDialog → 동작 키(db/img/out/sample)에 따라 설정 열기 또는 LoadSampleDataAsync (app.js maybeOnboard/showOnboard).</summary>
    private async Task MaybeOnboardAsync()
    {
        if (Settings.Ui.OnboardingDone) return;
        if (Db is not null && !string.IsNullOrWhiteSpace(Settings.Paths.ImgDir)) { MarkOnboardingDone(); return; }
        await ShowOnboardAsync();
    }

    /// <summary>처음 설정 안내를 띄우고 고른 동작을 실행한다. 설정을 다녀온 뒤 아직 남은 단계가 있으면 다시 보인다.</summary>
    private async Task ShowOnboardAsync()
    {
        while (true)
        {
            var act = await OnboardDialog.ShowAsync(this, Db is not null,
                !string.IsNullOrWhiteSpace(Settings.Paths.ImgDir), !string.IsNullOrWhiteSpace(Settings.Paths.OutDir));
            switch (act)
            {
                case "db":
                case "img":
                case "out":
                    await OpenSettingsAsync("paths");
                    if (Db is not null && !string.IsNullOrWhiteSpace(Settings.Paths.ImgDir)) { MarkOnboardingDone(); return; }
                    continue;
                case "sample":
                    await LoadSampleDataAsync();
                    return;
                case "never":
                    MarkOnboardingDone();
                    return;
                default:
                    return;
            }
        }
    }

    private void MarkOnboardingDone()
    {
        Settings.Ui.OnboardingDone = true;
        try { SettingsStore.Save(Settings); }
        catch (Exception ex) { AppLog.Warn("설정 저장 실패: " + ex.Message); }
    }

    /// <summary>브라우저판의 폴더 권한 재연결 확인. C# 에서는 폴더가 권한을 잃지 않으므로 할 일이 없다 — 호출 순서를 지키기 위한 no-op.</summary>
    private Task CheckReconnectAsync() => Task.CompletedTask;

    /// <summary>F1 — 단축키 도움말 (app.js showHelp). Ctrl+P / Ctrl+Shift+P 는 DESIGN §4-1 의 고정된 뜻으로 적는다.</summary>
    private void ShowHelp()
    {
        var K = new (string Key, string Desc)[]
        {
            ("작업", ""),
            ("Ctrl + Enter", "현재 입력을 연속 작업 큐에 추가"),
            ("Ctrl + P", "이 라벨 1장 출력 — 큐와 관계없이 현재 입력 1장만"),
            ("Ctrl + Shift + P", "큐 전체 출력"),
            ("Ctrl + V (큐)", "엑셀에서 복사한 표를 큐로 붙여넣기"),
            ("Ctrl + ↓ / ↑", "큐 펼치기 / 접기"),
            ("화면", ""),
            ("Ctrl + L", "레이아웃 잠금 켜기/끄기"),
            ("F5", "실물 미리보기 (편집 보조선 숨김)"),
            ("Ctrl + 0 / Ctrl + 1", "화면 맞춤 / 실측 100%"),
            ("휠", "커서 기준 확대·축소"),
            ("Space + 드래그", "화면 이동"),
            ("Ctrl + ,", "설정"),
            ("편집 (잠금 해제 상태)", ""),
            ("Ctrl + Z / Ctrl + Shift + Z", "실행 취소 / 다시 실행"),
            ("Ctrl + D", "선택 객체 복제"),
            ("Ctrl + A", "전체 선택"),
            ("방향키", "0.1mm 이동 (Shift 1mm, Alt 0.01mm)"),
            ("Delete", "선택 객체 삭제"),
            ("Shift + 클릭", "다중 선택"),
            ("빈 곳 드래그", "사각형 선택"),
            ("Alt (드래그 중)", "스냅 일시 해제"),
            ("Tab", "다음 객체 선택"),
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var r = 0;
        foreach (var (key, desc) in K)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            if (desc.Length == 0)
            {
                var h = new TextBlock { Text = key, FontWeight = FontWeights.Bold, Foreground = Res<Brush>("AccentDeepBrush"), Margin = new Thickness(0, r == 0 ? 0 : 12, 0, 4) };
                Grid.SetRow(h, r); Grid.SetColumnSpan(h, 2);
                grid.Children.Add(h);
            }
            else
            {
                var kb = new Border
                {
                    Background = Res<Brush>("Paper2Brush"), BorderBrush = Res<Brush>("LineBrush"), BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(0, 2, 10, 2),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Child = new TextBlock { Text = key, FontFamily = new FontFamily("Consolas"), FontSize = 12 },
                };
                var d = new TextBlock { Text = desc, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
                Grid.SetRow(kb, r); Grid.SetRow(d, r); Grid.SetColumn(d, 1);
                grid.Children.Add(kb);
                grid.Children.Add(d);
            }
            r++;
        }
        Dialogs.Info(this, "단축키", grid, width: 520);
    }

    /// <summary>SettingsStore.Changed — 다른 창에서 저장했을 때 Settings 교체 + ApplyUiSettings + UpdateChips.</summary>
    private void OnSettingsChanged(AppSettings s)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => OnSettingsChanged(s)); return; }
        if (_applyingSettings) return;   // ApplyUiSettings 가 콤보를 바꾸며 다시 저장하는 되먹임을 끊는다
        _applyingSettings = true;
        try
        {
            Settings = s;
            if (IsLoaded)
            {
                ApplyUiSettings();
                UpdateChips();
            }
        }
        finally { _applyingSettings = false; }
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e) => Run(() => OpenSettingsAsync(null));
    private void OnHelpClick(object sender, RoutedEventArgs e) => ShowHelp();

    /// <summary>chipDb/chipImg/chipOut — Tag 페이지("paths")로 설정 열기.</summary>
    private void OnChipClick(object sender, RoutedEventArgs e)
        => Run(() => OpenSettingsAsync((sender as FrameworkElement)?.Tag as string ?? "paths"));

    /* ================= 설정 창이 부르는 도우미 ================= */

    /// <summary>설정 › 폴더 경로 [지금 읽기] — 프로필의 DB 파일을 읽는다. 다른 프로필이면 출고 구분도 바꾼다.</summary>
    internal async Task SettingsReloadDbAsync(string profileKey, Window owner)
    {
        var bsc = profileKey == "bsc";
        var fileName = bsc ? Settings.Paths.DbFileNameBsc : Settings.Paths.DbFileName;
        var path = string.IsNullOrWhiteSpace(Settings.Paths.DbDir) || string.IsNullOrWhiteSpace(fileName)
            ? null : Path.Combine(Settings.Paths.DbDir, fileName);
        if (path is null || !File.Exists(path)) { Dialogs.Toast(owner, "DB 폴더와 파일을 먼저 지정하세요.", ToastLevel.Warn); return; }
        if (profileKey != ProfileKey) await SetDbProfileAsync(profileKey, silent: true);
        await LoadDbFromFileAsync(path);
        SyncProfileUi();
        UpdateChips();
    }

    /// <summary>설정 › [파일에서 직접 불러오기…].</summary>
    internal Task<bool> SettingsLoadDbFileAsync(string path) => LoadDbFromFileAsync(path, silent: false, remember: false);

    /// <summary>설정 › [샘플 DB 불러오기] / 처음 안내 [샘플 데이터로 체험].</summary>
    internal Task SettingsLoadSampleAsync() => LoadSampleDataAsync();

    /// <summary>설정 › 데이터 매칭 [데이터 매칭 편집기 열기…].</summary>
    internal Task SettingsOpenMapperAsync(Window owner) => OpenDataMapperAsync(owner);

    /// <summary>설정 › 데이터 매칭 [기본값으로] — 현재 프로필의 열 매칭과 조회 키를 기본값으로.</summary>
    internal void SettingsResetMapping()
    {
        Map.Reset();
        Map.KeyCol = "H";
        Settings.Data.Profiles[ProfileKey] = new ProfileSettings();
        try { SettingsStore.Save(Settings); }
        catch (Exception ex) { AppLog.Warn("설정 저장 실패: " + ex.Message); }
        if (Db is not null && Db.Rows.Count > 0) Index = LabelIndex.Build(Db.Rows, Map);
        Refresh();
        RevalidateQueue();
    }

    /// <summary>읽어 온 라벨DB 행 (없으면 빈 목록).</summary>
    internal IReadOnlyList<DbRow> DbRowsOrEmpty => Db?.Rows ?? (IReadOnlyList<DbRow>)Array.Empty<DbRow>();

    /// <summary>설정 › 라벨 · 용지 [이 크기로] — 라벨 크기만 바꾼다 (객체는 그대로).</summary>
    internal void SettingsApplyLabelSize(LabelPreset it)
    {
        Template.Label.W = it.W;
        Template.Label.H = it.H;
        SyncLabelSizeUi();
        SyncPaperUi();
        Editor.Invalidate();
        Editor.ZoomFit();
        RenderChecks();
        ScheduleSave();
        UpdatePrintButton();
    }

    /// <summary>설정 › 라벨 · 용지 [용지 배치 설정…].</summary>
    internal Task SettingsOpenPaperDialogAsync(Window owner) => OpenPaperDialogAsync(owner);

    /// <summary>설정 › 라벨 · 용지 [라벨 실물 크기로].</summary>
    internal void SettingsResetLayout()
    {
        PutLayout(l =>
        {
            var d = new LayoutOptions();
            l.Paper = d.Paper; l.CustomW = d.CustomW; l.CustomH = d.CustomH; l.Orientation = d.Orientation;
            l.MarginMm = d.MarginMm; l.GapX = d.GapX; l.GapY = d.GapY; l.Align = d.Align;
            l.CropMarks = d.CropMarks; l.Outline = d.Outline; l.Repeat = d.Repeat;
        });
    }

    /// <summary>설정 › 라벨 · 용지 [떼어내기…].</summary>
    internal Task SettingsOpenExtractAsync(Window owner) => OpenExtractDialogAsync(owner);

    /// <summary>설정 › 이미지 값이 바뀌었을 때 — 캐시를 비우고 슬롯을 다시 읽는다.</summary>
    internal void SettingsImagesChanged()
    {
        RebuildImageStore();
        UpdateChips();
        Run(() => LoadSlotImagesAsync(true));
    }

    /// <summary>설정 › 화면 값이 바뀌었을 때.</summary>
    internal void SettingsUiChanged() => ApplyUiSettings();

    /// <summary>설정 › 화면 [처음 설정 안내 다시 보기].</summary>
    internal Task SettingsShowOnboardAsync()
    {
        Settings.Ui.OnboardingDone = false;
        return ShowOnboardAsync();
    }

    /// <summary>설정 › 폴더 경로 [이미지 폴더 점검] — 폴더의 그림 수, DB 가 참조하는 파일 수, 없는 파일 목록. 폴더가 없으면 null.</summary>
    internal (int InFolder, int Needed, List<string> Missing)? CheckImageFolder()
    {
        if (string.IsNullOrWhiteSpace(Settings.Paths.ImgDir) || !Directory.Exists(Settings.Paths.ImgDir)) return null;
        IReadOnlyList<string> list;
        try { list = new Core.Imaging.ImageStore(Settings.Paths.ImgDir).ListImages(); }
        catch (Exception ex)
        {
            AppLog.Warn("이미지 폴더 목록 읽기 실패: " + ex.Message);
            try { list = Directory.EnumerateFiles(Settings.Paths.ImgDir).Select(Path.GetFileName).Where(n => n is not null).Select(n => n!).ToList(); }
            catch (Exception) { return null; }
        }
        var needed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in Template.Objects.OfType<ImageObject>())
        {
            if (string.IsNullOrEmpty(o.SourceField)) continue;
            var col = o.SourceField.StartsWith('@') ? o.SourceField[1..].ToUpperInvariant() : Map.Cols.GetValueOrDefault(o.SourceField);
            if (string.IsNullOrEmpty(col)) continue;
            foreach (var r in DbRowsOrEmpty)
            {
                var v = r.Get(col).Trim();
                if (v.Length > 0) needed.Add(v);
            }
        }
        var have = new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
        var missing = needed.Where(n => !have.Contains(n)).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        return (list.Count, needed.Count, missing);
    }

    /// <summary>설정 › 정보 [설정 초기화] — 폴더 지정(paths)·서식·출력 이력은 그대로 두고 나머지를 기본값으로.</summary>
    internal void SettingsResetAll()
    {
        var d = new AppSettings();
        Settings.Output = d.Output;
        Settings.Imaging = d.Imaging;
        Settings.TextDefaults = d.TextDefaults;
        Settings.BarcodeDefaults = d.BarcodeDefaults;
        Settings.Printer = d.Printer;
        Settings.Ui = d.Ui;
        Settings.Ui.OnboardingDone = true;
        Settings.Validation = d.Validation;
        Settings.Template = d.Template;
        Settings.Layout = d.Layout;
        Settings.Data = d.Data;
        ProfileKey = "general";
        ApplyProfileToMap();
        try { SettingsStore.Save(Settings); }
        catch (Exception ex) { AppLog.Warn("설정 저장 실패: " + ex.Message); }
        ApplyUiSettings();
        SyncProfileUi();
    }
}

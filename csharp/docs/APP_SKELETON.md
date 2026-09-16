# LaPrint.App 골격 — 구현 에이전트 인수인계

`csharp/src/LaPrint.App` 는 **컴파일되는 WPF 골격**이다 (`dotnet build LaPrint.sln -c Release` → 0 오류 · 0 경고).
세 구현 에이전트는 아래 **partial 파일의 stub 본문만** 채운다. 시그니처·`x:Name`·이벤트 이름은 이미 XAML 과 조율 파일이 참조하므로 바꾸지 않는다.
브라우저판 원본은 괄호의 `js/*.js` 함수다 — 알고리즘과 한국어 문구를 그대로 옮긴다 (DESIGN.md §5).

## 1. 파일 목록

| 파일 | 상태 | 내용 |
|---|---|---|
| `App.xaml` / `App.xaml.cs` | **완성** | StartupUri, Theme 병합, 단일 인스턴스 Mutex `Local\LaPrint_SingleInstance`("LaPrint가 이미 실행 중입니다."), AppLog 시작/종료, DispatcherUnhandledException → 로그 + MessageBox(로그 경로) + Handled, UnobservedTaskException 로그 |
| `Theme.xaml` | **완성** | 색 토큰 + 스타일: `PrimaryButton` `DangerButton`(=`DestructiveButton`) `CautionButton` `GhostButton` `SmallButton` `SmallGhostButton` `SmallDangerButton` `ToolToggle`(ToggleButton) `Chip`(Button) `GBox`(HeaderedContentControl) `LegendSub` `Hint` `Mini` `Mono`(TextBox) `Ovl`(TextBlock) `InspTabs` + DataGrid 암시 스타일 |
| `MainWindow.xaml` | **완성** | ARCHITECTURE §3 / index.html 전체 배치. 앱바 46 · 레일 330 · 인스펙터 308 · 상태줄 28 · 드로어(접힘 38 / 펼침 +260) |
| `MainWindow.xaml.cs` | **완성(조율)** | 공유 상태 · 생성자 조립 · `SetStatus` · `ScheduleSave`/`SaveNow` · `Recompute`/`Refresh`/`RefreshSoon` · `RestoreAsync` · `ApplyUiSettings` · Loaded/Closing · 단축키 · 도우미 |
| `MainWindow.Job.cs` | **stub** | 작업 입력 · 라벨DB · DB 참조값 · 점검 · 칩 |
| `MainWindow.Print.cs` | **stub** | 출력(PDF/ZEBRA) · 용지 배치 · 떼어내기 · 서식 · 잠금 · 실물 보기 |
| `MainWindow.Stage.cs` | **stub** | 스테이지 툴바 · 라벨 크기 · 편집기 이벤트 |
| `MainWindow.Inspector.cs` | **stub** | 속성 패널 · 객체 트리 |
| `MainWindow.Queue.cs` | **stub** | 연속 작업 큐 드로어 |
| `MainWindow.Settings.cs` | **stub** | 설정 창 · 데이터 매칭 · 처음 안내 · 도움말 |
| `Controls/EditorCanvas.cs` | **부분** | 공개 API 전부 고정. 라벨(배경+객체, Core `LabelRenderer.Render`) · 용지 그림자 · 외곽선 · ZoomFit/ZoomTo · Select · Commit(실행+알림) · Add · LabelOf · Hover 구현. 이력·편집 명령·마우스 상호작용·장식(눈금자/격자/선택/스냅/링크)은 TODO |
| `Windows/SettingsWindow.xaml(.cs)` | **stub** | 1000×820 CenterOwner, 좌 `nav`(10 페이지) / 우 `pageHost`. `BuildPage`·`SelectPage`·`SettingRow` TODO |
| `Windows/MapperWindow.xaml(.cs)` | **stub** | map/pick 모드, `MapperSource` 레코드, 좌 `fieldsList` / 우 `grid` |
| `Windows/PaperDialog.cs` `ExtractDialog.cs` `PrintConfirmDialog.cs` `ZebraConfirmDialog.cs` `TemplateManageDialog.cs` `OnboardDialog.cs` | **stub** | 코드 빌드 Window 파생, 정적 `ShowAsync` |
| `Services/AppLog.cs` | **완성** | `%APPDATA%\LaPrint\app.log`, 1 MB 회전, `Info/Warn/Error`, `FilePath` |
| `Services/Dialogs.cs` | **완성** | `Confirm`(기본 = 아니오/취소, Esc 취소, danger) · `Prompt` · `Toast`(우하단, `toastHost`) |
| `Services/SkiaWpf.cs` | **완성** | `SKBitmap → BitmapSource`(Frozen) |

권장 분담 (파일이 서로 겹치지 않는다):
- **A. 데이터·출력** — `MainWindow.Job.cs` · `MainWindow.Print.cs` · `Windows/PaperDialog.cs` · `ExtractDialog.cs` · `PrintConfirmDialog.cs` · `ZebraConfirmDialog.cs` · `TemplateManageDialog.cs`
- **B. 편집기** — `Controls/EditorCanvas.cs`(TODO 부분) · `MainWindow.Stage.cs` · `MainWindow.Inspector.cs`
- **C. 큐·설정** — `MainWindow.Queue.cs` · `MainWindow.Settings.cs` · `Windows/SettingsWindow.xaml(.cs)` · `MapperWindow.xaml(.cs)` · `OnboardDialog.cs`

## 2. 조율 파일이 제공하는 것 (`MainWindow.xaml.cs`)

공유 상태 (`internal`): `AppSettings Settings` · `SettingsStore SettingsStore` · `TemplateStore Templates` · `SessionStore Session` · `HistoryStore History` · `DbCache DbCache` ·
`LabelTemplate Template`(`new` — `Control.Template` 을 가린다) · `FieldMap Map` · `LabelDb? Db` · `LabelIndex? Index` · `Fields Fields` · `DbRow? Row` · `QueueEngine Queue` · `ImageStore? Images` · `PdfExporter Exporter` ·
`EditorCanvas Editor => stage` · `bool Locked`(기본 true) · `bool LayoutDirty` · `bool Preview` · `string ProfileKey` · `JobInputs Inputs`(불변 레코드 — `with`) · `int Copies` · `string? ActiveQueueId` · `string TemplateName` ·
`string? DbFileName` · `List<string> DbIssues` · `(int N, DateTime? At)? RestoredQueue` · `PreflightResult? LastPreflight` · `bool Dirty` · `static string CurrentTemplateFile`(`%APPDATA%\LaPrint\current_template.json`).

메서드:
```csharp
internal void SetStatus(string msg, StatusLevel level = StatusLevel.Info)   // "[HH:mm:ss] msg", Warn/Error 굵게 + 로그, 툴팁
internal void Toast(string msg, ToastLevel level = ToastLevel.Info)
internal void Recompute()                 // Row/Fields 재계산 + Editor.Context 갱신 (ExpAuto 면 Inputs.Exp 갱신)
internal void Refresh(bool skipImages = false)  // Recompute → RenderRefTable → Editor.Invalidate → LoadSlotImagesAsync(false) → RenderChecks → RefreshInspector → UpdatePrintButton → ScheduleSave
internal void RefreshSoon()               // 90ms 디바운스 Refresh
internal void ScheduleSave()              // '변경됨' + 600ms 뒤 SaveNow
internal void SaveNow()                   // current_template.json(원자적) + Session.Save(Inputs, Queue.Rows, Locked)
internal void ApplyUiSettings()           // Settings.Ui → Editor/툴바 토글, selTarget/selQueueMode, SyncPaperUi, Invalidate
internal void ApplyProfileToMap()         // Settings.Data.Profiles[ProfileKey] → Map.Apply/KeyCol
internal string ResolveText(string text)  // Placeholders.Resolve(text, Fields, Row)
internal SKBitmap? ImageOf(string id)     // 이미지 객체의 Bitmap
internal async void Run(Func<Task> action)          // 예외 → 로그 + 상태줄 Error
internal T Res<T>(string key)                        // FindResource
internal static void SelectByTag(ComboBox cb, string? tag) / static string? TagOf(ComboBox cb) / static double Num(TextBox tb, double fallback)
internal static T? FindAncestor<T>(DependencyObject d)
```
복원 순서 (`RestoreAsync`, app.js restore): ApplyUiSettings → current_template.json 또는 `ApplyDefaultTemplateAsync(false)` → Normalize → Session.Load(Inputs/Queue/Locked) → `SyncInputsToUi` → `SyncProfileUi` → `SetLocked(Locked, true)` → `UpdateChips` → `AutoLoadDbAsync` → `Refresh` → `Editor.ZoomFit` → `CheckReconnectAsync` → `MaybeOnboardAsync`.
Loaded: `RefreshTemplateListAsync` → RestoreAsync → `RenderQueue` → `NoticeRestoredQueue` → 상태 "준비됨" → `inpItem.Focus()`.
Closing: `Queue.Running` 이면 확인(기본 취소) 후 `Queue.Cancel`; `SaveNow`.
단축키(창 PreviewKeyDown, Ctrl 조합만): Ctrl+Enter `AddCurrentToQueue` · Ctrl+P `DoPrintSingleAsync` · Ctrl+Shift+P `DoPrintQueueAsync` · Ctrl+, `OpenSettingsAsync` · F1 `ShowHelp` · F5 `TogglePreview` · Ctrl+L `SetLocked` · Ctrl+S `SaveTemplateAsync` · Ctrl+↓/↑ `OpenDrawer` · Ctrl+B/I(입력칸 밖) 굵게/기울임 · Ctrl+V(입력칸 밖, 표 텍스트) `PasteToQueueAsync(text)` · Ctrl+0 ZoomFit · Ctrl+1 100% · Ctrl+Z/Y · 나머지 `Editor.HandleKey`.

## 3. Partial 파일별 stub (전부 `MainWindow` 의 `private`)

### `MainWindow.Job.cs`
POCO: `public sealed record RefRow(string Label, string Value, bool Empty)` (tblRef) · `public sealed record CheckRow(string Icon, string Msg, string Level, string? ObjId = null)` (checks, Level = error|warn|ok|info)
```csharp
void InitJob()
void SyncInputsToUi()
void SyncProfileUi()
JobInputs ReadInputs()                                   // throws
void ShowItemPop() / void HideItemPop() / void PickItem(string key)
void RenderRefTable() / void RenderChecks() / void UpdatePrintButton() / void UpdateChips()
Task SetDbProfileAsync(string key, bool silent = false)
Task<bool> LoadDbFromFileAsync(string path, bool silent = false)   // throws
void ApplyDb(LabelDb db, string fileName)
Task AutoLoadDbAsync() / Task LoadSampleDataAsync()
void NoticeDbQuality()
// 핸들러
void OnProfileChanged(object, SelectionChangedEventArgs)      // selProfile
void OnItemTextChanged(object, TextChangedEventArgs)          // inpItem
void OnItemGotFocus / OnItemLostFocus(object, KeyboardFocusChangedEventArgs)
void OnItemKeyDown(object, KeyEventArgs)                       // ↑↓ Enter Esc
void OnItemPopPick(object, MouseButtonEventArgs)               // itemPopList
void OnInputChanged(object, RoutedEventArgs)                   // inpLot/inpSn/inpMfg/inpMonths/chkExpAuto/inpExp/inpCopies 공용
void OnLotKeyDown(object, KeyEventArgs)                        // Enter → inpMfg
void OnMapDataClick(object, RoutedEventArgs)                   // btnMapData → OpenDataMapperAsync
void OnClearJobClick(object, RoutedEventArgs)                  // btnClearJob
```

### `MainWindow.Print.cs`
```csharp
void InitPrint()
Task DoPrintSingleAsync() / Task DoPrintQueueAsync()
Task PrintSingleAsync() / Task PrintQueueAsync() / Task PrintZebraAsync(bool queue)
Task<bool> ConfirmDialogAsync(IReadOnlyList<ExportJob> jobs, PreflightResult pf, bool queueMode = false, int total = 0, int skipped = 0)  // throws
Task<bool> ConfirmZebraAsync(ZebraJobInfo info, IReadOnlyList<Issue> issues, int jobCount, PrinterSettings printer)                  // throws
Task LoadSlotImagesAsync(bool force)
Task<bool> LoadSlotImagesIntoAsync(IEnumerable<LabelObject> objs, Fields f, DbRow? row, bool force)  // throws — ★ 큐 실행 시 행마다
LayoutOptions LayoutOpt()                 // 구현됨: Settings.Layout
void PutLayout(Action<LayoutOptions> patch)
void SyncPaperUi()
Task OpenPaperDialogAsync() / Task OpenExtractDialogAsync()
Task ApplyPresetTemplateAsync(string presetId)
Task ApplyDefaultTemplateAsync(bool confirm = true)
void InstallTemplate(LabelSize label, IEnumerable<LabelObject> objects, string name)
Task RefreshTemplateListAsync() / Task SaveTemplateAsync() / Task LoadTemplateAsync(string name) / Task ManageTemplatesAsync()
void SetLocked(bool v, bool silent = false)
void TogglePreview()
// 핸들러
void OnTemplateSelected(object, SelectionChangedEventArgs)     // selTemplate
void OnTplSaveClick / OnTplManageClick / OnLockClick / OnPreviewClick(object, RoutedEventArgs)
void OnTargetChanged(object, SelectionChangedEventArgs)        // selTarget (Tag pdf|zebra)
void OnPrinterSetupClick(object, RoutedEventArgs)              // → OpenSettingsAsync("printer")
void OnPaperChanged(object, SelectionChangedEventArgs)         // selPaper
void OnPaperSetupClick / OnPrintClick / OnPrintQueueClick(object, RoutedEventArgs)
```

### `MainWindow.Stage.cs`
```csharp
void InitStage() / void FillLabelPresets() / void SyncLabelSizeUi()
void OnEditorSelectionChanged() / void OnEditorModelChanged(string label) / void OnEditorViewChanged() / void OnEditorHoverChanged(string posText)
// 툴바 (모두 (object sender, RoutedEventArgs e), 별도 표기 제외)
OnAddTextClick / OnAddImgSlotClick / OnAddBarcodeClick / OnAddImgFileClick
OnAlignClick  (sender.Tag = left|hcenter|right|top|vcenter|bottom)    OnDistributeClick (Tag = h|v)
OnFrontClick / OnBackClick / OnDupClick / OnDeleteClick / OnUndoClick / OnRedoClick
OnSnapClick / OnGridClick / OnRulerClick / OnLinkClick          (btnSnap/btnGrid/btnRuler/btnLink 는 ToggleButton)
OnZoomOutClick / OnZoomInClick / OnZoomFitClick
void OnZoomSelected(object, SelectionChangedEventArgs)          // selZoom (Tag 25..600)
void OnLabelPresetChanged(object, SelectionChangedEventArgs)    // selLabelPreset
void OnLabelSizeChanged(object, RoutedEventArgs)                // inpLabelW/H LostFocus
void OnLabelSizeKeyDown(object, KeyEventArgs)                   // Enter 확정
OnExtractClick
```

### `MainWindow.Inspector.cs`
```csharp
void InitInspector() / void RefreshInspector() / void RefreshTree()
void RenderLinkPanel(LabelObject? sel)
Task<(string Col, string Value)?> PickColumnAsync(string hint)  // throws
void ApplyToSelection(string label, Action<LabelObject> fn)
// 핸들러
void OnInspectorTabChanged(object, SelectionChangedEventArgs)
void OnPropNameChanged(object, RoutedEventArgs)                // propName LostFocus
void OnPropGeomChanged(object, RoutedEventArgs)                // propX/Y/W/H LostFocus (sender 구분)
void OnPropEnterKey(object, KeyEventArgs)                       // 모든 숫자/이름 칸 PreviewKeyDown
void OnPropLockedChanged / OnPropVisibleChanged(object, RoutedEventArgs)        // chkLocked / chkVisible
void OnPropTextChanged(object, TextChangedEventArgs)           // propText
void OnInsertPhClick / OnPickColTextClick(object, RoutedEventArgs)
void OnFontChanged(object, SelectionChangedEventArgs)          // selFont
void OnFontSizeChanged(object, RoutedEventArgs)                // inpFontSize LostFocus
void OnBoldClick / OnItalicClick(object, RoutedEventArgs)      // chkBold / chkItalic (ToggleButton)
void OnColorChanged(object, TextChangedEventArgs)              // propColor (hex) + propColorSwatch
void OnSpacingChanged(object, RoutedEventArgs)                 // inpLetterSpacing/inpWordSpacing/inpLineHeight/inpHScale (Tag: letterSpacing|wordSpacing|lineHeight|hScale)
void OnSpacingSliderChanged(object, RoutedPropertyChangedEventArgs<double>)   // rng* (같은 Tag)
void OnKerningChanged / OnWrapChanged / OnAutoShrinkChanged(object, RoutedEventArgs)
void OnAlignChanged / OnVAlignChanged(object, SelectionChangedEventArgs)      // selAlign / selVAlign (Tag)
void OnLinkMapClick(object, RoutedEventArgs)
void OnImgSourceChanged(object, SelectionChangedEventArgs)     // propSource
void OnPickColImgClick / OnPickImgFileClick(object, RoutedEventArgs)
void OnImgFitChanged(object, SelectionChangedEventArgs)        // selImgFitMode/selImgFit/selImgVFit (sender 구분)
void OnSymbologyChanged / OnBcSourceChanged / OnBcBindingChanged / OnBcLinkChanged / OnBcFitChanged(object, SelectionChangedEventArgs)
void OnBcExprChanged(object, TextChangedEventArgs)             // inpBcExpr
void OnPickColBcClick / OnHriChanged(object, RoutedEventArgs)
void OnTreeSearchChanged(object, TextChangedEventArgs)         // treeSearch
void OnTreeSelectionChanged(object, SelectionChangedEventArgs) // objectTree
```

### `MainWindow.Queue.cs`
POCO: `public sealed class QueueRowView { int No; string Id, Item, Lot, Sn, Mfg, Exp, StatusText, Message, Level; int Months, Copies; QueueRow? Row; }` (queueGrid 열 바인딩: No·Item·Lot·Sn·Mfg·Months·Exp·Copies·StatusText·Message, 행 색 = Level error|warn|done|running)
필드: `internal ObservableCollection<QueueRowView> QueueRows` · `internal HashSet<string> SelectedQueueRows`
```csharp
void InitQueue()                          // queueGrid.ItemsSource = QueueRows
void RenderQueue() / void UpdateQueueStatusCells()
void OpenDrawer(bool open)                // drawerBody 표시, drawerCaret, drawerHint
Task PasteToQueueAsync(string? text = null)
void AddCurrentToQueue()
Task QueueFromFileAsync() / Task ExpandSerialAsync()
void RevalidateQueue() / void NoticeRestoredQueue()
void LoadRowToInputs(QueueRow r)
void OnQueueChanged()                     // Queue.Changed (UI 스레드 마샬링 필요)
// 핸들러 ((object, RoutedEventArgs) 별도 표기 제외)
OnDrawerToggleClick / OnQueueAddClick / OnQueuePasteClick / OnQueueFileClick / OnQueueAddRowClick / OnQueueSerialClick
OnQueueDupClick / OnQueueDelClick / OnQueueClearDoneClick / OnQueueClearClick / OnQueueExportClick / OnQueueRunClick / OnQueuePauseClick / OnQueueCancelClick
void OnQueueModeChanged(object, SelectionChangedEventArgs)     // selQueueMode (Tag separate|merged)
void OnQueueRowDoubleClick(object, MouseButtonEventArgs)       // queueGrid
void OnQueueSelectionChanged(object, SelectionChangedEventArgs)
```

### `MainWindow.Settings.cs`
```csharp
Task OpenSettingsAsync(string? page = null)
Task OpenDataMapperAsync()
Task MaybeOnboardAsync()
Task CheckReconnectAsync()                // 구현됨(no-op): C# 은 폴더 권한을 잃지 않는다
void ShowHelp()
void OnSettingsChanged(AppSettings s)     // SettingsStore.Changed
void OnSettingsClick / OnHelpClick / OnChipClick(object, RoutedEventArgs)   // chipDb/chipImg/chipOut Tag="paths"
```

## 4. `Controls/EditorCanvas` 공개 API (`public sealed class EditorCanvas : SKElement`)

```csharp
const int Ruler = 22;  const double PxPerMmAt100 = 96/25.4;
LabelTemplate Template; List<LabelObject> Objects => Template.Objects; HashSet<string> Selection; ViewState View { Scale, Ox, Oy }; double ZoomPercent;
bool ShowRulers=true; bool ShowGrid; double GridMm=5; bool SnapEnabled=true; double SnapPx=6; string LinkMode="on"|"off"; bool PreviewMode; bool Locked=true;
RenderContext Context; Dictionary<string,string> Issues; string? LastRenderError; int RulerSize;
event Action? SelectionChanged; event Action<string>? ModelChanged; event Action? ViewChanged; event Action<string>? HoverChanged;
(double X,double Y) MmToPx(double x,double y); (double X,double Y) PxToMm(double x,double y);
void Invalidate();                                   // 구현
void ZoomFit(); void ZoomTo(double percent);         // 구현
bool CanUndo; bool CanRedo; string UndoLabel; string RedoLabel; void Undo(); void Redo();            // TODO
void BeginTx(string label); void EndTx(bool changed=true); void ResetHistory();                       // TODO
void Commit(string label, Action fn);                // 구현(실행 + ModelChanged + Invalidate) — 이력 기록 TODO
LabelObject? ById(string id); IReadOnlyList<LabelObject> SelectedObjects(); IEnumerable<LabelObject> VisibleObjects();   // 구현
void Select(IEnumerable<string> ids, bool silent=false); void SelectAll(); SKRect? SelectionBounds(); string LabelOf(LabelObject o); string NewId(string prefix);  // 구현
LabelObject Add(LabelObject o, string label="객체 추가");  // 구현(이력 TODO)
void DeleteSelection(); void Duplicate(); void Nudge(double dx,double dy); void Align(string how, string reference="selection"); void Distribute(string axis); void MatchSize(string how);  // TODO
void BringToFront(); void SendToBack(); void Forward(); void Backward(); void SetLocked(bool v); void SetVisible(bool v);   // TODO
LabelObject? HitObject(double mx,double my); bool HandleKey(KeyEventArgs e);   // TODO
protected override OnMouseDown/OnMouseMove(Hover 구현)/OnMouseUp/OnMouseWheel/OnMouseLeave   // TODO
protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)   // 구현: 바탕·용지·Core Render·외곽선; DrawGrid/DrawIssueFrames/DrawLinkOverlay/DrawSelection/DrawSnapGuides/DrawRulers 는 private TODO
```

## 5. Windows · Services 공개 API

```csharp
SettingsWindow.Pages : (Key, Title)[10] = paths 폴더 경로 · mapping 데이터 매칭 · layout 라벨 · 용지 · output 출력 · printer ZEBRA 프린터 · imaging 이미지 · validation 검증 규칙 · ui 화면 · history 출력 이력 · about 정보
static Task<bool> SettingsWindow.ShowAsync(Window owner, AppSettings settings, SettingsStore store, HistoryStore? history = null, string? page = null)
record MapperSource(IReadOnlyList<DbRow> Rows, DbRow? Header, int ColCount, string KeyCol, string Sheet, DbRow? SampleRow)
static Task<bool> MapperWindow.OpenEditorAsync(Window owner, MapperSource? src, FieldMap map, DbRow? sampleRow = null)
static Task<(string Col,string Value)?> MapperWindow.PickColumnAsync(Window owner, MapperSource? src, string? hint = null, DbRow? sampleRow = null)
static Task<bool> PaperDialog.ShowAsync(Window owner, LabelSize label, LayoutOptions opt)
record ExtractChoice(LabelPreset Preset, bool IncludePartial, bool KeepBackground); static Task<ExtractChoice?> ExtractDialog.ShowAsync(Window owner, LabelTemplate sheet)
static Task<bool> PrintConfirmDialog.ShowAsync(Window owner, IReadOnlyList<ExportJob> jobs, PreflightResult pf, ExportOptions options, bool queueMode = false, int total = 0, int skipped = 0)
record ZebraJobInfo(int Dpi, int WidthDots, int HeightDots, double LabelW, double LabelH, string Transport, int ZplLength)
static Task<bool> ZebraConfirmDialog.ShowAsync(Window owner, ZebraJobInfo info, IReadOnlyList<Issue> issues, int jobCount, PrinterSettings printer)
static Task TemplateManageDialog.ShowAsync(Window owner, TemplateStore store, Func<Task> onChanged)
static Task<string?> OnboardDialog.ShowAsync(Window owner, bool dbDone, bool imgDone, bool outDone)   // "db"|"img"|"out"|"sample"|null
static Task<bool> Dialogs.Confirm(Window? owner, string msg, string title="확인", string okLabel="예", string cancelLabel="아니오", bool danger=false, string detail="")
static Task<string?> Dialogs.Prompt(Window? owner, string msg, string title="입력", string value="", string placeholder="", string okLabel="확인")
static void Dialogs.Toast(Window? owner, string msg, ToastLevel level=Info, int? ms=null)   // enum ToastLevel { Info, Ok, Warn, Err }
static void AppLog.Info/Warn(string) · Error(string, Exception? ex=null) · string AppLog.FilePath
static BitmapSource SkiaWpf.ToBitmapSource(SKBitmap)
```
`Dialogs.Confirm/Prompt` 와 모든 `ShowAsync` 는 `ShowDialog` 로 **동기 완료된 Task** 를 돌려주는 규약이다 (Closing 같은 동기 문맥에서도 `.GetAwaiter().GetResult()` 가 안전).

## 6. XAML 이름 — index.html id 와 다른 것

| index.html | MainWindow.xaml | 비고 |
|---|---|---|
| `btnAddSlot` `btnAddImage` `selNewBarcode` | `btnAddImgSlot` `btnAddImgFile` `selBarcodeKind` | 과제 명세 이름 |
| `[data-align]` ×6, `[data-dist]` ×2 | `btnAlignLeft/HCenter/Right/Top/VCenter/Bottom`, `btnDistH/V` (`Tag`) | 공용 핸들러 |
| `btnBold` `btnItalic` | `chkBold` `chkItalic` (ToggleButton) | |
| `propLocked` `propVisible` | `chkLocked` `chkVisible` | |
| `propColor` (color input) | `propColor` (hex TextBox) + `propColorSwatch` | WPF 에 색 선택기가 없다 |
| `objTree` | `objectTree` | |
| `drawerHead` 클릭 | `drawerToggle` (ToggleButton) + `drawerCaret` | |
| `cntTotal` | `queueCount` | `cntDone` `cntErr` 는 그대로 |
| `queueProgressText` | `queueStatus` | |
| `btnQPaste` `btnQImport` `btnQExpandSn` `btnQClear` `btnQPause` `btnQCancel` | `btnQueuePaste` `btnQueueFile` `btnQueueSerial` `btnQueueClear` `btnQueuePause` `btnQueueCancel` | `btnQAddRow` `btnQDup` `btnQDel` `btnQClearDone` `btnQExport` 는 그대로 |
| (없음) | `btnQueueRun` | 드로어 안의 "큐 출력" (btnPrintQueue 와 같은 명령) |
| `queueTable/queueBody` `qCheckAll` | `queueGrid` (DataGrid, Extended 선택) | 체크박스 열 대신 다중 선택 |
| `fileImg` `fileQueue` | (없음) | OpenFileDialog 로 |
| (없음) | `statusSize` · `bscBadge` · `toastHost` · `chip*Dot/chip*Text` · `propBadgeText` · `itemPopList` · `brandVer` | 추가 |
| `paneProps/paneTree` `.insp-tabs` | `inspTabs` TabControl, `tabProps` `tabTree` | |

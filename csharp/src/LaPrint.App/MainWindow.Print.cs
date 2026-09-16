// 출력(PDF · ZEBRA) · 용지 배치 · 원판 떼어내기 · 서식(저장/불러오기/관리/프리셋) · 잠금 · 실물 보기
// (app.js: doPrintSingle/doPrintQueue/printSingle/printQueue/printZebra/confirmDialog/confirmZebra, loadSlotImages(Into),
//  layoutOpt/putLayout/syncPaperUi/openPaperDialog/openExtractDialog, installTemplate/applyPresetTemplate/applyDefaultTemplate/
//  refreshTemplateList/saveTemplate/loadTemplate/manageTemplates, setLocked/setPreview).
// ★ TODO wave — 이 파일의 본문을 채우는 에이전트가 있다. 시그니처는 바꾸지 않는다.
using System.Windows;
using System.Windows.Controls;
using LaPrint.App.Windows;
using LaPrint.Core.Data;
using LaPrint.Core.Export;
using LaPrint.Core.Model;
using LaPrint.Core.Storage;

namespace LaPrint.App;

public partial class MainWindow
{
    /// <summary>selPaper 채우기(Paper.Papers) 등 초기화.</summary>
    private void InitPrint() { /* TODO wave */ }

    /* ---- 출력 진입 (HFE: 1장과 큐는 언제나 다른 명령) ---- */

    /// <summary>Ctrl+P / btnPrint — 오류 있으면 거부, target 에 따라 PrintSingleAsync 또는 PrintZebraAsync(false).</summary>
    private Task DoPrintSingleAsync() => Task.CompletedTask; // TODO wave

    /// <summary>Ctrl+Shift+P / btnPrintQueue — 큐 비었으면 안내, target 에 따라 PrintQueueAsync 또는 PrintZebraAsync(true).</summary>
    private Task DoPrintQueueAsync() => Task.CompletedTask; // TODO wave

    /// <summary>1장 PDF: 슬롯 이미지 → Preflight → ConfirmDialogAsync → Exporter.ExportOne → History.Append → 상태줄.</summary>
    private Task PrintSingleAsync() => Task.CompletedTask; // TODO wave

    /// <summary>큐 PDF: RevalidateQueue → ConfirmDialogAsync(queueMode) → Queue.RunAsync(prepareJob = 행마다 필드+이미지 슬롯 교체 ★) → 요약.</summary>
    private Task PrintQueueAsync() => Task.CompletedTask; // TODO wave

    /// <summary>ZEBRA: RenderToBitmap(프린터 dpi) → Monochrome → ZplBuilder → SanityCheck → ConfirmZebraAsync → IZplTransport.SendAsync.</summary>
    private Task PrintZebraAsync(bool queue) => Task.CompletedTask; // TODO wave

    /// <summary>PDF 출력 확인 (PrintConfirmDialog). 진행이면 true.</summary>
    private Task<bool> ConfirmDialogAsync(IReadOnlyList<ExportJob> jobs, PreflightResult pf, bool queueMode = false, int total = 0, int skipped = 0)
        => throw new NotImplementedException("MainWindow.ConfirmDialogAsync — TODO wave");

    /// <summary>ZEBRA 전송 확인 (ZebraConfirmDialog). 보내면 true.</summary>
    private Task<bool> ConfirmZebraAsync(ZebraJobInfo info, IReadOnlyList<Issue> issues, int jobCount, PrinterSettings printer)
        => throw new NotImplementedException("MainWindow.ConfirmZebraAsync — TODO wave");

    /* ---- 이미지 슬롯 (★ 행마다 다시 읽는다) ---- */

    /// <summary>현재 서식의 이미지 슬롯을 현재 Fields/Row 로 읽는다 → 바뀌었으면 Editor.Invalidate + RenderChecks.</summary>
    private Task LoadSlotImagesAsync(bool force) => Task.CompletedTask; // TODO wave

    /// <summary>SlotLoader.LoadIntoAsync(objs, f, row, Images, force, Settings.Imaging…). 하나라도 바뀌었으면 true.</summary>
    private Task<bool> LoadSlotImagesIntoAsync(IEnumerable<LabelObject> objs, Fields f, DbRow? row, bool force)
        => throw new NotImplementedException("MainWindow.LoadSlotImagesIntoAsync — TODO wave");

    /* ---- 용지 배치 ---- */

    /// <summary>현재 배치 옵션 (Settings.Layout).</summary>
    private LayoutOptions LayoutOpt() => Settings.Layout;

    /// <summary>배치 옵션을 고쳐 저장하고 SyncPaperUi (app.js putLayout).</summary>
    private void PutLayout(Action<LayoutOptions> patch) { /* TODO wave */ }

    /// <summary>selPaper 선택 · printLayoutHint(Paper.Describe) (app.js syncPaperUi).</summary>
    private void SyncPaperUi() { /* TODO wave */ }

    private Task OpenPaperDialogAsync() => Task.CompletedTask; // TODO wave: PaperDialog.ShowAsync
    private Task OpenExtractDialogAsync() => Task.CompletedTask; // TODO wave: 원판 확인 → ExtractDialog → SheetExtractor → InstallTemplate

    /* ---- 서식 ---- */

    /// <summary>프리셋(라벨 규격 id 또는 "SHEET-A3")으로 서식 교체 (app.js applyPresetTemplate).</summary>
    private Task ApplyPresetTemplateAsync(string presetId) => Task.CompletedTask; // TODO wave

    /// <summary>기본 서식(Settings.Template.Preset)으로 되돌린다. confirm 이면 파괴적 확인 먼저 (app.js applyDefaultTemplate).</summary>
    private Task ApplyDefaultTemplateAsync(bool confirm = true) => Task.CompletedTask; // TODO wave

    /// <summary>Template 의 라벨·객체·이름을 교체하고 편집기·이력·크기 UI 를 맞춘다 (app.js installTemplate).</summary>
    private void InstallTemplate(LabelSize label, IEnumerable<LabelObject> objects, string name) { /* TODO wave */ }

    /// <summary>selTemplate 채우기 (Templates.List + 프리셋) (app.js refreshTemplateList).</summary>
    private Task RefreshTemplateListAsync() => Task.CompletedTask; // TODO wave

    /// <summary>Ctrl+S / btnTplSave — 이름 입력(Prompt) → Templates.Save → LayoutDirty=false (app.js saveTemplate).</summary>
    private Task SaveTemplateAsync() => Task.CompletedTask; // TODO wave

    /// <summary>selTemplate 선택 → Templates.Load → InstallTemplate (app.js loadTemplate).</summary>
    private Task LoadTemplateAsync(string name) => Task.CompletedTask; // TODO wave

    private Task ManageTemplatesAsync() => Task.CompletedTask; // TODO wave: TemplateManageDialog.ShowAsync(this, Templates, RefreshTemplateListAsync)

    /* ---- 잠금 · 실물 보기 ---- */

    /// <summary>Locked, btnLock("🔒 잠금"/"✎ 편집"), statusLock("🔒 잠금"/"✎ 편집 가능"), tgrpInsert/tgrpArrange 비활성, Editor.Locked, 상태줄 (app.js setLocked).</summary>
    private void SetLocked(bool v, bool silent = false) { /* TODO wave */ }

    /// <summary>F5 / btnPreview — Preview, Editor.PreviewMode, 상태줄 (app.js setPreview).</summary>
    private void TogglePreview() { /* TODO wave */ }

    /* ---- 핸들러 ---- */

    private void OnTemplateSelected(object sender, SelectionChangedEventArgs e) { /* TODO wave: Run(() => LoadTemplateAsync(name)) */ }
    private void OnTplSaveClick(object sender, RoutedEventArgs e) { /* TODO wave: Run(SaveTemplateAsync) */ }
    private void OnTplManageClick(object sender, RoutedEventArgs e) { /* TODO wave: Run(ManageTemplatesAsync) */ }
    private void OnLockClick(object sender, RoutedEventArgs e) { /* TODO wave: SetLocked(!Locked) */ }
    private void OnPreviewClick(object sender, RoutedEventArgs e) { /* TODO wave: TogglePreview() */ }

    /// <summary>selTarget → Settings.Output.Target 저장, UpdatePrintButton.</summary>
    private void OnTargetChanged(object sender, SelectionChangedEventArgs e) { /* TODO wave */ }
    private void OnPrinterSetupClick(object sender, RoutedEventArgs e) { /* TODO wave: Run(() => OpenSettingsAsync("printer")) */ }

    /// <summary>selPaper — "custom" 이면 PutLayout + OpenPaperDialogAsync, 아니면 PutLayout + 상태줄("라벨 실물 크기로 1장씩 출력합니다." / Paper.Describe).</summary>
    private void OnPaperChanged(object sender, SelectionChangedEventArgs e) { /* TODO wave */ }
    private void OnPaperSetupClick(object sender, RoutedEventArgs e) { /* TODO wave: Run(OpenPaperDialogAsync) */ }

    private void OnPrintClick(object sender, RoutedEventArgs e) { /* TODO wave: Run(DoPrintSingleAsync) */ }
    private void OnPrintQueueClick(object sender, RoutedEventArgs e) { /* TODO wave: Run(DoPrintQueueAsync) */ }
}

// 스테이지 툴바 · 라벨 크기 · 편집기 이벤트 (app.js: bind 스테이지 툴바부, fillLabelPresets, syncLabelSizeUi, ed.onSelectionChange/onModelChange/onViewChange/onHover).
// ★ TODO wave — 이 파일의 본문을 채우는 에이전트가 있다. 시그니처는 바꾸지 않는다.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LaPrint.App;

public partial class MainWindow
{
    /// <summary>selBarcodeKind 채우기(Symbologies.All) · FillLabelPresets 등 초기화.</summary>
    private void InitStage() { /* TODO wave */ }

    /// <summary>selLabelPreset — 제품 라벨 / 일반 규격 / 원판 그룹 + "사용자 지정" (app.js fillLabelPresets).</summary>
    private void FillLabelPresets() { /* TODO wave */ }

    /// <summary>Template.Label → inpLabelW/H, selLabelPreset(Paper.MatchPreset), printLayoutHint, statusSize (app.js syncLabelSizeUi).</summary>
    private void SyncLabelSizeUi() { /* TODO wave */ }

    /* ---- 편집기 이벤트 ---- */

    /// <summary>선택 변경 → RefreshInspector, ovlSel.</summary>
    private void OnEditorSelectionChanged() { /* TODO wave */ }

    /// <summary>모델 변경 — !Locked 면 LayoutDirty = true; ScheduleSave; RenderChecks; RefreshInspector.</summary>
    private void OnEditorModelChanged(string label) { /* TODO wave */ }

    /// <summary>보기 변경 — statusZoom("W×Hmm · Z%"), selZoom 동기화, btnUndo/btnRedo 활성·툴팁("실행 취소: {label} (Ctrl+Z)").</summary>
    private void OnEditorViewChanged() { /* TODO wave */ }

    /// <summary>ovlPos.Text = posText ("x.x, y.y mm").</summary>
    private void OnEditorHoverChanged(string posText) { /* TODO wave */ }

    /* ---- 툴바 핸들러 ---- */

    /// <summary>텍스트 추가 — Settings.TextDefaults 적용, x/y 10, w 50, h 10, "새 텍스트".</summary>
    private void OnAddTextClick(object sender, RoutedEventArgs e) { /* TODO wave */ }
    /// <summary>이미지 슬롯 추가 — 40×20, SourceField "IMG_STENT".</summary>
    private void OnAddImgSlotClick(object sender, RoutedEventArgs e) { /* TODO wave */ }
    /// <summary>바코드 추가 — selBarcodeKind, 2D 20×20 / 1D 50×14, source field, binding UDI_FULL.</summary>
    private void OnAddBarcodeClick(object sender, RoutedEventArgs e) { /* TODO wave */ }
    /// <summary>이미지 파일 직접 추가 — OpenFileDialog → ImageProcessor.Process → 선택이 이미지 1개면 그 객체에, 아니면 새 객체.</summary>
    private void OnAddImgFileClick(object sender, RoutedEventArgs e) { /* TODO wave */ }

    /// <summary>Tag = left|hcenter|right|top|vcenter|bottom. 선택 1개면 라벨 기준, 아니면 선택 기준.</summary>
    private void OnAlignClick(object sender, RoutedEventArgs e) { /* TODO wave */ }
    /// <summary>Tag = h|v.</summary>
    private void OnDistributeClick(object sender, RoutedEventArgs e) { /* TODO wave */ }
    private void OnFrontClick(object sender, RoutedEventArgs e) { /* TODO wave: Editor.BringToFront() */ }
    private void OnBackClick(object sender, RoutedEventArgs e) { /* TODO wave: Editor.SendToBack() */ }
    private void OnDupClick(object sender, RoutedEventArgs e) { /* TODO wave: Editor.Duplicate() */ }
    private void OnDeleteClick(object sender, RoutedEventArgs e) { /* TODO wave: Editor.DeleteSelection() */ }
    private void OnUndoClick(object sender, RoutedEventArgs e) { /* TODO wave: Editor.Undo() */ }
    private void OnRedoClick(object sender, RoutedEventArgs e) { /* TODO wave: Editor.Redo() */ }

    /// <summary>Settings.Ui.Snap 토글 → 저장 → ApplyUiSettings.</summary>
    private void OnSnapClick(object sender, RoutedEventArgs e) { /* TODO wave */ }
    private void OnGridClick(object sender, RoutedEventArgs e) { /* TODO wave */ }
    private void OnRulerClick(object sender, RoutedEventArgs e) { /* TODO wave */ }
    /// <summary>링크 보기 — Editor.LinkMode, Settings.Ui.LinkMode, 상태줄("링크 보기 켬 — 객체를 하나 고르면 같은 값을 쓰는 곳이 함께 표시됩니다." / "링크 보기를 껐습니다.").</summary>
    private void OnLinkClick(object sender, RoutedEventArgs e) { /* TODO wave */ }

    private void OnZoomOutClick(object sender, RoutedEventArgs e) { /* TODO wave: Editor.ZoomTo(Math.Max(5, Editor.ZoomPercent / 1.25)) */ }
    private void OnZoomInClick(object sender, RoutedEventArgs e) { /* TODO wave: Editor.ZoomTo(Math.Min(2000, Editor.ZoomPercent * 1.25)) */ }
    private void OnZoomFitClick(object sender, RoutedEventArgs e) { /* TODO wave: Editor.ZoomFit() */ }
    private void OnZoomSelected(object sender, SelectionChangedEventArgs e) { /* TODO wave: Editor.ZoomTo(TagOf(selZoom)) */ }

    /// <summary>selLabelPreset — 프리셋이면 Template.Label 크기 변경, SyncLabelSizeUi, ZoomFit, RenderChecks, ScheduleSave, 상태줄("라벨 크기를 {n} ({w}×{h}mm) 로 맞췄습니다.").</summary>
    private void OnLabelPresetChanged(object sender, SelectionChangedEventArgs e) { /* TODO wave */ }
    /// <summary>inpLabelW/H 확정 — 최소 5mm, SyncLabelSizeUi, Invalidate, RenderChecks, ScheduleSave, UpdatePrintButton.</summary>
    private void OnLabelSizeChanged(object sender, RoutedEventArgs e) { /* TODO wave */ }
    /// <summary>Enter → 확정(포커스 이동).</summary>
    private void OnLabelSizeKeyDown(object sender, KeyEventArgs e) { /* TODO wave */ }
    private void OnExtractClick(object sender, RoutedEventArgs e) { /* TODO wave: Run(OpenExtractDialogAsync) */ }
}

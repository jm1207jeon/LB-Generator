// 우측 인스펙터 — 속성 패널(위치·크기 / 내용 / 글꼴 / 데이터 링크 / 이미지 소스·맞춤 / 바코드·모양)과 객체 트리 (js/inspector.js).
// ★ TODO wave — 이 파일의 본문을 채우는 에이전트가 있다. 시그니처는 바꾸지 않는다.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LaPrint.Core.Model;

namespace LaPrint.App;

public partial class MainWindow
{
    /// <summary>정적 목록 채우기 — selFont(FontProvider.Choices), selPlaceholder(Placeholders.List), selSymbology(Symbologies.All), selBcBinding(필드 키), propSource(이미지 필드 + "@열") (inspector.js fillStaticOptions).</summary>
    private void InitInspector() { /* TODO wave */ }

    /// <summary>선택에 맞춰 propsEmpty/propsBody, 종류별 섹션 표시, 모든 컨트롤 값 채우기, propTextPreview/bcPreview/bcIssues, RefreshTree (inspector.js refreshInner).</summary>
    private void RefreshInspector() { /* TODO wave */ }

    /// <summary>objectTree(Editor.LabelOf · 잠금/숨김 표시) + treeCount, treeSearch 필터 (inspector.js refreshTree).</summary>
    private void RefreshTree() { /* TODO wave */ }

    /// <summary>propLinkWrap — 선택 객체가 참조하는 필드/열과 같은 값을 쓰는 다른 객체 (inspector.js renderLinkPanel).</summary>
    private void RenderLinkPanel(LabelObject? sel) { /* TODO wave */ }

    /// <summary>MapperWindow.PickColumnAsync 로 열 하나 고르기 (inspector.js pickCol). 취소면 null.</summary>
    private Task<(string Col, string Value)?> PickColumnAsync(string hint)
        => throw new NotImplementedException("MainWindow.PickColumnAsync — TODO wave");

    /// <summary>선택 객체(들)에 변경을 적용 — Editor.Commit(label, …) → RefreshInspector (inspector.js apply).</summary>
    private void ApplyToSelection(string label, Action<LabelObject> fn) { /* TODO wave */ }

    /* ---- 공통 ---- */

    private void OnInspectorTabChanged(object sender, SelectionChangedEventArgs e) { /* TODO wave */ }
    private void OnPropNameChanged(object sender, RoutedEventArgs e) { /* TODO wave */ }
    /// <summary>propX/propY/propW/propH (sender 로 구분).</summary>
    private void OnPropGeomChanged(object sender, RoutedEventArgs e) { /* TODO wave */ }
    /// <summary>Enter → 포커스를 옮겨 LostFocus 확정을 일으킨다.</summary>
    private void OnPropEnterKey(object sender, KeyEventArgs e) { /* TODO wave */ }
    private void OnPropLockedChanged(object sender, RoutedEventArgs e) { /* TODO wave */ }
    private void OnPropVisibleChanged(object sender, RoutedEventArgs e) { /* TODO wave */ }

    /* ---- 텍스트 ---- */

    private void OnPropTextChanged(object sender, TextChangedEventArgs e) { /* TODO wave */ }
    /// <summary>selPlaceholder 토큰을 propText 커서 위치에 삽입.</summary>
    private void OnInsertPhClick(object sender, RoutedEventArgs e) { /* TODO wave */ }
    /// <summary>PickColumnAsync → "{@AB}" 삽입.</summary>
    private void OnPickColTextClick(object sender, RoutedEventArgs e) { /* TODO wave */ }
    private void OnFontChanged(object sender, SelectionChangedEventArgs e) { /* TODO wave */ }
    private void OnFontSizeChanged(object sender, RoutedEventArgs e) { /* TODO wave */ }
    private void OnBoldClick(object sender, RoutedEventArgs e) { /* TODO wave */ }
    private void OnItalicClick(object sender, RoutedEventArgs e) { /* TODO wave */ }
    /// <summary>propColor(hex) → 색 + propColorSwatch.</summary>
    private void OnColorChanged(object sender, TextChangedEventArgs e) { /* TODO wave */ }
    /// <summary>inpLetterSpacing/inpWordSpacing/inpLineHeight/inpHScale (Tag 로 구분) → 값 + 슬라이더 동기화.</summary>
    private void OnSpacingChanged(object sender, RoutedEventArgs e) { /* TODO wave */ }
    /// <summary>rngLetterSpacing/rngWordSpacing/rngLineHeight/rngHScale (Tag) → 값 + 숫자칸 동기화.</summary>
    private void OnSpacingSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { /* TODO wave */ }
    private void OnKerningChanged(object sender, RoutedEventArgs e) { /* TODO wave */ }
    private void OnAlignChanged(object sender, SelectionChangedEventArgs e) { /* TODO wave */ }
    private void OnVAlignChanged(object sender, SelectionChangedEventArgs e) { /* TODO wave */ }
    private void OnWrapChanged(object sender, RoutedEventArgs e) { /* TODO wave */ }
    private void OnAutoShrinkChanged(object sender, RoutedEventArgs e) { /* TODO wave */ }

    /* ---- 데이터 링크 ---- */

    private void OnLinkMapClick(object sender, RoutedEventArgs e) { /* TODO wave: Run(OpenDataMapperAsync) */ }

    /* ---- 이미지 ---- */

    /// <summary>propSource → SourceField, 슬롯 다시 읽기(LoadSlotImagesAsync(true)).</summary>
    private void OnImgSourceChanged(object sender, SelectionChangedEventArgs e) { /* TODO wave */ }
    private void OnPickColImgClick(object sender, RoutedEventArgs e) { /* TODO wave: PickColumnAsync → SourceField "@AB" */ }
    private void OnPickImgFileClick(object sender, RoutedEventArgs e) { /* TODO wave: OnAddImgFileClick 과 같은 파일 대화상자 */ }
    /// <summary>selImgFitMode/selImgFit/selImgVFit (sender 로 구분).</summary>
    private void OnImgFitChanged(object sender, SelectionChangedEventArgs e) { /* TODO wave */ }

    /* ---- 바코드 ---- */

    private void OnSymbologyChanged(object sender, SelectionChangedEventArgs e) { /* TODO wave */ }
    /// <summary>selBcSource → bcFieldWrap/bcExprWrap/bcObjWrap 표시.</summary>
    private void OnBcSourceChanged(object sender, SelectionChangedEventArgs e) { /* TODO wave */ }
    private void OnBcBindingChanged(object sender, SelectionChangedEventArgs e) { /* TODO wave */ }
    private void OnBcExprChanged(object sender, TextChangedEventArgs e) { /* TODO wave */ }
    private void OnPickColBcClick(object sender, RoutedEventArgs e) { /* TODO wave */ }
    private void OnBcLinkChanged(object sender, SelectionChangedEventArgs e) { /* TODO wave */ }
    private void OnHriChanged(object sender, RoutedEventArgs e) { /* TODO wave */ }
    /// <summary>selBcFitMode/selBcFit/selBcVFit (sender 로 구분).</summary>
    private void OnBcFitChanged(object sender, SelectionChangedEventArgs e) { /* TODO wave */ }

    /* ---- 객체 트리 ---- */

    private void OnTreeSearchChanged(object sender, TextChangedEventArgs e) { /* TODO wave: RefreshTree */ }
    private void OnTreeSelectionChanged(object sender, SelectionChangedEventArgs e) { /* TODO wave: Editor.Select(ids) */ }
}

// 스테이지 툴바 · 라벨 크기 · 편집기 이벤트 (app.js: bind 스테이지 툴바부, fillLabelPresets, syncLabelSizeUi, ed.onSelectionChange/onModelChange/onViewChange/onHover).
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LaPrint.App.Services;
using LaPrint.Core.Barcode;
using LaPrint.Core.Export;
using LaPrint.Core.Imaging;
using LaPrint.Core.Model;
using Microsoft.Win32;
using SkiaSharp;

namespace LaPrint.App;

public partial class MainWindow
{
    /// <summary>툴바 콤보를 코드로 맞추는 동안 SelectionChanged 를 무시한다.</summary>
    private bool _stageSyncing;
    private bool _presetsFilled;
    /// <summary>selZoom 에 끼워 넣은 '현재 배율' 항목 (목록에 없는 배율일 때).</summary>
    private ComboBoxItem? _zoomCurItem;

    /// <summary>selBarcodeKind 채우기(Symbologies.All) · FillLabelPresets 등 초기화.</summary>
    private void InitStage()
    {
        selBarcodeKind.Items.Clear();
        foreach (var s in Symbologies.All)
            selBarcodeKind.Items.Add(new ComboBoxItem { Content = s.Name, Tag = s.Id, ToolTip = s.Note });
        SelectByTag(selBarcodeKind, Settings.BarcodeDefaults.Symbology);
        if (selBarcodeKind.SelectedIndex < 0) selBarcodeKind.SelectedIndex = 0;

        FillLabelPresets();
        SyncLabelSizeUi();
        UpdateUndoButtons();

        // 더블클릭 = 텍스트 내용 편집 (app.js ed.onDoubleClick).
        // EditorCanvas 가 모든 왼쪽 클릭을 Handled 로 처리하므로 MouseLeftButtonDown 대신 캔버스의 DoubleClicked 를 받는다.
        stage.DoubleClicked += () =>
        {
            if (Locked) return;
            var sel = Editor.SelectedObjects();
            if (sel.Count != 1 || sel[0] is not TextObject) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                inspTabs.SelectedItem = tabProps;
                propText.Focus();
                propText.SelectAll();
            }));
        };
    }

    /// <summary>selLabelPreset — 제품 라벨 / 일반 규격 / 원판 그룹 + "사용자 지정" (app.js fillLabelPresets).</summary>
    private void FillLabelPresets()
    {
        if (_presetsFilled) return;
        _stageSyncing = true;
        try
        {
            selLabelPreset.Items.Clear();
            void Group(string name, IEnumerable<LabelPreset> items)
            {
                // WPF 콤보에는 optgroup 이 없다 — 고를 수 없는 머리글 항목으로 대신한다
                selLabelPreset.Items.Add(new ComboBoxItem { Content = name, IsEnabled = false, FontWeight = FontWeights.Bold, Foreground = Res<System.Windows.Media.Brush>("Ink3Brush") });
                foreach (var it in items)
                    selLabelPreset.Items.Add(new ComboBoxItem { Content = $"{it.Name} — {F(it.W)}×{F(it.H)}", Tag = it.Id });
            }
            Group("이 제품 라벨", Paper.ProductLabels);
            Group("일반 규격", Paper.CommonLabels);
            Group("원판", new[] { Paper.Sheet });
            selLabelPreset.Items.Add(new ComboBoxItem { Content = "사용자 지정", Tag = "" });
            _presetsFilled = true;
        }
        finally { _stageSyncing = false; }
    }

    private static string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);

    private static LabelPreset? PresetById(string? id)
        => Paper.ProductLabels.Concat(Paper.CommonLabels).Concat(new[] { Paper.Sheet }).FirstOrDefault(p => p.Id == id);

    /// <summary>Template.Label → inpLabelW/H, selLabelPreset(Paper.MatchPreset), printLayoutHint, statusSize (app.js syncLabelSizeUi).</summary>
    private void SyncLabelSizeUi()
    {
        FillLabelPresets();
        var L = Template.Label;
        _stageSyncing = true;
        try
        {
            inpLabelW.Text = F(Math.Round(L.W * 10) / 10);
            inpLabelH.Text = F(Math.Round(L.H * 10) / 10);
            LabelPreset? m = null;
            try { m = Paper.MatchPreset(L.W, L.H); }
            catch { /* 대조 실패 = 사용자 지정 */ }
            SelectByTag(selLabelPreset, m?.Id ?? "");
            try { printLayoutHint.Text = Paper.Describe(L, LayoutOpt()); }
            catch { printLayoutHint.Text = ""; }
            statusSize.Text = $"{F(L.W)}×{F(L.H)}mm";
        }
        finally { _stageSyncing = false; }
    }

    /* ---- 편집기 이벤트 ---- */

    /// <summary>선택 변경 → RefreshInspector, ovlSel.</summary>
    private void OnEditorSelectionChanged()
    {
        RefreshInspector();
        UpdateUndoButtons();
    }

    /// <summary>모델 변경 — !Locked 면 LayoutDirty = true; ScheduleSave; RenderChecks; RefreshInspector.</summary>
    private void OnEditorModelChanged(string label)
    {
        if (!Locked) LayoutDirty = true;     // 잠금 해제 상태의 변경은 '미검증 서식'
        ScheduleSave();
        RenderChecks();
        RefreshInspector();
        UpdateUndoButtons();
    }

    /// <summary>보기 변경 — statusZoom("W×Hmm · Z%"), selZoom 동기화, btnUndo/btnRedo 활성·툴팁("실행 취소: {label} (Ctrl+Z)").</summary>
    private void OnEditorViewChanged()
    {
        var L = Template.Label;
        var z = (int)Math.Round(Editor.ZoomPercent);
        statusZoom.Text = $"{F(L.W)}×{F(L.H)}mm · {z}%";
        if (!selZoom.IsDropDownOpen && !selZoom.IsKeyboardFocusWithin)
        {
            _stageSyncing = true;
            try
            {
                ComboBoxItem? exact = null;
                foreach (var it in selZoom.Items)
                    if (it is ComboBoxItem ci && !ReferenceEquals(ci, _zoomCurItem) && ci.Tag is string t
                        && int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v == z)
                    { exact = ci; break; }
                if (exact is null)
                {
                    if (_zoomCurItem is null)
                    {
                        _zoomCurItem = new ComboBoxItem();
                        selZoom.Items.Insert(0, _zoomCurItem);
                    }
                    _zoomCurItem.Tag = z.ToString(CultureInfo.InvariantCulture);
                    _zoomCurItem.Content = z + "%";
                    exact = _zoomCurItem;
                }
                selZoom.SelectedItem = exact;
            }
            finally { _stageSyncing = false; }
        }
        UpdateUndoButtons();
    }

    /// <summary>btnUndo/btnRedo 활성 + 툴팁 (app.js onViewChange 후반).</summary>
    private void UpdateUndoButtons()
    {
        btnUndo.IsEnabled = Editor.CanUndo;
        btnRedo.IsEnabled = Editor.CanRedo;
        btnUndo.ToolTip = Editor.CanUndo ? $"실행 취소: {Editor.UndoLabel} (Ctrl+Z)" : "실행 취소 (Ctrl+Z)";
        btnRedo.ToolTip = Editor.CanRedo ? $"다시 실행: {Editor.RedoLabel}" : "다시 실행";
    }

    /// <summary>ovlPos.Text = posText ("x.x, y.y mm").</summary>
    private void OnEditorHoverChanged(string posText) => ovlPos.Text = posText;

    /* ---- 툴바 핸들러 ---- */

    /// <summary>텍스트 추가 — Settings.TextDefaults 적용, x/y 10, w 50, h 10, "새 텍스트".</summary>
    private void OnAddTextClick(object sender, RoutedEventArgs e)
    {
        if (Locked) return;
        var d = Settings.TextDefaults;
        var o = new TextObject
        {
            X = 10, Y = 10, W = 50, H = 10, Text = "새 텍스트",
            Font = string.IsNullOrEmpty(d.Font) ? "Arial" : d.Font,
            SizePt = d.SizePt > 0 ? d.SizePt : 8,
            LetterSpacing = d.LetterSpacing,
            LineHeight = d.LineHeight > 0 ? d.LineHeight : 1.15,
            HScale = d.HScale > 0 ? d.HScale : 100,
            Align = string.IsNullOrEmpty(d.Align) ? "left" : d.Align,
            VAlign = string.IsNullOrEmpty(d.VAlign) ? "top" : d.VAlign,
            AutoShrink = d.AutoShrink,
            Color = string.IsNullOrEmpty(d.Color) ? "#000000" : d.Color,
        };
        TemplateJson.Normalize(o, Editor.Objects.Count);
        o.Id = "";
        Editor.Add(o, "텍스트 추가");
    }

    /// <summary>이미지 슬롯 추가 — 40×20, SourceField "IMG_STENT".</summary>
    private void OnAddImgSlotClick(object sender, RoutedEventArgs e)
    {
        if (Locked) return;
        var o = new ImageObject { X = 10, Y = 10, W = 40, H = 20, SourceField = "IMG_STENT" };
        TemplateJson.Normalize(o, Editor.Objects.Count);
        o.Id = "";
        Editor.Add(o, "이미지 슬롯 추가");
    }

    /// <summary>바코드 추가 — selBarcodeKind, 2D 20×20 / 1D 50×14, source field, binding UDI_FULL.</summary>
    private void OnAddBarcodeClick(object sender, RoutedEventArgs e)
    {
        if (Locked) return;
        var sym = TagOf(selBarcodeKind) ?? "gs1datamatrix";
        var is2d = Symbologies.ById(sym).Is2D;
        var d = Settings.BarcodeDefaults;
        var o = new BarcodeObject
        {
            X = 10, Y = 10, W = is2d ? 20 : 50, H = is2d ? 20 : 14,
            Symbology = sym,
            Source = string.IsNullOrEmpty(d.Source) ? "field" : d.Source,
            Binding = string.IsNullOrEmpty(d.Binding) ? "UDI_FULL" : d.Binding,
            HumanReadable = d.HumanReadable,
            FitMode = string.IsNullOrEmpty(d.FitMode) ? "module" : d.FitMode,
            Fit = string.IsNullOrEmpty(d.Fit) ? "center" : d.Fit,
        };
        TemplateJson.Normalize(o, Editor.Objects.Count);
        o.Id = "";
        Editor.Add(o, "바코드 추가");
    }

    /// <summary>이미지 파일 직접 추가 — OpenFileDialog → ImageProcessor.Process → 선택이 이미지 1개면 그 객체에, 아니면 새 객체.</summary>
    private void OnAddImgFileClick(object sender, RoutedEventArgs e)
    {
        if (Locked) return;
        var dlg = new OpenFileDialog
        {
            Title = "이미지 파일",
            Filter = "이미지 파일|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|모든 파일|*.*",
        };
        if (dlg.ShowDialog(this) != true) return;
        var path = dlg.FileName;
        Run(async () =>
        {
            try
            {
                var bytes = await File.ReadAllBytesAsync(path);
                var auto = Settings.Imaging.AutoTransparent;
                var tol = Settings.Imaging.Tolerance;
                var bmp = await Task.Run(() => ImageProcessor.Process(bytes, auto, tol));
                if (bmp is null || bmp.Width <= 0 || bmp.Height <= 0) throw new InvalidOperationException("그림을 읽을 수 없습니다.");
                var dataUrl = ToDataUrl(bmp);
                var name = Path.GetFileName(path);
                var ar = bmp.Width / (double)bmp.Height;
                var w = Math.Min(60, Template.Label.W / 3);
                var sel = Editor.SelectedObjects();
                if (sel.Count == 1 && sel[0] is ImageObject im)
                {
                    Editor.Commit("이미지 지정", () =>
                    {
                        im.Bitmap = bmp; im.DataUrl = dataUrl; im.FileName = name; im.SourceField = ""; im.Error = "";
                    });
                }
                else
                {
                    var o = new ImageObject
                    {
                        X = 10, Y = 10, W = Math.Round(w * 10) / 10, H = Math.Round(w / ar * 10) / 10,
                        FileName = name, Bitmap = bmp, DataUrl = dataUrl,
                    };
                    TemplateJson.Normalize(o, Editor.Objects.Count);
                    o.Id = "";
                    Editor.Add(o, "이미지 추가");
                }
                Refresh();
            }
            catch (Exception ex)
            {
                Toast("이미지를 불러오지 못했습니다: " + ex.Message, ToastLevel.Err);
            }
        });
    }

    /// <summary>비트맵 → PNG data URL (서식 JSON 에 함께 저장되어 다음 실행에도 남는다 — 브라우저판 dataUrl 과 동일).</summary>
    private static string? ToDataUrl(SKBitmap bmp)
    {
        try
        {
            using var img = SKImage.FromBitmap(bmp);
            using var data = img.Encode(SKEncodedImageFormat.Png, 100);
            return "data:image/png;base64," + Convert.ToBase64String(data.ToArray());
        }
        catch { return null; }
    }

    /// <summary>Tag = left|hcenter|right|top|vcenter|bottom. 선택 1개면 라벨 기준, 아니면 선택 기준.</summary>
    private void OnAlignClick(object sender, RoutedEventArgs e)
    {
        if (Locked || (sender as FrameworkElement)?.Tag is not string how) return;
        Editor.Align(how, Editor.Selection.Count == 1 ? "label" : "selection");
    }

    /// <summary>Tag = h|v.</summary>
    private void OnDistributeClick(object sender, RoutedEventArgs e)
    {
        if (Locked || (sender as FrameworkElement)?.Tag is not string axis) return;
        Editor.Distribute(axis);
    }

    private void OnFrontClick(object sender, RoutedEventArgs e) { if (!Locked) Editor.BringToFront(); }
    private void OnBackClick(object sender, RoutedEventArgs e) { if (!Locked) Editor.SendToBack(); }
    private void OnDupClick(object sender, RoutedEventArgs e) { if (!Locked) Editor.Duplicate(); }
    private void OnDeleteClick(object sender, RoutedEventArgs e) { if (!Locked) Editor.DeleteSelection(); }
    private void OnUndoClick(object sender, RoutedEventArgs e) => Editor.Undo();
    private void OnRedoClick(object sender, RoutedEventArgs e) => Editor.Redo();

    /// <summary>설정을 저장하고 화면에 반영한다. 저장 실패는 상태줄 경고.</summary>
    private void SaveUiSetting()
    {
        try { SettingsStore.Save(Settings); }
        catch (Exception ex) { SetStatus("설정 저장 실패: " + ex.Message, StatusLevel.Warn); }
        ApplyUiSettings();
    }

    /// <summary>Settings.Ui.Snap 토글 → 저장 → ApplyUiSettings.</summary>
    private void OnSnapClick(object sender, RoutedEventArgs e)
    {
        Settings.Ui.Snap = !Settings.Ui.Snap;
        SaveUiSetting();
    }

    private void OnGridClick(object sender, RoutedEventArgs e)
    {
        Settings.Ui.ShowGrid = !Settings.Ui.ShowGrid;
        SaveUiSetting();
        Editor.Invalidate();
    }

    private void OnRulerClick(object sender, RoutedEventArgs e)
    {
        Settings.Ui.ShowRulers = !Settings.Ui.ShowRulers;
        SaveUiSetting();
        Editor.Invalidate();
    }

    /// <summary>링크 보기 — Editor.LinkMode, Settings.Ui.LinkMode, 상태줄("링크 보기 켬 — 객체를 하나 고르면 같은 값을 쓰는 곳이 함께 표시됩니다." / "링크 보기를 껐습니다.").</summary>
    private void OnLinkClick(object sender, RoutedEventArgs e)
    {
        var next = Editor.LinkMode == "on" ? "off" : "on";
        Editor.LinkMode = next;
        Settings.Ui.LinkMode = next;
        try { SettingsStore.Save(Settings); }
        catch (Exception ex) { SetStatus("설정 저장 실패: " + ex.Message, StatusLevel.Warn); }
        btnLink.IsChecked = next == "on";
        Editor.Invalidate();
        SetStatus(next == "on"
            ? "링크 보기 켬 — 객체를 하나 고르면 같은 값을 쓰는 곳이 함께 표시됩니다."
            : "링크 보기를 껐습니다.");
    }

    private void OnZoomOutClick(object sender, RoutedEventArgs e) => Editor.ZoomTo(Math.Max(5, Editor.ZoomPercent / 1.25));
    private void OnZoomInClick(object sender, RoutedEventArgs e) => Editor.ZoomTo(Math.Min(2000, Editor.ZoomPercent * 1.25));
    private void OnZoomFitClick(object sender, RoutedEventArgs e) => Editor.ZoomFit();

    private void OnZoomSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_stageSyncing) return;
        if (double.TryParse(TagOf(selZoom), NumberStyles.Float, CultureInfo.InvariantCulture, out var z) && z > 0)
            Editor.ZoomTo(z);
    }

    /// <summary>selLabelPreset — 프리셋이면 Template.Label 크기 변경, SyncLabelSizeUi, ZoomFit, RenderChecks, ScheduleSave, 상태줄("라벨 크기를 {n} ({w}×{h}mm) 로 맞췄습니다.").</summary>
    private void OnLabelPresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_stageSyncing) return;
        var p = PresetById(TagOf(selLabelPreset));
        if (p is null) return;                       // '사용자 지정' 은 숫자칸으로 직접
        Template.Label.W = p.W;
        Template.Label.H = p.H;
        SyncLabelSizeUi();
        Editor.Invalidate();
        Editor.ZoomFit();
        RenderChecks();
        ScheduleSave();
        UpdatePrintButton();
        SetStatus($"라벨 크기를 {p.Name} ({F(p.W)}×{F(p.H)}mm) 로 맞췄습니다.");
    }

    /// <summary>inpLabelW/H 확정 — 최소 5mm, SyncLabelSizeUi, Invalidate, RenderChecks, ScheduleSave, UpdatePrintButton.</summary>
    private void OnLabelSizeChanged(object sender, RoutedEventArgs e)
    {
        if (_stageSyncing) return;
        var w = Num(inpLabelW, 0); if (w == 0) w = 173.8;
        var h = Num(inpLabelH, 0); if (h == 0) h = 26.3;
        w = Math.Max(5, w);
        h = Math.Max(5, h);
        if (Math.Abs(w - Template.Label.W) < 0.0001 && Math.Abs(h - Template.Label.H) < 0.0001)
        {
            SyncLabelSizeUi();
            return;
        }
        Template.Label.W = w;
        Template.Label.H = h;
        SyncLabelSizeUi();
        Editor.Invalidate();
        OnEditorViewChanged();                       // 상태줄 크기 문구만 갱신 — 배율은 그대로
        RenderChecks();
        ScheduleSave();
        UpdatePrintButton();
    }

    /// <summary>Enter → 확정(포커스 이동).</summary>
    private void OnLabelSizeKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        (sender as UIElement)?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
    }

    private void OnExtractClick(object sender, RoutedEventArgs e) => Run(OpenExtractDialogAsync);
}

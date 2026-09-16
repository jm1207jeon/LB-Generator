// 우측 인스펙터 — 속성 패널(위치·크기 / 내용 / 글꼴 / 데이터 링크 / 이미지 소스·맞춤 / 바코드·모양)과 객체 트리 (js/inspector.js).
//
// 선택된 객체의 모든 속성을 편집한다. 여러 개를 선택하면 공통 속성만 일괄 적용한다.
// 모든 변경은 Editor.Commit 을 지나 실행취소 이력에 남고, 화면을 코드로 채우는 동안(_syncing)은 핸들러가 되돌아간다.
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using LaPrint.App.Controls;
using LaPrint.App.Services;
using LaPrint.App.Windows;
using LaPrint.Core.Barcode;
using LaPrint.Core.Data;
using LaPrint.Core.Model;
using LaPrint.Core.Storage;
using LaPrint.Core.Typography;

namespace LaPrint.App;

public partial class MainWindow
{
    /// <summary>화면을 코드로 채우는 동안 true — 핸들러의 되먹임을 막는다 (inspector.js syncing).</summary>
    private bool _syncing;
    /// <summary>슬라이더를 끄는 동안 true — 놓을 때 한 칸으로 기록한다.</summary>
    private bool _sliderDragging;
    /// <summary>propSource 에 임시로 넣은 "{@열} 직접 지정" 항목.</summary>
    private ComboBoxItem? _tmpSourceItem;

    private static readonly (string Type, string Icon, string Name)[] TypeMeta =
    {
        ("text", "T", "텍스트"),
        ("image", "🖼", "이미지"),
        ("barcode", "▦", "바코드"),
    };

    /// <summary>barcode.js FIELD_CHOICES.</summary>
    private static readonly (string Id, string Name)[] BcFieldChoices =
    {
        ("UDI_FULL", "UDI 전체 (01)(10)(17)(240)(21)"),
        ("UDI_L1", "UDI 1행 (01)(10)"),
        ("UDI_L2", "UDI 2행 (17)(240)(21)"),
        ("GTIN01", "UDI-DI (01)만"),
        ("GTIN", "GTIN 숫자만"),
        ("LOT", "LOT"),
        ("SN", "SN"),
        ("ITEM", "품목번호"),
        ("REF", "규격 (REF)"),
    };

    /// <summary>inspector.js IMG_LABELS.</summary>
    private static readonly Dictionary<string, string> ImgLabels = new()
    {
        ["IMG_NAME1"] = "제품명 그림 1줄 (J열)", ["IMG_NAME2"] = "제품명 그림 2줄 (K열)",
        ["IMG_STENT"] = "스텐트 그림 (O열)", ["IMG_DELIVERY"] = "딜리버리 그림 (P열)",
        ["IMG_AM"] = "STENT OD 그림 (AM열)", ["IMG_AP"] = "CI 그림 (AP열)",
    };

    private static (string Icon, string Name) MetaOf(string type)
    {
        foreach (var m in TypeMeta) if (m.Type == type) return (m.Icon, m.Name);
        return (TypeMeta[0].Icon, TypeMeta[0].Name);
    }

    /// <summary>정적 목록 채우기 — selFont(FontProvider.Choices), selPlaceholder(Placeholders.List), selSymbology(Symbologies.All), selBcBinding(필드 키), propSource(이미지 필드 + "@열") (inspector.js fillStaticOptions).</summary>
    private void InitInspector()
    {
        _syncing = true;
        try
        {
            selFont.Items.Clear();
            foreach (var (id, name) in FontProvider.Choices)
            {
                bool avail;
                try { avail = FontProvider.IsAvailable(id); } catch { avail = true; }
                selFont.Items.Add(new ComboBoxItem { Content = name + (avail ? "" : " (설치 안 됨)"), Tag = id });
            }

            selSymbology.Items.Clear();
            foreach (var s in Symbologies.All)
                selSymbology.Items.Add(new ComboBoxItem { Content = s.Name, Tag = s.Id, ToolTip = s.Note });

            selBcBinding.Items.Clear();
            foreach (var (id, name) in BcFieldChoices)
                selBcBinding.Items.Add(new ComboBoxItem { Content = name, Tag = id });

            propSource.Items.Clear();
            propSource.Items.Add(new ComboBoxItem { Content = "(수동 파일)", Tag = "" });
            foreach (var f in FieldMap.ImageFields)
                propSource.Items.Add(new ComboBoxItem { Content = ImgLabels.TryGetValue(f, out var l) ? l : f, Tag = f });

            FillPlaceholders();
        }
        finally { _syncing = false; }

        // 타이핑 중 변경은 모델에 바로 반영하고, 포커스가 머무는 동안을 한 칸의 이력으로 묶는다
        propText.GotKeyboardFocus += (_, _) => Editor.BeginTx("내용 변경");
        propText.LostKeyboardFocus += (_, _) => Editor.EndTx(true);
        inpBcExpr.GotKeyboardFocus += (_, _) => Editor.BeginTx("바코드 식 변경");
        inpBcExpr.LostKeyboardFocus += (_, _) => Editor.EndTx(true);

        // 슬라이더 드래그 = 한 칸의 이력
        foreach (var rng in new[] { rngLetterSpacing, rngWordSpacing, rngLineHeight, rngHScale })
        {
            var label = SpacingLabel(rng.Tag as string);
            rng.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((_, _) =>
            {
                _sliderDragging = true;
                Editor.BeginTx(label);
            }));
            rng.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) =>
            {
                _sliderDragging = false;
                Editor.EndTx(true);
                Editor.Commit(label, () => { });   // 스냅샷은 같아 이력은 늘지 않고, 변경 알림만 낸다
            }));
        }
    }

    /// <summary>플레이스홀더 목록을 채운 매칭의 열 키 서명 — 프로필/매칭이 바뀌면 다시 채운다.</summary>
    private string _phSig = "";

    /// <summary>selPlaceholder — 현재 매칭(Map)의 플레이스홀더 목록.</summary>
    private void FillPlaceholders()
    {
        _phSig = string.Join(",", Map.Cols.Keys);
        var cur = TagOf(selPlaceholder);
        selPlaceholder.Items.Clear();
        foreach (var (_, token, label) in Placeholders.List(Map))
            selPlaceholder.Items.Add(new ComboBoxItem { Content = $"{token}  {label}", Tag = token });
        SelectByTag(selPlaceholder, cur);
        if (selPlaceholder.SelectedIndex < 0 && selPlaceholder.Items.Count > 0) selPlaceholder.SelectedIndex = 0;
    }

    /* ---------------- 값 적용 헬퍼 ---------------- */

    /// <summary>선택 객체(들)에 변경을 적용 — Editor.Commit(label, …) → RefreshInspector (inspector.js apply).</summary>
    private void ApplyToSelection(string label, Action<LabelObject> fn) => Apply(label, fn, null);

    /// <summary>type 이 있으면 그 종류(text|image|barcode)에만. 잠긴 객체는 건드리지 않는다.</summary>
    private void Apply(string label, Action<LabelObject> fn, string? type)
    {
        if (_syncing) return;
        var sel = Editor.SelectedObjects().Where(o => !o.Locked && (type is null || o.Type == type)).ToList();
        if (sel.Count == 0) return;
        Editor.Commit(label, () => { foreach (var o in sel) fn(o); });
        // Editor.ModelChanged → OnEditorModelChanged 가 ScheduleSave · RenderChecks · RefreshInspector 를 맡는다
    }

    /// <summary>여러 객체에서 공통값을 뽑는다. 다르면 (false, default).</summary>
    private static (bool Same, T? Value) Common<T>(IEnumerable<LabelObject> sel, Func<LabelObject, T> get)
    {
        var first = true;
        T? v = default;
        foreach (var o in sel)
        {
            var x = get(o);
            if (first) { v = x; first = false; }
            else if (!EqualityComparer<T>.Default.Equals(x, v)) return (false, default);
        }
        return (!first, v);
    }

    private static string Fmt(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>숫자 칸 채우기 — 공통값이 없으면 비우고 툴팁 '여러 값'.</summary>
    private static void SetNum(TextBox tb, (bool Same, double Value) v, double? fallback = null)
    {
        tb.Text = v.Same ? Fmt(v.Value) : "";
        tb.ToolTip = v.Same ? null : "여러 값";
        if (!v.Same && fallback is not null) tb.ToolTip = $"여러 값 (기본 {Fmt(fallback.Value)})";
    }

    private static void SetPair(TextBox tb, Slider rng, (bool Same, double Value) v, double fallback)
    {
        SetNum(tb, v, fallback);
        rng.Value = v.Same ? v.Value : fallback;
    }

    private static string SpacingLabel(string? key) => key switch
    {
        "letterSpacing" => "자간 변경",
        "wordSpacing" => "어간 변경",
        "lineHeight" => "행간 변경",
        "hScale" => "장평 변경",
        _ => "속성 변경",
    };

    private static (double Min, double Max) SpacingRange(string? key) => key switch
    {
        "letterSpacing" => (-5, 30),
        "wordSpacing" => (-10, 60),
        "lineHeight" => (0.4, 6),
        "hScale" => (10, 500),
        _ => (double.MinValue, double.MaxValue),
    };

    private static double SpacingOf(TextObject t, string? key) => key switch
    {
        "letterSpacing" => t.LetterSpacing,
        "wordSpacing" => t.WordSpacing,
        "lineHeight" => t.LineHeight,
        "hScale" => t.HScale,
        _ => 0,
    };

    private static void SetSpacing(TextObject t, string? key, double v)
    {
        switch (key)
        {
            case "letterSpacing": t.LetterSpacing = v; break;
            case "wordSpacing": t.WordSpacing = v; break;
            case "lineHeight": t.LineHeight = v; break;
            case "hScale": t.HScale = v; break;
        }
    }

    private void HintStyle(TextBlock tb, string level)
    {
        tb.Foreground = level switch
        {
            "ok" => Res<Brush>("PassBrush"),
            "warn" => Res<Brush>("WarnBrush"),
            "err" => Res<Brush>("FailBrush"),
            _ => Res<Brush>("Ink3Brush"),
        };
    }

    /* ---------------- 새로 고침 ---------------- */

    /// <summary>선택에 맞춰 propsEmpty/propsBody, 종류별 섹션 표시, 모든 컨트롤 값 채우기, propTextPreview/bcPreview/bcIssues, RefreshTree (inspector.js refreshInner).</summary>
    private void RefreshInspector()
    {
        _syncing = true;
        try
        {
            if (!selPlaceholder.IsDropDownOpen && _phSig != string.Join(",", Map.Cols.Keys)) FillPlaceholders();
            RefreshInner();
        }
        catch (Exception ex) { AppLog.Warn("속성 패널 갱신 실패: " + ex.Message); }
        finally { _syncing = false; }
        RefreshTree();
    }

    private void RefreshInner()
    {
        var sel = Editor.SelectedObjects();
        if (sel.Count == 0)
        {
            propsEmpty.Visibility = Visibility.Visible;
            propsBody.Visibility = Visibility.Collapsed;
            ovlSel.Visibility = Visibility.Collapsed;
            return;
        }
        propsEmpty.Visibility = Visibility.Collapsed;
        propsBody.Visibility = Visibility.Visible;

        var types = sel.Select(o => o.Type).Distinct().ToList();
        var multi = sel.Count > 1;
        var one = sel[0];
        var meta = MetaOf(types.Count == 1 ? types[0] : "text");

        propIcon.Text = types.Count == 1 ? meta.Icon : "⬚";
        propTitle.Text = multi
            ? $"{sel.Count}개 선택 ({string.Join(", ", types.Select(t => MetaOf(t).Name))})"
            : meta.Name;
        if (one.Locked)
        {
            propBadge.Visibility = Visibility.Visible;
            propBadgeText.Text = "잠김";
            propBadge.Background = Res<Brush>("WarnFillBrush");
            propBadgeText.Foreground = Res<Brush>("WarnBrush");
        }
        else propBadge.Visibility = Visibility.Collapsed;

        var b = Editor.SelectionBounds();
        ovlSel.Visibility = Visibility.Visible;
        ovlSel.Text = multi && b is not null
            ? $"{sel.Count}개 · {b.Value.Width:F1}×{b.Value.Height:F1}mm"
            : $"{one.X:F1}, {one.Y:F1} · {one.W:F1}×{one.H:F1}mm";

        // 공통: 이름 / 위치 / 크기
        propName.Text = multi ? "" : (one.Name ?? "");
        propName.ToolTip = multi ? "여러 객체" : "(자동: " + Editor.LabelOf(one) + ")";
        propName.IsEnabled = !multi;
        SetNum(propX, Common(sel, o => o.X));
        SetNum(propY, Common(sel, o => o.Y));
        SetNum(propW, Common(sel, o => o.W));
        SetNum(propH, Common(sel, o => o.H));
        chkLocked.IsChecked = sel.All(o => o.Locked);
        chkVisible.IsChecked = sel.All(o => o.Visible);

        var hasText = types.Contains("text");
        var hasImg = types.Contains("image");
        var hasBc = types.Contains("barcode");
        propTextWrap.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;
        propImgWrap.Visibility = hasImg && !multi ? Visibility.Visible : Visibility.Collapsed;
        propBcWrap.Visibility = hasBc && !multi ? Visibility.Visible : Visibility.Collapsed;

        if (hasText)
        {
            var ts = sel.OfType<TextObject>().ToList();
            var single = ts.Count == 1;
            if (!propText.IsKeyboardFocusWithin || !single) propText.Text = single ? (ts[0].Text ?? "") : "";
            propText.IsEnabled = single;
            propText.ToolTip = single ? "{PRODUCT}" : "여러 객체는 내용을 함께 바꿀 수 없습니다";
            if (single)
            {
                var outText = ResolveText(ts[0].Text ?? "");
                var over = false; var shrunk = false; double sizePt = ts[0].SizePt;
                try
                {
                    var bb = TextLayoutEngine.Bounds(ts[0], outText);
                    over = bb.OverflowX || bb.OverflowY; shrunk = bb.Shrunk; sizePt = bb.SizePt;
                }
                catch { /* 조판 실패는 미리보기만 */ }
                var prev = outText.Length > 0 ? "→ " + outText.Replace("\r\n", "\n").Replace("\n", " ⏎ ") : "(빈 내용)";
                if (over) prev += "  ⚠ 영역을 넘칩니다";
                else if (shrunk) prev += $"  (자동 축소 {Fmt(sizePt)}pt)";
                propTextPreview.Text = prev;
                HintStyle(propTextPreview, over ? "warn" : "");
            }
            else propTextPreview.Text = "";

            var f = Common(ts, o => ((TextObject)o).Font);
            SelectByTag(selFont, f.Same ? (string.IsNullOrEmpty(f.Value) ? "Arial" : f.Value) : null);
            if (!f.Same) selFont.SelectedIndex = -1;
            SetNum(inpFontSize, Common(ts, o => ((TextObject)o).SizePt), 8);
            chkBold.IsChecked = ts.All(o => o.Bold);
            chkItalic.IsChecked = ts.All(o => o.Italic);
            var color = Common(ts, o => ((TextObject)o).Color);
            propColor.Text = color.Same && !string.IsNullOrEmpty(color.Value) ? color.Value : "#000000";
            UpdateColorSwatch(propColor.Text);
            SetPair(inpLetterSpacing, rngLetterSpacing, Common(ts, o => ((TextObject)o).LetterSpacing), 0);
            SetPair(inpWordSpacing, rngWordSpacing, Common(ts, o => ((TextObject)o).WordSpacing), 0);
            SetPair(inpLineHeight, rngLineHeight, Common(ts, o => ((TextObject)o).LineHeight), 1.15);
            var kern = ts.All(o => o.Kerning);
            chkKerning.IsChecked = kern;
            kerningHint.Text = kern ? "" : "끔 — 글자 간격이 일정해집니다";
            SetPair(inpHScale, rngHScale, Common(ts, o => ((TextObject)o).HScale), 100);
            var al = Common(ts, o => ((TextObject)o).Align);
            SelectByTag(selAlign, al.Same && !string.IsNullOrEmpty(al.Value) ? al.Value : "left");
            var val = Common(ts, o => ((TextObject)o).VAlign);
            SelectByTag(selVAlign, val.Same && !string.IsNullOrEmpty(val.Value) ? val.Value : "top");
            chkWrap.IsChecked = ts.All(o => o.Wrap);
            chkAutoShrink.IsChecked = ts.All(o => o.AutoShrink);
        }

        // 데이터 링크
        RenderLinkPanel(sel.Count == 1 ? one : null);

        if (hasImg && !multi && one is ImageObject im)
        {
            var sf = im.SourceField ?? "";
            // {@열} 직접 참조는 고정 목록에 없으므로 임시 항목을 만들어 보여 준다
            if (_tmpSourceItem is not null) { propSource.Items.Remove(_tmpSourceItem); _tmpSourceItem = null; }
            if (sf.StartsWith('@'))
            {
                _tmpSourceItem = new ComboBoxItem { Content = $"DB {sf[1..]}열 (직접 지정)", Tag = sf };
                propSource.Items.Add(_tmpSourceItem);
            }
            SelectByTag(propSource, sf);
            if (!string.IsNullOrEmpty(sf))
            {
                var name = sf.StartsWith('@') ? (Row?.Get(sf[1..].ToUpperInvariant()) ?? "") : Fields.Get(sf);
                if (string.IsNullOrEmpty(name)) { propFileName.Text = $"이 품목에 {sf} 파일명이 없습니다."; HintStyle(propFileName, "warn"); }
                else if (im.Bitmap is not null) { propFileName.Text = $"✓ {name}"; HintStyle(propFileName, "ok"); }
                else { propFileName.Text = $"✕ {name} — {(string.IsNullOrEmpty(im.Error) ? "불러오지 못했습니다" : im.Error)}"; HintStyle(propFileName, "err"); }
            }
            else if (!string.IsNullOrEmpty(im.FileName)) { propFileName.Text = $"파일: {im.FileName}"; HintStyle(propFileName, "ok"); }
            else { propFileName.Text = "연결된 이미지가 없습니다."; HintStyle(propFileName, ""); }
            SelectByTag(selImgFitMode, string.IsNullOrEmpty(im.FitMode) ? "contain" : im.FitMode);
            SelectByTag(selImgFit, string.IsNullOrEmpty(im.Fit) ? "center" : im.Fit);
            SelectByTag(selImgVFit, string.IsNullOrEmpty(im.VFit) ? "middle" : im.VFit);
        }

        if (hasBc && !multi && one is BarcodeObject bc)
        {
            SelectByTag(selSymbology, string.IsNullOrEmpty(bc.Symbology) ? "gs1datamatrix" : bc.Symbology);
            var src = string.IsNullOrEmpty(bc.Source) ? "field" : bc.Source;
            SelectByTag(selBcSource, src);
            bcFieldWrap.Visibility = src == "field" ? Visibility.Visible : Visibility.Collapsed;
            bcExprWrap.Visibility = src == "expression" ? Visibility.Visible : Visibility.Collapsed;
            bcObjWrap.Visibility = src == "object" ? Visibility.Visible : Visibility.Collapsed;
            SelectByTag(selBcBinding, string.IsNullOrEmpty(bc.Binding) ? "UDI_FULL" : bc.Binding);
            if (!inpBcExpr.IsKeyboardFocusWithin) inpBcExpr.Text = bc.Expression ?? "";

            selBcLink.Items.Clear();
            var opts = BarcodeBinding.LinkableObjects(Editor.Objects, bc.Id);
            if (opts.Count == 0) selBcLink.Items.Add(new ComboBoxItem { Content = "(링크할 객체가 없습니다)", Tag = "" });
            foreach (var (id, label) in opts) selBcLink.Items.Add(new ComboBoxItem { Content = label, Tag = id });
            SelectByTag(selBcLink, bc.LinkObjectId ?? "");

            string data;
            try { data = BarcodeBinding.ResolveData(bc, new BindingContext(Fields, Editor.Objects, ResolveText)); }
            catch (Exception ex) { data = ""; AppLog.Warn("바코드 데이터 해석 실패: " + ex.Message); }
            bcPreview.Text = data.Length > 0 ? data : "(데이터 없음)";
            var all = new List<Issue>();
            if (data.Length > 0)
            {
                try { all.AddRange(Gs1.ValidateData(bc.Symbology, data)); } catch (Exception ex) { all.Add(new Issue("error", "VALIDATE", ex.Message)); }
                try { all.AddRange(BarcodeBinding.CheckPhysical(bc, data, Settings.Output.Dpi)); } catch (Exception ex) { all.Add(new Issue("warn", "PHYS", ex.Message)); }
                try
                {
                    var g = BarcodeEncoder.Encode(bc.Symbology, data, bc.HumanReadable);
                    if (g.Modules is null) all.Insert(0, new Issue("error", "ENCODE", g.Error ?? "바코드를 만들 수 없습니다."));
                }
                catch (Exception ex) { all.Insert(0, new Issue("error", "ENCODE", ex.Message)); }
            }
            if (data.Length == 0) { bcIssues.Text = ""; HintStyle(bcIssues, ""); }
            else if (all.Count == 0) { bcIssues.Text = "✓ 검증 통과"; HintStyle(bcIssues, "ok"); }
            else
            {
                bcIssues.Text = string.Join("  /  ", all.Select(i => (i.Level == "error" ? "✕ " : "⚠ ") + i.Msg));
                HintStyle(bcIssues, all.Any(i => i.Level == "error") ? "err" : "warn");
            }

            chkHri.IsChecked = bc.HumanReadable;
            SelectByTag(selBcFitMode, string.IsNullOrEmpty(bc.FitMode) ? "module" : bc.FitMode);
            SelectByTag(selBcFit, string.IsNullOrEmpty(bc.Fit) ? "center" : bc.Fit);
            SelectByTag(selBcVFit, string.IsNullOrEmpty(bc.VFit) ? "middle" : bc.VFit);
        }
    }

    /* ---------------- 데이터 링크 패널 ---------------- */

    /// <summary>propLinkWrap — 선택 객체가 참조하는 필드/열과 같은 값을 쓰는 다른 객체 (inspector.js renderLinkPanel).</summary>
    private void RenderLinkPanel(LabelObject? sel)
    {
        linkList.Items.Clear();
        if (sel is null) { propLinkWrap.Visibility = Visibility.Collapsed; return; }
        var objs = Editor.Objects;
        var info = LinkAnalysis.Summary(objs, sel);
        var direct = LinkAnalysis.DirectLinks(objs).Where(l => l.From == sel.Id || l.To == sel.Id).ToList();
        if (info.Count == 0 && direct.Count == 0) { propLinkWrap.Visibility = Visibility.Collapsed; return; }
        propLinkWrap.Visibility = Visibility.Visible;

        var repeated = info.Count(i => i.Count > 1);
        linkSummary.Text = repeated > 0 ? $"{repeated}개 값이 라벨에서 반복됨" : "";

        foreach (var i in info)
        {
            var others = i.Others;
            var row = LinkRow(i.Color, i.Label, i.Count > 1 ? $"{i.Count}곳" : "1곳", i.Count > 1);
            if (others.Count > 0)
            {
                row.ToolTip = "클릭하면 같은 데이터를 쓰는 객체를 모두 선택합니다.";
                row.Cursor = Cursors.Hand;
                row.MouseLeftButtonUp += (_, _) => Editor.Select(new[] { sel.Id }.Concat(others));
            }
            else row.ToolTip = "이 라벨에서 한 번만 쓰이는 값입니다.";
            linkList.Items.Add(row);
        }
        foreach (var l in direct)
        {
            var otherId = l.From == sel.Id ? l.To : l.From;
            var o = Editor.ById(otherId);
            if (o is null) continue;
            var row = LinkRow("#9C27B0", (l.From == sel.Id ? "→ 참조: " : "← 참조됨: ") + Editor.LabelOf(o), null, false);
            row.ToolTip = "클릭하면 연결된 객체로 이동합니다.";
            row.Cursor = Cursors.Hand;
            row.MouseLeftButtonUp += (_, _) => Editor.Select(new[] { otherId });
            linkList.Items.Add(row);
        }
    }

    /// <summary>링크 한 줄 — 색 점 · 이름 · (개수 배지).</summary>
    private Border LinkRow(string color, string name, string? badge, bool strong)
    {
        var dock = new DockPanel { LastChildFill = true };
        Brush dotBrush;
        try { dotBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); }
        catch { dotBrush = Res<Brush>("OrangeBrush"); }
        dock.Children.Add(new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(2), Background = dotBrush, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
        if (badge is not null)
        {
            var bd = new Border
            {
                Background = strong ? Res<Brush>("AccentSoftBrush") : Res<Brush>("Paper2Brush"),
                CornerRadius = new CornerRadius(4), Padding = new Thickness(5, 1, 5, 1), Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = badge, FontSize = 11, Foreground = strong ? Res<Brush>("AccentDeepBrush") : Res<Brush>("Ink3Brush") },
            };
            DockPanel.SetDock(bd, Dock.Right);
            dock.Children.Add(bd);
        }
        dock.Children.Add(new TextBlock { Text = name, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        return new Border
        {
            Padding = new Thickness(6, 3, 6, 3), Margin = new Thickness(0, 0, 0, 1), CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent, Background = Brushes.Transparent,
            Child = dock,
        };
    }

    /* ---------------- 객체 트리 ---------------- */

    /// <summary>objectTree(Editor.LabelOf · 잠금/숨김 표시) + treeCount, treeSearch 필터 (inspector.js refreshTree).</summary>
    private void RefreshTree()
    {
        var wasSyncing = _syncing;
        _syncing = true;
        try
        {
            var q = (treeSearch.Text ?? "").Trim().ToLowerInvariant();
            objectTree.Items.Clear();
            var objs = Editor.Objects;
            treeCount.Text = objs.Count > 0 ? $"({objs.Count})" : "";

            // 위에 있는 객체가 위에 보이도록 역순
            for (var i = objs.Count - 1; i >= 0; i--)
            {
                var o = objs[i];
                var label = Editor.LabelOf(o);
                if (q.Length > 0 && !(label.ToLowerInvariant().Contains(q) || (o.Id ?? "").ToLowerInvariant().Contains(q))) continue;
                objectTree.Items.Add(TreeItem(o, label));
            }
            if (objectTree.Items.Count == 0)
            {
                objectTree.Items.Add(new ListBoxItem
                {
                    IsEnabled = false, HorizontalContentAlignment = HorizontalAlignment.Center,
                    Content = new TextBlock { Text = objs.Count > 0 ? "검색 결과가 없습니다." : "객체가 없습니다.", FontSize = 12, Foreground = Res<Brush>("Ink3Brush"), Margin = new Thickness(12, 26, 12, 26) },
                });
            }
        }
        catch (Exception ex) { AppLog.Warn("객체 트리 갱신 실패: " + ex.Message); }
        finally { _syncing = wasSyncing; }
    }

    /// <summary>트리 한 줄 — 종류 아이콘 · 이름 · 표시/잠금 토글 버튼.</summary>
    private ListBoxItem TreeItem(LabelObject o, string label)
    {
        var dock = new DockPanel { LastChildFill = true };
        var lk = new Button
        {
            Content = o.Locked ? "🔒" : "🔓", Style = Res<Style>("SmallGhostButton"), MinWidth = 24, Padding = new Thickness(2, 0, 2, 0), Margin = new Thickness(2, 0, 0, 0),
            Opacity = o.Locked ? 1 : .45, ToolTip = o.Locked ? "잠김 — 클릭하면 해제" : "편집 가능 — 클릭하면 잠금",
        };
        lk.Click += (_, e) =>
        {
            e.Handled = true;
            Editor.Commit("잠금 전환", () => o.Locked = !o.Locked);
        };
        DockPanel.SetDock(lk, Dock.Right);
        var vis = new Button
        {
            Content = o.Visible ? "◉" : "◌", Style = Res<Style>("SmallGhostButton"), MinWidth = 24, Padding = new Thickness(2, 0, 2, 0), Margin = new Thickness(2, 0, 0, 0),
            Opacity = o.Visible ? 1 : .45, ToolTip = o.Visible ? "표시 중 — 클릭하면 숨김" : "숨김 — 클릭하면 표시",
        };
        vis.Click += (_, e) =>
        {
            e.Handled = true;
            Editor.Commit("표시 전환", () => o.Visible = !o.Visible);
        };
        DockPanel.SetDock(vis, Dock.Right);
        dock.Children.Add(lk);
        dock.Children.Add(vis);
        dock.Children.Add(new TextBlock { Text = MetaOf(o.Type).Icon, Width = 15, TextAlignment = TextAlignment.Center, Opacity = .75, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        var nm = new TextBlock { Text = label, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        if (!o.Visible) { nm.Opacity = .45; nm.TextDecorations = TextDecorations.Strikethrough; }
        dock.Children.Add(nm);

        var item = new ListBoxItem { Content = dock, Tag = o.Id, ToolTip = label, IsSelected = Editor.Selection.Contains(o.Id), Padding = new Thickness(4, 2, 4, 2) };
        if (Editor.Issues.TryGetValue(o.Id, out var level) && level is "error" or "warn")
        {
            item.BorderThickness = new Thickness(2, 0, 0, 0);
            item.BorderBrush = Res<Brush>(level == "error" ? "FailBrush" : "WarnBrush");
        }
        return item;
    }

    /* ---------------- 열 고르기 ---------------- */

    /// <summary>MapperWindow.PickColumnAsync 로 열 하나 고르기 (inspector.js pickCol). 취소면 null.</summary>
    private Task<(string Col, string Value)?> PickColumnAsync(string hint)
    {
        if (Db is null || Db.Rows.Count == 0)
        {
            Toast("라벨DB를 먼저 불러오세요.", ToastLevel.Warn);
            return Task.FromResult<(string Col, string Value)?>(null);
        }
        var src = new MapperSource(Db.Rows, Db.Header, Db.ColCount, Map.KeyCol, Db.Sheet, Row);
        return MapperWindow.PickColumnAsync(this, src, hint, Row);
    }

    /// <summary>커서 자리에 토큰을 끼워 넣는다 (inspector.js insertToken). 한 칸의 이력으로 기록한다.</summary>
    private void InsertToken(TextBox t, string? tok, string label)
    {
        if (string.IsNullOrEmpty(tok)) return;
        var s = t.SelectionStart;
        var len = t.SelectionLength;
        var text = t.Text ?? "";
        if (s < 0 || s > text.Length) { s = text.Length; len = 0; }
        Editor.BeginTx(label);
        try
        {
            t.Text = text[..s] + tok + text[(s + len)..];
            t.SelectionStart = s + tok.Length;
            t.SelectionLength = 0;
        }
        finally { Editor.EndTx(true); }
        t.Focus();
    }

    /* ---- 공통 ---- */

    private void OnInspectorTabChanged(object sender, SelectionChangedEventArgs e)
    {
        // 안쪽 콤보의 SelectionChanged 도 여기까지 올라온다 — 탭 자신의 변경만 본다
        if (!ReferenceEquals(e.OriginalSource, inspTabs)) return;
        if (ReferenceEquals(inspTabs.SelectedItem, tabTree)) RefreshTree();
    }

    private void OnPropNameChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        var sel = Editor.SelectedObjects();
        if (sel.Count != 1) return;
        var v = propName.Text ?? "";
        if ((sel[0].Name ?? "") == v) return;
        Editor.Commit("속성 변경", () => sel[0].Name = v.Length == 0 ? null : v);
    }

    /// <summary>propX/propY/propW/propH (sender 로 구분).</summary>
    private void OnPropGeomChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing || sender is not TextBox tb) return;
        var sel = Editor.SelectedObjects();
        if (sel.Count == 0) return;
        if (!double.TryParse(tb.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
        {
            RefreshInspector();       // 못 읽는 값은 되돌린다
            return;
        }
        var key = tb.Name;
        var isSize = key is "propW" or "propH";
        v = Math.Max(isSize ? 0.5 : -9999, v);
        Func<LabelObject, double> get = key switch
        {
            "propX" => o => o.X,
            "propY" => o => o.Y,
            "propW" => o => o.W,
            _ => o => o.H,
        };
        var cur = Common(sel, get);
        if (cur.Same && Math.Abs(cur.Value - v) < 0.0001) return;
        Apply("속성 변경", o =>
        {
            switch (key)
            {
                case "propX": o.X = v; break;
                case "propY": o.Y = v; break;
                case "propW": o.W = v; break;
                default: o.H = v; break;
            }
        }, null);
    }

    /// <summary>Enter → 포커스를 옮겨 LostFocus 확정을 일으킨다.</summary>
    private void OnPropEnterKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        (sender as UIElement)?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
    }

    private void OnPropLockedChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        Editor.SetLocked(chkLocked.IsChecked == true);
    }

    private void OnPropVisibleChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        Editor.SetVisible(chkVisible.IsChecked == true);
    }

    /* ---- 텍스트 ---- */

    private void OnPropTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing) return;
        var sel = Editor.SelectedObjects().OfType<TextObject>().ToList();
        if (sel.Count != 1) return;
        sel[0].Text = propText.Text ?? "";
        if (!Locked) LayoutDirty = true;
        ScheduleSave();
        RenderChecks();
        Editor.Invalidate();
        _syncing = true;
        try { RefreshInner(); }
        catch (Exception ex) { AppLog.Warn("속성 패널 갱신 실패: " + ex.Message); }
        finally { _syncing = false; }
        RefreshTree();
    }

    /// <summary>selPlaceholder 토큰을 propText 커서 위치에 삽입.</summary>
    private void OnInsertPhClick(object sender, RoutedEventArgs e)
    {
        if (Editor.SelectedObjects().OfType<TextObject>().Count() != 1) return;
        InsertToken(propText, TagOf(selPlaceholder), "내용 변경");
    }

    /// <summary>PickColumnAsync → "{@AB}" 삽입.</summary>
    private void OnPickColTextClick(object sender, RoutedEventArgs e) => Run(async () =>
    {
        if (Editor.SelectedObjects().OfType<TextObject>().Count() != 1) return;
        var r = await PickColumnAsync("텍스트에 넣을 DB 열 고르기 — 표에서 열을 클릭하면 그 열이 {@열} 형태로 본문에 들어갑니다.");
        if (r is not null) InsertToken(propText, $"{{@{r.Value.Col}}}", "내용 변경");
    });

    private void OnFontChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || TagOf(selFont) is not string font) return;
        Apply("글꼴 변경", o => ((TextObject)o).Font = font, "text");
    }

    private void OnFontSizeChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        if (!double.TryParse(inpFontSize.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || v <= 0)
        {
            RefreshInspector();
            return;
        }
        var cur = Common(Editor.SelectedObjects().OfType<TextObject>(), o => ((TextObject)o).SizePt);
        if (cur.Same && Math.Abs(cur.Value - v) < 0.0001) return;
        Apply("크기 변경", o => ((TextObject)o).SizePt = v, "text");
    }

    private void OnBoldClick(object sender, RoutedEventArgs e)
    {
        var sel = Editor.SelectedObjects().OfType<TextObject>().ToList();
        var all = sel.Count > 0 && sel.All(o => o.Bold);
        Apply("굵게", o => ((TextObject)o).Bold = !all, "text");
        if (sel.Count == 0) chkBold.IsChecked = false;
    }

    private void OnItalicClick(object sender, RoutedEventArgs e)
    {
        var sel = Editor.SelectedObjects().OfType<TextObject>().ToList();
        var all = sel.Count > 0 && sel.All(o => o.Italic);
        Apply("기울임", o => ((TextObject)o).Italic = !all, "text");
        if (sel.Count == 0) chkItalic.IsChecked = false;
    }

    /// <summary>propColor(hex) → 색 + propColorSwatch.</summary>
    private void OnColorChanged(object sender, TextChangedEventArgs e)
    {
        var hex = (propColor.Text ?? "").Trim();
        if (!UpdateColorSwatch(hex) || _syncing) return;
        var norm = TemplateJson.NormalizeColor(hex);
        var cur = Common(Editor.SelectedObjects().OfType<TextObject>(), o => ((TextObject)o).Color);
        if (cur.Same && string.Equals(cur.Value, norm, StringComparison.OrdinalIgnoreCase)) return;
        Apply("색 변경", o => ((TextObject)o).Color = norm, "text");
    }

    /// <summary>#RGB / #RRGGBB 이면 견본 색을 바꾸고 true.</summary>
    private bool UpdateColorSwatch(string hex)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(hex, "^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6})$")) return false;
        try
        {
            propColorSwatch.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            return true;
        }
        catch { return false; }
    }

    /// <summary>inpLetterSpacing/inpWordSpacing/inpLineHeight/inpHScale (Tag 로 구분) → 값 + 슬라이더 동기화.</summary>
    private void OnSpacingChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing || sender is not TextBox tb) return;
        var key = tb.Tag as string;
        if (!double.TryParse(tb.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
        {
            RefreshInspector();
            return;
        }
        var (min, max) = SpacingRange(key);
        v = Math.Min(max, Math.Max(min, v));
        var cur = Common(Editor.SelectedObjects().OfType<TextObject>(), o => SpacingOf((TextObject)o, key));
        if (cur.Same && Math.Abs(cur.Value - v) < 0.0001) return;
        Apply(SpacingLabel(key), o => SetSpacing((TextObject)o, key, v), "text");
    }

    /// <summary>rngLetterSpacing/rngWordSpacing/rngLineHeight/rngHScale (Tag) → 값 + 숫자칸 동기화.</summary>
    private void OnSpacingSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncing || sender is not Slider rng) return;
        var key = rng.Tag as string;
        var (min, max) = SpacingRange(key);
        var v = Math.Min(max, Math.Max(min, e.NewValue));
        var tb = key switch
        {
            "letterSpacing" => inpLetterSpacing,
            "wordSpacing" => inpWordSpacing,
            "lineHeight" => inpLineHeight,
            _ => inpHScale,
        };
        if (_sliderDragging)
        {
            // 끄는 동안은 모델만 바꾸고 그린다 — 이력은 DragCompleted 에서 한 칸
            var sel = Editor.SelectedObjects().OfType<TextObject>().Where(o => !o.Locked).ToList();
            if (sel.Count == 0) return;
            foreach (var o in sel) SetSpacing(o, key, v);
            _syncing = true;
            try { tb.Text = Fmt(v); tb.ToolTip = null; }
            finally { _syncing = false; }
            Editor.Invalidate();
            return;
        }
        var cur = Common(Editor.SelectedObjects().OfType<TextObject>(), o => SpacingOf((TextObject)o, key));
        if (cur.Same && Math.Abs(cur.Value - v) < 0.0001) return;
        Apply(SpacingLabel(key), o => SetSpacing((TextObject)o, key, v), "text");
    }

    private void OnKerningChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        var v = chkKerning.IsChecked == true;
        Apply("커닝", o => ((TextObject)o).Kerning = v, "text");
    }

    private void OnAlignChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || TagOf(selAlign) is not string v) return;
        Apply("정렬", o => ((TextObject)o).Align = v, "text");
    }

    private void OnVAlignChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || TagOf(selVAlign) is not string v) return;
        Apply("세로 정렬", o => ((TextObject)o).VAlign = v, "text");
    }

    private void OnWrapChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        var v = chkWrap.IsChecked == true;
        Apply("줄바꿈", o => ((TextObject)o).Wrap = v, "text");
    }

    private void OnAutoShrinkChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        var v = chkAutoShrink.IsChecked == true;
        Apply("자동 축소", o => ((TextObject)o).AutoShrink = v, "text");
    }

    /* ---- 데이터 링크 ---- */

    private void OnLinkMapClick(object sender, RoutedEventArgs e) => Run(OpenDataMapperAsync);

    /* ---- 이미지 ---- */

    /// <summary>이미지 소스를 바꾸고 슬롯을 다시 읽는다 (inspector.js propSource change / btnPickColImg).</summary>
    private void ChangeImageSource(ImageObject im, string sourceField)
    {
        Editor.Commit("이미지 소스 변경", () =>
        {
            im.SourceField = sourceField;
            im.FileName = ""; im.Bitmap = null; im.DataUrl = null; im.Error = "";
        });
        Run(() => LoadSlotImagesAsync(true));
    }

    /// <summary>propSource → SourceField, 슬롯 다시 읽기(LoadSlotImagesAsync(true)).</summary>
    private void OnImgSourceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing) return;
        var sel = Editor.SelectedObjects();
        if (sel.Count != 1 || sel[0] is not ImageObject im || im.Locked) return;
        var v = TagOf(propSource);
        if (v is null || v == (im.SourceField ?? "")) return;
        ChangeImageSource(im, v);
    }

    private void OnPickColImgClick(object sender, RoutedEventArgs e) => Run(async () =>
    {
        var sel = Editor.SelectedObjects();
        if (sel.Count != 1 || sel[0] is not ImageObject im || im.Locked) return;
        var r = await PickColumnAsync("그림 파일명이 든 DB 열 고르기 — 표에서 열을 클릭하면 그 열의 값을 파일명으로 읽어 그림을 찾습니다.");
        if (r is null) return;
        ChangeImageSource(im, "@" + r.Value.Col);
    });

    private void OnPickImgFileClick(object sender, RoutedEventArgs e) => OnAddImgFileClick(sender, e);

    /// <summary>selImgFitMode/selImgFit/selImgVFit (sender 로 구분).</summary>
    private void OnImgFitChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || sender is not ComboBox cb || TagOf(cb) is not string v) return;
        if (ReferenceEquals(cb, selImgFitMode)) Apply("맞춤 방식", o => ((ImageObject)o).FitMode = v, "image");
        else if (ReferenceEquals(cb, selImgFit)) Apply("가로 정렬", o => ((ImageObject)o).Fit = v, "image");
        else Apply("세로 정렬", o => ((ImageObject)o).VFit = v, "image");
    }

    /* ---- 바코드 ---- */

    private void OnSymbologyChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || TagOf(selSymbology) is not string v) return;
        Apply("바코드 종류", o => ((BarcodeObject)o).Symbology = v, "barcode");
    }

    /// <summary>selBcSource → bcFieldWrap/bcExprWrap/bcObjWrap 표시.</summary>
    private void OnBcSourceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || TagOf(selBcSource) is not string v) return;
        Apply("데이터 소스", o => ((BarcodeObject)o).Source = v, "barcode");
    }

    private void OnBcBindingChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || TagOf(selBcBinding) is not string v) return;
        Apply("바코드 필드", o => ((BarcodeObject)o).Binding = v, "barcode");
    }

    private void OnBcExprChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing) return;
        var sel = Editor.SelectedObjects().OfType<BarcodeObject>().ToList();
        if (sel.Count != 1) return;
        sel[0].Expression = inpBcExpr.Text ?? "";
        if (!Locked) LayoutDirty = true;
        ScheduleSave();
        RenderChecks();
        Editor.Invalidate();
        _syncing = true;
        try { RefreshInner(); }
        catch (Exception ex) { AppLog.Warn("속성 패널 갱신 실패: " + ex.Message); }
        finally { _syncing = false; }
    }

    private void OnPickColBcClick(object sender, RoutedEventArgs e) => Run(async () =>
    {
        if (Editor.SelectedObjects().OfType<BarcodeObject>().Count() != 1) return;
        var r = await PickColumnAsync("바코드 데이터에 넣을 DB 열 고르기 — 표에서 열을 클릭하면 그 열이 {@열} 형태로 바코드 식에 들어갑니다.");
        if (r is not null) InsertToken(inpBcExpr, $"{{@{r.Value.Col}}}", "바코드 식 변경");
    });

    private void OnBcLinkChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || TagOf(selBcLink) is not string v) return;
        Apply("객체 링크", o => ((BarcodeObject)o).LinkObjectId = v.Length == 0 ? null : v, "barcode");
    }

    private void OnHriChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        var v = chkHri.IsChecked == true;
        Apply("HRI", o => ((BarcodeObject)o).HumanReadable = v, "barcode");
    }

    /// <summary>selBcFitMode/selBcFit/selBcVFit (sender 로 구분).</summary>
    private void OnBcFitChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || sender is not ComboBox cb || TagOf(cb) is not string v) return;
        if (ReferenceEquals(cb, selBcFitMode)) Apply("배율 방식", o => ((BarcodeObject)o).FitMode = v, "barcode");
        else if (ReferenceEquals(cb, selBcFit)) Apply("가로 정렬", o => ((BarcodeObject)o).Fit = v, "barcode");
        else Apply("세로 정렬", o => ((BarcodeObject)o).VFit = v, "barcode");
    }

    /* ---- 객체 트리 ---- */

    private void OnTreeSearchChanged(object sender, TextChangedEventArgs e) => RefreshTree();

    private void OnTreeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || !ReferenceEquals(e.OriginalSource, objectTree)) return;
        var ids = objectTree.SelectedItems.OfType<ListBoxItem>().Select(i => i.Tag as string).Where(id => !string.IsNullOrEmpty(id)).Cast<string>().ToList();
        // 선택 알림 → 트리 재구성이 같은 이벤트 안에서 일어나지 않도록 한 박자 늦춘다
        Dispatcher.BeginInvoke(new Action(() => Editor.Select(ids)));
    }
}

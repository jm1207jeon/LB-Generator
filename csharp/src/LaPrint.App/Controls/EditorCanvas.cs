// 캔버스 라벨 편집기 — js/editor.js 의 SKElement 판.
//
// 좌표계: 라벨 좌상단이 원점, 단위 mm. 화면 px(DIP) = mm * View.Scale + View.Ox
// 눈금자(Ruler)가 캔버스 안쪽 좌/상단을 차지한다.
//
// 이 골격은 라벨 자체(배경 + 객체, Core LabelRenderer)만 그린다. 편집 장식(선택 테두리·핸들·스냅 안내선·눈금자·격자·
// 링크 오버레이)과 마우스/키보드 상호작용·실행취소 이력은 TODO wave 에서 채운다 — 공개 API 이름은 여기서 고정.
using System.Windows;
using System.Windows.Input;
using LaPrint.Core.Barcode;
using LaPrint.Core.Model;
using LaPrint.Core.Render;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace LaPrint.App.Controls;

/// <summary>화면 변환 — Scale 은 px/mm, Ox/Oy 는 라벨 원점의 화면 위치(DIP).</summary>
public sealed class ViewState
{
    public double Scale { get; set; } = 4;
    public double Ox { get; set; } = 60;
    public double Oy { get; set; } = 60;
}

/// <summary>라벨 편집 캔버스. 라벨은 Core 렌더러로, 편집 장식은 이 컨트롤이 덧그린다.</summary>
public sealed class EditorCanvas : SKElement
{
    /// <summary>눈금자 두께(DIP) (editor.js RULER).</summary>
    public const int Ruler = 22;

    /// <summary>100% 배율의 px/mm (96dpi 기준, editor.js zoomPercent).</summary>
    public const double PxPerMmAt100 = 96 / 25.4;

    private static readonly SKColor CanvasBg = new(0x8A, 0x97, 0xA6);
    private static readonly SKColor CanvasEdge = new(0x4B, 0x55, 0x63);

    public EditorCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    /* ================= 상태 ================= */

    /// <summary>편집 중인 서식 (MainWindow 가 넣는다).</summary>
    public LabelTemplate Template { get; set; } = new() { Label = new LabelSize { W = 173.8, H = 26.3 } };

    /// <summary>서식의 객체 목록.</summary>
    public List<LabelObject> Objects => Template.Objects;

    /// <summary>선택된 객체 id 집합.</summary>
    public HashSet<string> Selection { get; } = new();

    public ViewState View { get; } = new();

    /// <summary>화면 표시용 배율(%) — 100% = 실제 물리 크기.</summary>
    public double ZoomPercent => View.Scale / PxPerMmAt100 * 100;

    public bool ShowRulers { get; set; } = true;
    public bool ShowGrid { get; set; }
    public double GridMm { get; set; } = 5;
    public bool SnapEnabled { get; set; } = true;
    public double SnapPx { get; set; } = 6;

    /// <summary>"on" | "off" — 데이터 링크 표시 (한 번에 한 객체).</summary>
    public string LinkMode { get; set; } = "on";

    /// <summary>true 면 편집 보조선을 감추고 인쇄될 모습만 그린다.</summary>
    public bool PreviewMode { get; set; }

    /// <summary>레이아웃 잠금 — 켜면 선택·이동·리사이즈를 받지 않는다.</summary>
    public bool Locked { get; set; } = true;

    /// <summary>렌더 문맥 — Fields/Row/ResolveText/ImageOf/Background 는 MainWindow 가 채운다.</summary>
    public RenderContext Context { get; set; } = new();

    /// <summary>객체별 점검 결과 (넘침·오류 장식용). objectId → 수준.</summary>
    public Dictionary<string, string> Issues { get; } = new();

    /// <summary>마지막 렌더에서 난 오류 문구 (Core 미구현/예외). 없으면 null.</summary>
    public string? LastRenderError { get; private set; }

    public int RulerSize => ShowRulers ? Ruler : 0;

    /* ================= 이벤트 ================= */

    public event Action? SelectionChanged;
    /// <summary>모델이 바뀌었다 (인수 = 이력 라벨).</summary>
    public event Action<string>? ModelChanged;
    public event Action? ViewChanged;
    /// <summary>마우스가 라벨 위를 지날 때 "x.x, y.y mm".</summary>
    public event Action<string>? HoverChanged;

    /* ================= 좌표 ================= */

    public (double X, double Y) MmToPx(double x, double y) => (x * View.Scale + View.Ox, y * View.Scale + View.Oy);
    public (double X, double Y) PxToMm(double x, double y) => ((x - View.Ox) / View.Scale, (y - View.Oy) / View.Scale);

    public void Invalidate() => InvalidateVisual();

    /* ================= 보기 ================= */

    /// <summary>라벨 전체가 보이도록 배율·원점을 맞춘다.</summary>
    public void ZoomFit()
    {
        var cw = ActualWidth - RulerSize;
        var ch = ActualHeight - RulerSize;
        var L = Template.Label;
        if (cw <= 10 || ch <= 10 || L.W <= 0 || L.H <= 0) return;
        const double pad = 28;
        var s = Math.Min((cw - pad * 2) / L.W, (ch - pad * 2) / L.H);
        View.Scale = Math.Max(0.05, s);
        View.Ox = RulerSize + (cw - L.W * View.Scale) / 2;
        View.Oy = RulerSize + (ch - L.H * View.Scale) / 2;
        ViewChanged?.Invoke();
        Invalidate();
    }

    /// <summary>화면 가운데를 기준으로 배율(%)을 바꾼다.</summary>
    public void ZoomTo(double percent)
    {
        percent = Math.Clamp(percent, 5, 2000);
        var cx = RulerSize + (ActualWidth - RulerSize) / 2;
        var cy = RulerSize + (ActualHeight - RulerSize) / 2;
        var (mx, my) = PxToMm(cx, cy);
        View.Scale = percent / 100 * PxPerMmAt100;
        View.Ox = cx - mx * View.Scale;
        View.Oy = cy - my * View.Scale;
        ViewChanged?.Invoke();
        Invalidate();
    }

    /* ================= 이력 (TODO wave: 스냅샷 + 트랜잭션, editor.js _snapshot/_restore) ================= */

    public bool CanUndo => false;
    public bool CanRedo => false;
    public string UndoLabel => "";
    public string RedoLabel => "";

    public void Undo() { /* TODO wave */ }
    public void Redo() { /* TODO wave */ }

    /// <summary>드래그 같은 연속 변경의 시작 — 스냅샷을 잡아 둔다.</summary>
    public void BeginTx(string label) { /* TODO wave */ }

    /// <summary>연속 변경의 끝. changed=false 면 스냅샷을 버린다.</summary>
    public void EndTx(bool changed = true) { /* TODO wave */ }

    /// <summary>한 번의 변경을 이력 한 칸으로 기록하며 실행한다.</summary>
    public void Commit(string label, Action fn)
    {
        // TODO wave: BeginTx(label) → fn() → EndTx(true). 골격은 실행과 알림만 한다.
        fn();
        ModelChanged?.Invoke(label);
        Invalidate();
    }

    public void ResetHistory() { /* TODO wave */ }

    /* ================= 선택 ================= */

    public LabelObject? ById(string id) => Objects.Find(o => o.Id == id);

    public IReadOnlyList<LabelObject> SelectedObjects() => Objects.Where(o => Selection.Contains(o.Id)).ToList();

    public IEnumerable<LabelObject> VisibleObjects() => Objects.Where(o => o.Visible);

    public void Select(IEnumerable<string> ids, bool silent = false)
    {
        Selection.Clear();
        foreach (var id in ids) Selection.Add(id);
        if (!silent) SelectionChanged?.Invoke();
        Invalidate();
    }

    public void SelectAll() => Select(VisibleObjects().Where(o => !o.Locked).Select(o => o.Id));

    /// <summary>선택 영역의 경계(mm). 선택이 없으면 null.</summary>
    public SKRect? SelectionBounds()
    {
        var sel = SelectedObjects();
        if (sel.Count == 0) return null;
        float x1 = float.MaxValue, y1 = float.MaxValue, x2 = float.MinValue, y2 = float.MinValue;
        foreach (var o in sel)
        {
            x1 = Math.Min(x1, (float)o.X); y1 = Math.Min(y1, (float)o.Y);
            x2 = Math.Max(x2, (float)(o.X + o.W)); y2 = Math.Max(y2, (float)(o.Y + o.H));
        }
        return new SKRect(x1, y1, x2, y2);
    }

    /// <summary>객체의 표시용 이름 (editor.js labelOf).</summary>
    public string LabelOf(LabelObject o)
    {
        if (!string.IsNullOrEmpty(o.Name)) return o.Name;
        switch (o)
        {
            case TextObject t:
            {
                string s;
                try { s = Context.ResolveText(t.Text ?? ""); } catch { s = t.Text ?? ""; }
                s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim();
                return s.Length == 0 ? "(빈 텍스트)" : (s.Length > 26 ? s[..26] + "…" : s);
            }
            case ImageObject im:
                if (string.IsNullOrEmpty(im.SourceField)) return string.IsNullOrEmpty(im.FileName) ? "이미지" : im.FileName;
                return im.SourceField.StartsWith('@') ? $"이미지 · DB {im.SourceField[1..]}열" : $"이미지 · {im.SourceField}";
            case BarcodeObject b:
                return Symbologies.ById(b.Symbology).Name;
        }
        return o.Type;
    }

    /* ================= 편집 명령 (TODO wave — editor.js addObject/deleteSelection/duplicate/align/distribute/matchSize/_reorder) ================= */

    /// <summary>새 id 를 만든다 ("t3" 처럼 접두어 + 번호).</summary>
    public string NewId(string prefix)
    {
        var n = Objects.Count + 1;
        while (Objects.Any(o => o.Id == prefix + n)) n++;
        return prefix + n;
    }

    /// <summary>객체를 추가하고 선택한다.</summary>
    public LabelObject Add(LabelObject o, string label = "객체 추가")
    {
        // TODO wave: 이력 기록. 골격은 추가·선택·알림만.
        if (string.IsNullOrEmpty(o.Id)) o.Id = NewId(o.Type[..1]);
        Objects.Add(o);
        Select(new[] { o.Id }, silent: true);
        ModelChanged?.Invoke(label);
        SelectionChanged?.Invoke();
        Invalidate();
        return o;
    }

    public void DeleteSelection() { /* TODO wave */ }
    public void Duplicate() { /* TODO wave */ }

    /// <summary>선택을 dx,dy(mm) 만큼 옮긴다 (방향키 0.1mm / Shift 1mm).</summary>
    public void Nudge(double dx, double dy) { /* TODO wave */ }

    /// <summary>how: left|hcenter|right|top|vcenter|bottom. reference: "selection" | "label".</summary>
    public void Align(string how, string reference = "selection") { /* TODO wave */ }

    /// <summary>axis: "h" | "v".</summary>
    public void Distribute(string axis) { /* TODO wave */ }

    /// <summary>how: "w" | "h" | "both".</summary>
    public void MatchSize(string how) { /* TODO wave */ }

    public void BringToFront() { /* TODO wave */ }
    public void SendToBack() { /* TODO wave */ }
    public void Forward() { /* TODO wave */ }
    public void Backward() { /* TODO wave */ }

    /// <summary>선택 객체의 잠금.</summary>
    public void SetLocked(bool v) { /* TODO wave */ }

    /// <summary>선택 객체의 표시.</summary>
    public void SetVisible(bool v) { /* TODO wave */ }

    /// <summary>객체 위 좌표(mm) 히트 테스트 — 맨 위 객체. 없으면 null.</summary>
    public LabelObject? HitObject(double mx, double my) => null; // TODO wave

    /// <summary>편집 단축키(Delete · 방향키 · Ctrl+D · Ctrl+A · Esc). 처리했으면 true.</summary>
    public bool HandleKey(KeyEventArgs e) => false; // TODO wave

    /* ================= 마우스 (TODO wave — editor.js _bind: 휠 줌 · 스페이스/중버튼 팬 · 마키 · 이동 · 8핸들 리사이즈 · 스냅) ================= */

    protected override void OnMouseDown(MouseButtonEventArgs e) { base.OnMouseDown(e); Focus(); /* TODO wave */ }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = e.GetPosition(this);
        var (mx, my) = PxToMm(p.X, p.Y);
        HoverChanged?.Invoke($"{mx:F1}, {my:F1} mm");
        // TODO wave: 드래그(이동·리사이즈·마키·팬) 처리
    }
    protected override void OnMouseUp(MouseButtonEventArgs e) { base.OnMouseUp(e); /* TODO wave */ }
    protected override void OnMouseWheel(MouseWheelEventArgs e) { base.OnMouseWheel(e); /* TODO wave: 커서 기준 줌 */ }
    protected override void OnMouseLeave(MouseEventArgs e) { base.OnMouseLeave(e); /* TODO wave */ }

    /* ================= 렌더 ================= */

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        base.OnPaintSurface(e);
        var canvas = e.Surface.Canvas;
        canvas.Clear(CanvasBg);

        // DIP → 장치 픽셀 (PerMonitorV2)
        var dpiScale = ActualWidth > 0 ? e.Info.Width / ActualWidth : 1.0;
        canvas.Save();
        canvas.Scale((float)dpiScale);

        var L = Template.Label;
        var s = View.Scale;
        var (lx, ly) = MmToPx(0, 0);
        var LW = (float)(L.W * s);
        var LH = (float)(L.H * s);
        var labelRect = new SKRect((float)lx, (float)ly, (float)lx + LW, (float)ly + LH);

        // 용지 (그림자 + 흰 바탕)
        using (var shadow = new SKPaint { Color = new SKColor(0, 0, 0, 90), IsAntialias = true,
                   MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 8) })
            canvas.DrawRect(new SKRect(labelRect.Left, labelRect.Top + 3, labelRect.Right, labelRect.Bottom + 3), shadow);
        using (var white = new SKPaint { Color = SKColors.White })
            canvas.DrawRect(labelRect, white);

        // 배경 + 객체 — Core 렌더러 하나를 지난다 (화면·PDF·ZPL 동일)
        LastRenderError = null;
        try
        {
            Context.ForExport = false;
            Context.Objects = Template.Objects;
            LabelRenderer.Render(canvas, Template, Context, s, lx, ly);
        }
        catch (Exception ex)
        {
            LastRenderError = ex.Message;
            DrawRenderError(canvas, labelRect, ex);
        }

        if (!PreviewMode)
        {
            DrawGrid(canvas, labelRect);
            DrawIssueFrames(canvas, s, lx, ly);
            DrawLinkOverlay(canvas, s, lx, ly);
            DrawSelection(canvas, s, lx, ly);
            DrawSnapGuides(canvas);
            DrawRulers(canvas, (float)ActualWidth, (float)ActualHeight);
        }

        // 라벨 외곽선
        using (var edge = new SKPaint { Color = CanvasEdge, Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = false })
            canvas.DrawRect(labelRect, edge);

        canvas.Restore();
    }

    /// <summary>Core 가 아직 그리지 못할 때(미구현·예외) 라벨 위에 사유를 적는다.</summary>
    private static void DrawRenderError(SKCanvas canvas, SKRect labelRect, Exception ex)
    {
        canvas.Save();
        canvas.ClipRect(labelRect);
        using var paint = new SKPaint { Color = new SKColor(0x9C, 0x00, 0x06), TextSize = 12, IsAntialias = true };
        canvas.DrawText("렌더 실패: " + ex.Message, labelRect.Left + 6, labelRect.Top + 16, paint);
        canvas.Restore();
    }

    // ---- 편집 장식 (TODO wave: editor.js render 후반 · _drawRulers · _frame · _ghost) ----
    private void DrawGrid(SKCanvas canvas, SKRect labelRect) { /* TODO wave: ShowGrid && s*GridMm > 4 → rgba(26,111,181,.16) 1px */ }
    private void DrawIssueFrames(SKCanvas canvas, double s, double ox, double oy) { /* TODO wave: 넘침·영역 밖·오류 테두리, 고스트 텍스트 */ }
    private void DrawLinkOverlay(SKCanvas canvas, double s, double ox, double oy) { /* TODO wave: LinkMode=="on" 이고 선택 1개면 같은 값을 쓰는 객체를 주황(#E67E00)으로 */ }
    private void DrawSelection(SKCanvas canvas, double s, double ox, double oy) { /* TODO wave: #1A6FB5 점선 + 8px 핸들 (Locked 면 없음) */ }
    private void DrawSnapGuides(SKCanvas canvas) { /* TODO wave: #E0218A 안내선 */ }
    private void DrawRulers(SKCanvas canvas, float cw, float ch) { /* TODO wave: ShowRulers 면 좌/상단 22px 눈금자(mm) */ }
}

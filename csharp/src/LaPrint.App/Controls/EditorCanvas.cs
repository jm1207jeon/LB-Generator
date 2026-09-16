// 캔버스 라벨 편집기 — js/editor.js 의 SKElement 판.
//
// 좌표계: 라벨 좌상단이 원점, 단위 mm. 화면 px(DIP) = mm * View.Scale + View.Ox
// 눈금자(Ruler)가 캔버스 안쪽 좌/상단을 차지한다.
// SKElement 는 장치 픽셀로 그리므로 OnPaintSurface 에서 DPI 배율(e.Info.Width / ActualWidth)을 캔버스 변환으로 걸고,
// 마우스 좌표(DIP)와 View 는 모두 DIP 로 통일한다.
//
// 기능
//  - 휠 줌(커서 기준) / 스페이스·중버튼 패닝 / 빈 곳 드래그 = 사각형 선택 (잠금이면 팬)
//  - 객체 이동·리사이즈(8핸들), 다중 선택 시 바운딩박스 단위 조작
//  - 스냅: 다른 객체의 6개 모서리·중심 + 라벨 경계·중심선 (Alt로 일시 해제)
//  - 실행취소/다시실행 (스냅샷 + 트랜잭션)
//  - 정렬/균등분배/크기맞춤 · 잠금/숨김/z-order
//  - 라벨 밖으로 나가거나 내용이 넘치는 객체 경고 표시 · 데이터 링크 오버레이
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using LaPrint.Core.Barcode;
using LaPrint.Core.Model;
using LaPrint.Core.Render;
using LaPrint.Core.Typography;
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

    private const double MinZoomPercent = 5;
    private const double MaxZoomPercent = 2000;

    private static readonly SKColor CanvasBg = new(0x8A, 0x97, 0xA6);
    private static readonly SKColor CanvasEdge = new(0x4B, 0x55, 0x63);
    private static readonly SKColor Accent = new(0x1A, 0x6F, 0xB5);
    private static readonly SKColor AccentSoft = new(0xE3, 0xEE, 0xF8);
    private static readonly SKColor Fail = new(0x9C, 0x00, 0x06);
    private static readonly SKColor Warn = new(0x9A, 0x5B, 0x00);
    private static readonly SKColor Ink3 = new(0x6B, 0x72, 0x80);
    private static readonly SKColor PaperBg = new(0xF4, 0xF6, 0xF9);
    private static readonly SKColor Line = new(0xCC, 0xD3, 0xDB);
    private static readonly SKColor SnapGuideColor = new(0xE0, 0x21, 0x8A);
    private static readonly SKColor LinkAccent = new(0xE6, 0x7E, 0x00);
    private static readonly SKColor LinkPurple = new(0x9C, 0x27, 0xB0);

    private static SKTypeface? _uiFace;
    private static SKTypeface? _monoFace;

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

    private readonly EditorHistory _history = new();
    private Drag? _drag;
    private readonly List<SnapGuide> _guides = new();

    /// <summary>드래그 상태 (editor.js this.drag).</summary>
    private sealed class Drag
    {
        public DragMode Mode;
        /// <summary>시작 위치 — 팬은 DIP, 나머지는 mm.</summary>
        public double Sx, Sy;
        /// <summary>팬 시작 시 원점.</summary>
        public double Ox, Oy;
        /// <summary>마키 현재 끝점(mm).</summary>
        public double Ex, Ey;
        public bool Add;
        public bool Moved;
        public string Dir = "";
        public List<(LabelObject O, double X, double Y, double W, double H)> Start = new();
        public SKRect Box;
    }

    private enum DragMode { Pan, Move, Resize, Marquee }

    /* ================= 이벤트 ================= */

    public event Action? SelectionChanged;
    /// <summary>모델이 바뀌었다 (인수 = 이력 라벨).</summary>
    public event Action<string>? ModelChanged;
    public event Action? ViewChanged;
    /// <summary>마우스가 라벨 위를 지날 때 "x.x, y.y mm" (선택이 있으면 "· w×h" 도).</summary>
    public event Action<string>? HoverChanged;

    /* ================= 좌표 ================= */

    public (double X, double Y) MmToPx(double x, double y) => (x * View.Scale + View.Ox, y * View.Scale + View.Oy);
    public (double X, double Y) PxToMm(double x, double y) => ((x - View.Ox) / View.Scale, (y - View.Oy) / View.Scale);

    public void Invalidate() => InvalidateVisual();

    private static double R2(double v) => Math.Round(v * 100) / 100;
    private static double R1(double v) => Math.Round(v * 10) / 10;

    /* ================= 보기 ================= */

    /// <summary>라벨 전체가 보이도록 배율·원점을 맞춘다 (여백 24px).</summary>
    public void ZoomFit()
    {
        var cw = ActualWidth - RulerSize;
        var ch = ActualHeight - RulerSize;
        var L = Template.Label;
        if (cw <= 10 || ch <= 10 || L.W <= 0 || L.H <= 0) return;
        const double pad = 24;
        var s = Math.Min((cw - pad * 2) / L.W, (ch - pad * 2) / L.H);
        View.Scale = ClampScale(s);
        View.Ox = RulerSize + (cw - L.W * View.Scale) / 2;
        View.Oy = RulerSize + (ch - L.H * View.Scale) / 2;
        ViewChanged?.Invoke();
        Invalidate();
    }

    /// <summary>화면 가운데를 기준으로 배율(%)을 바꾼다.</summary>
    public void ZoomTo(double percent)
    {
        percent = Math.Clamp(percent, MinZoomPercent, MaxZoomPercent);
        var cx = RulerSize + (ActualWidth - RulerSize) / 2;
        var cy = RulerSize + (ActualHeight - RulerSize) / 2;
        var (mx, my) = PxToMm(cx, cy);
        View.Scale = percent / 100 * PxPerMmAt100;
        View.Ox = cx - mx * View.Scale;
        View.Oy = cy - my * View.Scale;
        ViewChanged?.Invoke();
        Invalidate();
    }

    private static double ClampScale(double s)
        => Math.Clamp(s, MinZoomPercent / 100 * PxPerMmAt100, MaxZoomPercent / 100 * PxPerMmAt100);

    /* ================= 이력 (editor.js _snapshot/_restore/beginTx/endTx) ================= */

    public bool CanUndo => _history.CanUndo;
    public bool CanRedo => _history.CanRedo;
    public string UndoLabel => _history.UndoLabel;
    public string RedoLabel => _history.RedoLabel;

    public void Undo()
    {
        if (_drag is not null) return;
        var label = _history.UndoLabel;
        if (!_history.Undo(Template)) return;
        AfterRestore(label);
    }

    public void Redo()
    {
        if (_drag is not null) return;
        var label = _history.RedoLabel;
        if (!_history.Redo(Template)) return;
        AfterRestore(label);
    }

    private void AfterRestore(string label)
    {
        var alive = new HashSet<string>(Objects.Select(o => o.Id));
        Selection.RemoveWhere(id => !alive.Contains(id));
        Context.Objects = Objects;
        SelectionChanged?.Invoke();
        ModelChanged?.Invoke(label);
        Invalidate();
    }

    /// <summary>드래그 같은 연속 변경의 시작 — 스냅샷을 잡아 둔다.</summary>
    public void BeginTx(string label) => _history.BeginTx(label, Template);

    /// <summary>연속 변경의 끝. changed=false 면 스냅샷을 버린다.</summary>
    public void EndTx(bool changed = true) => _history.EndTx(changed, Template);

    /// <summary>한 번의 변경을 이력 한 칸으로 기록하며 실행한다.</summary>
    public void Commit(string label, Action fn)
    {
        BeginTx(label);
        try { fn(); }
        finally { EndTx(true); }
        ModelChanged?.Invoke(label);
        Invalidate();
    }

    public void ResetHistory() => _history.Reset();

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

    /* ================= 편집 명령 (editor.js addObject/deleteSelection/duplicate/align/distribute/matchSize/_reorder) ================= */

    /// <summary>새 id 를 만든다 ("t3" 처럼 접두어 + 번호).</summary>
    public string NewId(string prefix)
    {
        var n = 1;
        var used = new HashSet<string>(Objects.Select(o => o.Id));
        while (used.Contains(prefix + n)) n++;
        return prefix + n;
    }

    private static string PrefixOf(LabelObject o) => o switch
    {
        TextObject => "t",
        ImageObject => "i",
        BarcodeObject => "b",
        _ => "o",
    };

    /// <summary>객체를 추가하고 선택한다.</summary>
    public LabelObject Add(LabelObject o, string label = "객체 추가")
    {
        if (string.IsNullOrEmpty(o.Id) || Objects.Any(x => x.Id == o.Id)) o.Id = NewId(PrefixOf(o));
        BeginTx(label);
        try
        {
            Objects.Add(o);
            Select(new[] { o.Id }, silent: true);
        }
        finally { EndTx(true); }
        SelectionChanged?.Invoke();
        ModelChanged?.Invoke(label);
        Invalidate();
        return o;
    }

    /// <summary>선택 객체 삭제 — 목록을 제자리에서 고친다 (RenderContext 가 같은 목록을 본다).</summary>
    public void DeleteSelection()
    {
        if (Selection.Count == 0) return;
        var n = Selection.Count;
        var ids = new HashSet<string>(Selection);
        BeginTx($"객체 {n}개 삭제");
        try
        {
            Objects.RemoveAll(o => ids.Contains(o.Id));
            Selection.Clear();
        }
        finally { EndTx(true); }
        SelectionChanged?.Invoke();
        ModelChanged?.Invoke($"객체 {n}개 삭제");
        Invalidate();
    }

    /// <summary>선택 객체를 +2mm 옮겨 복제하고 복제본을 선택한다.</summary>
    public void Duplicate()
    {
        var sel = SelectedObjects();
        if (sel.Count == 0) return;
        var label = $"객체 {sel.Count}개 복제";
        BeginTx(label);
        try
        {
            var ids = new List<string>();
            foreach (var o in sel)
            {
                var c = EditorHistory.Clone(o);
                c.Id = NewId(PrefixOf(o));
                c.X = R1(c.X + 2);
                c.Y = R1(c.Y + 2);
                if (!string.IsNullOrEmpty(c.Name)) c.Name += " 복사본";
                Objects.Add(c);
                ids.Add(c.Id);
            }
            Select(ids, silent: true);
        }
        finally { EndTx(true); }
        SelectionChanged?.Invoke();
        ModelChanged?.Invoke(label);
        Invalidate();
    }

    /// <summary>선택을 dx,dy(mm) 만큼 옮긴다 (방향키 0.1mm / Shift 1mm).</summary>
    public void Nudge(double dx, double dy)
    {
        if (Selection.Count == 0) return;
        Commit("이동", () =>
        {
            foreach (var o in SelectedObjects())
            {
                if (o.Locked) continue;
                o.X = R2(o.X + dx);
                o.Y = R2(o.Y + dy);
            }
        });
        SelectionChanged?.Invoke();
    }

    /// <summary>how: left|hcenter|right|top|vcenter|bottom. reference: "selection" | "label".</summary>
    public void Align(string how, string reference = "selection")
    {
        var sel = SelectedObjects().Where(o => !o.Locked).ToList();
        if (sel.Count == 0) return;
        SKRect b;
        if (reference == "label") b = new SKRect(0, 0, (float)Template.Label.W, (float)Template.Label.H);
        else
        {
            var sb = SelectionBounds();
            if (sb is null) return;
            b = sb.Value;
        }
        Commit("정렬", () =>
        {
            foreach (var o in sel)
            {
                switch (how)
                {
                    case "left": o.X = b.Left; break;
                    case "right": o.X = b.Right - o.W; break;
                    case "hcenter": o.X = b.Left + (b.Width - o.W) / 2; break;
                    case "top": o.Y = b.Top; break;
                    case "bottom": o.Y = b.Bottom - o.H; break;
                    case "vcenter": o.Y = b.Top + (b.Height - o.H) / 2; break;
                }
                o.X = R2(o.X); o.Y = R2(o.Y);
            }
        });
        SelectionChanged?.Invoke();
    }

    /// <summary>axis: "h" | "v" — 객체 사이 간격을 균등하게 (3개 이상).</summary>
    public void Distribute(string axis)
    {
        var sel = SelectedObjects().Where(o => !o.Locked).ToList();
        if (sel.Count < 3) return;
        Commit("균등 분배", () =>
        {
            var h = axis == "h";
            Func<LabelObject, double> pos = o => h ? o.X : o.Y;
            Func<LabelObject, double> size = o => h ? o.W : o.H;
            sel.Sort((a, b) => pos(a).CompareTo(pos(b)));
            var first = sel[0]; var last = sel[^1];
            var span = pos(last) + size(last) - pos(first);
            var total = sel.Sum(size);
            var gap = (span - total) / (sel.Count - 1);
            var cur = pos(first);
            foreach (var o in sel)
            {
                if (h) o.X = R2(cur); else o.Y = R2(cur);
                cur += size(o) + gap;
            }
        });
        SelectionChanged?.Invoke();
    }

    /// <summary>how: "w" | "h" | "both" — 첫 선택 객체 크기에 맞춤.</summary>
    public void MatchSize(string how)
    {
        var sel = SelectedObjects().Where(o => !o.Locked).ToList();
        if (sel.Count < 2) return;
        var r = sel[0];
        Commit("크기 맞춤", () =>
        {
            for (var i = 1; i < sel.Count; i++)
            {
                if (how is "w" or "both") sel[i].W = r.W;
                if (how is "h" or "both") sel[i].H = r.H;
            }
        });
        SelectionChanged?.Invoke();
    }

    public void BringToFront() => Reorder("맨 앞으로", (rest, sel) => rest.Concat(sel).ToList());
    public void SendToBack() => Reorder("맨 뒤로", (rest, sel) => sel.Concat(rest).ToList());
    public void Forward() => MoveLayer(+1);
    public void Backward() => MoveLayer(-1);

    private void Reorder(string label, Func<List<LabelObject>, List<LabelObject>, List<LabelObject>> fn)
    {
        if (Selection.Count == 0) return;
        Commit(label, () =>
        {
            var sel = SelectedObjects().ToList();
            var rest = Objects.Where(o => !Selection.Contains(o.Id)).ToList();
            var next = fn(rest, sel);
            Objects.Clear();
            Objects.AddRange(next);
        });
    }

    // dir: -1 뒤로, +1 앞으로 (선택 1개일 때)
    private void MoveLayer(int dir)
    {
        if (Selection.Count != 1) return;
        var id = Selection.First();
        var i = Objects.FindIndex(o => o.Id == id);
        var j = i + dir;
        if (i < 0 || j < 0 || j >= Objects.Count) return;
        Commit(dir > 0 ? "앞으로" : "뒤로", () => (Objects[i], Objects[j]) = (Objects[j], Objects[i]));
    }

    /// <summary>선택 객체의 잠금.</summary>
    public void SetLocked(bool v)
    {
        var sel = SelectedObjects();
        if (sel.Count == 0) return;
        Commit("잠금 전환", () => { foreach (var o in sel) o.Locked = v; });
        SelectionChanged?.Invoke();
    }

    /// <summary>선택 객체의 표시.</summary>
    public void SetVisible(bool v)
    {
        var sel = SelectedObjects();
        if (sel.Count == 0) return;
        Commit("표시 전환", () => { foreach (var o in sel) o.Visible = v; });
        SelectionChanged?.Invoke();
    }

    /* ================= 히트 테스트 ================= */

    /// <summary>객체 위 좌표(mm) 히트 테스트 — 맨 위 객체. 숨긴 객체는 건너뛴다(잠긴 객체는 고를 수는 있다). 없으면 null.</summary>
    public LabelObject? HitObject(double mx, double my)
    {
        for (var i = Objects.Count - 1; i >= 0; i--)
        {
            var o = Objects[i];
            if (!o.Visible) continue;
            if (mx >= o.X && mx <= o.X + o.W && my >= o.Y && my <= o.Y + o.H) return o;
        }
        return null;
    }

    /// <summary>8개 핸들의 화면 위치(DIP) (editor.js _handleRects).</summary>
    private List<(string Dir, double X, double Y)> HandleRects()
    {
        var list = new List<(string, double, double)>();
        var b = SelectionBounds();
        if (b is null) return list;
        var (x1, y1) = MmToPx(b.Value.Left, b.Value.Top);
        var (x2, y2) = MmToPx(b.Value.Right, b.Value.Bottom);
        var cx = (x1 + x2) / 2; var cy = (y1 + y2) / 2;
        list.Add(("nw", x1, y1)); list.Add(("n", cx, y1)); list.Add(("ne", x2, y1));
        list.Add(("w", x1, cy)); list.Add(("e", x2, cy));
        list.Add(("sw", x1, y2)); list.Add(("s", cx, y2)); list.Add(("se", x2, y2));
        return list;
    }

    private string? HitHandle(double px, double py)
    {
        if (Selection.Count == 0 || Locked) return null;
        var sel = SelectedObjects();
        if (sel.Count == 0 || sel.All(o => o.Locked)) return null;
        const double H = 8;
        foreach (var (dir, x, y) in HandleRects())
            if (Math.Abs(px - x) <= H && Math.Abs(py - y) <= H) return dir;
        return null;
    }

    /* ================= 키보드 ================= */

    /// <summary>편집 단축키(Delete · 방향키 · Ctrl+D · Ctrl+A · Esc · Tab). 처리했으면 true.</summary>
    public bool HandleKey(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mod = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);

        if (mod && key == Key.A) { SelectAll(); return true; }
        if (mod && key == Key.D) { Duplicate(); return true; }
        if (mod && key == Key.D1) { ZoomTo(100); return true; }
        if (Locked) return false;
        if (key is Key.Delete or Key.Back)
        {
            if (Selection.Count > 0) { DeleteSelection(); return true; }
            return false;
        }
        if (key == Key.Escape) { Select(Array.Empty<string>()); return true; }
        if (key == Key.Tab && Objects.Count > 0)
        {
            var list = Objects.Where(o => !o.Locked && o.Visible).ToList();
            if (list.Count == 0) return true;
            var cur = Selection.FirstOrDefault();
            var i = list.FindIndex(o => o.Id == cur);
            var next = list[((i + (shift ? -1 : 1)) % list.Count + list.Count) % list.Count];
            Select(new[] { next.Id });
            return true;
        }
        var step = shift ? 1 : (alt ? 0.01 : 0.1);
        (double dx, double dy)? mv = key switch
        {
            Key.Left => (-step, 0),
            Key.Right => (step, 0),
            Key.Up => (0, -step),
            Key.Down => (0, step),
            _ => null,
        };
        if (mv is not null && Selection.Count > 0)
        {
            Nudge(mv.Value.dx, mv.Value.dy);
            return true;
        }
        return false;
    }

    /* ================= 마우스 (editor.js _bind) ================= */

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        var p = e.GetPosition(this);
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && !Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            // Shift+휠 = 가로 팬
            View.Ox += e.Delta > 0 ? 40 : -40;
        }
        else
        {
            var factor = e.Delta > 0 ? 1.12 : 1 / 1.12;
            var (mx, my) = PxToMm(p.X, p.Y);
            View.Scale = ClampScale(View.Scale * factor);
            View.Ox = p.X - mx * View.Scale;
            View.Oy = p.Y - my * View.Scale;
        }
        ViewChanged?.Invoke();
        Invalidate();
        e.Handled = true;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (_drag is not null) return;
        var p = e.GetPosition(this);
        var (mx, my) = PxToMm(p.X, p.Y);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) || Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var space = Keyboard.IsKeyDown(Key.Space);

        if (e.ChangedButton is MouseButton.Middle or MouseButton.Right || space || Locked)
        {
            _drag = new Drag { Mode = DragMode.Pan, Sx = p.X, Sy = p.Y, Ox = View.Ox, Oy = View.Oy };
            Cursor = Cursors.SizeAll;
            CaptureMouse();
            e.Handled = true;
            return;
        }
        if (e.ChangedButton != MouseButton.Left) return;

        var dir = HitHandle(p.X, p.Y);
        if (dir is not null)
        {
            var sel = SelectedObjects().Where(o => !o.Locked).ToList();
            BeginTx("크기 조절");
            _drag = new Drag
            {
                Mode = DragMode.Resize, Dir = dir, Sx = mx, Sy = my,
                Start = sel.Select(o => (o, o.X, o.Y, o.W, o.H)).ToList(),
                Box = SelectionBounds() ?? SKRect.Empty,
            };
            CaptureMouse();
            e.Handled = true;
            return;
        }

        var obj = HitObject(mx, my);
        if (obj is not null)
        {
            if (shift)
            {
                if (!Selection.Remove(obj.Id)) Selection.Add(obj.Id);
                Select(Selection.ToList());
            }
            else if (!Selection.Contains(obj.Id))
            {
                Select(new[] { obj.Id });
            }
            BeginTx("이동");
            _drag = new Drag
            {
                Mode = DragMode.Move, Sx = mx, Sy = my,
                Start = SelectedObjects().Where(o => !o.Locked).Select(o => (o, o.X, o.Y, o.W, o.H)).ToList(),
                Box = SelectionBounds() ?? SKRect.Empty,
            };
        }
        else
        {
            _drag = new Drag { Mode = DragMode.Marquee, Sx = mx, Sy = my, Ex = mx, Ey = my, Add = shift };
            if (!shift) Select(Array.Empty<string>());
        }
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = e.GetPosition(this);
        var (mx, my) = PxToMm(p.X, p.Y);

        if (_drag is null)
        {
            var dir = HitHandle(p.X, p.Y);
            Cursor = dir switch
            {
                "n" or "s" => Cursors.SizeNS,
                "e" or "w" => Cursors.SizeWE,
                "nw" or "se" => Cursors.SizeNWSE,
                "ne" or "sw" => Cursors.SizeNESW,
                _ => Keyboard.IsKeyDown(Key.Space) ? Cursors.Hand
                     : (!Locked && HitObject(mx, my) is not null ? Cursors.SizeAll : Cursors.Arrow),
            };
            RaiseHover(mx, my);
            return;
        }

        var d = _drag;
        if (d.Mode == DragMode.Pan)
        {
            View.Ox = d.Ox + (p.X - d.Sx);
            View.Oy = d.Oy + (p.Y - d.Sy);
            ViewChanged?.Invoke();
            Invalidate();
            return;
        }
        if (d.Mode == DragMode.Marquee)
        {
            d.Ex = mx; d.Ey = my;
            Invalidate();
            return;
        }

        var snapOff = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) || !SnapEnabled;
        var tolMm = SnapPx / View.Scale;
        var dx = mx - d.Sx; var dy = my - d.Sy;
        var ids = new HashSet<string>(d.Start.Select(s => s.O.Id));
        _guides.Clear();

        if (d.Mode == DragMode.Move)
        {
            d.Moved = true;
            if (!snapOff)
            {
                var s = Snapping.SnapMove(d.Box.Left + dx, d.Box.Top + dy, d.Box.Width, d.Box.Height, tolMm, Template.Label, Objects, ids);
                dx += s.Dx; dy += s.Dy;
                _guides.AddRange(s.Guides);
            }
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) { if (Math.Abs(dx) > Math.Abs(dy)) dy = 0; else dx = 0; }   // 직선 이동
            foreach (var s in d.Start)
            {
                s.O.X = R2(s.X + dx);
                s.O.Y = R2(s.Y + dy);
            }
            RaiseHover(mx, my);
            Invalidate();
        }
        else if (d.Mode == DragMode.Resize)
        {
            var b0 = d.Box;
            double nx = b0.Left, ny = b0.Top, nw = b0.Width, nh = b0.Height;
            var dir = d.Dir;

            if (dir.Contains('e'))
            {
                double v = b0.Right + dx;
                if (!snapOff) { var s = Snapping.SnapValue(v, 'x', tolMm, Template.Label, Objects, ids); v = s.V; if (s.Guide is not null) _guides.Add(s.Guide.Value); }
                nw = v - b0.Left;
            }
            if (dir.Contains('w'))
            {
                double v = b0.Left + dx;
                if (!snapOff) { var s = Snapping.SnapValue(v, 'x', tolMm, Template.Label, Objects, ids); v = s.V; if (s.Guide is not null) _guides.Add(s.Guide.Value); }
                nx = v; nw = b0.Right - v;
            }
            if (dir.Contains('s'))
            {
                double v = b0.Bottom + dy;
                if (!snapOff) { var s = Snapping.SnapValue(v, 'y', tolMm, Template.Label, Objects, ids); v = s.V; if (s.Guide is not null) _guides.Add(s.Guide.Value); }
                nh = v - b0.Top;
            }
            if (dir.Contains('n'))
            {
                double v = b0.Top + dy;
                if (!snapOff) { var s = Snapping.SnapValue(v, 'y', tolMm, Template.Label, Objects, ids); v = s.V; if (s.Guide is not null) _guides.Add(s.Guide.Value); }
                ny = v; nh = b0.Bottom - v;
            }
            // 종횡비 고정: 모서리 핸들 + (이미지/바코드 또는 Shift)
            var only = d.Start.Count == 1 ? d.Start[0].O : null;
            var onlyPic = only is ImageObject or BarcodeObject;
            var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            var keepAr = dir.Length == 2 && ((onlyPic && !shift) || (shift && !onlyPic));
            if (keepAr && b0.Width > 0 && b0.Height > 0)
            {
                var ar = b0.Width / (double)b0.Height;
                if (Math.Abs(nw / b0.Width) > Math.Abs(nh / b0.Height)) nh = nw / ar; else nw = nh * ar;
                if (dir.Contains('n')) ny = b0.Bottom - nh;
                if (dir.Contains('w')) nx = b0.Right - nw;
            }
            if (nw < 0.5 || nh < 0.5) return;

            var fx = nw / b0.Width; var fy = nh / b0.Height;
            foreach (var s in d.Start)
            {
                s.O.X = R2(nx + (s.X - b0.Left) * fx);
                s.O.Y = R2(ny + (s.Y - b0.Top) * fy);
                s.O.W = R2(s.W * fx);
                s.O.H = R2(s.H * fy);
            }
            RaiseHover(mx, my);
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        EndDrag();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_drag is null) Cursor = Cursors.Arrow;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        EndDrag();
    }

    private void EndDrag()
    {
        var d = _drag;
        _drag = null;
        _guides.Clear();
        Cursor = Cursors.Arrow;
        if (IsMouseCaptured) ReleaseMouseCapture();
        if (d is null) return;

        if (d.Mode == DragMode.Marquee)
        {
            var x0 = Math.Min(d.Sx, d.Ex); var x1 = Math.Max(d.Sx, d.Ex);
            var y0 = Math.Min(d.Sy, d.Ey); var y1 = Math.Max(d.Sy, d.Ey);
            if (Math.Abs(x1 - x0) > 0.5 || Math.Abs(y1 - y0) > 0.5)
            {
                var ids = d.Add ? Selection.ToList() : new List<string>();
                foreach (var o in Objects)
                {
                    if (o.Locked || !o.Visible) continue;
                    if (o.X < x1 && o.X + o.W > x0 && o.Y < y1 && o.Y + o.H > y0 && !ids.Contains(o.Id)) ids.Add(o.Id);
                }
                Select(ids);
            }
            Invalidate();
            return;
        }
        if (d.Mode is DragMode.Move or DragMode.Resize)
        {
            var label = d.Mode == DragMode.Move ? "이동" : "크기 조절";
            var changed = d.Start.Any(s => s.O.X != s.X || s.O.Y != s.Y || s.O.W != s.W || s.O.H != s.H);
            EndTx(changed);
            SelectionChanged?.Invoke();
            if (changed) ModelChanged?.Invoke(label);
        }
        Invalidate();
    }

    /// <summary>"x, y · w×h" (mm) — 선택이 있으면 크기도 함께.</summary>
    private void RaiseHover(double mx, double my)
    {
        var b = SelectionBounds();
        var text = b is null
            ? $"{mx:F1}, {my:F1} mm"
            : $"{mx:F1}, {my:F1} · {b.Value.Width:F1}×{b.Value.Height:F1} mm";
        HoverChanged?.Invoke(text);
    }

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
        }

        // 라벨 외곽선
        using (var edge = new SKPaint { Color = CanvasEdge, Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = false })
            canvas.DrawRect(labelRect, edge);

        if (!PreviewMode)
        {
            DrawSelection(canvas, s, lx, ly);
            DrawLinkOverlay(canvas, s, lx, ly);
            DrawSnapGuides(canvas);
            DrawMarquee(canvas, s);
            DrawRulers(canvas, (float)ActualWidth, (float)ActualHeight);
        }

        canvas.Restore();
    }

    /// <summary>Core 가 아직 그리지 못할 때(미구현·예외) 라벨 위에 사유를 적는다.</summary>
    private static void DrawRenderError(SKCanvas canvas, SKRect labelRect, Exception ex)
    {
        canvas.Save();
        canvas.ClipRect(labelRect);
        using var paint = new SKPaint { Color = Fail, TextSize = 12, IsAntialias = true, Typeface = UiFace() };
        canvas.DrawText("렌더 실패: " + ex.Message, labelRect.Left + 6, labelRect.Top + 16, paint);
        canvas.Restore();
    }

    /* ---- 글꼴 (한글이 있는 UI 글꼴 · 눈금 숫자용 고정폭) ---- */

    private static SKTypeface UiFace()
    {
        if (_uiFace is not null) return _uiFace;
        try
        {
            _uiFace = SKFontManager.Default.MatchCharacter('가')
                      ?? SKTypeface.FromFamilyName("Malgun Gothic")
                      ?? SKTypeface.Default;
        }
        catch { _uiFace = SKTypeface.Default; }
        return _uiFace;
    }

    private static SKTypeface MonoFace()
    {
        if (_monoFace is not null) return _monoFace;
        try { _monoFace = SKTypeface.FromFamilyName("Consolas") ?? SKTypeface.Default; }
        catch { _monoFace = SKTypeface.Default; }
        return _monoFace;
    }

    /// <summary>글자 윗선을 y 에 맞춰 그린다 (canvas textBaseline = 'top').</summary>
    private static void DrawTextTop(SKCanvas canvas, string text, float x, float y, SKPaint paint)
        => canvas.DrawText(text, x, y - paint.FontMetrics.Ascent, paint);

    /* ---- 편집 장식 (editor.js render 후반 · _drawRulers · _frame · _ghost) ---- */

    private void DrawGrid(SKCanvas canvas, SKRect labelRect)
    {
        var s = View.Scale;
        if (!ShowGrid || s * GridMm <= 4) return;
        var L = Template.Label;
        canvas.Save();
        canvas.ClipRect(labelRect);
        using var paint = new SKPaint { Color = new SKColor(26, 111, 181, 41), StrokeWidth = 1, IsAntialias = false, Style = SKPaintStyle.Stroke };
        for (double x = 0; x <= L.W + 0.001; x += GridMm)
        {
            var px = (float)Math.Round(labelRect.Left + x * s) + 0.5f;
            canvas.DrawLine(px, labelRect.Top, px, labelRect.Bottom, paint);
        }
        for (double y = 0; y <= L.H + 0.001; y += GridMm)
        {
            var py = (float)Math.Round(labelRect.Top + y * s) + 0.5f;
            canvas.DrawLine(labelRect.Left, py, labelRect.Right, py, paint);
        }
        canvas.Restore();
    }

    /// <summary>객체별 편집 보조 표시 — 넘침/오류 테두리, 빈 텍스트·이미지 없음·바코드 오류 고스트, 잠금 표시 (drawObject 비출력 분기).</summary>
    private void DrawIssueFrames(SKCanvas canvas, double s, double ox, double oy)
    {
        var accentFaint = new SKColor(26, 111, 181, 71);
        foreach (var o in Objects)
        {
            if (!o.Visible) continue;
            var X = (float)(o.X * s + ox); var Y = (float)(o.Y * s + oy);
            var W = (float)(o.W * s); var H = (float)(o.H * s);

            switch (o)
            {
                case TextObject t:
                {
                    string txt;
                    try { txt = Context.ResolveText(t.Text ?? ""); } catch { txt = t.Text ?? ""; }
                    var over = false;
                    try
                    {
                        var b = TextLayoutEngine.Bounds(t, txt);
                        over = b.OverflowX || b.OverflowY;
                    }
                    catch { /* 조판 실패는 넘침 표시 없이 */ }
                    Frame(canvas, X, Y, W, H, over ? Fail : accentFaint, over ? 1.2f : 1f);
                    if (string.IsNullOrWhiteSpace(txt)) Ghost(canvas, X, Y, s, "빈 텍스트", Ink3);
                    break;
                }
                case ImageObject im:
                {
                    var img = im.Bitmap ?? SafeImageOf(im.Id);
                    var missing = img is null && !string.IsNullOrEmpty(im.SourceField);
                    Frame(canvas, X, Y, W, H, img is not null ? accentFaint : (missing ? Warn : new SKColor(107, 114, 128, 128)), 1f);
                    if (img is null)
                    {
                        var lbl = !string.IsNullOrEmpty(im.SourceField)
                            ? (!string.IsNullOrEmpty(im.FileName) ? $"이미지 없음: {im.FileName}" : $"이미지 슬롯 · {im.SourceField}")
                            : "이미지 슬롯";
                        Ghost(canvas, X, Y, s, lbl, missing ? Warn : Ink3);
                    }
                    break;
                }
                case BarcodeObject b:
                {
                    var bad = false;
                    string? err = null;
                    try
                    {
                        var data = BarcodeBinding.ResolveData(b, new BindingContext(Context.Fields, Objects, Context.ResolveText));
                        var res = BarcodeEncoder.Encode(b.Symbology, data, b.HumanReadable);
                        bad = res.Modules is null;
                        err = res.Error;
                    }
                    catch (Exception ex) { bad = true; err = ex.Message; }
                    Frame(canvas, X, Y, W, H, bad ? Fail : new SKColor(46, 158, 91, 115), bad ? 1.2f : 1f);
                    if (bad) Ghost(canvas, X, Y, s, string.IsNullOrEmpty(err) ? "값 없음" : err, Fail);
                    break;
                }
            }

            // 점검 결과(영역 밖 등) 테두리
            if (Issues.TryGetValue(o.Id, out var level) && level is "error" or "warn")
                Frame(canvas, X, Y, W, H, level == "error" ? Fail : Warn, 1.2f);

            // 잠금 표시
            if (o.Locked) DrawLockGlyph(canvas, X + 2, Y + 2, (float)Math.Max(9, Math.Min(13, s * 2.4)));
        }
    }

    private SKBitmap? SafeImageOf(string id)
    {
        try { return Context.ImageOf?.Invoke(id); } catch { return null; }
    }

    private static void Frame(SKCanvas canvas, float X, float Y, float W, float H, SKColor color, float lw)
    {
        using var paint = new SKPaint
        {
            Color = color, Style = SKPaintStyle.Stroke, StrokeWidth = lw, IsAntialias = false,
            PathEffect = SKPathEffect.CreateDash(new float[] { 3, 3 }, 0),
        };
        canvas.DrawRect((float)Math.Round(X) + .5f, (float)Math.Round(Y) + .5f, W, H, paint);
    }

    private static void Ghost(SKCanvas canvas, float X, float Y, double scale, string label, SKColor color)
    {
        using var paint = new SKPaint
        {
            Color = color, IsAntialias = true, Typeface = UiFace(),
            TextSize = (float)Math.Max(9, Math.Min(14, scale * 2.2)),
        };
        DrawTextTop(canvas, label, X + 3, Y + 3, paint);
    }

    /// <summary>작은 자물쇠 — 이모지 글꼴에 기대지 않고 도형으로 그린다.</summary>
    private static void DrawLockGlyph(SKCanvas canvas, float x, float y, float size)
    {
        using var paint = new SKPaint { Color = new SKColor(107, 114, 128, 140), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var stroke = new SKPaint { Color = new SKColor(107, 114, 128, 140), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = Math.Max(1, size / 7) };
        var bw = size * 0.8f; var bh = size * 0.55f;
        var by = y + size - bh;
        canvas.DrawRoundRect(new SKRect(x, by, x + bw, by + bh), size * 0.12f, size * 0.12f, paint);
        var r = bw * 0.28f;
        var arc = new SKRect(x + bw / 2 - r, by - r * 1.3f, x + bw / 2 + r, by + r * 0.7f);
        canvas.DrawArc(arc, 180, 180, false, stroke);
    }

    /// <summary>링크 오버레이 (link.js draw) — 링크 보기가 켜져 있고 정확히 하나를 선택했을 때만.</summary>
    private void DrawLinkOverlay(SKCanvas canvas, double s, double ox, double oy)
    {
        if (LinkMode == "off" || Selection.Count != 1) return;
        var selId = Selection.First();
        var self = ById(selId);
        if (self is null) return;

        var (ids, tokens) = LinkAnalysis.Related(Objects, new[] { selId });
        var direct = LinkAnalysis.DirectLinks(Objects).Where(l => l.From == selId || l.To == selId).ToList();
        if (ids.Count == 0 && direct.Count == 0) return;

        SKRect Rect(LabelObject o) => new((float)(o.X * s + ox), (float)(o.Y * s + oy), (float)((o.X + o.W) * s + ox), (float)((o.Y + o.H) * s + oy));
        var byId = Objects.ToDictionary(o => o.Id);

        canvas.Save();
        // 같은 값을 쓰는 객체 — 은은한 채움 + 실선 테두리
        using (var fill = new SKPaint { Color = new SKColor(230, 126, 0, 26), Style = SKPaintStyle.Fill })
        using (var stroke = new SKPaint { Color = LinkAccent, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, IsAntialias = true })
        {
            foreach (var id in ids)
            {
                if (!byId.TryGetValue(id, out var o) || !o.Visible) continue;
                var r = Rect(o);
                canvas.DrawRect(r, fill);
                canvas.DrawRect(new SKRect((float)Math.Round(r.Left) - 1.5f, (float)Math.Round(r.Top) - 1.5f, (float)Math.Round(r.Left) + r.Width + 1.5f, (float)Math.Round(r.Top) + r.Height + 1.5f), stroke);
            }
        }

        // 바코드 ↔ 객체 직접 참조는 화살표 하나로
        using (var line = new SKPaint { Color = LinkPurple, Style = SKPaintStyle.Stroke, StrokeWidth = 1.6f, IsAntialias = true, PathEffect = SKPathEffect.CreateDash(new float[] { 5, 3 }, 0) })
        using (var head = new SKPaint { Color = LinkPurple, Style = SKPaintStyle.Fill, IsAntialias = true })
        {
            foreach (var l in direct)
            {
                if (!byId.TryGetValue(l.From, out var a) || !byId.TryGetValue(l.To, out var b2) || !a.Visible || !b2.Visible) continue;
                var ra = Rect(a); var rb = Rect(b2);
                var ax = ra.MidX; var ay = ra.MidY; var bx = rb.MidX; var by = rb.MidY;
                canvas.DrawLine(bx, by, ax, ay, line);
                var ang = Math.Atan2(ay - by, ax - bx);
                using var path = new SKPath();
                path.MoveTo(ax, ay);
                path.LineTo((float)(ax - 10 * Math.Cos(ang - 0.4)), (float)(ay - 10 * Math.Sin(ang - 0.4)));
                path.LineTo((float)(ax - 10 * Math.Cos(ang + 0.4)), (float)(ay - 10 * Math.Sin(ang + 0.4)));
                path.Close();
                canvas.DrawPath(path, head);
            }
        }

        // 선택 객체 위에 이름표 하나만 — "LOT · 11곳"
        if (tokens.Count > 0)
        {
            var t = tokens.First();
            var label = LinkAnalysis.ShortLabel(t) + " · " + (ids.Count + 1) + "곳" + (tokens.Count > 1 ? $" 외 {tokens.Count - 1}종" : "");
            var r = Rect(self);
            using var text = new SKPaint { Color = SKColors.White, TextSize = 11, IsAntialias = true, Typeface = UiFace() };
            using var chip = new SKPaint { Color = LinkAccent, Style = SKPaintStyle.Fill, IsAntialias = true };
            var w = text.MeasureText(label) + 12;
            var bx = (float)Math.Min(Math.Max(4, r.Left), ActualWidth - w - 4);
            var by = r.Top - 20 < 4 ? r.Bottom + 4 : r.Top - 20;
            canvas.DrawRoundRect(new SKRect(bx, by, bx + w, by + 17), 4, 4, chip);
            var fm = text.FontMetrics;
            canvas.DrawText(label, bx + 6, by + 8.5f - (fm.Ascent + fm.Descent) / 2, text);
        }
        canvas.Restore();
    }

    /// <summary>선택 표시 — #1A6FB5 점선 + 다중 선택 경계 + 8px 핸들 (Locked 면 핸들 없음).</summary>
    private void DrawSelection(SKCanvas canvas, double s, double ox, double oy)
    {
        var sel = SelectedObjects();
        if (sel.Count == 0) return;
        using var dashed = new SKPaint
        {
            Color = Accent, Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = false,
            PathEffect = SKPathEffect.CreateDash(new float[] { 4, 3 }, 0),
        };
        foreach (var o in sel)
        {
            var x1 = (float)Math.Round(o.X * s + ox) + .5f;
            var y1 = (float)Math.Round(o.Y * s + oy) + .5f;
            canvas.DrawRect(x1, y1, (float)(o.W * s), (float)(o.H * s), dashed);
        }
        var b = SelectionBounds();
        if (b is null) return;
        if (sel.Count > 1)
        {
            using var solid = new SKPaint { Color = Accent, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, IsAntialias = false };
            var bx = (float)Math.Round(b.Value.Left * s + ox) + .5f;
            var by = (float)Math.Round(b.Value.Top * s + oy) + .5f;
            canvas.DrawRect(bx, by, (float)(b.Value.Width * s), (float)(b.Value.Height * s), solid);
        }
        if (Locked) return;
        var locked = sel.All(o => o.Locked);
        using var fill = new SKPaint { Color = locked ? new SKColor(0xBB, 0xBB, 0xBB) : SKColors.White, Style = SKPaintStyle.Fill };
        using var stroke = new SKPaint { Color = locked ? new SKColor(0x88, 0x88, 0x88) : Accent, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, IsAntialias = false };
        foreach (var (_, hx, hy) in HandleRects())
        {
            var r = new SKRect((float)hx - 4, (float)hy - 4, (float)hx + 4, (float)hy + 4);
            canvas.DrawRect(r, fill);
            canvas.DrawRect(r, stroke);
        }
    }

    private void DrawMarquee(SKCanvas canvas, double s)
    {
        if (_drag is null || _drag.Mode != DragMode.Marquee) return;
        var d = _drag;
        var (ax, ay) = MmToPx(Math.Min(d.Sx, d.Ex), Math.Min(d.Sy, d.Ey));
        var w = (float)(Math.Abs(d.Ex - d.Sx) * s); var h = (float)(Math.Abs(d.Ey - d.Sy) * s);
        using var fill = new SKPaint { Color = new SKColor(26, 111, 181, 31), Style = SKPaintStyle.Fill };
        using var stroke = new SKPaint { Color = Accent, Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = false };
        canvas.DrawRect((float)ax, (float)ay, w, h, fill);
        canvas.DrawRect((float)Math.Round(ax) + .5f, (float)Math.Round(ay) + .5f, w, h, stroke);
    }

    /// <summary>스냅 안내선 — #E0218A 점선, 같은 선은 한 번만.</summary>
    private void DrawSnapGuides(SKCanvas canvas)
    {
        if (_guides.Count == 0) return;
        var s = View.Scale;
        var (lx, ly) = MmToPx(0, 0);
        var LW = (float)(Template.Label.W * s); var LH = (float)(Template.Label.H * s);
        using var paint = new SKPaint
        {
            Color = SnapGuideColor, Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = false,
            PathEffect = SKPathEffect.CreateDash(new float[] { 5, 3 }, 0),
        };
        var seen = new HashSet<string>();
        foreach (var g in _guides)
        {
            var k = g.Axis + g.V.ToString("F3", CultureInfo.InvariantCulture);
            if (!seen.Add(k)) continue;
            if (g.Axis == 'x')
            {
                var px = (float)Math.Round(lx + g.V * s) + .5f;
                canvas.DrawLine(px, (float)ly - 20, px, (float)ly + LH + 20, paint);
            }
            else
            {
                var py = (float)Math.Round(ly + g.V * s) + .5f;
                canvas.DrawLine((float)lx - 20, py, (float)lx + LW + 20, py, paint);
            }
        }
    }

    /// <summary>눈금자 — 좌/상단 22px, mm 눈금, 선택 영역 하이라이트 (editor.js _drawRulers).</summary>
    private void DrawRulers(SKCanvas canvas, float cw, float ch)
    {
        if (!ShowRulers) return;
        const float R = Ruler;
        var s = View.Scale;
        var (lxD, lyD) = MmToPx(0, 0);
        var lx = (float)lxD; var ly = (float)lyD;
        var L = Template.Label;

        canvas.Save();
        using var bg = new SKPaint { Color = PaperBg, Style = SKPaintStyle.Fill };
        using var line = new SKPaint { Color = Line, Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = false };
        using var ink = new SKPaint { Color = Ink3, Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = false };
        using var text = new SKPaint { Color = Ink3, TextSize = 9, IsAntialias = true, Typeface = MonoFace() };
        canvas.DrawRect(0, 0, cw, R, bg);
        canvas.DrawRect(0, 0, R, ch, bg);
        canvas.DrawLine(0, R + .5f, cw, R + .5f, line);
        canvas.DrawLine(R + .5f, 0, R + .5f, ch, line);

        // 눈금 간격: 화면에서 최소 6px 이상 되는 mm 단위 선택
        var steps = new[] { 1, 2, 5, 10, 20, 50, 100 };
        var step = steps.FirstOrDefault(v => v * s >= 6);
        if (step == 0) step = 100;
        var labelEvery = step * (step * s >= 40 ? 1 : (step * s >= 20 ? 2 : 5));

        var startX = Math.Floor(((R - lx) / s) / step) * step;
        for (var mm = startX; mm * s + lx < cw; mm += step)
        {
            var px = (float)Math.Round(lx + mm * s) + .5f;
            if (px < R) continue;
            var major = Math.Abs(mm % labelEvery) < 0.001;
            var inLabel = mm >= -0.001 && mm <= L.W + 0.001;
            canvas.DrawLine(px, major ? R - 9 : R - 4, px, R, inLabel ? ink : line);
            if (major) DrawTextTop(canvas, ((int)Math.Round(mm)).ToString(CultureInfo.InvariantCulture), px + 2, 2, text);
        }
        var startY = Math.Floor(((R - ly) / s) / step) * step;
        for (var mm = startY; mm * s + ly < ch; mm += step)
        {
            var py = (float)Math.Round(ly + mm * s) + .5f;
            if (py < R) continue;
            var major = Math.Abs(mm % labelEvery) < 0.001;
            var inLabel = mm >= -0.001 && mm <= L.H + 0.001;
            canvas.DrawLine(major ? R - 9 : R - 4, py, R, py, inLabel ? ink : line);
            if (major)
            {
                canvas.Save();
                canvas.Translate(2, py - 2);
                canvas.RotateDegrees(-90);
                // 회전 후 x 축이 화면 위쪽 — 눈금 바로 위에서 끝나도록 오른쪽 정렬
                text.TextAlign = SKTextAlign.Right;
                DrawTextTop(canvas, ((int)Math.Round(mm)).ToString(CultureInfo.InvariantCulture), 0, 0, text);
                text.TextAlign = SKTextAlign.Left;
                canvas.Restore();
            }
        }

        // 선택 영역을 눈금자에 하이라이트
        var b = SelectionBounds();
        if (b is not null)
        {
            using var soft = new SKPaint { Color = AccentSoft.WithAlpha(191), Style = SKPaintStyle.Fill };
            var (x1, _) = MmToPx(b.Value.Left, 0); var (x2, _) = MmToPx(b.Value.Right, 0);
            var (_, y1) = MmToPx(0, b.Value.Top); var (_, y2) = MmToPx(0, b.Value.Bottom);
            canvas.DrawRect((float)Math.Max(R, x1), R - 4, (float)Math.Max(1, x2 - x1), 4, soft);
            canvas.DrawRect(R - 4, (float)Math.Max(R, y1), 4, (float)Math.Max(1, y2 - y1), soft);
        }
        canvas.DrawRect(0, 0, R, R, bg);
        canvas.DrawRect(.5f, .5f, R, R, line);
        canvas.Restore();
    }
}

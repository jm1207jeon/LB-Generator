// 스냅 계산 — editor.js _snapTargets/_snapMove/_snapValue.
// 다른 객체의 6개 모서리·중심 + 라벨 경계·중심선에 맞춘다. 단위는 mm, 허용 오차는 화면 px 를 배율로 나눈 값.
using LaPrint.Core.Model;

namespace LaPrint.App.Controls;

/// <summary>스냅 안내선 — Axis 'x' 면 세로선(x = V), 'y' 면 가로선.</summary>
public readonly record struct SnapGuide(char Axis, double V);

/// <summary>스냅 후보와 맞춤 계산.</summary>
internal static class Snapping
{
    private readonly record struct Target(double V, string Kind);

    /// <summary>후보선 — 라벨 경계·중심 + (제외한 것을 뺀) 보이는 객체의 좌/중/우, 상/중/하.</summary>
    private static (List<Target> Xs, List<Target> Ys) Targets(LabelSize label, IEnumerable<LabelObject> objects, HashSet<string> excludeIds)
    {
        var xs = new List<Target> { new(0, "label"), new(label.W, "label"), new(label.W / 2, "center") };
        var ys = new List<Target> { new(0, "label"), new(label.H, "label"), new(label.H / 2, "center") };
        foreach (var o in objects)
        {
            if (excludeIds.Contains(o.Id) || !o.Visible) continue;
            xs.Add(new(o.X, "obj")); xs.Add(new(o.X + o.W / 2, "obj")); xs.Add(new(o.X + o.W, "obj"));
            ys.Add(new(o.Y, "obj")); ys.Add(new(o.Y + o.H / 2, "obj")); ys.Add(new(o.Y + o.H, "obj"));
        }
        return (xs, ys);
    }

    /// <summary>이동 중 스냅: 바운딩박스(mm)의 좌/중/우, 상/중/하를 후보와 맞춘다. 반환은 보정량과 실제로 맞은 안내선.</summary>
    public static (double Dx, double Dy, List<SnapGuide> Guides) SnapMove(
        double bx, double by, double bw, double bh, double tolMm,
        LabelSize label, IEnumerable<LabelObject> objects, HashSet<string> excludeIds)
    {
        var (xs, ys) = Targets(label, objects, excludeIds);
        var guides = new List<SnapGuide>();
        double dx = 0, dy = 0, bestX = tolMm, bestY = tolMm;
        var edgesX = new[] { bx, bx + bw / 2, bx + bw };
        var edgesY = new[] { by, by + bh / 2, by + bh };
        foreach (var e in edgesX) foreach (var t in xs)
        {
            var d = t.V - e;
            if (Math.Abs(d) < bestX) { bestX = Math.Abs(d); dx = d; }
        }
        foreach (var e in edgesY) foreach (var t in ys)
        {
            var d = t.V - e;
            if (Math.Abs(d) < bestY) { bestY = Math.Abs(d); dy = d; }
        }
        // 실제로 맞은 선만 가이드로 표시
        if (dx != 0 || bestX < tolMm)
            foreach (var e in edgesX) foreach (var t in xs)
                if (Math.Abs(t.V - (e + dx)) < 0.001) guides.Add(new SnapGuide('x', t.V));
        if (dy != 0 || bestY < tolMm)
            foreach (var e in edgesY) foreach (var t in ys)
                if (Math.Abs(t.V - (e + dy)) < 0.001) guides.Add(new SnapGuide('y', t.V));
        return (dx, dy, guides);
    }

    /// <summary>값 하나(리사이즈 모서리)를 가장 가까운 후보에 맞춘다. 맞지 않으면 안내선 null.</summary>
    public static (double V, SnapGuide? Guide) SnapValue(
        double v, char axis, double tolMm,
        LabelSize label, IEnumerable<LabelObject> objects, HashSet<string> excludeIds)
    {
        var (xs, ys) = Targets(label, objects, excludeIds);
        var list = axis == 'x' ? xs : ys;
        double best = tolMm, outV = v;
        double? hit = null;
        foreach (var t in list)
        {
            var d = Math.Abs(t.V - v);
            if (d < best) { best = d; outV = t.V; hit = t.V; }
        }
        return (outV, hit is null ? null : new SnapGuide(axis, hit.Value));
    }
}

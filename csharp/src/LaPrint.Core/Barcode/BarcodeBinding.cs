// 바코드 데이터 바인딩 — field | expression | object(다른 객체 텍스트 링크, 순환 참조 차단).
using System.Globalization;
using LaPrint.Core.Data;
using LaPrint.Core.Model;

namespace LaPrint.Core.Barcode;

/// <summary>바인딩 해석에 필요한 문맥.</summary>
public sealed record BindingContext(Fields Fields, IReadOnlyList<LabelObject> Objects, Func<string, string> ResolveText);

/// <summary>바코드 객체의 데이터 문자열을 구하고 물리 조건을 검사한다.</summary>
public static class BarcodeBinding
{
    /// <summary>바코드 객체의 실제 데이터 문자열. source=object 는 링크 대상을 따라가되 순환은 빈 문자열로 끊는다.</summary>
    public static string ResolveData(BarcodeObject o, BindingContext ctx, HashSet<string>? seen = null)
    {
        var src = string.IsNullOrEmpty(o.Source) ? "field" : o.Source;
        if (src == "field")
        {
            var key = string.IsNullOrEmpty(o.Binding) ? "UDI_FULL" : o.Binding;
            return ctx.Fields is not null && ctx.Fields.TryGetValue(key, out var v) && v is not null ? v : "";
        }
        if (src == "expression")
        {
            var expr = o.Expression ?? "";
            return ctx.ResolveText is not null ? ctx.ResolveText(expr) : expr;
        }
        if (src == "object")
        {
            var guard = seen ?? new HashSet<string>();
            if (guard.Contains(o.Id)) return "";                       // 순환 참조 차단
            guard.Add(o.Id);
            LabelObject? target = null;
            foreach (var t in ctx.Objects ?? Array.Empty<LabelObject>())
                if (t.Id == o.LinkObjectId) { target = t; break; }
            if (target is null) return "";
            if (target is TextObject tx)
            {
                var text = tx.Text ?? "";
                return ctx.ResolveText is not null ? ctx.ResolveText(text) : text;
            }
            if (target is BarcodeObject bc) return ResolveData(bc, ctx, guard);
            return "";
        }
        return "";
    }

    /// <summary>링크할 수 있는 객체 목록 (자기 자신 제외).</summary>
    public static IReadOnlyList<(string Id, string Label)> LinkableObjects(IEnumerable<LabelObject> objs, string selfId)
    {
        var outList = new List<(string Id, string Label)>();
        foreach (var t in objs ?? Array.Empty<LabelObject>())
        {
            if (t.Id == selfId) continue;
            if (t is TextObject) outList.Add((t.Id, ObjectLabel(t)));
            else if (t is BarcodeObject bc && bc.Source != "object") outList.Add((t.Id, ObjectLabel(t)));
        }
        return outList;
    }

    /// <summary>객체를 목록에 표시할 짧은 이름.</summary>
    public static string ObjectLabel(LabelObject t)
    {
        if (!string.IsNullOrEmpty(t.Name)) return t.Name;
        if (t is TextObject tx)
        {
            var s = (tx.Text ?? "").Replace("\n", " ").Trim();
            return s.Length > 0 ? (s.Length > 24 ? s[..24] + "…" : s) : "(빈 텍스트)";
        }
        if (t is BarcodeObject bc) return Symbologies.ById(bc.Symbology).Name;
        return t.Type;
    }

    /// <summary>X-dim ≥ 0.254mm, GS1-128 높이 ≥ max(6.35, 0.15W), 정수 도트 검사.</summary>
    public static IReadOnlyList<Issue> CheckPhysical(BarcodeObject o, string data, double dpi)
    {
        var sym = Symbologies.ById(o.Symbology);
        var issues = new List<Issue>();
        if (string.IsNullOrWhiteSpace(data)) return issues;
        var probe = BarcodeEncoder.Encode(sym.Id, data, o.HumanReadable);
        if (probe.Modules is null) return issues;

        var modulesW = probe.ModulesW;
        var xDimMm = o.W / Math.Max(1, modulesW);
        if (sym.Is2D)
        {
            if (xDimMm < 0.25)
            {
                issues.Add(new Issue(xDimMm < 0.15 ? "error" : "warn", "BC_XDIM",
                    $"바코드 모듈 크기가 {F(xDimMm, 3)}mm로 작습니다. GS1 권장 최소 0.254mm — 영역을 키우거나 데이터를 줄이세요."));
            }
        }
        else
        {
            var minH = Math.Max(6.35, o.W * 0.15);
            if (o.H < minH)
            {
                issues.Add(new Issue("warn", "BC_HEIGHT",
                    $"GS1-128 높이가 {F(o.H, 1)}mm입니다. 권장 최소 {F(minH, 1)}mm (6.35mm 또는 폭의 15%)."));
            }
            if (xDimMm < 0.25)
                issues.Add(new Issue("warn", "BC_XDIM", $"막대 폭이 {F(xDimMm, 3)}mm로 좁습니다. 권장 최소 0.25mm."));
        }
        // 출력 해상도에서 모듈이 정수 픽셀인지
        if (dpi > 0)
        {
            var modulePx = xDimMm * (dpi / 25.4);
            if (modulePx < 2)
                issues.Add(new Issue("warn", "BC_DPI", $"{dpi.ToString(CultureInfo.InvariantCulture)}dpi에서 모듈이 {F(modulePx, 1)}px입니다. 판독 안정성을 위해 해상도를 높이세요."));
        }
        return issues;
    }

    // JS Number.toFixed 와 같은 자릿수 고정 (문화권 무관 '.')
    private static string F(double v, int digits)
        => v.ToString("F" + digits, CultureInfo.InvariantCulture);
}

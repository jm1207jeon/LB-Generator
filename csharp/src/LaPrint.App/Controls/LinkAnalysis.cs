// 데이터 링크 분석 — js/link.js 의 tokensOf/groups/directLinks/related/summary.
//
// 라벨 한 장에는 같은 값(LOT, REF, UDI …)이 여러 곳에 반복해서 찍힌다. 어떤 객체가 어떤 데이터에 묶여 있는지
// 눈으로 바로 확인할 수 있어야 "한 곳만 고치고 다른 곳을 빠뜨리는" 실수를 막을 수 있다.
using System.Text.RegularExpressions;
using LaPrint.Core.Data;
using LaPrint.Core.Model;
using SkiaSharp;

namespace LaPrint.App.Controls;

/// <summary>인스펙터용 링크 요약 한 줄 — 토큰 · 이름 · 색 · 쓰는 곳 수 · 같은 값을 쓰는 다른 객체 id.</summary>
public sealed record LinkInfo(string Token, string Label, string Color, int Count, IReadOnlyList<string> Others);

/// <summary>바코드 → 참조 객체 직접 링크.</summary>
public sealed record DirectLink(string From, string To);

/// <summary>객체가 의존하는 데이터 토큰과 그 관계를 계산한다 (link.js).</summary>
public static class LinkAnalysis
{
    private static readonly Regex PhRe = new(@"\{(@?)([A-Za-z0-9_가-힣]+)(?::[^}|]+)?(?:\|[^}]*)?\}", RegexOptions.Compiled);

    /// <summary>토큰 이름 → 안정적인 색 (같은 데이터는 언제나 같은 색).</summary>
    private static readonly string[] Colors =
    {
        "#E67E00", "#1A6FB5", "#2E9E5B", "#9C27B0", "#D9534F", "#0097A7",
        "#7B5E00", "#5D4037", "#3949AB", "#C2185B", "#558B2F", "#00695C",
    };

    /// <summary>객체가 의존하는 데이터 토큰 집합 ("LOT", "@AB" …, 대문자).</summary>
    public static HashSet<string> TokensOf(LabelObject? o)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (o is null) return set;
        void Scan(string? str)
        {
            foreach (Match m in PhRe.Matches(str ?? ""))
                set.Add((m.Groups[1].Value.Length > 0 ? "@" : "") + m.Groups[2].Value.ToUpperInvariant());
        }
        switch (o)
        {
            case TextObject t: Scan(t.Text); break;
            case ImageObject im:
                if (!string.IsNullOrEmpty(im.SourceField)) set.Add(im.SourceField.ToUpperInvariant());
                break;
            case BarcodeObject b:
            {
                var src = string.IsNullOrEmpty(b.Source) ? "field" : b.Source;
                if (src == "field") set.Add((string.IsNullOrEmpty(b.Binding) ? "UDI_FULL" : b.Binding).ToUpperInvariant());
                else if (src == "expression") Scan(b.Expression);
                // 'object' 는 토큰이 아니라 직접 링크로 다룬다
                break;
            }
        }
        return set;
    }

    /// <summary>토큰 → 그 토큰을 쓰는 (보이는) 객체 id 목록.</summary>
    public static Dictionary<string, List<string>> Groups(IEnumerable<LabelObject> objects)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var o in objects)
        {
            if (!o.Visible) continue;
            foreach (var t in TokensOf(o))
            {
                if (!map.TryGetValue(t, out var list)) map[t] = list = new List<string>();
                list.Add(o.Id);
            }
        }
        return map;
    }

    /// <summary>2개 이상에서 쓰이는 토큰만 (반복되는 값).</summary>
    public static Dictionary<string, List<string>> RepeatedGroups(IEnumerable<LabelObject> objects)
        => Groups(objects).Where(kv => kv.Value.Count > 1).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

    /// <summary>바코드 → 참조 객체 직접 링크 목록.</summary>
    public static List<DirectLink> DirectLinks(IReadOnlyList<LabelObject> objects)
    {
        var ids = new HashSet<string>(objects.Select(o => o.Id));
        var outList = new List<DirectLink>();
        foreach (var o in objects)
            if (o is BarcodeObject b && b.Source == "object" && !string.IsNullOrEmpty(b.LinkObjectId) && ids.Contains(b.LinkObjectId))
                outList.Add(new DirectLink(o.Id, b.LinkObjectId));
        return outList;
    }

    /// <summary>선택된 객체들과 데이터를 공유하는 객체 id 집합과 선택이 쓰는 토큰 집합.</summary>
    public static (HashSet<string> Ids, HashSet<string> Tokens) Related(IReadOnlyList<LabelObject> objects, IEnumerable<string> selectedIds)
    {
        var sel = new HashSet<string>(selectedIds);
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        foreach (var o in objects)
            if (sel.Contains(o.Id))
                tokens.UnionWith(TokensOf(o));

        var ids = new HashSet<string>();
        if (tokens.Count > 0)
        {
            foreach (var o in objects)
            {
                if (sel.Contains(o.Id) || !o.Visible) continue;
                if (TokensOf(o).Overlaps(tokens)) ids.Add(o.Id);
            }
        }
        // 직접 링크도 포함 (양방향)
        foreach (var l in DirectLinks(objects))
        {
            if (sel.Contains(l.From) && !sel.Contains(l.To)) ids.Add(l.To);
            if (sel.Contains(l.To) && !sel.Contains(l.From)) ids.Add(l.From);
        }
        return (ids, tokens);
    }

    /// <summary>토큰 이름 → 안정적인 색 (hex).</summary>
    public static string ColorOf(string token)
    {
        uint h = 0;
        foreach (var ch in token) h = unchecked(h * 31 + ch);
        return Colors[(int)(h % (uint)Colors.Length)];
    }

    /// <summary>ColorOf 의 SKColor 판.</summary>
    public static SKColor SkColorOf(string token)
        => SKColor.TryParse(ColorOf(token), out var c) ? c : new SKColor(0xE6, 0x7E, 0x00);

    /// <summary>사람이 읽을 토큰 이름 ("LOT {LOT}", "AB열 직접참조").</summary>
    public static string LabelOf(string token)
    {
        if (token.StartsWith('@')) return $"{token[1..]}열 직접참조";
        return FieldMap.Labels.TryGetValue(token, out var l) ? $"{l} {{{token}}}" : $"{{{token}}}";
    }

    /// <summary>이름표에 쓸 짧은 이름 ("LOT", "AB열").</summary>
    public static string ShortLabel(string token)
    {
        if (token.StartsWith('@')) return token[1..] + "열";
        return FieldMap.Labels.TryGetValue(token, out var l) ? l : token;
    }

    /// <summary>인스펙터용: 이 객체가 쓰는 데이터와, 같은 데이터를 쓰는 다른 객체 (쓰는 곳 많은 순).</summary>
    public static List<LinkInfo> Summary(IReadOnlyList<LabelObject> objects, LabelObject obj)
    {
        var outList = new List<LinkInfo>();
        foreach (var t in TokensOf(obj))
        {
            var users = objects.Where(o => o.Visible && TokensOf(o).Contains(t)).ToList();
            outList.Add(new LinkInfo(t, LabelOf(t), ColorOf(t), users.Count,
                users.Where(o => o.Id != obj.Id).Select(o => o.Id).ToList()));
        }
        return outList.OrderByDescending(i => i.Count).ToList();
    }
}

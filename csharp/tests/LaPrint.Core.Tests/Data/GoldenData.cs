// 골든 fields 케이스에서 라벨DB 행을 되살리는 도우미 — 실제 DB 없이 계산 필드를 대조하기 위해서다.
using System.Text.Json;
using LaPrint.Core.Data;

namespace LaPrint.Core.Tests.Data;

/// <summary>골든 JSON → DbRow / JobInputs 변환.</summary>
public static class GoldenData
{
    public static JsonElement Golden(string key) => Fixtures.Golden().RootElement.GetProperty(key);

    /// <summary>골든 'fields' 의 열 필드를 FieldMap.Cols 로 되돌려 행을 만든다 (열 값은 입력과 무관하게 같다).</summary>
    public static DbRow RowFromFields(JsonElement fields, FieldMap map)
    {
        var row = new DbRow();
        foreach (var p in fields.EnumerateObject())
        {
            if (!map.Cols.TryGetValue(p.Name, out var col) || col.Length == 0) continue;
            row[col] = p.Value.GetString() ?? "";
        }
        return row;
    }

    /// <summary>품목번호가 item 인 골든 fields 케이스로부터 행을 되살린다. 없으면 null.</summary>
    public static DbRow? RowOf(string item, FieldMap map)
    {
        foreach (var c in Golden("fields").EnumerateArray())
            if (c.GetProperty("item").GetString() == item)
                return RowFromFields(c.GetProperty("fields"), map);
        return null;
    }

    /// <summary>골든 inputs 객체 → JobInputs. 빠진 키는 JS 기본값(months 36, expAuto true).</summary>
    public static JobInputs InputsFrom(JsonElement inputs, string? item = null)
    {
        string S(string k) => inputs.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        var months = inputs.TryGetProperty("months", out var m) && m.ValueKind == JsonValueKind.Number ? m.GetInt32() : 36;
        var expAuto = !(inputs.TryGetProperty("expAuto", out var ea) && ea.ValueKind == JsonValueKind.False);
        return new JobInputs(item ?? S("item"), S("lot"), S("sn"), S("mfg"), months, expAuto, S("exp"));
    }

    /// <summary>계산된 필드 중 실행 시각에 따라 달라지는 것.</summary>
    public static readonly HashSet<string> Volatile = new() { "TODAY", "NOW", "DATE", "TIME" };
}

// 엑셀 열 문자 유틸 — 0→A, 25→Z, 26→AA (data.js colName / colIndex).
namespace LaPrint.Core.Data;

/// <summary>엑셀 열 문자와 0 기반 색인 사이의 변환.</summary>
public static class ColumnName
{
    /// <summary>0 → "A", 25 → "Z", 26 → "AA". 음수는 "".</summary>
    public static string FromIndex(int i)
    {
        if (i < 0) return "";
        var s = "";
        while (i >= 0)
        {
            s = (char)('A' + i % 26) + s;
            i = i / 26 - 1;
        }
        return s;
    }

    /// <summary>"A" → 0, "aa" → 26. 대소문자 무시. 열 문자가 아니면 -1.</summary>
    public static int ToIndex(string? name)
    {
        var s = (name ?? "").Trim().ToUpperInvariant();
        if (s.Length == 0 || s.Length > 7) return -1;
        long n = 0;
        foreach (var ch in s)
        {
            if (ch is < 'A' or > 'Z') return -1;
            n = n * 26 + (ch - 'A' + 1);
        }
        return n - 1 > int.MaxValue ? -1 : (int)(n - 1);
    }
}

// 출력 이력 — 한 줄 JSON(jsonl) append-only (store.js history). 품질기록 관점에서 누가·언제·무엇을 출력했는지 남긴다.
using System.Text;
using System.Text.Json;

namespace LaPrint.Core.Storage;

/// <summary>출력 이력 한 건 (store.js addHistory 레코드).</summary>
public sealed class HistoryEntry
{
    public DateTime At { get; set; } = DateTime.Now;
    public string Item { get; set; } = "";
    public string Ref { get; set; } = "";
    public string Lot { get; set; } = "";
    public string Sn { get; set; } = "";
    public string Mfg { get; set; } = "";
    public string Exp { get; set; } = "";
    public string Udi { get; set; } = "";
    public int Copies { get; set; } = 1;
    public string FileName { get; set; } = "";
    public bool Ok { get; set; } = true;
    public string? Error { get; set; }
    /// <summary>single | separate | merged | zebra.</summary>
    public string Mode { get; set; } = "single";
    /// <summary>출고 구분 — general | bsc.</summary>
    public string Profile { get; set; } = "general";
}

/// <summary>history.jsonl 추가·조회.</summary>
public sealed class HistoryStore
{
    private static readonly object Gate = new();

    /// <summary>한 줄 추가. 이력 실패가 출력을 막지 않도록 예외는 로그로만 남긴다.</summary>
    public void Append(HistoryEntry e)
    {
        ArgumentNullException.ThrowIfNull(e);
        try
        {
            var line = JsonSerializer.Serialize(e, StorageJson.Compact) + "\n";
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.Root);
                File.AppendAllText(AppPaths.HistoryFile, line, new UTF8Encoding(false));
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("출력 이력을 기록할 수 없습니다.", ex);
        }
    }

    /// <summary>최근 것부터 limit 건. 깨진 줄은 건너뛴다.</summary>
    public IReadOnlyList<HistoryEntry> List(int limit)
    {
        var path = AppPaths.HistoryFile;
        if (limit <= 0 || !File.Exists(path)) return Array.Empty<HistoryEntry>();
        string[] lines;
        try
        {
            lock (Gate) lines = File.ReadAllLines(path, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            AppLog.Warn("출력 이력을 읽을 수 없습니다: " + path, ex);
            return Array.Empty<HistoryEntry>();
        }
        var all = new List<HistoryEntry>(lines.Length);
        foreach (var l in lines)
        {
            if (string.IsNullOrWhiteSpace(l)) continue;
            try
            {
                var e = JsonSerializer.Deserialize<HistoryEntry>(l, StorageJson.Compact);
                if (e is not null) all.Add(e);
            }
            catch (JsonException) { /* 깨진 줄은 건너뛴다 */ }
        }
        // 파일은 시간순 append 이므로 뒤에서부터 읽되, 시각이 어긋난 줄은 시각 기준으로 맞춘다
        all.Reverse();
        return all.OrderByDescending(e => e.At).Take(limit).ToList();
    }

    /// <summary>이력 CSV 문자열 (store.js historyToCsv). 엑셀에서 한글이 깨지지 않도록 BOM 을 붙인다.</summary>
    public static string ToCsv(IEnumerable<HistoryEntry> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var head = new[] { "출력일시", "품목번호", "규격", "LOT", "SN", "제조일", "유효일", "매수", "파일명", "성공", "오류", "UDI" };
        static string Esc(string? v)
        {
            var s = v ?? "";
            return s.IndexOfAny(new[] { '"', ',', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }
        var lines = new List<string> { string.Join(",", head) };
        foreach (var r in rows)
        {
            lines.Add(string.Join(",", new[]
            {
                r.At.ToString("o"), r.Item, r.Ref, r.Lot, r.Sn, r.Mfg, r.Exp,
                r.Copies.ToString(System.Globalization.CultureInfo.InvariantCulture),
                r.FileName, r.Ok ? "true" : "false", r.Error, r.Udi,
            }.Select(Esc)));
        }
        return "﻿" + string.Join("\r\n", lines);
    }
}

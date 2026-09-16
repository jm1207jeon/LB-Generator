// 출력 이력 — 한 줄 JSON(jsonl) append-only (store.js history).
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
}

/// <summary>history.jsonl 추가·조회.</summary>
public sealed class HistoryStore
{
    public void Append(HistoryEntry e)
        => throw new NotImplementedException("HistoryStore.Append — 아직 구현되지 않았습니다");

    /// <summary>최근 것부터 limit 건.</summary>
    public IReadOnlyList<HistoryEntry> List(int limit)
        => throw new NotImplementedException("HistoryStore.List — 아직 구현되지 않았습니다");
}

// 연속 작업 큐의 한 행 — 입력값·매수·상태·검증 결과 (batch.js row).
using LaPrint.Core.Data;

namespace LaPrint.Core.Batch;

/// <summary>큐 한 행. Status 는 pending | running | done | error | skipped.</summary>
public sealed class QueueRow
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Item { get; set; } = "";
    public string Lot { get; set; } = "";
    public string Sn { get; set; } = "";
    /// <summary>제조일 (ISO).</summary>
    public string Mfg { get; set; } = "";
    /// <summary>유효일 (ISO). ExpAuto 면 계산값.</summary>
    public string Exp { get; set; } = "";
    public int Months { get; set; } = 36;
    public bool ExpAuto { get; set; } = true;
    public int Copies { get; set; } = 1;
    public string Status { get; set; } = "pending";
    public string FileName { get; set; } = "";
    public string Error { get; set; } = "";
    public List<Issue> Issues { get; set; } = new();
    public Fields? Fields { get; set; }
    public DbRow? Row { get; set; }
}

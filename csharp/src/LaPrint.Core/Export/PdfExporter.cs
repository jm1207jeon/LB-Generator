// PDF 출력 — 벡터 PDF(SKDocument), 1장 출력과 연속 출력. 연속 출력은 행마다 beforeJob 으로 데이터·이미지 슬롯을 바꾼다.
using LaPrint.Core.Data;
using LaPrint.Core.Model;
using LaPrint.Core.Render;

namespace LaPrint.Core.Export;

/// <summary>연속 출력 작업 1건.</summary>
public sealed record ExportJob(string Id, int Copies, Fields Fields, DbRow? Row, IReadOnlyList<LabelObject>? Objects);

/// <summary>출력 옵션 — 설정(output + layout)과 같은 구조.</summary>
public sealed class ExportOptions
{
    /// <summary>separate(파일마다 1장) | merged(한 PDF 에 여러 쪽).</summary>
    public string Mode { get; set; } = "separate";
    public double Dpi { get; set; } = 300;
    public bool IncludeBg { get; set; } = true;
    public string Pattern { get; set; } = "{ITEM}_{LOT}_{DATE}";
    /// <summary>increment | overwrite | ask.</summary>
    public string Conflict { get; set; } = "increment";
    public string OutDir { get; set; } = "";
    public LayoutOptions Layout { get; set; } = new();
}

/// <summary>출력 1건의 결과.</summary>
public sealed record ExportResult(bool Ok, string? FileName, long Bytes, int Pages, string? Error);

/// <summary>연속 출력 전체 결과 (exporter.js exportBatch 반환값).</summary>
public sealed record BatchResult(bool Ok, int Done, int Failed, bool Cancelled, string? FileName, int Pages,
                                 IReadOnlyList<(string JobId, ExportResult Result)> Results,
                                 string? Error = null, bool OnPaper = false, int PerPage = 0);

/// <summary>PDF 출력기. 페이지 = 라벨 mm × (72/25.4) pt, LabelRenderer.Render(scale = 72/25.4).</summary>
public sealed class PdfExporter
{
    public Task<ExportResult> ExportOne(LabelTemplate t, RenderContext ctx, ExportOptions o, CancellationToken ct)
        => throw new NotImplementedException("PdfExporter.ExportOne — 아직 구현되지 않았습니다");

    /// <summary>연속 출력. beforeJob 은 매 행마다 await 되며 치환기와 이미지 슬롯을 그 행 데이터로 바꿔야 한다.</summary>
    public Task<BatchResult> ExportBatch(LabelTemplate t, IReadOnlyList<ExportJob> jobs, ExportOptions o,
        Func<ExportJob, Task> beforeJob,
        IProgress<(int Index, int Total, ExportJob Job, ExportResult? Res)>? progress, Func<bool>? isPaused, CancellationToken ct)
        => throw new NotImplementedException("PdfExporter.ExportBatch — 아직 구현되지 않았습니다");
}

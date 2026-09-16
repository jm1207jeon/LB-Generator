// PDF 출력 — 벡터 PDF(SKDocument), 1장 출력과 연속 출력. 연속 출력은 행마다 beforeJob 으로 데이터·이미지 슬롯을 바꾼다.
//
// 페이지 = 라벨 mm × (72/25.4) pt. LabelRenderer.Render 를 pt/mm 배율로 그대로 부르므로 화면·PDF·ZPL 의 레이아웃이 같다.
// 텍스트는 글꼴이 내장된 벡터, 이미지는 원본 해상도 그대로 삽입된다 (exporter.js 의 래스터 경로를 벡터로 대체).
using System.Text.RegularExpressions;
using LaPrint.Core.Data;
using LaPrint.Core.Model;
using LaPrint.Core.Render;
using LaPrint.Core.Typography;
using SkiaSharp;

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

/// <summary>연속 출력 전체 결과 (exporter.js exportBatch 반환값). Files 는 실제로 쓴 파일의 전체 경로.</summary>
public sealed record BatchResult(bool Ok, int Done, int Failed, bool Cancelled, string? FileName, int Pages,
                                 IReadOnlyList<(string JobId, ExportResult Result)> Results,
                                 string? Error = null, bool OnPaper = false, int PerPage = 0,
                                 IReadOnlyList<string>? Files = null);

/// <summary>PDF 출력기. 페이지 = 라벨 mm × (72/25.4) pt, LabelRenderer.Render(scale = 72/25.4).</summary>
public sealed class PdfExporter
{
    /// <summary>PDF 포인트 / mm.</summary>
    public const double PtPerMm = 72.0 / 25.4;

    // SkPDF 는 페이지 크기를 래스터 격자(RasterDpi)에 맞춰 반올림한다. 0.01mm 격자(2540dpi = 100px/mm)로
    // 잡아야 A4 595.2756pt, 60mm 170.0787pt 처럼 mm 치수가 그대로 MediaBox 에 들어간다.
    // (출력 해상도 opt.Dpi 를 쓰면 300dpi 에서 0.12pt 까지 어긋난다.)
    private const float PageRasterDpi = 2540f;

    private const string NoOutDir = "저장 폴더가 지정되지 않았습니다.";
    private static readonly Regex CopyTokenRe = new(@"\{COPY\}", RegexOptions.Compiled);
    private static readonly Regex NameSplitRe = new(@"^(.*?)(\.[^.]*)?$", RegexOptions.Compiled | RegexOptions.Singleline);

    /* ================= 단일 출력 ================= */

    /// <summary>라벨 1장을 PDF 로. Layout.Paper 가 label 이 아니면 용지에 앉힌다 (exporter.js exportOne).</summary>
    public async Task<ExportResult> ExportOne(LabelTemplate t, RenderContext ctx, ExportOptions o, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(t);
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(o);
        RequireOutDir(o);
        var layout = o.Layout ?? new LayoutOptions();
        if (!string.IsNullOrEmpty(layout.Paper) && layout.Paper != "label")
            return await ExportOneOnPaper(t, ctx, o, layout, ct).ConfigureAwait(false);

        ct.ThrowIfCancellationRequested();
        var label = t.Label ?? new LabelSize();
        var bytes = await Task.Run(() =>
        {
            using var ms = new MemoryStream();
            using (var doc = CreateDoc(ms, o, label))
            {
                DrawLabelPage(doc, t, ctx, o);
                doc.Close();
            }
            return ms.ToArray();
        }, ct).ConfigureAwait(false);

        var fileName = FileNaming.Build(o.Pattern, ctx.Fields, label, ctx.Row);
        var written = WriteOutput(o.OutDir, fileName, o.Conflict, bytes);
        return new ExportResult(true, written.Name, bytes.LongLength, 1, null);
    }

    /// <summary>용지 모드 단일 출력 — 한 페이지를 같은 라벨로 채우거나(fill) 1장만 앉힌다 (exporter.js exportOneOnPaper).</summary>
    public async Task<ExportResult> ExportOneOnPaper(LabelTemplate t, RenderContext ctx, ExportOptions o, LayoutOptions layout, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(t);
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(o);
        ArgumentNullException.ThrowIfNull(layout);
        RequireOutDir(o);
        var label = t.Label ?? new LabelSize();
        var plan = Paper.Plan(label, layout);
        if (!plan.Fits) return new ExportResult(false, null, 0, 0, plan.Reason);

        ct.ThrowIfCancellationRequested();
        var count = layout.Repeat == "one" ? 1 : plan.PerPage;
        var bytes = await Task.Run(() =>
        {
            using var ms = new MemoryStream();
            using (var doc = CreateDoc(ms, o, label))
            {
                using var tile = RecordTile(t, ctx, o);
                var slots = new List<SKPicture>(count);
                for (var i = 0; i < count; i++) slots.Add(tile);
                ComposeSheet(doc, slots, plan, label, layout);
                doc.Close();
            }
            return ms.ToArray();
        }, ct).ConfigureAwait(false);

        var fileName = FileNaming.Build(o.Pattern, ctx.Fields, label, ctx.Row);
        var written = WriteOutput(o.OutDir, fileName, o.Conflict, bytes);
        return new ExportResult(true, written.Name, bytes.LongLength, 1, null);
    }

    /* ================= 연속 출력 ================= */

    /// <summary>연속 출력. beforeJob 은 매 행마다 await 되며 치환기와 ★이미지 슬롯을 그 행 데이터로 바꿔야 한다 (exporter.js exportBatch).</summary>
    public async Task<BatchResult> ExportBatch(LabelTemplate t, IReadOnlyList<ExportJob> jobs, ExportOptions o,
        Func<ExportJob, Task> beforeJob,
        IProgress<(int Index, int Total, ExportJob Job, ExportResult? Res)>? progress, Func<bool>? isPaused, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(t);
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(o);
        RequireOutDir(o);
        var layout = o.Layout ?? new LayoutOptions();
        if (!string.IsNullOrEmpty(layout.Paper) && layout.Paper != "label")
            return await ExportBatchOnPaper(t, jobs, o, layout, beforeJob, progress, isPaused, ct).ConfigureAwait(false);

        var mode = string.IsNullOrEmpty(o.Mode) ? "separate" : o.Mode;
        var pattern = o.Pattern ?? "";
        var label = t.Label ?? new LabelSize();

        var results = new List<(string, ExportResult)>();
        var files = new List<string>();
        int done = 0, failed = 0;
        var cancelled = false;
        MemoryStream? mergedStream = null;
        SKDocument? mergedDoc = null;
        var mergedPages = 0;

        var total = jobs.Sum(j => Math.Max(1, j.Copies));
        var index = 0;

        try
        {
            foreach (var job in jobs)
            {
                if (ct.IsCancellationRequested) { cancelled = true; break; }
                if (await WaitWhilePaused(isPaused, ct).ConfigureAwait(false)) { cancelled = true; break; }

                var copies = Math.Max(1, job.Copies);
                ExportResult res;
                try
                {
                    // 이 건의 데이터로 치환기·이미지 슬롯을 바꿔 끼운다 (행마다 LOT/SN/날짜·그림이 다르다)
                    if (beforeJob is not null) await beforeJob(job).ConfigureAwait(false);
                    TextLayoutEngine.Invalidate();
                    var ctx = ContextOf(t, job, o);

                    if (mode == "merged")
                    {
                        if (mergedDoc is null)
                        {
                            mergedStream = new MemoryStream();
                            mergedDoc = CreateDoc(mergedStream, o, label);
                        }
                        for (var c = 0; c < copies; c++)
                        {
                            DrawLabelPage(mergedDoc, t, ctx, o);
                            mergedPages++;
                            index++;
                            progress?.Report((index, total, job, null));
                        }
                        res = new ExportResult(true, null, 0, copies, null);
                    }
                    else
                    {
                        byte[] bytes;
                        using (var ms = new MemoryStream())
                        {
                            using (var doc = CreateDoc(ms, o, label))
                            {
                                DrawLabelPage(doc, t, ctx, o);
                                doc.Close();
                            }
                            bytes = ms.ToArray();
                        }
                        // 같은 라벨을 여러 장 낼 때 파일명이 겹치지 않도록 한다. 규칙에 {COPY}가 없으면 뒤에 붙여 준다.
                        var multiPattern = copies > 1 && !CopyTokenRe.IsMatch(pattern) ? pattern + "_{COPY}" : pattern;
                        res = new ExportResult(true, null, bytes.LongLength, 1, null);
                        for (var c = 0; c < copies; c++)
                        {
                            var f = CloneFields(job.Fields);
                            if (copies > 1) f["COPY"] = (c + 1).ToString();
                            var fileName = FileNaming.Build(copies > 1 ? multiPattern : pattern, f, label, job.Row);
                            var written = WriteOutput(o.OutDir, fileName, o.Conflict, bytes);
                            files.Add(written.Path);
                            index++;
                            res = new ExportResult(true, written.Name, bytes.LongLength, 1, null);
                            progress?.Report((index, total, job, res));
                            if (c < copies - 1) await Task.Yield();
                        }
                    }
                    done += copies;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    cancelled = true;
                    break;
                }
                catch (Exception e)
                {
                    failed++;
                    res = new ExportResult(false, null, 0, 0, string.IsNullOrEmpty(e.Message) ? e.ToString() : e.Message);
                    index += copies;
                    progress?.Report((index, total, job, res));
                }

                results.Add((job.Id, res));
                await Task.Yield();          // UI 양보
            }

            string? mergedName = null;
            if (mergedDoc is not null && mergedPages > 0 && !cancelled)
            {
                var first = jobs[0];
                mergedName = FileNaming.Build(
                    pattern + (pattern.Contains('{') ? $"_{mergedPages}매" : $"배치_{mergedPages}매"),
                    first.Fields, label, first.Row);
                mergedDoc.Close();
                var written = WriteOutput(o.OutDir, mergedName, o.Conflict, mergedStream!.ToArray());
                mergedName = written.Name;
                files.Add(written.Path);
            }
            else if (mergedDoc is not null)
            {
                mergedDoc.Abort();
            }

            return new BatchResult(true, done, failed, cancelled, mergedName, mergedPages, results, null, false, 0, files);
        }
        finally
        {
            mergedDoc?.Dispose();
            mergedStream?.Dispose();
        }
    }

    /// <summary>
    /// 용지 모드 연속 출력 — 큐의 여러 건을 한 용지에 차례로 앉히고, 칸이 차면 다음 페이지로 넘긴다. 결과는 언제나 한 PDF (exporter.js exportBatchOnPaper).
    /// </summary>
    public async Task<BatchResult> ExportBatchOnPaper(LabelTemplate t, IReadOnlyList<ExportJob> jobs, ExportOptions o, LayoutOptions layout,
        Func<ExportJob, Task> beforeJob,
        IProgress<(int Index, int Total, ExportJob Job, ExportResult? Res)>? progress, Func<bool>? isPaused, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(t);
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(o);
        ArgumentNullException.ThrowIfNull(layout);
        RequireOutDir(o);
        var label = t.Label ?? new LabelSize();
        var plan = Paper.Plan(label, layout);
        if (!plan.Fits)
            return new BatchResult(false, 0, 0, false, null, 0, Array.Empty<(string, ExportResult)>(), plan.Reason, true, plan.PerPage, Array.Empty<string>());

        var expanded = new List<ExportJob>();
        foreach (var j in jobs)
        {
            var c = Math.Max(1, j.Copies);
            for (var i = 0; i < c; i++) expanded.Add(j);
        }
        var total = expanded.Count;
        var results = new List<(string, ExportResult)>();
        var files = new List<string>();
        int pages = 0, done = 0, failed = 0;
        var cancelled = false;
        var slots = new List<SKPicture>();
        MemoryStream? stream = null;
        SKDocument? doc = null;

        void Flush()
        {
            if (slots.Count == 0) return;
            if (doc is null)
            {
                stream = new MemoryStream();
                doc = CreateDoc(stream, o, label);
            }
            ComposeSheet(doc, slots, plan, label, layout);
            foreach (var s in slots) s.Dispose();
            slots.Clear();
            pages++;
        }

        try
        {
            for (var i = 0; i < expanded.Count; i++)
            {
                if (ct.IsCancellationRequested) { cancelled = true; break; }
                if (await WaitWhilePaused(isPaused, ct).ConfigureAwait(false)) { cancelled = true; break; }
                var job = expanded[i];
                ExportResult res;
                try
                {
                    if (beforeJob is not null) await beforeJob(job).ConfigureAwait(false);
                    TextLayoutEngine.Invalidate();
                    var ctx = ContextOf(t, job, o);
                    slots.Add(RecordTile(t, ctx, o));
                    done++;
                    if (slots.Count >= plan.PerPage) Flush();
                    res = new ExportResult(true, null, 0, 0, null);
                    progress?.Report((i + 1, total, job, res));
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    cancelled = true;
                    break;
                }
                catch (Exception e)
                {
                    failed++;
                    res = new ExportResult(false, null, 0, 0, string.IsNullOrEmpty(e.Message) ? e.ToString() : e.Message);
                    progress?.Report((i + 1, total, job, res));
                }
                results.Add((job.Id, res));
                await Task.Yield();
            }
            if (!cancelled) Flush();
            foreach (var s in slots) s.Dispose();
            slots.Clear();

            string? fileName = null;
            if (doc is not null && pages > 0)
            {
                var first = jobs[0];
                var pat = (o.Pattern ?? "") + $"_{pages}쪽";
                fileName = FileNaming.Build(pat, first.Fields, label, first.Row);
                doc.Close();
                var written = WriteOutput(o.OutDir, fileName, o.Conflict, stream!.ToArray());
                fileName = written.Name;
                files.Add(written.Path);
            }
            else
            {
                doc?.Abort();
            }
            return new BatchResult(true, done, failed, cancelled, fileName, pages, results, null, true, plan.PerPage, files);
        }
        finally
        {
            foreach (var s in slots) s.Dispose();
            doc?.Dispose();
            stream?.Dispose();
        }
    }

    /* ================= 렌더 ================= */

    /// <summary>라벨 한 장을 PDF 한 쪽으로 (페이지 = 라벨 mm × pt/mm).</summary>
    public static void DrawLabelPage(SKDocument doc, LabelTemplate t, RenderContext ctx, ExportOptions o)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(t);
        var label = t.Label ?? new LabelSize();
        var w = (float)(label.W * PtPerMm);
        var h = (float)(label.H * PtPerMm);
        var canvas = doc.BeginPage(w, h);
        try
        {
            RenderForExport(canvas, t, ctx, o);
        }
        finally
        {
            doc.EndPage();
        }
    }

    /// <summary>라벨 한 장을 벡터 그림(SKPicture)으로 기록 — 용지 칸에 합성할 때 쓴다 (exporter.js renderLabelTile).</summary>
    public static SKPicture RecordTile(LabelTemplate t, RenderContext ctx, ExportOptions o)
    {
        ArgumentNullException.ThrowIfNull(t);
        var label = t.Label ?? new LabelSize();
        var w = (float)(label.W * PtPerMm);
        var h = (float)(label.H * PtPerMm);
        using var rec = new SKPictureRecorder();
        var canvas = rec.BeginRecording(SKRect.Create(0, 0, w, h));
        RenderForExport(canvas, t, ctx, o);
        return rec.EndRecording();
    }

    /// <summary>용지 한 쪽을 만든다 — 각 칸에 라벨 그림을 앉히고 테두리선·재단선을 그린다 (exporter.js composeSheet).</summary>
    public static void ComposeSheet(SKDocument doc, IReadOnlyList<SKPicture> slots, ImpositionPlan plan, LabelSize label, LayoutOptions layout)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(label);
        layout ??= new LayoutOptions();
        var pw = (float)(plan.PaperW * PtPerMm);
        var ph = (float)(plan.PaperH * PtPerMm);
        var lw = (float)(label.W * PtPerMm);
        var lh = (float)(label.H * PtPerMm);
        var canvas = doc.BeginPage(pw, ph);
        try
        {
            using var white = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill };
            canvas.DrawRect(SKRect.Create(0, 0, pw, ph), white);
            for (var i = 0; i < slots.Count && i < plan.PerPage; i++)
            {
                var (sx, sy) = Paper.SlotAt(plan, i);
                var x = (float)(sx * PtPerMm);
                var y = (float)(sy * PtPerMm);
                if (slots[i] is not null)
                {
                    canvas.Save();
                    canvas.Translate(x, y);
                    canvas.ClipRect(SKRect.Create(0, 0, lw, lh));
                    canvas.DrawPicture(slots[i]);
                    canvas.Restore();
                }
                if (layout.Outline)
                {
                    using var outline = new SKPaint
                    {
                        Color = new SKColor(0x99, 0x99, 0x99),
                        Style = SKPaintStyle.Stroke,
                        StrokeWidth = (float)(0.2 * PtPerMm),
                        IsAntialias = true,
                    };
                    canvas.DrawRect(SKRect.Create(x, y, lw, lh), outline);
                }
                if (layout.CropMarks) Paper.DrawCropMarks(canvas, plan, i, label, PtPerMm);
            }
        }
        finally
        {
            doc.EndPage();
        }
    }

    /* ================= 파일 쓰기 ================= */

    /// <summary>같은 이름이 있으면 " (2)", " (3)" … 을 붙인다 (settings.js uniqueName).</summary>
    public static string UniqueName(string dir, string fileName)
    {
        ArgumentNullException.ThrowIfNull(dir);
        ArgumentNullException.ThrowIfNull(fileName);
        var m = NameSplitRe.Match(fileName);
        var baseName = m.Groups[1].Value;
        var ext = m.Groups[2].Success ? m.Groups[2].Value : "";
        var name = fileName;
        var n = 1;
        for (var i = 0; i < 500; i++)
        {
            if (!File.Exists(Path.Combine(dir, name))) return name;
            n++;
            name = $"{baseName} ({n}){ext}";
        }
        return $"{baseName} ({DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}){ext}";
    }

    /// <summary>저장 폴더에 쓴다. increment(및 ask) 는 겹치지 않는 이름, overwrite 는 덮어쓴다 (settings.js writeOutput).</summary>
    public static (string Name, string Path) WriteOutput(string outDir, string fileName, string? conflict, byte[] bytes)
    {
        if (string.IsNullOrWhiteSpace(outDir)) throw new ArgumentException(NoOutDir, nameof(outDir));
        ArgumentNullException.ThrowIfNull(bytes);
        Directory.CreateDirectory(outDir);
        var name = conflict == "overwrite" ? fileName : UniqueName(outDir, fileName);
        var path = Path.Combine(outDir, name);
        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            fs.Write(bytes, 0, bytes.Length);
        }
        return (name, path);
    }

    /* ================= 내부 ================= */

    private static void RequireOutDir(ExportOptions o)
    {
        if (string.IsNullOrWhiteSpace(o.OutDir)) throw new ArgumentException(NoOutDir, nameof(o));
    }

    private static SKDocument CreateDoc(Stream stream, ExportOptions o, LabelSize label)
    {
        var meta = new SKDocumentPdfMetadata
        {
            Title = $"LaPrint {Fmt(label.W)}×{Fmt(label.H)}mm",
            Creator = "LaPrint",
            Producer = "LaPrint (SkiaSharp)",
            RasterDpi = PageRasterDpi,          // 페이지 크기를 mm 그대로 유지하기 위한 격자 (그림 해상도는 ctx.Dpi)
            Creation = DateTime.Now,
            Modified = DateTime.Now,
        };
        return SKDocument.CreatePdf(stream, meta)
               ?? throw new InvalidOperationException("PDF 문서를 만들 수 없습니다.");
    }

    private static string Fmt(double v) => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

    // 출력용 문맥으로 그린다 (ForExport · IncludeBg · Dpi 는 호출 뒤 되돌린다)
    private static void RenderForExport(SKCanvas canvas, LabelTemplate t, RenderContext ctx, ExportOptions o)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        var prevExport = ctx.ForExport;
        var prevBg = ctx.IncludeBg;
        var prevDpi = ctx.Dpi;
        try
        {
            ctx.ForExport = true;
            ctx.IncludeBg = o.IncludeBg && prevBg;
            ctx.Dpi = o.Dpi > 0 ? o.Dpi : prevDpi;
            LabelRenderer.Render(canvas, t, ctx, PtPerMm, 0, 0);
        }
        finally
        {
            ctx.ForExport = prevExport;
            ctx.IncludeBg = prevBg;
            ctx.Dpi = prevDpi;
        }
    }

    // 작업 1건의 렌더 문맥 — 치환기는 이 건의 필드·행으로
    private static RenderContext ContextOf(LabelTemplate t, ExportJob job, ExportOptions o)
    {
        var f = job.Fields ?? new Fields();
        var row = job.Row;
        return new RenderContext
        {
            Fields = f,
            Row = row,
            ResolveText = s => Placeholders.Resolve(s, f, row),
            Objects = job.Objects ?? t.Objects,
            ForExport = true,
            IncludeBg = o.IncludeBg,
            Dpi = o.Dpi,
        };
    }

    private static Fields CloneFields(Fields? src)
    {
        var f = new Fields();
        if (src is null) return f;
        foreach (var kv in src) f[kv.Key] = kv.Value;
        f.MfgDate = src.MfgDate;
        f.ExpDate = src.ExpDate;
        return f;
    }

    // 일시정지 동안 120ms 마다 살핀다. 그 사이 중지되면 true.
    private static async Task<bool> WaitWhilePaused(Func<bool>? isPaused, CancellationToken ct)
    {
        while (isPaused is not null && isPaused())
        {
            await Task.Delay(120, CancellationToken.None).ConfigureAwait(false);
            if (ct.IsCancellationRequested) return true;
        }
        return false;
    }
}

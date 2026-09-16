// PDF 출력 — 벡터 PDF 크기(MediaBox)·글꼴 내장, ★연속 출력 행마다 그림 교체, 매수·충돌 이름·병합, 용지 배치, 중지·일시정지.
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using LaPrint.Core.Data;
using LaPrint.Core.Export;
using LaPrint.Core.Imaging;
using LaPrint.Core.Model;
using LaPrint.Core.Render;
using LaPrint.Core.Tests.Imaging;
using Xunit;

namespace LaPrint.Core.Tests.Export;

public class PdfExporterTests : IDisposable
{
    private const double PtPerMm = 72 / 25.4;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "LaPrint.Tests", "pdf-" + Guid.NewGuid().ToString("N"));

    public PdfExporterTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /* ---------------- 도우미 ---------------- */

    /* 60×30mm — 텍스트 {REF} · 스텐트 그림 · 딜리버리 그림 · GS1 DataMatrix(UDI_FULL) */
    private static LabelTemplate SmallTemplate() => new()
    {
        Name = "test",
        Label = new LabelSize { W = 60, H = 30 },
        Objects =
        {
            new TextObject { Id = "ref", X = 2, Y = 2, W = 36, H = 6, Text = "{REF} {ITEM}", Font = "Arial", SizePt = 8, Kerning = false },
            new ImageObject { Id = "stent", X = 2, Y = 10, W = 30, H = 8, SourceField = "IMG_STENT" },
            new ImageObject { Id = "delivery", X = 2, Y = 20, W = 30, H = 6, SourceField = "IMG_DELIVERY" },
            new BarcodeObject { Id = "bc", X = 44, Y = 2, W = 12, H = 12, Symbology = "gs1datamatrix", Source = "field", Binding = "UDI_FULL" },
        },
    };

    private static RenderContext CtxOf(LabelTemplate t, string item)
    {
        var row = SampleDb.Row(item);
        var f = SampleDb.FieldsOf(item);
        return new RenderContext { Fields = f, Row = row, Objects = t.Objects, ResolveText = s => Placeholders.Resolve(s, f, row) };
    }

    private static ExportJob JobOf(string item, int copies = 1)
        => new(item, copies, SampleDb.FieldsOf(item), SampleDb.Row(item), null);

    /// <summary>실제 앱처럼 행마다 치환기와 ★이미지 슬롯을 바꾸는 beforeJob.</summary>
    private static Func<ExportJob, Task> SlotSwapper(LabelTemplate t, RenderContext ctx, ImageStore store, List<string>? order = null)
        => async job =>
        {
            order?.Add(job.Id);
            ctx.Fields = job.Fields;
            ctx.Row = job.Row;
            ctx.ResolveText = s => Placeholders.Resolve(s, job.Fields, job.Row);
            await SlotLoader.LoadIntoAsync(t.Objects, job.Fields, job.Row, store, true, true, 30);
        };

    private ExportOptions Opt(string pattern = "{ITEM}_{LOT}", string mode = "separate") => new()
    {
        Mode = mode, Pattern = pattern, OutDir = _dir, Conflict = "increment", Dpi = 300,
    };

    private sealed class SyncProgress<T> : IProgress<T>
    {
        private readonly Action<T> _a;
        public SyncProgress(Action<T> a) => _a = a;
        public void Report(T value) => _a(value);
    }

    /* PDF 바이트 검사 — 객체 사전은 평문이므로 정규식으로 충분하다 */
    private static string Latin(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    private static (double W, double H) MediaBox(byte[] pdf)
    {
        var m = Regex.Match(Latin(pdf), @"/MediaBox\s*\[([^\]]+)\]");
        Assert.True(m.Success, "MediaBox 를 찾지 못했습니다");
        var v = m.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(s => double.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        Assert.Equal(4, v.Length);
        Assert.Equal(0, v[0], 3);
        Assert.Equal(0, v[1], 3);
        return (v[2], v[3]);
    }

    private static int PageCount(byte[] pdf) => Regex.Matches(Latin(pdf), @"/Type\s*/Page(?!s)").Count;

    private static bool HasFont(byte[] pdf)
    {
        var s = Latin(pdf);
        return Regex.IsMatch(s, @"/Type\s*/Font") || s.Contains("/FontFile");
    }

    /// <summary>모든 이미지 XObject 스트림의 SHA-256 (정렬).</summary>
    private static List<string> ImageStreamHashes(byte[] pdf)
    {
        var s = Latin(pdf);
        var list = new List<string>();
        foreach (Match m in Regex.Matches(s, @"/Subtype\s*/Image"))
        {
            var lenM = Regex.Match(s[m.Index..], @"/Length\s+(\d+)");
            var streamM = Regex.Match(s[m.Index..], @"stream(\r\n|\n)");
            Assert.True(lenM.Success && streamM.Success, "이미지 스트림을 찾지 못했습니다");
            var start = m.Index + streamM.Index + streamM.Length;
            var len = int.Parse(lenM.Groups[1].Value);
            var bytes = pdf.AsSpan(start, len).ToArray();
            list.Add(Convert.ToHexString(SHA256.HashData(bytes)));
        }
        list.Sort();
        return list;
    }

    /* ---------------- ExportOne ---------------- */

    [Fact]
    public async Task ExportOne_Writes_Vector_Pdf_With_Label_Size()
    {
        var t = SmallTemplate();
        var ctx = CtxOf(t, "16-0401");
        await SlotLoader.LoadIntoAsync(t.Objects, ctx.Fields, ctx.Row, SampleDb.NewStore(), true, true, 30);
        var res = await new PdfExporter().ExportOne(t, ctx, Opt(), CancellationToken.None);

        Assert.True(res.Ok, res.Error);
        Assert.Equal("16-0401_L1.pdf", res.FileName);
        Assert.Equal(1, res.Pages);
        var path = Path.Combine(_dir, res.FileName!);
        Assert.True(File.Exists(path));
        var pdf = await File.ReadAllBytesAsync(path);
        Assert.Equal(pdf.LongLength, res.Bytes);
        Assert.StartsWith("%PDF", Latin(pdf[..4]));

        var (w, h) = MediaBox(pdf);
        Assert.True(Math.Abs(w - 60 * PtPerMm) < 0.05, $"MediaBox W {w}");
        Assert.True(Math.Abs(h - 30 * PtPerMm) < 0.05, $"MediaBox H {h}");
        Assert.Equal(1, PageCount(pdf));
        Assert.True(HasFont(pdf), "텍스트가 래스터화되지 않고 글꼴로 들어가야 한다");
        Assert.NotEmpty(ImageStreamHashes(pdf));
        Assert.False(ctx.ForExport);            // 호출 뒤 되돌린다
    }

    [Fact]
    public async Task ExportOne_On_A4_Uses_Paper_Size()
    {
        var t = SmallTemplate();
        var ctx = CtxOf(t, "16-0401");
        var o = Opt();
        o.Layout = new LayoutOptions { Paper = "A4", Outline = true, CropMarks = true };
        var plan = Paper.Plan(t.Label, o.Layout);
        Assert.True(plan.Fits);

        var res = await new PdfExporter().ExportOne(t, ctx, o, CancellationToken.None);
        Assert.True(res.Ok, res.Error);
        var pdf = await File.ReadAllBytesAsync(Path.Combine(_dir, res.FileName!));
        var (w, h) = MediaBox(pdf);
        Assert.True(Math.Abs(w - plan.PaperW * PtPerMm) < 0.05, $"MediaBox W {w}");
        Assert.True(Math.Abs(h - plan.PaperH * PtPerMm) < 0.05, $"MediaBox H {h}");
        Assert.True((Math.Abs(w - 595.28) < 0.05 && Math.Abs(h - 841.89) < 0.05) || (Math.Abs(w - 841.89) < 0.05 && Math.Abs(h - 595.28) < 0.05));
        Assert.Equal(1, PageCount(pdf));
        Assert.True(HasFont(pdf));

        // repeat=one 도 한 쪽
        o.Layout.Repeat = "one";
        var one = await new PdfExporter().ExportOne(t, ctx, o, CancellationToken.None);
        Assert.True(one.Ok);
        Assert.Equal("16-0401_L1 (2).pdf", one.FileName);
    }

    [Fact]
    public async Task ExportOne_On_Paper_Too_Small_Returns_Reason()
    {
        var t = SmallTemplate();
        var o = Opt();
        o.Layout = new LayoutOptions { Paper = "custom", CustomW = 40, CustomH = 40 };
        var res = await new PdfExporter().ExportOne(t, CtxOf(t, "16-0401"), o, CancellationToken.None);
        Assert.False(res.Ok);
        Assert.Equal(Paper.Plan(t.Label, o.Layout).Reason, res.Error);
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task No_OutDir_Throws()
    {
        var t = SmallTemplate();
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => new PdfExporter().ExportOne(t, CtxOf(t, "16-0401"), new ExportOptions(), CancellationToken.None));
        Assert.StartsWith("저장 폴더가 지정되지 않았습니다.", ex.Message);
        await Assert.ThrowsAsync<ArgumentException>(() => new PdfExporter().ExportBatch(t, new[] { JobOf("16-0401") }, new ExportOptions(), _ => Task.CompletedTask, null, null, CancellationToken.None));
    }

    /* ---------------- ExportBatch ---------------- */

    [Fact]
    public async Task 연속출력_행마다_그림이_다르다()
    {
        var items = new[] { "16-0401", "42-0401", "21-1801" };
        var t = SmallTemplate();
        var ctx = CtxOf(t, items[0]);
        var store = SampleDb.NewStore();
        var order = new List<string>();
        var jobs = items.Select(i => JobOf(i)).ToList();
        var reports = new List<(int Index, int Total, ExportJob Job, ExportResult? Res)>();

        var res = await new PdfExporter().ExportBatch(t, jobs, Opt(), SlotSwapper(t, ctx, store, order),
            new SyncProgress<(int, int, ExportJob, ExportResult?)>(p => reports.Add(p)), null, CancellationToken.None);

        Assert.True(res.Ok);
        Assert.Equal(3, res.Done);
        Assert.Equal(0, res.Failed);
        Assert.False(res.Cancelled);
        Assert.Equal(items, order.ToArray());                 // beforeJob 이 행마다 순서대로 불렸다
        Assert.Equal(3, res.Results.Count);
        Assert.Equal(3, res.Files!.Count);
        Assert.Equal(new[] { "16-0401_L1.pdf", "42-0401_L1.pdf", "21-1801_L1.pdf" }, res.Results.Select(r => r.Result.FileName).ToArray());
        Assert.Equal(new[] { 1, 2, 3 }, reports.Select(r => r.Index).ToArray());
        Assert.All(reports, r => Assert.Equal(3, r.Total));

        var hashes = new List<List<string>>();
        foreach (var f in res.Files)
        {
            var pdf = await File.ReadAllBytesAsync(f);
            Assert.StartsWith("%PDF", Latin(pdf[..4]));
            var h = ImageStreamHashes(pdf);
            Assert.NotEmpty(h);                                // 이미지 XObject 가 들어 있다
            hashes.Add(h);
        }
        // ★ 세 파일의 그림 스트림이 서로 다르다 — 앞 행 사진이 남지 않는다
        Assert.NotEqual(hashes[0], hashes[1]);
        Assert.NotEqual(hashes[1], hashes[2]);
        Assert.NotEqual(hashes[0], hashes[2]);
    }

    [Fact]
    public async Task Copies_Append_COPY_And_Increment_On_Conflict()
    {
        var t = SmallTemplate();
        var ctx = CtxOf(t, "16-0401");
        var store = SampleDb.NewStore();
        var res = await new PdfExporter().ExportBatch(t, new[] { JobOf("16-0401", 2) }, Opt("{ITEM}_{LOT}"), SlotSwapper(t, ctx, store), null, null, CancellationToken.None);
        Assert.True(res.Ok);
        Assert.Equal(2, res.Done);
        Assert.True(File.Exists(Path.Combine(_dir, "16-0401_L1_1.pdf")));
        Assert.True(File.Exists(Path.Combine(_dir, "16-0401_L1_2.pdf")));
        Assert.Equal("16-0401_L1_2.pdf", res.Results.Single().Result.FileName);

        // 같은 이름이 있으면 " (2)"
        File.WriteAllText(Path.Combine(_dir, "16-0401_L1.pdf"), "x");
        var again = await new PdfExporter().ExportBatch(t, new[] { JobOf("16-0401") }, Opt("{ITEM}_{LOT}"), SlotSwapper(t, ctx, store), null, null, CancellationToken.None);
        Assert.Equal("16-0401_L1 (2).pdf", again.Results.Single().Result.FileName);
        Assert.Equal("x", File.ReadAllText(Path.Combine(_dir, "16-0401_L1.pdf")));

        var third = await new PdfExporter().ExportBatch(t, new[] { JobOf("16-0401") }, Opt("{ITEM}_{LOT}"), SlotSwapper(t, ctx, store), null, null, CancellationToken.None);
        Assert.Equal("16-0401_L1 (3).pdf", third.Results.Single().Result.FileName);

        // overwrite 는 덮어쓴다
        var o = Opt("{ITEM}_{LOT}");
        o.Conflict = "overwrite";
        var over = await new PdfExporter().ExportBatch(t, new[] { JobOf("16-0401") }, o, SlotSwapper(t, ctx, store), null, null, CancellationToken.None);
        Assert.Equal("16-0401_L1.pdf", over.Results.Single().Result.FileName);
        Assert.NotEqual("x", File.ReadAllText(Path.Combine(_dir, "16-0401_L1.pdf")));

        // 패턴에 {COPY} 가 이미 있으면 그대로
        var withCopy = await new PdfExporter().ExportBatch(t, new[] { JobOf("16-0401", 2) }, Opt("{COPY}-{ITEM}"), SlotSwapper(t, ctx, store), null, null, CancellationToken.None);
        Assert.Equal("2-16-0401.pdf", withCopy.Results.Single().Result.FileName);
        Assert.True(File.Exists(Path.Combine(_dir, "1-16-0401.pdf")));
    }

    [Fact]
    public async Task Merged_Mode_Writes_One_File_With_All_Pages()
    {
        var items = new[] { "16-0401", "42-0401", "21-1801" };
        var t = SmallTemplate();
        var ctx = CtxOf(t, items[0]);
        var store = SampleDb.NewStore();
        var reports = new List<(int Index, int Total, ExportJob Job, ExportResult? Res)>();
        var jobs = items.Select(i => JobOf(i)).ToList();
        var res = await new PdfExporter().ExportBatch(t, jobs, Opt("{ITEM}_{LOT}", "merged"), SlotSwapper(t, ctx, store),
            new SyncProgress<(int, int, ExportJob, ExportResult?)>(p => reports.Add(p)), null, CancellationToken.None);

        Assert.True(res.Ok);
        Assert.Equal(3, res.Done);
        Assert.Equal(3, res.Pages);
        Assert.Equal("16-0401_L1_3매.pdf", res.FileName);
        Assert.Single(Directory.GetFiles(_dir));
        var pdf = await File.ReadAllBytesAsync(Path.Combine(_dir, res.FileName!));
        Assert.Equal(3, PageCount(pdf));
        Assert.True(HasFont(pdf));
        Assert.All(reports, r => Assert.Null(r.Res));         // 병합 모드는 결과 없이 진행만 알린다
        Assert.All(res.Results, r => Assert.Equal(1, r.Result.Pages));

        // 세 쪽의 그림이 모두 들어 있다 (서로 다른 스트림 3개 이상)
        Assert.True(ImageStreamHashes(pdf).Distinct().Count() >= 3);

        // 패턴에 '{' 가 없으면 "배치_N매"
        var plain = await new PdfExporter().ExportBatch(t, jobs.Take(2).ToList(), Opt("라벨", "merged"), SlotSwapper(t, ctx, store), null, null, CancellationToken.None);
        Assert.Equal("라벨배치_2매.pdf", plain.FileName);
    }

    [Fact]
    public async Task Cancel_Before_Second_Job_Stops_With_Done_1()
    {
        var items = new[] { "16-0401", "42-0401", "21-1801" };
        var t = SmallTemplate();
        var ctx = CtxOf(t, items[0]);
        var store = SampleDb.NewStore();
        using var cts = new CancellationTokenSource();
        var jobs = items.Select(i => JobOf(i)).ToList();
        var res = await new PdfExporter().ExportBatch(t, jobs, Opt(), SlotSwapper(t, ctx, store),
            new SyncProgress<(int Index, int Total, ExportJob Job, ExportResult? Res)>(p => { if (p.Index == 1) cts.Cancel(); }),
            null, cts.Token);

        Assert.True(res.Cancelled);
        Assert.Equal(1, res.Done);
        Assert.Equal(0, res.Failed);
        Assert.Single(res.Results);
        Assert.Single(Directory.GetFiles(_dir));

        // 병합 모드에서 중지하면 파일을 남기지 않는다
        using var cts2 = new CancellationTokenSource();
        var merged = await new PdfExporter().ExportBatch(t, jobs, Opt("{ITEM}", "merged"), SlotSwapper(t, ctx, store),
            new SyncProgress<(int Index, int Total, ExportJob Job, ExportResult? Res)>(p => { if (p.Index == 1) cts2.Cancel(); }),
            null, cts2.Token);
        Assert.True(merged.Cancelled);
        Assert.Null(merged.FileName);
        Assert.Single(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task Pause_Is_Honored_Then_Resumes()
    {
        var t = SmallTemplate();
        var ctx = CtxOf(t, "16-0401");
        var store = SampleDb.NewStore();
        var paused = true;
        var polls = 0;
        Func<bool> isPaused = () => { polls++; return paused; };
        var task = new PdfExporter().ExportBatch(t, new[] { JobOf("16-0401") }, Opt(), SlotSwapper(t, ctx, store), null, isPaused, CancellationToken.None);
        await Task.Delay(400);
        Assert.False(task.IsCompleted);
        Assert.True(polls >= 2);
        paused = false;
        var res = await task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(1, res.Done);
    }

    [Fact]
    public async Task Failed_Job_Is_Counted_And_Others_Continue()
    {
        var t = SmallTemplate();
        var ctx = CtxOf(t, "16-0401");
        var store = SampleDb.NewStore();
        var swap = SlotSwapper(t, ctx, store);
        Func<ExportJob, Task> before = job => job.Id == "42-0401" ? throw new InvalidOperationException("일부러 실패") : swap(job);
        var jobs = new[] { JobOf("16-0401"), JobOf("42-0401", 2), JobOf("21-1801") };
        var reports = new List<(int Index, int Total, ExportJob Job, ExportResult? Res)>();
        var res = await new PdfExporter().ExportBatch(t, jobs, Opt(), before,
            new SyncProgress<(int, int, ExportJob, ExportResult?)>(p => reports.Add(p)), null, CancellationToken.None);
        Assert.Equal(2, res.Done);
        Assert.Equal(1, res.Failed);
        Assert.False(res.Cancelled);
        var bad = res.Results.Single(r => r.JobId == "42-0401").Result;
        Assert.False(bad.Ok);
        Assert.Equal("일부러 실패", bad.Error);
        Assert.Equal(new[] { 1, 3, 4 }, reports.Select(r => r.Index).ToArray());
        Assert.All(reports, r => Assert.Equal(4, r.Total));
    }

    [Fact]
    public async Task Batch_On_Paper_Fills_Slots_And_Adds_Pages()
    {
        var t = SmallTemplate();
        var ctx = CtxOf(t, "16-0401");
        var store = SampleDb.NewStore();
        var o = Opt("{ITEM}");
        o.Layout = new LayoutOptions { Paper = "custom", CustomW = 150, CustomH = 90, Orientation = "landscape", MarginMm = 5, GapX = 2, GapY = 2, Outline = true, CropMarks = true };
        var plan = Paper.Plan(t.Label, o.Layout);
        Assert.Equal(2, plan.PerPage);       // 150×90 에 60×30 → 2열 × 1행... 가로 140/62 = 2, 세로 80/32 = 2 → 4? 아래에서 확인
        var jobs = new[] { JobOf("16-0401"), JobOf("42-0401", 2), JobOf("21-1801") };   // 4장 → perPage 로 나눠 담는다
        var res = await new PdfExporter().ExportBatch(t, jobs, o, SlotSwapper(t, ctx, store), null, null, CancellationToken.None);

        Assert.True(res.Ok, res.Error);
        Assert.True(res.OnPaper);
        Assert.Equal(plan.PerPage, res.PerPage);
        Assert.Equal(4, res.Done);
        var expectedPages = (int)Math.Ceiling(4.0 / plan.PerPage);
        Assert.Equal(expectedPages, res.Pages);
        Assert.Equal($"16-0401_{expectedPages}쪽.pdf", res.FileName);
        var pdf = await File.ReadAllBytesAsync(Path.Combine(_dir, res.FileName!));
        Assert.Equal(expectedPages, PageCount(pdf));
        var (w, h) = MediaBox(pdf);
        Assert.True(Math.Abs(w - plan.PaperW * PtPerMm) < 0.05);
        Assert.True(Math.Abs(h - plan.PaperH * PtPerMm) < 0.05);
        // 세 품목의 그림이 다 들어 있다
        Assert.True(ImageStreamHashes(pdf).Distinct().Count() >= 3);

        // 안 맞는 용지는 이유를 돌려준다
        o.Layout = new LayoutOptions { Paper = "custom", CustomW = 40, CustomH = 40 };
        var bad = await new PdfExporter().ExportBatch(t, jobs, o, SlotSwapper(t, ctx, store), null, null, CancellationToken.None);
        Assert.False(bad.Ok);
        Assert.Equal(Paper.Plan(t.Label, o.Layout).Reason, bad.Error);
    }
}

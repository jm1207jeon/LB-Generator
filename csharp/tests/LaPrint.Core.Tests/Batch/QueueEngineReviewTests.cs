// 적대적 리뷰에서 확인된 결함의 회귀 테스트 — 오류 행은 절대 출력되지 않고, prepareJob 은 행마다 한 번만, 최종 저장 실패는 '완료'로 남지 않는다.
using LaPrint.Core.Batch;
using LaPrint.Core.Export;
using LaPrint.Core.Model;
using Xunit;

namespace LaPrint.Core.Tests.Batch;

public class QueueEngineReviewTests
{
    // Progress<T> 는 동기화 컨텍스트로 미루므로 순서를 재려면 즉시 부르는 IProgress 가 필요하다
    private sealed class Sync<T> : IProgress<T>
    {
        private readonly Action<T> _a;
        public Sync(Action<T> a) => _a = a;
        public void Report(T v) => _a(v);
    }

    private static string TempDir()
    {
        var d = Path.Combine(AppContext.BaseDirectory, "_batch_review_tmp", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    private static LabelTemplate SmallTemplate() => new()
    {
        Label = new LabelSize { W = 40, H = 20 },
        Objects = { new TextObject { Id = "t", X = 2, Y = 2, W = 30, H = 8, Text = "{ITEM}", Font = "Arial", SizePt = 8 } },
    };

    [Fact]
    public async Task RunAsync_SkipsRowsMarkedError_AndNeverPreparesThem()
    {
        // batch.js:326 skipErrors:true — 검증 오류 행(품목 없음·체크디짓 오류 …)이 라벨로 나가면 안 된다
        var q = new QueueEngine();
        q.Add(new QueueRow { Item = "OK", Lot = "L1", Status = "pending" });
        q.Add(new QueueRow { Item = "BAD", Lot = "L2", Status = "error", Error = "품목번호가 라벨DB에 없습니다." });
        var prepared = new List<string>();
        var outDir = TempDir();
        var o = new ExportOptions { OutDir = outDir, Pattern = "{ITEM}", Mode = "separate" };
        var res = await q.RunAsync(SmallTemplate(), o, new PdfExporter(), r => { prepared.Add(r.Item); return Task.CompletedTask; }, null, CancellationToken.None);

        Assert.True(res.Ok, res.Error);
        Assert.Equal(new[] { "OK" }, prepared);
        var files = Directory.GetFiles(outDir).Select(Path.GetFileName).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { "OK.pdf" }, files);
        Assert.Equal("done", q.Rows.Single(r => r.Item == "OK").Status);
        var bad = q.Rows.Single(r => r.Item == "BAD");
        Assert.Equal("error", bad.Status);
        Assert.Equal("품목번호가 라벨DB에 없습니다.", bad.Error);
        Assert.Equal("", bad.FileName);
    }

    [Fact]
    public async Task RunAsync_OnlyErrorRows_ReturnsNoRowsError()
    {
        var q = new QueueEngine();
        q.Add(new QueueRow { Item = "BAD", Status = "error" });
        var res = await q.RunAsync(SmallTemplate(), new ExportOptions { OutDir = TempDir() }, new PdfExporter(), _ => Task.CompletedTask, null, CancellationToken.None);
        Assert.False(res.Ok);
        Assert.Equal("출력할 행이 없습니다.", res.Error);
    }

    [Fact]
    public async Task RunAsync_PrepareJob_CalledExactlyOncePerRow_JustBeforeItsExport()
    {
        // batch.js 는 beforeJob 에서만 prepareJob 을 부른다 — 앞서 전체를 한 바퀴 돌면 첫 장이 늦고 그림을 두 번 읽는다
        var q = new QueueEngine();
        q.Add(new QueueRow { Item = "A", Lot = "L1", Copies = 2 });
        q.Add(new QueueRow { Item = "B", Lot = "L2" });
        var log = new List<string>();
        var outDir = TempDir();
        var o = new ExportOptions { OutDir = outDir, Pattern = "{ITEM}", Mode = "separate" };
        var progress = new Sync<(int Index, int Total, QueueRow? Row)>(p => { if (p.Row is not null && p.Row.Status == "done") log.Add("done:" + p.Row.Item); });
        var res = await q.RunAsync(SmallTemplate(), o, new PdfExporter(), r => { log.Add("prep:" + r.Item); return Task.CompletedTask; }, progress, CancellationToken.None);

        Assert.True(res.Ok, res.Error);
        Assert.Equal(1, log.Count(x => x == "prep:A"));
        Assert.Equal(1, log.Count(x => x == "prep:B"));
        // B 의 준비는 A 가 다 나간 뒤에 온다
        Assert.True(log.IndexOf("prep:B") > log.LastIndexOf("done:A"), string.Join(",", log));
    }

    [Fact]
    public async Task RunAsync_MergedWriteFailure_MarksRowsError_NotDone()
    {
        // merged 모드는 마지막에 한 파일을 쓴다 — 그 저장이 실패하면 행이 '완료'(파일 없음)로 남아 재출력이 막히면 안 된다
        var q = new QueueEngine();
        q.Add(new QueueRow { Item = "A", Lot = "L1" });
        q.Add(new QueueRow { Item = "B", Lot = "L2" });
        var outDir = TempDir();
        // 저장 폴더 자리에 파일을 두어 WriteOutput 이 반드시 실패하게 한다
        var blocked = Path.Combine(outDir, "blocked");
        File.WriteAllText(blocked, "x");
        var o = new ExportOptions { OutDir = blocked, Pattern = "{ITEM}", Mode = "merged" };
        var res = await q.RunAsync(SmallTemplate(), o, new PdfExporter(), _ => Task.CompletedTask, null, CancellationToken.None);

        Assert.False(res.Ok);
        Assert.False(res.Cancelled);
        Assert.False(string.IsNullOrEmpty(res.Error));
        Assert.All(q.Rows, r => Assert.Equal("error", r.Status));
        Assert.All(q.Rows, r => Assert.Equal(res.Error, r.Error));
        Assert.DoesNotContain(q.Rows, r => r.Status == "done");
        Assert.False(q.Running);
        // 이력에 실패로 남길 수 있도록 행별 실패 결과가 온다
        Assert.Equal(q.Rows.Select(r => r.Id).OrderBy(x => x), res.Results.Select(x => x.JobId).OrderBy(x => x));
        Assert.All(res.Results, x => Assert.False(x.Result.Ok));
    }
}

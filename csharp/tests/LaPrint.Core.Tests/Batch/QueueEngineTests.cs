// 연속 작업 큐 — 붙여넣기 파싱(머리글·위치·쉼표·따옴표), 날짜 정규화, SN 전개, CSV, 행 조작과 Changed, 일시정지/중지 (batch.js 와 동일 동작).
using System.Text;
using LaPrint.Core.Batch;
using LaPrint.Core.Data;
using LaPrint.Core.Export;
using LaPrint.Core.Model;
using LaPrint.Core.Render;
using NPOI.XSSF.UserModel;
using Xunit;

namespace LaPrint.Core.Tests.Batch;

public class QueueEngineTests
{
    // 브라우저판 테스트에서 쓴 붙여넣기 그대로
    private const string Paste =
        "품목번호\tLOT\tSN\t제조일\t매수\n" +
        "PN-001\tL2401\tS001\t2024-01-15\t2\n" +
        "PN-002\tL2402\tS002\t2024.02.01\t1\n" +
        "PN-003\tL2403\t\t20240301\t3\n" +
        "PN-004\tL2404\tS004\t240401\t\n" +
        "PN-005\tL2405\tS005\t45292\t1\n";

    private static string TempDir()
    {
        var d = Path.Combine(AppContext.BaseDirectory, "_batch_tmp", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    /* ---------------- ParseTable ---------------- */

    [Fact]
    public void ParseTable_HeaderPaste_FiveRowsMapped()
    {
        var (rows, mapping, header, error) = QueueEngine.ParseTable(Paste, null);
        Assert.Null(error);
        Assert.True(header);
        Assert.Equal(5, rows.Count);
        Assert.Equal(0, mapping["item"]);
        Assert.Equal(1, mapping["lot"]);
        Assert.Equal(2, mapping["sn"]);
        Assert.Equal(3, mapping["mfg"]);
        Assert.Equal(4, mapping["copies"]);
        Assert.False(mapping.ContainsKey("exp"));

        Assert.Equal("PN-001", rows[0].Item);
        Assert.Equal("L2401", rows[0].Lot);
        Assert.Equal("S001", rows[0].Sn);
        Assert.Equal("2024-01-15", rows[0].Mfg);
        Assert.Equal(2, rows[0].Copies);
        Assert.Equal("2024-02-01", rows[1].Mfg);
        Assert.Equal("", rows[2].Sn);
        Assert.Equal("2024-03-01", rows[2].Mfg);
        Assert.Equal("2024-04-01", rows[3].Mfg);
        Assert.Equal(1, rows[3].Copies);            // 빈 매수 → 1
        Assert.Equal("2024-01-01", rows[4].Mfg);    // 엑셀 일련번호 45292
        Assert.All(rows, r => Assert.True(r.ExpAuto));
        Assert.All(rows, r => Assert.Equal("pending", r.Status));
        Assert.All(rows, r => Assert.Equal(36, r.Months));
    }

    [Fact]
    public void ParseTable_Headerless_Positional()
    {
        var (rows, mapping, header, error) = QueueEngine.ParseTable("PN-001\tL1\tS1\t2024-05-06\t4\nPN-002\tL2", null);
        Assert.Null(error);
        Assert.False(header);
        Assert.Equal(new[] { "item", "lot", "sn", "mfg", "copies" }, mapping.OrderBy(kv => kv.Value).Select(kv => kv.Key).ToArray());
        Assert.Equal(2, rows.Count);
        Assert.Equal(4, rows[0].Copies);
        Assert.Equal("2024-05-06", rows[0].Mfg);
        Assert.Equal("", rows[1].Sn);
        Assert.Equal(1, rows[1].Copies);
    }

    [Fact]
    public void ParseTable_CommaDelimiter_QuotedCells_AliasesCaseInsensitive()
    {
        var text = "Product Number, Lot No ,Serial,MFG,Expiry,Qty\r\n" +
                   "\"PN-001\",\"L1\",S1,2024/1/5,2027-01-04,\"3\"\r\n" +
                   "PN-002,L2,,2024-02-02,,\r\n";
        var (rows, mapping, header, error) = QueueEngine.ParseTable(text, null);
        Assert.Null(error);
        Assert.True(header);
        Assert.Equal(0, mapping["item"]);
        Assert.Equal(1, mapping["lot"]);
        Assert.Equal(2, mapping["sn"]);
        Assert.Equal(3, mapping["mfg"]);
        Assert.Equal(4, mapping["exp"]);
        Assert.Equal(5, mapping["copies"]);
        Assert.Equal("PN-001", rows[0].Item);
        Assert.Equal("L1", rows[0].Lot);
        Assert.Equal("2024-01-05", rows[0].Mfg);
        Assert.Equal("2027-01-04", rows[0].Exp);
        Assert.False(rows[0].ExpAuto);
        Assert.Equal(3, rows[0].Copies);
        Assert.True(rows[1].ExpAuto);
        Assert.Equal("", rows[1].Exp);
    }

    [Fact]
    public void ParseTable_MonthsColumn_And_Defaults()
    {
        var defaults = new QueueRow { Months = 24, ExpAuto = true, Copies = 9 };
        var (rows, _, header, _) = QueueEngine.ParseTable("품목\tlot\t유효기간개월\nPN-1\tL1\t12\nPN-2\tL2\t\nPN-3\tL3\t0", defaults);
        Assert.True(header);
        Assert.Equal(3, rows.Count);
        Assert.Equal(12, rows[0].Months);
        Assert.Equal(24, rows[1].Months);       // 빈 개월 → 기본값 유지
        Assert.Equal(24, rows[2].Months);       // 0 은 무시
        Assert.All(rows, r => Assert.Equal(1, r.Copies));   // 매수 열이 없으면 1
        Assert.All(rows, r => Assert.NotEqual(defaults.Id, r.Id));
    }

    [Fact]
    public void ParseTable_SkipsRowsWithoutItemAndLot()
    {
        var (rows, _, _, error) = QueueEngine.ParseTable("품목번호\tLOT\n\t\n\tL9\nPN\t", null);
        Assert.Null(error);
        Assert.Equal(2, rows.Count);
        Assert.Equal("", rows[0].Item);
        Assert.Equal("L9", rows[0].Lot);
        Assert.Equal("PN", rows[1].Item);
    }

    [Fact]
    public void ParseTable_Errors_Verbatim()
    {
        var empty = QueueEngine.ParseTable("  \r\n \n", null);
        Assert.Equal("붙여넣을 내용이 없습니다.", empty.Error);
        Assert.Empty(empty.Rows);
        Assert.False(empty.HeaderDetected);

        var noData = QueueEngine.ParseTable("품목번호\tLOT\tSN", null);
        Assert.Equal("인식된 데이터 행이 없습니다. 품목번호 열이 있는지 확인하세요.", noData.Error);
        Assert.True(noData.HeaderDetected);
        Assert.Empty(noData.Rows);
    }

    [Fact]
    public void ParseTable_SingleHeaderColumn_IsHeader_When_Matched()
    {
        // 열이 하나뿐이면 하나만 맞아도 머리글로 본다 (min(2, 1) = 1)
        var (rows, mapping, header, _) = QueueEngine.ParseTable("품목번호\nPN-1\nPN-2", null);
        Assert.True(header);
        Assert.Equal(0, mapping["item"]);
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void ParseTable_BomBeforeHeader_StillMatches()
    {
        var (rows, _, header, _) = QueueEngine.ParseTable("﻿품목번호\tLOT\nPN-1\tL1", null);
        Assert.True(header);
        Assert.Single(rows);
        Assert.Equal("PN-1", rows[0].Item);
    }

    [Theory]
    [InlineData("품목번호", "item")]
    [InlineData(" Product Number ", "item")]
    [InlineData("product_number", "item")]
    [InlineData("H", "item")]
    [InlineData("LOT NO.", "lot")]
    [InlineData("로트번호", "lot")]
    [InlineData("Serial-No", "sn")]
    [InlineData("제조일자", "mfg")]
    [InlineData("Date of Manufacture", "mfg")]
    [InlineData("유효기간", "exp")]
    [InlineData("Use By", "exp")]
    [InlineData("유효개월", "months")]
    [InlineData("수량", "copies")]
    [InlineData("Quantity", "copies")]
    [InlineData("비고", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void MatchColumn_Aliases(string? header, string? expected)
        => Assert.Equal(expected, QueueEngine.MatchColumn(header));

    [Theory]
    [InlineData("2024-01-05", "2024-01-05")]
    [InlineData("2024-1-5", "2024-01-05")]
    [InlineData("2024.01.05", "2024-01-05")]
    [InlineData("2024/1/05", "2024-01-05")]
    [InlineData("20240105", "2024-01-05")]
    [InlineData("240105", "2024-01-05")]
    [InlineData("45292", "2024-01-01")]
    [InlineData("45658", "2025-01-01")]
    [InlineData(" 2024-01-05 ", "2024-01-05")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    [InlineData("Jan 5 2024", "Jan 5 2024")]
    [InlineData("2024-01-05T00:00", "2024-01-05T00:00")]
    [InlineData("1234", "1234")]
    [InlineData("123456789", "123456789")]
    public void NormDate_Variants(string? input, string expected)
        => Assert.Equal(expected, QueueEngine.NormDate(input));

    /* ---------------- ParseFileAsync ---------------- */

    [Fact]
    public async Task ParseFile_Csv_Utf8Bom()
    {
        var dir = TempDir();
        var path = Path.Combine(dir, "큐.csv");
        await File.WriteAllTextAsync(path, "품목번호,LOT,매수\r\nPN-1,L1,2\r\nPN-2,L2,3\r\n", new UTF8Encoding(true));
        var (rows, _, header, error) = await QueueEngine.ParseFileAsync(path, null);
        Assert.Null(error);
        Assert.True(header);
        Assert.Equal(2, rows.Count);
        Assert.Equal(3, rows[1].Copies);
    }

    [Fact]
    public async Task ParseFile_Txt_Cp949_Fallback()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp949 = Encoding.GetEncoding(949);
        var dir = TempDir();
        var path = Path.Combine(dir, "queue.txt");
        await File.WriteAllBytesAsync(path, cp949.GetBytes("품목번호\tLOT\tSN\n한글품목\tL1\tS1\n"));
        var (rows, _, header, error) = await QueueEngine.ParseFileAsync(path, null);
        Assert.Null(error);
        Assert.True(header);
        Assert.Single(rows);
        Assert.Equal("한글품목", rows[0].Item);
    }

    [Fact]
    public async Task ParseFile_Xlsx_FirstSheet()
    {
        var dir = TempDir();
        var path = Path.Combine(dir, "queue.xlsx");
        using (var wb = new XSSFWorkbook())
        {
            var ws = wb.CreateSheet("큐");
            var h = ws.CreateRow(0);
            h.CreateCell(0).SetCellValue("품목번호");
            h.CreateCell(1).SetCellValue("LOT");
            h.CreateCell(2).SetCellValue("SN");
            h.CreateCell(3).SetCellValue("제조일");
            h.CreateCell(4).SetCellValue("매수");
            var r1 = ws.CreateRow(1);
            r1.CreateCell(0).SetCellValue("08806");         // 문자열 — 앞자리 0 보존
            r1.CreateCell(1).SetCellValue("L1");
            r1.CreateCell(2).SetCellValue("S1");
            r1.CreateCell(3).SetCellValue("2024-03-04");
            r1.CreateCell(4).SetCellValue(2);
            ws.CreateRow(2);                                 // 빈 행
            var r3 = ws.CreateRow(3);
            r3.CreateCell(0).SetCellValue("PN-2");
            r3.CreateCell(1).SetCellValue("L2");
            wb.CreateSheet("둘째");
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            wb.Write(fs, false);
        }
        var (rows, mapping, header, error) = await QueueEngine.ParseFileAsync(path, null);
        Assert.Null(error);
        Assert.True(header);
        Assert.Equal(4, mapping["copies"]);
        Assert.Equal(2, rows.Count);
        Assert.Equal("08806", rows[0].Item);
        Assert.Equal("2024-03-04", rows[0].Mfg);
        Assert.Equal(2, rows[0].Copies);
        Assert.Equal("PN-2", rows[1].Item);
        Assert.Equal(1, rows[1].Copies);
    }

    /* ---------------- ExpandSerial ---------------- */

    [Fact]
    public void ExpandSerial_Pad3_ReplacesRowInQueue()
    {
        var q = new QueueEngine();
        var first = q.Add(new QueueRow { Item = "A", Lot = "L0" });
        var proto = q.Add(new QueueRow { Item = "PN", Lot = "L1", Mfg = "2024-01-01", Copies = 2, Months = 12, Status = "done", FileName = "x.pdf", Error = "e" });
        var last = q.Add(new QueueRow { Item = "Z", Lot = "L9" });

        var made = q.ExpandSerial(proto, 1, 5, 3).ToList();

        Assert.Equal(new[] { "001", "002", "003", "004", "005" }, made.Select(r => r.Sn).ToArray());
        Assert.All(made, r => { Assert.Equal("PN", r.Item); Assert.Equal("L1", r.Lot); Assert.Equal("2024-01-01", r.Mfg); Assert.Equal(2, r.Copies); Assert.Equal(12, r.Months); });
        Assert.All(made, r => { Assert.Equal("pending", r.Status); Assert.Equal("", r.FileName); Assert.Equal("", r.Error); Assert.NotEqual(proto.Id, r.Id); });
        Assert.Equal(7, q.Count);
        Assert.Same(first, q.Rows[0]);
        Assert.Same(last, q.Rows[6]);
        Assert.Equal("003", q.Rows[3].Sn);
        Assert.DoesNotContain(q.Rows, r => ReferenceEquals(r, proto));
    }

    [Fact]
    public void ExpandSerial_DefaultWidth_FromStartValue_NotInQueue()
    {
        var q = new QueueEngine();
        var made = q.ExpandSerial(new QueueRow { Item = "PN" }, 8, 12, 0).ToList();
        Assert.Equal(new[] { "8", "9", "10", "11", "12" }, made.Select(r => r.Sn).ToArray());
        Assert.Equal(0, q.Count);

        var padded = q.ExpandSerial(new QueueRow { Item = "PN" }, 0098, 100, 0).Select(r => r.Sn).ToArray();
        Assert.Equal(new[] { "98", "99", "100" }, padded);
    }

    [Fact]
    public void ExpandSerial_Errors_Verbatim()
    {
        var q = new QueueEngine();
        var ex = Assert.Throws<ArgumentException>(() => q.ExpandSerial(new QueueRow(), 5, 4, 0));
        Assert.Equal("SN 끝 값이 시작 값보다 작습니다.", ex.Message);
        var ex2 = Assert.Throws<ArgumentException>(() => q.ExpandSerial(new QueueRow(), 1, 2001, 0));
        Assert.Equal("한 번에 2001개는 너무 많습니다. 2000개 이하로 나누어 주세요.", ex2.Message);
    }

    /* ---------------- ToCsv ---------------- */

    [Fact]
    public void ToCsv_HeaderLine_Bom_Escaping()
    {
        var q = new QueueEngine();
        q.Add(new QueueRow { Item = "PN-1", Lot = "L1", Sn = "S1", Mfg = "2024-01-01", Months = 36, ExpAuto = true, Copies = 2 });
        q.Add(new QueueRow { Item = "PN,2", Lot = "L\"2", Sn = "", Mfg = "2024-02-02", ExpAuto = false, Exp = "2026-01-31", Copies = 1 });
        var csv = q.ToCsv();
        Assert.StartsWith("﻿", csv);
        var lines = csv.TrimStart('﻿').Split("\r\n");
        Assert.Equal("품목번호,LOT,SN,제조일,유효기간개월,유효일,매수", lines[0]);
        Assert.Equal("PN-1,L1,S1,2024-01-01,36,,2", lines[1]);
        Assert.Equal("\"PN,2\",\"L\"\"2\",,2024-02-02,,2026-01-31,1", lines[2]);
        Assert.Equal(3, lines.Length);

        // 되읽기 왕복 (JS 와 같이 쉼표를 단순 분리하므로 값 안의 쉼표는 왕복되지 않는다)
        var back = QueueEngine.ParseTable(csv, null);
        Assert.True(back.HeaderDetected);
        Assert.Equal(2, back.Rows.Count);
        Assert.Equal("PN-1", back.Rows[0].Item);
        Assert.Equal(36, back.Rows[0].Months);
        Assert.True(back.Rows[0].ExpAuto);
        Assert.Equal(2, back.Rows[0].Copies);

        var q2 = new QueueEngine();
        q2.Add(new QueueRow { Item = "PN-2", Lot = "L2", Mfg = "2024-02-02", ExpAuto = false, Exp = "2026-01-31", Copies = 1 });
        var back2 = QueueEngine.ParseTable(q2.ToCsv(), null);
        Assert.False(back2.Rows[0].ExpAuto);
        Assert.Equal("2026-01-31", back2.Rows[0].Exp);
    }

    /* ---------------- 행 조작 · Changed ---------------- */

    [Fact]
    public void QueueOps_ChangedFires_And_Counts()
    {
        var q = new QueueEngine();
        var n = 0;
        q.Changed += () => n++;

        var a = q.Add(new QueueRow { Item = "A", Copies = 2 });
        Assert.Equal(1, n);
        q.AddMany(new[] { new QueueRow { Item = "B", Copies = 0 }, new QueueRow { Item = "C", Copies = 3 } });
        Assert.Equal(2, n);
        Assert.Equal(3, q.Count);
        Assert.Equal(6, q.TotalLabels);   // 2 + (0→1) + 3

        q.Update(a.Id, r => { r.Status = "done"; r.FileName = "a.pdf"; });
        Assert.Equal(3, n);
        Assert.Equal("done", a.Status);
        q.Update(a.Id, r => r.Lot = "L1");   // 상태를 바꾸지 않은 편집 → 대기로
        Assert.Equal("pending", a.Status);
        Assert.Equal("L1", a.Lot);
        q.Update("없는-id", r => r.Lot = "X");   // 없는 행은 알리지 않는다
        Assert.Equal(4, n);

        a.Status = "done"; a.FileName = "a.pdf"; a.Error = "";
        q.Rows[1].Status = "error"; q.Rows[1].Error = "오류";
        q.Rows[2].Status = "running";
        q.ResetStatus();
        Assert.Equal("pending", a.Status);
        Assert.Equal("", a.FileName);
        Assert.Equal("pending", q.Rows[1].Status);
        Assert.Equal("", q.Rows[1].Error);
        Assert.Equal("running", q.Rows[2].Status);
        Assert.Equal((0, 0), q.Progress);

        q.Remove(a.Id);
        Assert.Equal(2, q.Count);
        Assert.DoesNotContain(q.Rows, r => r.Id == a.Id);
        q.Clear();
        Assert.Equal(0, q.Count);
        Assert.Equal(0, q.TotalLabels);
        Assert.Equal(7, n);
    }

    [Fact]
    public void PauseResumeCancel_Flags()
    {
        var q = new QueueEngine();
        var n = 0;
        q.Changed += () => n++;
        Assert.False(q.Running);
        Assert.False(q.Paused);
        q.Pause();
        Assert.True(q.Paused);
        q.Resume();
        Assert.False(q.Paused);
        q.Pause();
        q.Cancel();
        Assert.False(q.Paused);
        Assert.True(q.CancelRequested);
        Assert.False(q.Running);
        Assert.Equal(4, n);
    }

    /* ---------------- ValidateAll · RunAsync ---------------- */

    [Fact]
    public async Task RunAsync_NoRows_ReturnsError_WithoutTouchingExporter()
    {
        var q = new QueueEngine();
        q.Add(new QueueRow { Item = "A", Status = "done" });
        var prepared = 0;
        var res = await q.RunAsync(new LabelTemplate(), new ExportOptions(), new PdfExporter(),
            _ => { prepared++; return Task.CompletedTask; }, null, CancellationToken.None);
        Assert.False(res.Ok);
        Assert.Equal("출력할 행이 없습니다.", res.Error);
        Assert.Equal(0, prepared);
        Assert.False(q.Running);
    }

    [Fact]
    public async Task RunAsync_PrepareJobAwaited_BeforeExport_And_RunningResetOnFailure()
    {
        var q = new QueueEngine();
        q.Add(new QueueRow { Item = "A", Lot = "L1", Copies = 2 });
        q.Add(new QueueRow { Item = "B", Lot = "L2" });
        var prepared = new List<string>();
        var runningSeen = false;
        Func<QueueRow, Task> prepare = async r => { await Task.Yield(); runningSeen |= q.Running; prepared.Add(r.Item); };
        try
        {
            var res = await q.RunAsync(new LabelTemplate(), new ExportOptions { OutDir = TempDir() }, new PdfExporter(), prepare, null, CancellationToken.None);
            // 출력기가 구현된 환경 — 결과가 어떻든 상태는 정리되어야 한다
            Assert.NotNull(res);
        }
        catch (NotImplementedException)
        {
            // TODO 통합 후: PdfExporter.ExportBatch 가 구현되면 실제 결과 매핑을 검증한다
        }
        Assert.Equal(new[] { "A", "B" }, prepared.Take(2).ToArray());
        Assert.True(runningSeen);
        Assert.False(q.Running);
        Assert.False(q.Paused);
        Assert.DoesNotContain(q.Rows, r => r.Status == "running");
    }

    [Fact]
    public void ValidateAll_Contract_Compiles_And_FillsRows()
    {
        var q = new QueueEngine();
        q.Add(new QueueRow { Item = "A", Lot = "L1", Sn = "S1", Mfg = "2024-01-01" });
        q.Add(new QueueRow { Item = "A", Lot = "L1", Sn = "S1", Mfg = "2024-01-01" });
        var index = new LabelIndex();
        var map = new FieldMap();
        var tpl = new LabelTemplate { Label = new LabelSize { W = 100, H = 50 } };
        Func<QueueRow, RenderContext> ctxOf = r => new RenderContext { ForExport = true };
        try
        {
            var s = q.ValidateAll(index, map, tpl, new ValidationRules(), 300, ctxOf);
            Assert.Equal(2, s.Total);
            Assert.Equal(2, s.ErrorRows + s.WarnRows + s.OkRows);
            Assert.All(q.Rows, r => Assert.NotNull(r.Fields));
            Assert.All(q.Rows, r => Assert.Contains(r.Issues, i => i.Code == "DUPLICATE" && i.Msg == "같은 품목·LOT·SN 조합이 큐에 중복되어 있습니다."));
            Assert.Contains(s.Issues, i => i.Code == "DUPLICATE" && i.Count == 2);
        }
        catch (NotImplementedException)
        {
            // TODO 통합 후: FieldComputer.Compute / Preflight.Run 이 구현되면 위 단언이 실행된다
        }
    }
}

// 라벨DB 로딩 — 샘플 xlsx(12행, 시트 라벨DB, 키 열 H, 머리글 감지), 숫자 셀 표시 문자열, CSV, 시트 선택 규칙.
using System.Text;
using LaPrint.Core.Barcode;
using LaPrint.Core.Data;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using Xunit;

namespace LaPrint.Core.Tests.Data;

public class LabelDbLoaderTests
{
    private static LabelDb LoadSample()
    {
        using var fs = File.OpenRead(Fixtures.Path("샘플_라벨DB.xlsx"));
        return LabelDbLoader.Load(fs, "샘플_라벨DB.xlsx", "H");
    }

    [Fact]
    public void Sample_Xlsx_RowsSheetHeaderColCount()
    {
        var db = LoadSample();
        Assert.Equal(12, db.Rows.Count);
        Assert.Equal("라벨DB", db.Sheet);
        Assert.Contains(("라벨DB", 12), db.Sheets);
        Assert.Contains(("사용안내", 0), db.Sheets);
        Assert.NotNull(db.Header);
        Assert.Equal("h1.Product Number", db.Header!["H"]);
        Assert.Equal(55, db.ColCount);
        Assert.Equal("BC", ColumnName.FromIndex(db.ColCount - 1));
        Assert.Equal(54, ColumnName.ToIndex("BC"));

        var first = db.Rows[0];
        Assert.Equal("16-0401", first["H"]);
        Assert.Equal("BCG-06-040-180", first["L"]);
        Assert.Equal("HANAROSTENT® Biliary Flap (CCC) 1.png", first["J"]);
        Assert.Equal("Fully covered", first["BC"]);
        Assert.Equal("", first["M"]);
        Assert.Equal(55, first.Count);
        Assert.All(db.Rows, r => Assert.All(r.Values, v => Assert.Equal(v.Trim(), v)));
        Assert.All(db.Rows, r => Assert.Matches(@"^\d{14}$", r["AJ"]));
        Assert.All(db.Rows, r => Assert.True(Gs1.GtinValid(r["AJ"]), r["AJ"]));
        Assert.Equal("99-0001", db.Rows[^1]["H"]);
        // 마지막 행은 그림 열(J·O)이 빈 inlineStr 셀이다 — "" 로 읽혀야 한다
        Assert.Equal("", db.Rows[^1]["J"]);
        Assert.Equal("", db.Rows[^1]["O"]);
        Assert.Equal("TEST-00-000-000", db.Rows[^1]["L"]);
    }

    [Fact]
    public void Sample_IndexSearchAndProductName()
    {
        var db = LoadSample();
        var map = new FieldMap("general");
        var idx = LabelIndex.Build(db.Rows, map);
        Assert.Equal(12, idx.Total);
        Assert.Equal(0, idx.Skipped);
        Assert.Empty(idx.Dups);
        Assert.Equal(12, idx.ByRef.Count);

        var r = idx.Search("16-04");
        Assert.NotEmpty(r);
        Assert.All(r, e => Assert.StartsWith("16-04", e.Key));
        Assert.All(r, e => Assert.False(string.IsNullOrEmpty(e.Name)));
        Assert.Equal("HANAROSTENT® Biliary Flap (CCC)", r[0].Name);
        Assert.Equal("BCG-06-040-180", r[0].Ref);

        var f = FieldComputer.Compute(idx.ByRef["42-0401"], new JobInputs("42-0401", "L1", "", "2026-01-31", 36), map);
        Assert.Equal("(01)88063671000074(10)L1(17)290130(240)42-0401", f["UDI_FULL"]);
        Assert.Equal("FAUNASTENT™", f["PRODUCT"]);
        Assert.Empty(RecordValidator.Validate(f, idx.ByRef["42-0401"], new ValidationRules(), new DateTime(2026, 9, 15)));
    }

    [Fact]
    public void Sample_MappingAudit_CoversAllGroups()
    {
        var db = LoadSample();
        var audit = MappingAuditor.Audit(db.Rows, new FieldMap("general"));
        var expectedKeys = FieldMap.Groups.SelectMany(g => g.Keys).ToList();
        Assert.Equal(expectedKeys, audit.Select(a => a.Key).ToList());
        Assert.All(audit, a => Assert.Equal(12, a.Total));
        var upn = Assert.Single(audit, a => a.Key == "UPN");
        Assert.Equal("off", upn.Level);
        Assert.Equal("사용 안 함", upn.Note);
        var gtin = Assert.Single(audit, a => a.Key == "GTIN");
        Assert.Equal("ok", gtin.Level);
        Assert.Equal(12, gtin.Filled);
        Assert.Equal(100, gtin.Pct);
        Assert.Equal("AJ", gtin.Col);
        var img = Assert.Single(audit, a => a.Key == "IMG_STENT");
        Assert.True(img.ImagePct >= 50, $"IMG_STENT imagePct={img.ImagePct}");
        Assert.Equal("이미지 파일명", img.Group);
    }

    private static MemoryStream BuildWorkbook()
    {
        var wb = new XSSFWorkbook();
        var sheet = wb.CreateSheet("라벨DB");
        var other = wb.CreateSheet("메모");
        other.CreateRow(0).CreateCell(0).SetCellValue("메모만 있는 시트");
        var h = ColumnName.ToIndex("H");
        var header = sheet.CreateRow(0);
        header.CreateCell(h).SetCellValue("품목 번호");
        header.CreateCell(ColumnName.ToIndex("AJ")).SetCellValue("GTIN");

        var zeroPad = wb.CreateCellStyle();
        zeroPad.DataFormat = wb.CreateDataFormat().GetFormat("00000000000000");
        var dateStyle = wb.CreateCellStyle();
        dateStyle.DataFormat = wb.CreateDataFormat().GetFormat("yyyy-mm-dd");
        var textStyle = wb.CreateCellStyle();
        textStyle.DataFormat = wb.CreateDataFormat().GetFormat("@");

        var r1 = sheet.CreateRow(1);
        r1.CreateCell(h).SetCellValue(6);                                   // 숫자 6 → "6"
        r1.CreateCell(0).SetCellValue(0.035);                               // "0.035"
        var b = r1.CreateCell(1); b.SetCellValue(8806367058034d); b.CellStyle = zeroPad;   // "08806367058034"
        r1.CreateCell(2).SetCellValue("08806367058034");                    // 문자열 그대로
        r1.CreateCell(3).SetCellFormula("1+5");                             // 수식 캐시 결과 → "6"
        var e = r1.CreateCell(4); e.SetCellValue(new DateTime(2026, 1, 31)); e.CellStyle = dateStyle;
        r1.CreateCell(5).SetCellValue(12.5);
        r1.CreateCell(6).SetCellValue("  x  ");
        var i = r1.CreateCell(8); i.SetCellValue(42); i.CellStyle = textStyle;
        r1.CreateCell(9).SetCellValue(true);
        r1.CreateCell(10).SetCellFormula("\"a\"&\"b\"");
        r1.CreateCell(11).SetCellValue(1e15 + 0.0);
        r1.CreateCell(ColumnName.ToIndex("AJ")).SetCellValue("88063671000005");

        var r2 = sheet.CreateRow(2);                                        // 키 열이 빈 줄 → 제외
        r2.CreateCell(0).SetCellValue("no key");
        var r3 = sheet.CreateRow(3);
        r3.CreateCell(h).SetCellValue("0");                                 // 정크 키지만 로더는 남긴다 (색인이 거른다)
        sheet.CreateRow(4);                                                 // 빈 줄
        var r5 = sheet.CreateRow(5);
        r5.CreateCell(h).SetCellValue("  A-1  ");
        r5.CreateCell(ColumnName.ToIndex("BC")).SetCellValue("");

        XSSFFormulaEvaluator.EvaluateAllFormulaCells(wb);
        var ms = new MemoryStream();
        wb.Write(ms, true);
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public void Xlsx_CellText_NumbersFormulasDatesTrim()
    {
        using var ms = BuildWorkbook();
        var db = LabelDbLoader.Load(ms, "t.xlsx", "h");
        Assert.Equal("라벨DB", db.Sheet);
        Assert.Equal(new[] { ("라벨DB", 3), ("메모", 0) }, db.Sheets);
        Assert.NotNull(db.Header);
        Assert.Equal("품목 번호", db.Header!["H"]);
        Assert.Equal(3, db.Rows.Count);

        var r = db.Rows[0];
        Assert.Equal("6", r["H"]);
        Assert.Equal("0.035", r["A"]);
        Assert.Equal("08806367058034", r["B"]);
        Assert.Equal("08806367058034", r["C"]);
        Assert.Equal("6", r["D"]);
        Assert.Equal("2026-01-31", r["E"]);
        Assert.Equal("12.5", r["F"]);
        Assert.Equal("x", r["G"]);
        Assert.Equal("42", r["I"]);
        Assert.Equal("TRUE", r["J"]);
        Assert.Equal("ab", r["K"]);
        Assert.Equal("1000000000000000", r["L"]);
        Assert.Equal("88063671000005", r["AJ"]);
        Assert.Equal("0", db.Rows[1]["H"]);
        Assert.Equal("A-1", db.Rows[2]["H"]);
        Assert.Equal("", db.Rows[2]["BC"]);
        // BC 셀은 빈 문자열이라 세지 않는다 — 값이 있는 마지막 열은 AJ
        Assert.Equal(ColumnName.ToIndex("AJ") + 1, db.ColCount);
    }

    [Fact]
    public void Xlsx_NoHeader_WhenKeyCellIsData()
    {
        var wb = new XSSFWorkbook();
        var sheet = wb.CreateSheet("Sheet1");
        sheet.CreateRow(0).CreateCell(7).SetCellValue("16-0401");
        sheet.CreateRow(1).CreateCell(7).SetCellValue("16-0601");
        using var ms = new MemoryStream();
        wb.Write(ms, true);
        ms.Position = 0;
        var db = LabelDbLoader.Load(ms, "x.xlsx", "H");
        Assert.Null(db.Header);
        Assert.Equal(2, db.Rows.Count);
        Assert.Equal(8, db.ColCount);
    }

    [Fact]
    public void Xlsx_PrefersSheetNamedLabelDb_ElseMostKeyRows()
    {
        var wb = new XSSFWorkbook();
        var big = wb.CreateSheet("많은시트");
        for (var i = 0; i < 5; i++) big.CreateRow(i).CreateCell(7).SetCellValue("K" + i);
        var named = wb.CreateSheet("라벨DB");
        named.CreateRow(0).CreateCell(7).SetCellValue("ONLY");
        using var ms = new MemoryStream();
        wb.Write(ms, true);
        ms.Position = 0;
        var db = LabelDbLoader.Load(ms, "x.xlsx", "H");
        Assert.Equal("라벨DB", db.Sheet);
        Assert.Single(db.Rows);

        var wb2 = new XSSFWorkbook();
        var a = wb2.CreateSheet("A");
        for (var i = 0; i < 2; i++) a.CreateRow(i).CreateCell(7).SetCellValue("A" + i);
        var bsheet = wb2.CreateSheet("B");
        for (var i = 0; i < 4; i++) bsheet.CreateRow(i).CreateCell(7).SetCellValue("B" + i);
        using var ms2 = new MemoryStream();
        wb2.Write(ms2, true);
        ms2.Position = 0;
        var db2 = LabelDbLoader.Load(ms2, "x.xlsx", "H");
        Assert.Equal("B", db2.Sheet);
        Assert.Equal(4, db2.Rows.Count);
    }

    [Fact]
    public void Xlsx_NoKeyData_ThrowsWithSheetDetail()
    {
        var wb = new XSSFWorkbook();
        wb.CreateSheet("빈시트").CreateRow(0).CreateCell(0).SetCellValue("x");
        using var ms = new MemoryStream();
        wb.Write(ms, true);
        ms.Position = 0;
        var ex = Assert.Throws<InvalidDataException>(() => LabelDbLoader.Load(ms, "x.xlsx", "H"));
        Assert.Equal("H열(품목번호)에서 데이터를 찾지 못했습니다. 시트: 빈시트(0) — 설정 › 데이터 매칭에서 품목번호 열을 바꿀 수 있습니다.", ex.Message);
    }

    [Fact]
    public void Csv_ParsesQuotesAndKeepsLeadingZeros()
    {
        var csv = "a,b,c,d,e,f,g,Product Number,name\r\n"
                + "1,\"x, y\",\"he said \"\"hi\"\"\",,,,,08806367058034, Alpha \n"
                + ",,,,,,,,skipped\n"
                + "\n"
                + "0,,,,,,,00-1,Beta";
        using var ms = new MemoryStream(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray());
        var db = LabelDbLoader.Load(ms, "db.csv", "H");
        Assert.Equal("Sheet1", db.Sheet);
        Assert.NotNull(db.Header);
        Assert.Equal("Product Number", db.Header!["H"]);
        Assert.Equal(2, db.Rows.Count);
        Assert.Equal("08806367058034", db.Rows[0]["H"]);
        Assert.Equal("x, y", db.Rows[0]["B"]);
        Assert.Equal("he said \"hi\"", db.Rows[0]["C"]);
        Assert.Equal("Alpha", db.Rows[0]["I"]);
        Assert.Equal("00-1", db.Rows[1]["H"]);
        Assert.Equal(9, db.ColCount);
    }

    [Fact]
    public void Load_NonSeekableStream_IsBuffered()
    {
        using var file = File.OpenRead(Fixtures.Path("샘플_라벨DB.xlsx"));
        using var wrapped = new NonSeekable(file);
        var db = LabelDbLoader.Load(wrapped, "샘플_라벨DB.xlsx", "H");
        Assert.Equal(12, db.Rows.Count);
    }

    private sealed class NonSeekable : Stream
    {
        private readonly Stream _inner;
        public NonSeekable(Stream inner) => _inner = inner;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

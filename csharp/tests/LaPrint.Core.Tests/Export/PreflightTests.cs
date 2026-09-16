// 출력 전 점검 — 기본 서식(PMFL-001) + 샘플 품목: 오류 없음 / 빈 품목 NO_ITEM / 미해결 플레이스홀더 / 이미지 슬롯 / 바코드 / 영역 밖 / 객체 없음.
using LaPrint.Core.Data;
using LaPrint.Core.Export;
using LaPrint.Core.Imaging;
using LaPrint.Core.Model;
using LaPrint.Core.Render;
using LaPrint.Core.Tests.Imaging;
using Xunit;

namespace LaPrint.Core.Tests.Export;

public class PreflightTests
{
    private static LabelTemplate Pmfl()
        => SheetExtractor.ExtractPreset(TemplateJson.Default(), Paper.ProductLabels.Single(p => p.Id == "PMFL-001"), false).Item1;

    private static RenderContext CtxOf(LabelTemplate t, Fields f, DbRow? row) => new()
    {
        Fields = f, Row = row, Objects = t.Objects,
        ResolveText = s => Placeholders.Resolve(s, f, row),
    };

    [Fact]
    public async Task Sample_Item_Has_No_Errors()
    {
        var t = Pmfl();
        var row = SampleDb.Row("16-0401");
        var f = SampleDb.FieldsOf("16-0401");
        await SlotLoader.LoadIntoAsync(t.Objects, f, row, SampleDb.NewStore(), true, true, 30);
        var res = Preflight.Run(t, CtxOf(t, f, row), new ValidationRules(), 300);
        Assert.Empty(res.Errors);
        Assert.Equal(res.All.Count, res.Errors.Count + res.Warnings.Count + res.Infos.Count);
        Assert.DoesNotContain(res.All, i => i.Code == "NO_OBJECTS");
        Assert.DoesNotContain(res.All, i => i.Code == "BC_EMPTY" || i.Code == "BC_FAIL");
    }

    [Fact]
    public void Empty_Item_Is_Error_NO_ITEM()
    {
        var t = Pmfl();
        var f = FieldComputer.Compute(null, new JobInputs("", "", "", ""), SampleDb.Map);
        var res = Preflight.Run(t, CtxOf(t, f, null), new ValidationRules(), 300);
        Assert.Contains(res.Errors, i => i.Code == "NO_ITEM");
        Assert.Contains(res.All, i => i.Level == "error" && i.Code == "NO_ITEM" && i.Field == "item");
    }

    [Fact]
    public void Unresolved_Placeholder_Is_Warn()
    {
        var t = Pmfl();
        t.Objects.Add(new TextObject { Id = "x1", Name = "테스트", X = 1, Y = 1, W = 40, H = 5, Text = "{NOPE}" });
        var row = SampleDb.Row("16-0401");
        var f = SampleDb.FieldsOf("16-0401");
        var res = Preflight.Run(t, CtxOf(t, f, row), new ValidationRules(), 300);
        var w = Assert.Single(res.Warnings, i => i.Code == "UNRESOLVED");
        Assert.Equal("x1", w.ObjId);
        Assert.Equal("\"테스트\" 의 {NOPE} 항목이 치환되지 않았습니다. 필드 이름을 확인하세요.", w.Msg);
        Assert.Empty(res.Errors);

        // 규칙을 끄면 조용하다
        var quiet = Preflight.Run(t, CtxOf(t, f, row), new ValidationRules { WarnUnresolved = false }, 300);
        Assert.DoesNotContain(quiet.All, i => i.Code == "UNRESOLVED");
    }

    [Fact]
    public void Object_Checks_Messages()
    {
        var t = new LabelTemplate
        {
            Label = new LabelSize { W = 60, H = 30 },
            Objects =
            {
                new TextObject { Id = "t1", X = 50, Y = 2, W = 20, H = 5, Text = "abc" },                     // 영역 밖
                new TextObject { Id = "t2", Name = "긴글", X = 2, Y = 2, W = 3, H = 2, Text = "{PRODUCT}", Wrap = false, SizePt = 12 },   // 넘침
                new ImageObject { Id = "i1", X = 2, Y = 10, W = 10, H = 5, SourceField = "IMG_STENT" },          // 로딩 안 됨
                new ImageObject { Id = "i2", X = 2, Y = 16, W = 10, H = 5, SourceField = "NOPE_FIELD" },         // 파일명 없음
                new ImageObject { Id = "i3", X = 2, Y = 22, W = 10, H = 5, SourceField = "MEMO" },               // 파일명이 아님
                new BarcodeObject { Id = "b1", X = 30, Y = 10, W = 12, H = 12, Symbology = "gs1datamatrix", Source = "expression", Expression = "" },   // 비어 있음
                new TextObject { Id = "hidden", X = 100, Y = 100, W = 5, H = 5, Text = "{NOPE}", Visible = false },   // 안 보이면 검사 안 함
            },
        };
        var row = SampleDb.Row("16-0401");
        var f = SampleDb.FieldsOf("16-0401");
        f["MEMO"] = "그림파일 없음";
        var res = Preflight.Run(t, CtxOf(t, f, row), new ValidationRules(), 300);

        Assert.Contains(res.All, i => i.Code == "OUT_OF_BOUNDS" && i.ObjId == "t1" && i.Msg == "\"t1\" 객체가 라벨 영역을 벗어났습니다.");
        Assert.Contains(res.All, i => i.Code == "TEXT_OVERFLOW" && i.ObjId == "t2" && i.Msg == "\"긴글\" 텍스트가 영역을 넘칩니다. 영역을 키우거나 자동 축소를 켜세요.");
        var stent = f["IMG_STENT"];
        Assert.Contains(res.All, i => i.Code == "IMG_MISSING" && i.ObjId == "i1" && i.Msg == $"\"i1\" 슬롯: 이미지 파일을 불러오지 못했습니다 — {stent}");
        Assert.Contains(res.All, i => i.Code == "IMG_NO_NAME" && i.ObjId == "i2" && i.Msg == "\"i2\" 슬롯: 이 품목의 NOPE_FIELD 파일명이 라벨DB에 없습니다.");
        Assert.Contains(res.All, i => i.Code == "IMG_NOT_FILE" && i.ObjId == "i3" && i.Msg == "\"i3\" 슬롯: 라벨DB 값이 그림 파일명이 아닙니다 — \"그림파일 없음\". DB를 확인하세요.");
        Assert.Contains(res.Errors, i => i.Code == "BC_EMPTY" && i.ObjId == "b1" && i.Msg == "\"GS1 DataMatrix\" 바코드의 데이터가 비어 있습니다.");
        Assert.DoesNotContain(res.All, i => i.ObjId == "hidden");

        // 슬롯을 읽고 나면 IMG_MISSING 이 사라진다
        SlotLoader.LoadInto(t.Objects, f, row, SampleDb.NewStore(), true, true, 30);
        var after = Preflight.Run(t, CtxOf(t, f, row), new ValidationRules(), 300);
        Assert.DoesNotContain(after.All, i => i.Code == "IMG_MISSING" && i.ObjId == "i1");
    }

    [Fact]
    public void Barcode_Physical_And_Data_Issues_Are_Prefixed()
    {
        var t = new LabelTemplate
        {
            Label = new LabelSize { W = 60, H = 30 },
            Objects =
            {
                // 0.5mm 폭에 GS1-128 → 모듈이 너무 작다
                new BarcodeObject { Id = "b1", X = 1, Y = 1, W = 6, H = 4, Symbology = "gs1-128", Source = "expression", Expression = "(01)08806367058034(10)L1" },
            },
        };
        var f = new Fields();
        var res = Preflight.Run(t, CtxOf(t, f, null), new ValidationRules { RequireItem = false, RequireLot = false, RequireMfg = false }, 300);
        Assert.DoesNotContain(res.All, i => i.Code == "BC_EMPTY" || i.Code == "BC_FAIL");
        var bc = res.All.Where(i => i.ObjId == "b1").ToList();
        Assert.NotEmpty(bc);
        Assert.All(bc, i => Assert.StartsWith("바코드: ", i.Msg));
    }

    [Fact]
    public void No_Objects_Is_Warn()
    {
        var t = new LabelTemplate { Label = new LabelSize { W = 60, H = 30 } };
        var f = SampleDb.FieldsOf("16-0401");
        var res = Preflight.Run(t, CtxOf(t, f, SampleDb.Row("16-0401")), new ValidationRules(), 300);
        var w = Assert.Single(res.Warnings, i => i.Code == "NO_OBJECTS");
        Assert.Equal("라벨에 객체가 없습니다.", w.Msg);
        Assert.Null(w.ObjId);
    }
}

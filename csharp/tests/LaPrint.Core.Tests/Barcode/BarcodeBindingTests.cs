// 바코드 바인딩 — field · expression · object 링크(순환 차단), 링크 가능 목록, 물리 규격 점검 문구.
using LaPrint.Core.Barcode;
using LaPrint.Core.Data;
using LaPrint.Core.Model;
using Xunit;

namespace LaPrint.Core.Tests.Barcode;

public class BarcodeBindingTests
{
    private const string Udi = "(01)08806367058034(10)LOT1(17)290531(240)42-0401(21)3";

    private static BindingContext Ctx(IReadOnlyList<LabelObject> objects)
    {
        var f = new Fields { ["UDI_FULL"] = Udi, ["LOT"] = "LOT1", ["GTIN"] = "08806367058034" };
        // 간단한 치환기: {KEY} → 필드 값
        string Resolve(string s)
        {
            foreach (var (k, v) in f) s = s.Replace("{" + k + "}", v);
            return s;
        }
        return new BindingContext(f, objects, Resolve);
    }

    [Fact]
    public void ResolveData_Field()
    {
        var o = new BarcodeObject { Id = "b1", Source = "field", Binding = "LOT" };
        Assert.Equal("LOT1", BarcodeBinding.ResolveData(o, Ctx(Array.Empty<LabelObject>())));
        o.Binding = "";
        Assert.Equal(Udi, BarcodeBinding.ResolveData(o, Ctx(Array.Empty<LabelObject>())));
        o.Binding = "NOPE";
        Assert.Equal("", BarcodeBinding.ResolveData(o, Ctx(Array.Empty<LabelObject>())));
        o.Source = "";
        o.Binding = "GTIN";
        Assert.Equal("08806367058034", BarcodeBinding.ResolveData(o, Ctx(Array.Empty<LabelObject>())));
    }

    [Fact]
    public void ResolveData_Expression()
    {
        var o = new BarcodeObject { Id = "b1", Source = "expression", Expression = "(01){GTIN}(10){LOT}" };
        Assert.Equal("(01)08806367058034(10)LOT1", BarcodeBinding.ResolveData(o, Ctx(Array.Empty<LabelObject>())));
    }

    [Fact]
    public void ResolveData_ObjectLinksTextAndBarcode()
    {
        var t1 = new TextObject { Id = "t1", Text = "LOT {LOT}" };
        var b2 = new BarcodeObject { Id = "b2", Source = "field", Binding = "GTIN" };
        var b1 = new BarcodeObject { Id = "b1", Source = "object", LinkObjectId = "t1" };
        var img = new ImageObject { Id = "i1" };
        var objs = new LabelObject[] { t1, b2, b1, img };
        Assert.Equal("LOT LOT1", BarcodeBinding.ResolveData(b1, Ctx(objs)));
        b1.LinkObjectId = "b2";
        Assert.Equal("08806367058034", BarcodeBinding.ResolveData(b1, Ctx(objs)));
        b1.LinkObjectId = "i1";
        Assert.Equal("", BarcodeBinding.ResolveData(b1, Ctx(objs)));
        b1.LinkObjectId = "missing";
        Assert.Equal("", BarcodeBinding.ResolveData(b1, Ctx(objs)));
        b1.LinkObjectId = null;
        Assert.Equal("", BarcodeBinding.ResolveData(b1, Ctx(objs)));
    }

    [Fact]
    public void ResolveData_CycleIsBroken()
    {
        var a = new BarcodeObject { Id = "a", Source = "object", LinkObjectId = "b" };
        var b = new BarcodeObject { Id = "b", Source = "object", LinkObjectId = "c" };
        var c = new BarcodeObject { Id = "c", Source = "object", LinkObjectId = "a" };
        var objs = new LabelObject[] { a, b, c };
        Assert.Equal("", BarcodeBinding.ResolveData(a, Ctx(objs)));
        // 자기 자신을 가리켜도 빈 문자열
        var self = new BarcodeObject { Id = "s", Source = "object", LinkObjectId = "s" };
        Assert.Equal("", BarcodeBinding.ResolveData(self, Ctx(new LabelObject[] { self })));
        // 순환이 아닌 체인은 끝까지 따라간다
        c.Source = "field"; c.Binding = "LOT";
        Assert.Equal("LOT1", BarcodeBinding.ResolveData(a, Ctx(objs)));
    }

    [Fact]
    public void LinkableObjects_ExcludesSelfAndObjectSourcedBarcodes()
    {
        var objs = new LabelObject[]
        {
            new TextObject { Id = "t1", Text = "  Hello\nWorld  " },
            new TextObject { Id = "t2", Text = "" },
            new TextObject { Id = "t3", Text = "0123456789012345678901234567" },
            new TextObject { Id = "t4", Name = "이름", Text = "x" },
            new BarcodeObject { Id = "b1", Symbology = "gs1-128", Source = "field" },
            new BarcodeObject { Id = "b2", Source = "object", LinkObjectId = "t1" },
            new BarcodeObject { Id = "self", Source = "expression" },
            new ImageObject { Id = "i1" },
        };
        var list = BarcodeBinding.LinkableObjects(objs, "self");
        Assert.Equal(new[] { "t1", "t2", "t3", "t4", "b1" }, list.Select(x => x.Id).ToArray());
        Assert.Equal("Hello World", list[0].Label);
        Assert.Equal("(빈 텍스트)", list[1].Label);
        Assert.Equal("012345678901234567890123…", list[2].Label);
        Assert.Equal("이름", list[3].Label);
        Assert.Equal("GS1-128", list[4].Label);
        Assert.Equal("image", BarcodeBinding.ObjectLabel(objs[7]));
    }

    [Fact]
    public void CheckPhysical_DataMatrixTooSmallWarns()
    {
        var probe = BarcodeEncoder.Encode("gs1datamatrix", Udi);
        Assert.Null(probe.Error);
        // 모듈 크기가 0.236mm 가 되도록 영역을 잡는다 (ZXing 은 이 UDI 를 22×22 로 부호화 → 5.2mm)
        var w = Math.Round(0.236 * probe.ModulesW, 1);
        var o = new BarcodeObject { Id = "b", Symbology = "gs1datamatrix", W = w, H = w };
        var issues = BarcodeBinding.CheckPhysical(o, Udi, 300);
        var x = Assert.Single(issues, i => i.Code == "BC_XDIM");
        Assert.Equal("warn", x.Level);
        Assert.StartsWith("바코드 모듈 크기가", x.Msg);
        var xDim = (w / probe.ModulesW).ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal($"바코드 모듈 크기가 {xDim}mm로 작습니다. GS1 권장 최소 0.254mm — 영역을 키우거나 데이터를 줄이세요.", x.Msg);
        Assert.DoesNotContain(issues, i => i.Code == "BC_HEIGHT");

        // 12×12mm 는 0.25mm 이상이라 문제 없음
        var big = new BarcodeObject { Id = "b", Symbology = "gs1datamatrix", W = 12, H = 12 };
        Assert.Empty(BarcodeBinding.CheckPhysical(big, Udi, 300));

        // 0.15mm 미만이면 error
        var tiny = new BarcodeObject { Id = "b", Symbology = "gs1datamatrix", W = 2, H = 2 };
        var e = Assert.Single(BarcodeBinding.CheckPhysical(tiny, Udi, 300), i => i.Code == "BC_XDIM");
        Assert.Equal("error", e.Level);
    }

    [Fact]
    public void CheckPhysical_Gs1128HeightWarns()
    {
        var o = new BarcodeObject { Id = "b", Symbology = "gs1-128", W = 50, H = 5 };
        var issues = BarcodeBinding.CheckPhysical(o, "(01)08806367058034(10)LOT1", 300);
        var h = Assert.Single(issues);
        Assert.Equal(("warn", "BC_HEIGHT"), (h.Level, h.Code));
        Assert.Equal("GS1-128 높이가 5.0mm입니다. 권장 최소 7.5mm (6.35mm 또는 폭의 15%).", h.Msg);

        // 넓지 않은 라벨은 6.35mm 기준
        var o2 = new BarcodeObject { Id = "b", Symbology = "gs1-128", W = 30, H = 6 };
        var h2 = Assert.Single(BarcodeBinding.CheckPhysical(o2, "(01)08806367058034(10)LOT1", 0), i => i.Code == "BC_HEIGHT");
        Assert.Equal("GS1-128 높이가 6.0mm입니다. 권장 최소 6.3mm (6.35mm 또는 폭의 15%).", h2.Msg);   // 6.35 는 이진수로 6.3499… → JS toFixed(1) 도 "6.3"
    }

    [Fact]
    public void CheckPhysical_NarrowBarsAndLowDpi()
    {
        // code128 "ABC-123" = 112 모듈 → 25mm 에서 0.223mm, 203dpi 에서 1.8px
        var o = new BarcodeObject { Id = "b", Symbology = "code128", W = 25, H = 10 };
        var issues = BarcodeBinding.CheckPhysical(o, "ABC-123", 203);
        Assert.Equal(new[] { "BC_XDIM", "BC_DPI" }, issues.Select(i => i.Code).ToArray());
        Assert.Equal("막대 폭이 0.223mm로 좁습니다. 권장 최소 0.25mm.", issues[0].Msg);
        Assert.Equal("203dpi에서 모듈이 1.8px입니다. 판독 안정성을 위해 해상도를 높이세요.", issues[1].Msg);
        Assert.All(issues, i => Assert.Equal("warn", i.Level));
    }

    [Fact]
    public void CheckPhysical_EmptyOrInvalidDataGivesNothing()
    {
        var o = new BarcodeObject { Id = "b", Symbology = "gs1-128", W = 50, H = 5 };
        Assert.Empty(BarcodeBinding.CheckPhysical(o, "", 300));
        Assert.Empty(BarcodeBinding.CheckPhysical(o, "  ", 300));
        Assert.Empty(BarcodeBinding.CheckPhysical(o, "한글", 300));
    }
}

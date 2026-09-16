// 서식 JSON — 내장 기본 서식 읽기, 정규화, 저장 → 읽기 왕복.
using System.Text.Json;
using LaPrint.Core.Model;
using Xunit;

namespace LaPrint.Core.Tests;

public class TemplateJsonTests
{
    private static string TinyPngDataUrl()
    {
        using var bmp = new SkiaSharp.SKBitmap(4, 3);
        bmp.Erase(SkiaSharp.SKColors.Red);
        using var img = SkiaSharp.SKImage.FromBitmap(bmp);
        using var data = img.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        return "data:image/png;base64," + Convert.ToBase64String(data.ToArray());
    }

    [Fact]
    public void Load_ManualImage_DecodesDataUrlIntoBitmap_AndKeepsDataUrlForSaving()
    {
        // '+이미지 파일' 로 넣은 그림은 dataUrl 만 저장된다 — 다시 읽을 때 비트맵이 없으면 라벨에서 소리 없이 사라진다
        var json = "{\"label\":{\"w\":50,\"h\":30},\"objects\":[{\"type\":\"image\",\"id\":\"logo\",\"x\":1,\"y\":1,\"w\":10,\"h\":10,\"dataUrl\":\"" + TinyPngDataUrl() + "\"}]}";
        var t = TemplateJson.Load(json);
        var im = Assert.IsType<ImageObject>(t.Objects[0]);
        Assert.NotNull(im.Bitmap);
        Assert.Equal((4, 3), (im.Bitmap!.Width, im.Bitmap.Height));
        Assert.False(string.IsNullOrEmpty(im.DataUrl));
        // 저장 → 다시 읽기 왕복에서도 살아남는다
        var again = TemplateJson.Load(TemplateJson.Save(t));
        Assert.NotNull(((ImageObject)again.Objects[0]).Bitmap);
    }

    [Fact]
    public void Load_SlotImage_DropsStaleBrowserDataUrl()
    {
        // 브라우저판은 슬롯의 dataUrl 을 행마다 바꿔 쓰는 캐시로 썼다 — 서식에 남은 다른 품목의 그림은 버려야 한다
        var json = "{\"label\":{\"w\":50,\"h\":30},\"objects\":[{\"type\":\"image\",\"id\":\"s\",\"x\":1,\"y\":1,\"w\":10,\"h\":10,\"sourceField\":\"IMG_STENT\",\"dataUrl\":\"" + TinyPngDataUrl() + "\"}]}";
        var t = TemplateJson.Load(json);
        var im = Assert.IsType<ImageObject>(t.Objects[0]);
        Assert.Null(im.DataUrl);
        Assert.Null(im.Bitmap);
        Assert.DoesNotContain("dataUrl\": \"data:", TemplateJson.Save(t));
    }

    [Fact]
    public void Load_BrokenDataUrl_LeavesBitmapNull_WithoutThrowing()
    {
        var json = "{\"label\":{\"w\":50,\"h\":30},\"objects\":[{\"type\":\"image\",\"id\":\"logo\",\"x\":1,\"y\":1,\"w\":10,\"h\":10,\"dataUrl\":\"data:image/png;base64,AAAA\"}]}";
        var im = Assert.IsType<ImageObject>(TemplateJson.Load(json).Objects[0]);
        Assert.Null(im.Bitmap);
        Assert.Equal("data:image/png;base64,AAAA", im.DataUrl);
    }

    [Fact]
    public void Default_HasA3SheetWithEmbeddedBackground()
    {
        var t = TemplateJson.Default();

        Assert.Equal(297, t.Label.W);
        Assert.Equal(420, t.Label.H);
        Assert.Equal("res:template_bg", t.Label.Bg);
        Assert.True(t.Label.BgInclude);
        Assert.Equal(105, t.Objects.Count);
        Assert.Equal(89, t.Objects.OfType<TextObject>().Count());
        Assert.Equal(9, t.Objects.OfType<BarcodeObject>().Count());
        Assert.Equal(7, t.Objects.OfType<ImageObject>().Count());
    }

    [Fact]
    public void Default_ObjectsAreNormalized()
    {
        var t = TemplateJson.Default();

        Assert.All(t.Objects, o => Assert.False(string.IsNullOrEmpty(o.Id)));
        Assert.All(t.Objects, o => Assert.True(o.Visible));
        Assert.All(t.Objects, o => Assert.False(o.Locked));
        Assert.All(t.Objects.OfType<BarcodeObject>(), b =>
        {
            Assert.Equal("gs1datamatrix", b.Symbology);
            Assert.Equal("field", b.Source);
            Assert.Contains(b.Binding, new[] { "UDI_FULL", "GTIN01" });
            Assert.Equal("module", b.FitMode);
            Assert.Equal("middle", b.VFit);
        });
        Assert.All(t.Objects.OfType<TextObject>(), x =>
        {
            Assert.Equal("#000000", x.Color);
            Assert.Equal("Arial", x.Font);
            Assert.True(x.SizePt > 0);
            Assert.Equal(100, x.HScale);
            Assert.Equal("top", x.VAlign);
            Assert.True(x.Wrap);
            Assert.True(x.Kerning);
        });
        Assert.All(t.Objects.OfType<ImageObject>(), im =>
        {
            Assert.Equal("contain", im.FitMode);
            Assert.Equal("middle", im.VFit);
            Assert.False(string.IsNullOrEmpty(im.Fit));
        });
    }

    [Fact]
    public void Load_BarcodeWithoutSymbology_GetsGs1DataMatrix()
    {
        const string json = """
            { "label": { "w": 100, "h": 50 },
              "objects": [ { "type": "barcode", "x": 1, "y": 2, "w": 10, "h": 10, "binding": "UDI_FULL" },
                           { "id": "t1", "type": "text", "x": 0, "y": 0, "w": 5, "h": 5, "text": "{ITEM}", "color": "#0aF" } ] }
            """;
        var t = TemplateJson.Load(json);

        var b = Assert.IsType<BarcodeObject>(t.Objects[0]);
        Assert.Equal("gs1datamatrix", b.Symbology);
        Assert.Equal("o1", b.Id);
        Assert.Equal("", t.Label.Bg);

        var x = Assert.IsType<TextObject>(t.Objects[1]);
        Assert.Equal("#00aaFF", x.Color);
        Assert.Equal("t1", x.Id);
    }

    [Fact]
    public void SaveThenLoad_PreservesIdsAndCoordinates()
    {
        var src = TemplateJson.Default();
        var json = TemplateJson.Save(src);
        var back = TemplateJson.Load(json);

        Assert.Equal(src.Objects.Count, back.Objects.Count);
        Assert.Equal(src.Label.W, back.Label.W);
        Assert.Equal(src.Label.H, back.Label.H);
        Assert.Equal("res:template_bg", back.Label.Bg);
        for (var i = 0; i < src.Objects.Count; i++)
        {
            var a = src.Objects[i];
            var b = back.Objects[i];
            Assert.Equal(a.GetType(), b.GetType());
            Assert.Equal(a.Id, b.Id);
            Assert.Equal(a.X, b.X);
            Assert.Equal(a.Y, b.Y);
            Assert.Equal(a.W, b.W);
            Assert.Equal(a.H, b.H);
        }
        Assert.Equal(TemplateJson.Save(back), json);
    }

    [Fact]
    public void Save_WritesCamelCaseTypeDiscriminatorAndUseTemplateBg()
    {
        var t = TemplateJson.Default();
        using var doc = JsonDocument.Parse(TemplateJson.Save(t));
        var root = doc.RootElement;

        Assert.True(root.GetProperty("useTemplateBg").GetBoolean());
        Assert.Equal("", root.GetProperty("label").GetProperty("bg").GetString());
        var first = root.GetProperty("objects")[0];
        Assert.Equal("tpl1", first.GetProperty("id").GetString());
        Assert.Equal("text", first.GetProperty("type").GetString());
        Assert.True(first.TryGetProperty("sizePt", out _));
        Assert.False(first.TryGetProperty("SizePt", out _));
        Assert.False(first.TryGetProperty("bitmap", out _));
    }

    [Fact]
    public void Golden_DefaultTemplate_MatchesEmbeddedResource()
    {
        var golden = Fixtures.Golden().RootElement.GetProperty("defaultTemplate");
        var t = TemplateJson.Default();

        Assert.Equal(golden.GetProperty("objects").GetArrayLength(), t.Objects.Count);
        Assert.Equal(golden.GetProperty("label").GetProperty("w").GetDouble(), t.Label.W);
        Assert.Equal(golden.GetProperty("label").GetProperty("h").GetDouble(), t.Label.H);
    }

    [Fact]
    public void LoadBackgroundPng_ReturnsPngBytes()
    {
        var png = TemplateJson.LoadBackgroundPng();
        Assert.True(png.Length > 1000);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png.Take(4).ToArray());
    }
}

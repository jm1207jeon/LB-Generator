// 슬롯 로딩 — 샘플 DB 3건에서 IMG_STENT/IMG_DELIVERY 슬롯이 그 행의 파일을 읽는다. @열 참조, 오류 문구, 캐시 건너뛰기.
using LaPrint.Core.Data;
using LaPrint.Core.Imaging;
using LaPrint.Core.Model;
using Xunit;

namespace LaPrint.Core.Tests.Imaging;

public class SlotLoaderTests
{
    /// <summary>기본 서식의 이미지 슬롯(tplimg*) 만 뽑는다.</summary>
    private static List<LabelObject> TemplateSlots()
        => TemplateJson.Default().Objects.Where(o => o.Id.StartsWith("tplimg", StringComparison.Ordinal)).ToList();

    [Theory]
    [InlineData("16-0401")]
    [InlineData("42-0401")]
    [InlineData("21-1801")]
    public async Task Default_Template_Slots_Load_Row_Files(string item)
    {
        var slots = TemplateSlots();
        Assert.Equal(7, slots.Count);
        var row = SampleDb.Row(item);
        var f = SampleDb.FieldsOf(item);
        var store = SampleDb.NewStore();

        var changed = await SlotLoader.LoadIntoAsync(slots, f, row, store, force: true, autoTransparent: true, tol: 30);
        Assert.True(changed);

        var stent = slots.OfType<ImageObject>().Where(o => o.SourceField == "IMG_STENT").ToList();
        var delivery = slots.OfType<ImageObject>().Where(o => o.SourceField == "IMG_DELIVERY").ToList();
        Assert.Equal(2, stent.Count);
        Assert.Single(delivery);
        foreach (var o in stent)
        {
            Assert.Equal(row["O"], o.FileName);
            Assert.Equal(f["IMG_STENT"], o.FileName);
            Assert.Equal("", o.Error);
            Assert.NotNull(o.Bitmap);
        }
        foreach (var o in delivery)
        {
            Assert.Equal(row["P"], o.FileName);
            Assert.Equal("", o.Error);
            Assert.NotNull(o.Bitmap);
        }
        // 제품명 그림(2줄) 도 동봉되어 있다
        foreach (var o in slots.OfType<ImageObject>().Where(o => o.SourceField == "IMG_NAME2"))
        {
            Assert.Equal(row["K"], o.FileName);
            Assert.True(o.Bitmap is not null, $"{item} IMG_NAME2: {o.Error}");
        }
    }

    [Fact]
    public async Task ThreeItems_GiveDifferentDeliveryBitmaps()
    {
        var store = SampleDb.NewStore();
        var slot = new ImageObject { Id = "d", SourceField = "IMG_DELIVERY", W = 20, H = 5 };
        var names = new List<string>();
        var sizes = new List<(int, int)>();
        foreach (var item in new[] { "16-0401", "42-0401", "21-1801" })
        {
            await SlotLoader.LoadIntoAsync(new[] { slot }, SampleDb.FieldsOf(item), SampleDb.Row(item), store, true, true, 30);
            Assert.NotNull(slot.Bitmap);
            names.Add(slot.FileName);
            sizes.Add((slot.Bitmap!.Width, slot.Bitmap.Height));
        }
        Assert.Equal(new[] { "E.png", "F.jpg", "D.png" }, names);
        Assert.Equal(3, names.Distinct().Count());
    }

    [Fact]
    public async Task ColumnReference_LoadsThatColumnsFile()
    {
        var row = SampleDb.Row("42-0401");
        var f = SampleDb.FieldsOf("42-0401");
        var slot = new ImageObject { Id = "c", SourceField = "@O" };
        var changed = await SlotLoader.LoadIntoAsync(new[] { slot }, f, row, SampleDb.NewStore(), false, true, 30);
        Assert.True(changed);
        Assert.Equal("BNHS.jpg", slot.FileName);
        Assert.Equal(row["O"], slot.FileName);
        Assert.NotNull(slot.Bitmap);
        Assert.Equal("", slot.Error);

        // 소문자 열 이름도 대문자로 맞춘다
        var lower = new ImageObject { Id = "c2", SourceField = "@p" };
        await SlotLoader.LoadIntoAsync(new[] { lower }, f, row, SampleDb.NewStore(), false, true, 30);
        Assert.Equal("F.jpg", lower.FileName);
        Assert.NotNull(lower.Bitmap);
    }

    [Fact]
    public async Task NotAnImageName_SetsError()
    {
        var f = new Fields { ["IMG_STENT"] = "그림파일 없음" };
        var slot = new ImageObject { Id = "x", SourceField = "IMG_STENT" };
        var changed = await SlotLoader.LoadIntoAsync(new[] { slot }, f, null, SampleDb.NewStore(), false, true, 30);
        Assert.True(changed);
        Assert.Null(slot.Bitmap);
        Assert.Equal("그림파일 없음", slot.FileName);
        Assert.Equal("파일명이 아닙니다: \"그림파일 없음\"", slot.Error);
    }

    [Fact]
    public async Task MissingFile_And_NoStore_SetErrors()
    {
        var f = new Fields { ["IMG_STENT"] = "nope.png" };
        var slot = new ImageObject { Id = "x", SourceField = "IMG_STENT" };
        await SlotLoader.LoadIntoAsync(new[] { slot }, f, null, SampleDb.NewStore(), false, true, 30);
        Assert.Null(slot.Bitmap);
        Assert.Equal("파일 없음: nope.png", slot.Error);

        var slot2 = new ImageObject { Id = "y", SourceField = "IMG_STENT" };
        await SlotLoader.LoadIntoAsync(new[] { slot2 }, f, null, null, false, true, 30);
        Assert.Null(slot2.Bitmap);
        Assert.Equal("이미지 폴더가 지정되지 않았습니다. 설정에서 지정하세요.", slot2.Error);
    }

    [Fact]
    public async Task EmptyName_ClearsBitmap_And_UnchangedIsSkipped()
    {
        var store = SampleDb.NewStore();
        var slot = new ImageObject { Id = "x", SourceField = "IMG_DELIVERY" };
        var f = SampleDb.FieldsOf("16-0401");
        Assert.True(await SlotLoader.LoadIntoAsync(new[] { slot }, f, SampleDb.Row("16-0401"), store, false, true, 30));
        Assert.NotNull(slot.Bitmap);
        // 같은 파일명 + 비트맵 있음 → 건너뛴다
        Assert.False(await SlotLoader.LoadIntoAsync(new[] { slot }, f, SampleDb.Row("16-0401"), store, false, true, 30));
        // force 면 다시 읽는다
        Assert.True(await SlotLoader.LoadIntoAsync(new[] { slot }, f, SampleDb.Row("16-0401"), store, true, true, 30));

        var empty = new Fields { ["IMG_DELIVERY"] = "" };
        Assert.True(await SlotLoader.LoadIntoAsync(new[] { slot }, empty, null, store, false, true, 30));
        Assert.Null(slot.Bitmap);
        Assert.Equal("", slot.FileName);
        Assert.Equal("", slot.Error);
        Assert.False(await SlotLoader.LoadIntoAsync(new[] { slot }, empty, null, store, false, true, 30));

        // 텍스트·바코드·소스 없는 이미지는 건드리지 않는다
        var others = new LabelObject[] { new TextObject { Id = "t" }, new BarcodeObject { Id = "b" }, new ImageObject { Id = "i" } };
        Assert.False(await SlotLoader.LoadIntoAsync(others, f, null, store, true, true, 30));
    }
}

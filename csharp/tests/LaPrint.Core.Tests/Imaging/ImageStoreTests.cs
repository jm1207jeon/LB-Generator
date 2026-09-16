// 이미지 폴더 — 대소문자 무시 매칭, 파일 없음 문구, 목록, 처리 캐시.
using LaPrint.Core.Imaging;
using Xunit;

namespace LaPrint.Core.Tests.Imaging;

public class ImageStoreTests : IDisposable
{
    private readonly string _dir;

    public ImageStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "laprint_imgstore_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        File.Copy(Fixtures.Path(Path.Combine("images", "F.jpg")), Path.Combine(_dir, "E.JPG"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* 무시 */ }
    }

    [Fact]
    public void Read_IsCaseInsensitive()
    {
        var store = new ImageStore(_dir);
        var exact = store.Read("E.JPG");
        var lower = store.Read("e.jpg");
        var mixed = store.Read("E.jpg");
        Assert.True(exact.Length > 0);
        Assert.Equal(exact, lower);
        Assert.Equal(exact, mixed);
    }

    [Fact]
    public void Read_Missing_Throws()
    {
        var store = new ImageStore(_dir);
        var e = Assert.Throws<FileNotFoundException>(() => store.Read("nope.png"));
        Assert.StartsWith("파일 없음: ", e.Message);
        Assert.Equal("파일 없음: nope.png", e.Message);
    }

    [Fact]
    public void Read_EmptyFolder_Throws()
    {
        var store = new ImageStore("");
        var e = Assert.Throws<InvalidOperationException>(() => store.Read("E.JPG"));
        Assert.Equal("이미지 폴더가 지정되지 않았습니다. 설정에서 지정하세요.", e.Message);
    }

    [Fact]
    public void ListImages_And_Invalidate()
    {
        var store = new ImageStore(_dir);
        Assert.Equal(new[] { "E.JPG" }, store.ListImages());
        File.WriteAllText(Path.Combine(_dir, "notes.txt"), "x");
        File.Copy(Fixtures.Path(Path.Combine("images", "D.png")), Path.Combine(_dir, "a.png"));
        Assert.Equal(new[] { "E.JPG", "a.png" }, store.ListImages());

        // 이름 목록 캐시는 Invalidate 뒤에 새 파일을 본다
        _ = store.Read("e.jpg");
        File.Copy(Fixtures.Path(Path.Combine("images", "D.png")), Path.Combine(_dir, "NEW.PNG"));
        store.Invalidate();
        Assert.True(store.Read("new.png").Length > 0);
    }

    [Fact]
    public void GetProcessed_CachesByKey()
    {
        var store = new ImageStore(_dir);
        var a = store.GetProcessed("E.JPG", true, 30);
        var b = store.GetProcessed("E.JPG", true, 30);
        var c = store.GetProcessed("E.JPG", false, 30);
        var d = store.GetProcessed("E.JPG", true, 40);
        Assert.Same(a, b);
        Assert.NotSame(a, c);
        Assert.NotSame(a, d);
        Assert.Equal(0, a.GetPixel(0, 0).Alpha);     // 자동 투명
        Assert.Equal(255, c.GetPixel(0, 0).Alpha);   // 끔
        store.Invalidate();
        Assert.NotSame(a, store.GetProcessed("E.JPG", true, 30));
    }

    [Fact]
    public void GetProcessed_Missing_ThrowsFileNotFound()
    {
        var store = new ImageStore(_dir);
        var e = Assert.Throws<FileNotFoundException>(() => store.GetProcessed("zzz.png", true, 30));
        Assert.Equal("파일 없음: zzz.png", e.Message);
    }
}

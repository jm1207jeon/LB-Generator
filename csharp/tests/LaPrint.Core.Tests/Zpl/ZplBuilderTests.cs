// ZPL 조립·mm→도트·전송 전 점검·진단 명령.
using LaPrint.Core.Model;
using LaPrint.Core.Zpl;
using Xunit;

namespace LaPrint.Core.Tests.Zpl;

public class ZplBuilderTests
{
    private static MonoBitmap Sample() => new(new byte[] { 0xFF, 0x00, 0x0F, 0xF0 }, 2, 16, 2);

    [Fact]
    public void Build_Default()
    {
        var zpl = ZplBuilder.Build(Sample(), new ZplOptions());
        var lines = zpl.Split('\n');
        Assert.Equal("^XA", lines[0]);
        Assert.Equal("^CI28", lines[1]);
        Assert.Equal("^PW16", lines[2]);
        Assert.Equal("^LL2", lines[3]);
        Assert.Equal("^LH0,0", lines[4]);
        Assert.Equal("^FO0,0^GFA,4,4,2,HF,0HF,^FS", lines[5]);
        Assert.Equal("^PQ1", lines[6]);
        Assert.Equal("^XZ", lines[7]);
        Assert.Equal(8, lines.Length);
        Assert.DoesNotContain("^POI", zpl);
        Assert.DoesNotContain("^MD", zpl);
        Assert.DoesNotContain("^PR", zpl);
        Assert.DoesNotContain("^MM", zpl);
    }

    [Fact]
    public void Build_Quantity3_Invert_AllOptions()
    {
        var o = new ZplOptions
        {
            Quantity = 3, Invert = true, Darkness = -5, Speed = 4, MediaMode = "T",
            HomeX = 10, HomeY = 20, LabelWidthDots = 812, LabelLenDots = 600, Compress = false,
        };
        var zpl = ZplBuilder.Build(Sample(), o);
        Assert.Equal(
            "^XA\n^CI28\n^PW812\n^LL600\n^LH10,20\n^POI\n^MD-5\n^PR4\n^MMT\n^FO0,0^GFA,4,4,2,FF000FF0^FS\n^PQ3\n^XZ",
            zpl);
    }

    [Fact]
    public void Build_QuantityAtLeast1()
    {
        Assert.Contains("\n^PQ1\n", ZplBuilder.Build(Sample(), new ZplOptions { Quantity = 0 }));
        Assert.Contains("\n^PQ1\n", ZplBuilder.Build(Sample(), new ZplOptions { Quantity = -3 }));
    }

    [Theory]
    [InlineData(25.4, 203, 203)]
    [InlineData(25.4, 300, 300)]
    [InlineData(100, 203, 799)]
    [InlineData(50, 300, 591)]
    [InlineData(0, 203, 0)]
    public void MmToDots_Rounds(double mm, int dpi, int expected)
        => Assert.Equal(expected, ZplBuilder.MmToDots(mm, dpi));

    [Fact]
    public void SanityCheck_WideLabel_Warns()
    {
        var label = new LabelSize { W = 300, H = 100 };
        int w = ZplBuilder.MmToDots(300, 203), h = ZplBuilder.MmToDots(100, 203);
        var issues = ZplBuilder.SanityCheck("^XA^XZ", w, h, 203, label);
        var warn = Assert.Single(issues, i => i.Level == "warn");
        Assert.Equal("라벨 폭이 300mm입니다. 일반 데스크톱 제브라 모델의 최대 인쇄 폭(약 104mm)을 넘습니다.", warn.Msg);
        // 2398 도트 > 1400 @203dpi → 산업용 안내
        Assert.Contains(issues, i => i.Level == "info" && i.Msg == "폭이 넓습니다. 산업용 모델(ZT 계열)인지 확인하세요.");
    }

    [Fact]
    public void SanityCheck_SmallLabel_NoIssues()
    {
        var label = new LabelSize { W = 100, H = 50 };
        Assert.Empty(ZplBuilder.SanityCheck("^XA^XZ", 799, 400, 203, label));
    }

    [Fact]
    public void SanityCheck_LargeZpl_Warns()
    {
        var label = new LabelSize { W = 100, H = 50 };
        var big = new string('0', 5 * 1048576);
        var issues = ZplBuilder.SanityCheck(big, 799, 400, 203, label);
        var warn = Assert.Single(issues);
        Assert.Equal("warn", warn.Level);
        Assert.Equal("ZPL 크기가 5.0MB입니다. 프린터 메모리를 넘거나 전송이 느릴 수 있습니다. 해상도를 낮추거나 라벨을 나누세요.", warn.Msg);
    }

    [Fact]
    public void Diagnostics_MatchBrowser()
    {
        Assert.Equal("~WC", ZplBuilder.PrintConfig());
        Assert.Equal("~JC", ZplBuilder.Calibrate());
        Assert.Equal(
            "^XA^CI28^FO40,40^A0N,40,40^FDLB Generator TEST^FS" +
            "^FO40,100^BXN,6,200^FD(01)08806367087911(10)TEST0001^FS" +
            "^FO40,320^A0N,28,28^FDZPL OK^FS^PQ1^XZ",
            ZplBuilder.TestLabel(203));
        Assert.Equal(ZplBuilder.TestLabel(203), ZplBuilder.TestLabel(300));
    }
}

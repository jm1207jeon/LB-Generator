// ^GFA 압축 — zpl_golden.json 의 gfaCompressed / gfaRaw 와 문자열 완전 일치.
using System.Text.Json;
using LaPrint.Core.Zpl;
using Xunit;

namespace LaPrint.Core.Tests.Zpl;

public class ZplCompressTests
{
    private static IEnumerable<(string Name, MonoBitmap Mono, string Compressed, string Raw)> GoldenCases()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Fixtures.Path("zpl_golden.json")));
        foreach (var c in doc.RootElement.EnumerateArray())
        {
            var name = c.GetProperty("name").GetString()!;
            int w = c.GetProperty("w").GetInt32();
            int h = c.GetProperty("h").GetInt32();
            int bpr = c.GetProperty("bytesPerRow").GetInt32();
            var rows = c.GetProperty("hex").EnumerateArray().Select(r => r.GetString()!).ToArray();
            Assert.Equal(h, rows.Length);
            var bytes = new byte[bpr * h];
            for (int y = 0; y < h; y++)
            {
                Assert.Equal(bpr * 2, rows[y].Length);
                Convert.FromHexString(rows[y]).CopyTo(bytes, y * bpr);
            }
            yield return (name, new MonoBitmap(bytes, bpr, w, h),
                c.GetProperty("gfaCompressed").GetString()!, c.GetProperty("gfaRaw").GetString()!);
        }
    }

    [Fact]
    public void Golden_Compressed()
    {
        int n = 0;
        foreach (var (name, mono, compressed, _) in GoldenCases())
        {
            Assert.True(compressed == ZplCompress.ToGfa(mono, compress: true), $"{name}: 압축 GFA 가 골든과 다릅니다");
            n++;
        }
        Assert.True(n >= 5);
    }

    [Fact]
    public void Golden_Raw()
    {
        foreach (var (name, mono, _, raw) in GoldenCases())
            Assert.True(raw == ZplCompress.ToGfa(mono, compress: false), $"{name}: 비압축 GFA 가 골든과 다릅니다");
    }

    [Fact]
    public void RepeatCodes_LongRuns()
    {
        // 한 줄 400 바이트 = 800 니블 F 뒤에 0 하나 → z(400)+z(400) 은 아니고 'z' 는 400 까지만이므로 확인만: 40 개 F → 'h' + 'F'
        var row = new byte[21];
        for (int i = 0; i < 20; i++) row[i] = 0xFF;   // 40 니블 F, 마지막 바이트 00
        var s = ZplCompress.CompressRows(row, 21, 1);
        Assert.Equal("hF,", s);

        // 21 니블 → 'gG' (20 + 1)
        var row2 = new byte[11];
        for (int i = 0; i < 10; i++) row2[i] = 0xFF;
        row2[10] = 0xF0;
        Assert.Equal("gGF,", ZplCompress.CompressRows(row2, 11, 1));
    }

    [Fact]
    public void SameRow_UsesColon_ButNotForFirstRow()
    {
        var bytes = new byte[] { 0x0F, 0x0F, 0x0F };
        Assert.Equal("0!::", ZplCompress.CompressRows(bytes, 1, 3));
    }
}

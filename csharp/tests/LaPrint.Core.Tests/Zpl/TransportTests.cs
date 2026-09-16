// 전송 경로 — TCP(로컬 리스너로 바이트 일치), 파일(쓰기·이어붙이기), 스풀러(비 Windows 는 PlatformNotSupported).
using System.Net;
using System.Net.Sockets;
using System.Text;
using LaPrint.Core.Zpl;
using Xunit;

namespace LaPrint.Core.Tests.Zpl;

public class TransportTests
{
    private const string Zpl = "^XA\n^CI28\n^FO0,0^GFA,2,2,1,!,^FS\n^PQ1\n^XZ 한글";

    [Fact]
    public async Task Tcp_SendsExactBytes()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var received = Task.Run(async () =>
            {
                using var client = await listener.AcceptTcpClientAsync();
                using var ms = new MemoryStream();
                await client.GetStream().CopyToAsync(ms);
                return ms.ToArray();
            });

            var t = new TcpTransport("127.0.0.1", port);
            Assert.Equal($"TCP 127.0.0.1:{port}", t.Name);
            await t.SendAsync(Zpl, CancellationToken.None);

            var bytes = await received.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(Encoding.UTF8.GetBytes(Zpl), bytes);
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public async Task Tcp_RefusedPort_Throws()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var t = new TcpTransport("127.0.0.1", port);
        await Assert.ThrowsAnyAsync<Exception>(() => t.SendAsync(Zpl, CancellationToken.None));
    }

    [Fact]
    public async Task File_WritesThenAppends()
    {
        var dir = Path.Combine(Path.GetTempPath(), "laprint-zpl-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "sub", "out.zpl");
        try
        {
            var t = new FileTransport(path);
            Assert.Equal($"파일 {path}", t.Name);
            await t.SendAsync(Zpl, CancellationToken.None);
            Assert.Equal(Encoding.UTF8.GetBytes(Zpl), await File.ReadAllBytesAsync(path));

            await t.SendAsync("^XA^XZ", CancellationToken.None);
            Assert.Equal(Zpl + "\n^XA^XZ", await File.ReadAllTextAsync(path, Encoding.UTF8));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

#pragma warning disable CA1416 // 플랫폼별 API — 비 Windows 에서 예외가 나는지 확인하는 테스트
    [Fact]
    public async Task RawSpooler_NotWindows_Throws()
    {
        if (OperatingSystem.IsWindows()) return;
        var t = new RawSpoolerTransport("ZDesigner ZD421");
        Assert.Equal("프린터 ZDesigner ZD421", t.Name);
        await Assert.ThrowsAsync<PlatformNotSupportedException>(() => t.SendAsync(Zpl, CancellationToken.None));
        Assert.Throws<PlatformNotSupportedException>(() => RawSpoolerTransport.InstalledPrinters());
    }
#pragma warning restore CA1416
}

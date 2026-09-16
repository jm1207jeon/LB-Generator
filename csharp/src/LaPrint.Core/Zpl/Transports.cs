// ZPL 전송 경로 — 네트워크(9100) · 파일 · Windows 스풀러(RAW, USB 포함).
using System.Runtime.Versioning;

namespace LaPrint.Core.Zpl;

/// <summary>ZPL 문자열을 프린터(또는 파일)로 보내는 경로.</summary>
public interface IZplTransport
{
    string Name { get; }
    Task SendAsync(string zpl, CancellationToken ct);
}

/// <summary>네트워크 프린터 (기본 포트 9100).</summary>
public sealed class TcpTransport : IZplTransport
{
    public TcpTransport(string host, int port = 9100)
    {
        Host = host ?? "";
        Port = port;
    }

    public string Host { get; }
    public int Port { get; }
    public string Name => $"TCP {Host}:{Port}";

    public Task SendAsync(string zpl, CancellationToken ct)
        => throw new NotImplementedException("TcpTransport.SendAsync — 아직 구현되지 않았습니다");
}

/// <summary>ZPL 을 파일로 저장 (프린터 없이 확인용).</summary>
public sealed class FileTransport : IZplTransport
{
    public FileTransport(string path)
    {
        Path = path ?? "";
    }

    public string Path { get; }
    public string Name => $"파일 {Path}";

    public Task SendAsync(string zpl, CancellationToken ct)
        => throw new NotImplementedException("FileTransport.SendAsync — 아직 구현되지 않았습니다");
}

/// <summary>winspool.drv OpenPrinter/StartDocPrinter(RAW)/WritePrinter — 설치된 Zebra 드라이버로 원시 ZPL 전송.</summary>
[SupportedOSPlatform("windows")]
public sealed class RawSpoolerTransport : IZplTransport
{
    public RawSpoolerTransport(string printerName)
    {
        PrinterName = printerName ?? "";
    }

    public string PrinterName { get; }
    public string Name => $"프린터 {PrinterName}";

    public Task SendAsync(string zpl, CancellationToken ct)
        => throw new NotImplementedException("RawSpoolerTransport.SendAsync — 아직 구현되지 않았습니다");

    /// <summary>Windows 에 설치된 프린터 이름 목록.</summary>
    public static IReadOnlyList<string> InstalledPrinters()
        => throw new NotImplementedException("RawSpoolerTransport.InstalledPrinters — 아직 구현되지 않았습니다");
}

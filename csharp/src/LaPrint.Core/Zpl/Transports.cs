// ZPL 전송 경로 — 네트워크(9100) · 파일 · Windows 스풀러(RAW, USB 포함).
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

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
    /// <summary>연결 제한 시간.</summary>
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    public TcpTransport(string host, int port = 9100)
    {
        Host = host ?? "";
        Port = port;
    }

    public string Host { get; }
    public int Port { get; }
    public string Name => $"TCP {Host}:{Port}";

    /// <summary>UTF-8 바이트를 그대로 보내고 연결을 닫는다. 연결은 5초 안에 되어야 한다.</summary>
    public async Task SendAsync(string zpl, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(Host)) throw new InvalidOperationException("프린터 주소가 비어 있습니다.");
        var data = Encoding.UTF8.GetBytes(zpl ?? "");
        using var client = new TcpClient();
        client.NoDelay = true;
        using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            connectCts.CancelAfter(ConnectTimeout);
            try
            {
                await client.ConnectAsync(Host, Port, connectCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException($"프린터에 연결하지 못했습니다 ({Host}:{Port}, {ConnectTimeout.TotalSeconds:0}초 초과).");
            }
        }
        using var stream = client.GetStream();
        await stream.WriteAsync(data, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
        // 보낸 것이 프린터에 다 닿은 뒤 닫힌다는 뜻을 분명히 한다
        try { client.Client.Shutdown(SocketShutdown.Send); } catch (SocketException) { }
    }
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

    /// <summary>파일이 이미 있으면 줄바꿈으로 이어 붙인다 (브라우저판이 여러 건을 '\n' 으로 잇는 것과 같다).</summary>
    public async Task SendAsync(string zpl, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(Path)) throw new InvalidOperationException("저장할 파일 경로가 비어 있습니다.");
        var dir = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var text = zpl ?? "";
        bool append = File.Exists(Path) && new FileInfo(Path).Length > 0;
        var enc = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        using var fs = new FileStream(Path, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read, 4096, useAsync: true);
        var bytes = enc.GetBytes(append ? "\n" + text : text);
        await fs.WriteAsync(bytes, ct).ConfigureAwait(false);
        await fs.FlushAsync(ct).ConfigureAwait(false);
    }
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

    /// <summary>RAW 데이터형으로 한 문서를 스풀러에 넣는다. Windows 가 아니면 PlatformNotSupportedException.</summary>
    public Task SendAsync(string zpl, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows 스풀러 전송은 Windows 에서만 쓸 수 있습니다.");
        if (string.IsNullOrWhiteSpace(PrinterName))
            throw new InvalidOperationException("프린터 이름이 비어 있습니다.");
        var data = Encoding.UTF8.GetBytes(zpl ?? "");
        return Task.Run(() => WriteRaw(PrinterName, data, ct), ct);
    }

    /// <summary>Windows 에 설치된 프린터 이름 목록 (로컬 + 연결된 네트워크 프린터).</summary>
    public static IReadOnlyList<string> InstalledPrinters()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("프린터 목록은 Windows 에서만 읽을 수 있습니다.");
        return EnumPrinterNames();
    }

    /* ---------------- winspool.drv ---------------- */

    private const uint PrinterEnumLocal = 0x00000002;
    private const uint PrinterEnumConnections = 0x00000004;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DocInfo1
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string pDocName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pOutputFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string pDatatype;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PrinterInfo4
    {
        public IntPtr pPrinterName;
        public IntPtr pServerName;
        public uint Attributes;
    }

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", EntryPoint = "StartDocPrinterW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint StartDocPrinter(IntPtr hPrinter, int level, ref DocInfo1 pDocInfo);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndDocPrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool StartPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool WritePrinter(IntPtr hPrinter, byte[] pBuf, int cbBuf, out int pcWritten);

    [DllImport("winspool.drv", EntryPoint = "EnumPrintersW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool EnumPrinters(uint flags, string? name, uint level, IntPtr pPrinterEnum, uint cbBuf, out uint pcbNeeded, out uint pcReturned);

    private static void WriteRaw(string printerName, byte[] data, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!OpenPrinter(printerName, out var h, IntPtr.Zero))
            throw new IOException($"프린터를 열지 못했습니다: {printerName} ({Marshal.GetLastWin32Error()})");
        try
        {
            var di = new DocInfo1 { pDocName = "LaPrint ZPL", pOutputFile = null, pDatatype = "RAW" };
            if (StartDocPrinter(h, 1, ref di) == 0)
                throw new IOException($"인쇄 문서를 시작하지 못했습니다 ({Marshal.GetLastWin32Error()})");
            try
            {
                if (!StartPagePrinter(h))
                    throw new IOException($"인쇄 페이지를 시작하지 못했습니다 ({Marshal.GetLastWin32Error()})");
                try
                {
                    // 스풀러가 한 번에 다 받지 못할 수 있어 쓰인 만큼 잘라 가며 보낸다
                    int sent = 0;
                    while (sent < data.Length)
                    {
                        ct.ThrowIfCancellationRequested();
                        var chunk = sent == 0 ? data : data[sent..];
                        if (!WritePrinter(h, chunk, chunk.Length, out int written))
                            throw new IOException($"프린터에 쓰지 못했습니다 ({Marshal.GetLastWin32Error()})");
                        if (written <= 0)
                            throw new IOException("프린터가 데이터를 받지 않습니다.");
                        sent += written;
                    }
                }
                finally { EndPagePrinter(h); }
            }
            finally { EndDocPrinter(h); }
        }
        finally { ClosePrinter(h); }
    }

    private static IReadOnlyList<string> EnumPrinterNames()
    {
        uint flags = PrinterEnumLocal | PrinterEnumConnections;
        EnumPrinters(flags, null, 4, IntPtr.Zero, 0, out uint needed, out _);
        if (needed == 0) return Array.Empty<string>();
        IntPtr buf = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!EnumPrinters(flags, null, 4, buf, needed, out _, out uint returned))
                throw new IOException($"프린터 목록을 읽지 못했습니다 ({Marshal.GetLastWin32Error()})");
            var names = new List<string>((int)returned);
            int size = Marshal.SizeOf<PrinterInfo4>();
            for (int i = 0; i < returned; i++)
            {
                var info = Marshal.PtrToStructure<PrinterInfo4>(buf + i * size);
                var name = Marshal.PtrToStringUni(info.pPrinterName);
                if (!string.IsNullOrEmpty(name)) names.Add(name);
            }
            return names;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }
}

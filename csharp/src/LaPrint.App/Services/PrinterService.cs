// ZEBRA 출력 서비스 — 현재 서식을 프린터 해상도의 흑백 비트맵으로 그려 ZPL(^GFA)로 만들고, 설정된 경로(스풀러·TCP·파일)로 보낸다
// (js/zpl.js fromEditor + app.js printZebra 의 전송부). 화면·PDF와 완전히 같은 그림이 찍힌다.
using System.IO;
using System.Text;
using System.Windows;
using LaPrint.Core.Model;
using LaPrint.Core.Render;
using LaPrint.Core.Storage;
using LaPrint.Core.Zpl;

namespace LaPrint.App.Services;

/// <summary>만들어진 ZPL 한 건 — 문자열과 도트 크기, 전송 바이트 수.</summary>
public sealed record ZplJob(string Zpl, int WidthDots, int HeightDots, int Dpi, long Bytes);

/// <summary>ZPL 생성·전송 경로 선택·진단 전송.</summary>
public static class PrinterService
{
    /// <summary>서식을 프린터 해상도로 그려 ZPL 로 만든다. UI 스레드에 매이지 않으므로 Task.Run 안에서 불러도 된다.</summary>
    public static ZplJob Build(LabelTemplate t, RenderContext ctx, PrinterSettings p, int quantity)
    {
        ArgumentNullException.ThrowIfNull(t);
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(p);
        var dpi = p.Dpi > 0 ? p.Dpi : 203;
        using var bmp = LabelRenderer.RenderToBitmap(t, ctx, dpi);
        var mono = Monochrome.Convert(bmp, p.Threshold, p.Dither);
        var zpl = ZplBuilder.Build(mono, new ZplOptions
        {
            Dpi = dpi,
            Darkness = p.Darkness,
            Speed = p.Speed,
            Quantity = Math.Max(1, quantity),
            MediaMode = string.IsNullOrEmpty(p.MediaMode) ? null : p.MediaMode,
            Invert = p.Invert,
            Threshold = p.Threshold,
            Dither = p.Dither,
        });
        return new ZplJob(zpl, mono.Width, mono.Height, dpi, Encoding.UTF8.GetByteCount(zpl));
    }

    /// <summary>전송 방법의 한국어 이름 (설정 › ZEBRA 프린터 콤보와 같은 문구).</summary>
    public static string MethodName(string? method) => method switch
    {
        "tcp" => "네트워크 프린터 (TCP 9100)",
        "file" => ".zpl 파일로 저장",
        _ => "Windows 프린터 (스풀러)",
    };

    /// <summary>확인 대화상자에 보일 전송 대상 요약.</summary>
    public static string TransportLabel(PrinterSettings p)
    {
        ArgumentNullException.ThrowIfNull(p);
        return p.Method switch
        {
            "tcp" => string.IsNullOrWhiteSpace(p.Host) ? "네트워크 (주소 미지정)" : $"TCP {p.Host}:{(p.Port > 0 ? p.Port : 9100)}",
            "file" => ".zpl 파일",
            _ => string.IsNullOrWhiteSpace(p.PrinterName) ? "프린터 (미지정)" : $"프린터 {p.PrinterName}",
        };
    }

    /// <summary>설정에 맞는 전송 경로. 파일 저장이면 SaveFileDialog 를 띄우며, 취소하면 null.</summary>
    public static IZplTransport? CreateTransport(PrinterSettings p, Window? owner, string suggestedFileName)
    {
        ArgumentNullException.ThrowIfNull(p);
        switch (p.Method)
        {
            case "tcp":
                if (string.IsNullOrWhiteSpace(p.Host))
                    throw new InvalidOperationException("프린터 주소가 비어 있습니다 - [⚙ 설정 › ZEBRA 프린터] 에서 지정하세요.");
                return new TcpTransport(p.Host.Trim(), p.Port > 0 ? p.Port : 9100);
            case "file":
            {
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "ZPL 파일로 저장",
                    FileName = suggestedFileName,
                    DefaultExt = ".zpl",
                    Filter = "ZPL 파일 (*.zpl)|*.zpl|모든 파일 (*.*)|*.*",
                };
                var ok = owner is not null ? dlg.ShowDialog(owner) : dlg.ShowDialog();
                if (ok != true) return null;
                // 새로 고른 파일은 비워 두고 시작한다 (FileTransport 는 이어 붙인다)
                try { if (File.Exists(dlg.FileName)) File.WriteAllText(dlg.FileName, ""); } catch (IOException) { }
                return new FileTransport(dlg.FileName);
            }
            default:
                if (string.IsNullOrWhiteSpace(p.PrinterName))
                    throw new InvalidOperationException("프린터가 지정되지 않았습니다 - [⚙ 설정 › ZEBRA 프린터] 에서 프린터를 고르세요.");
                return new RawSpoolerTransport(p.PrinterName);
        }
    }

    /// <summary>설치된 프린터 목록. 읽지 못하면 빈 목록 (로그만 남긴다).</summary>
    public static IReadOnlyList<string> InstalledPrinters()
    {
        try { return RawSpoolerTransport.InstalledPrinters(); }
        catch (Exception ex)
        {
            AppLog.Warn("프린터 목록을 읽지 못했습니다: " + ex.Message);
            return Array.Empty<string>();
        }
    }

    /// <summary>진단용 ZPL(테스트 라벨·설정 라벨·캘리브레이션)을 설정된 경로로 보낸다. 파일 저장이면 label.zpl 로 저장.</summary>
    public static async Task SendRawAsync(PrinterSettings p, string zpl, string label, Window? owner, CancellationToken ct)
    {
        var transport = CreateTransport(p, owner, label + ".zpl");
        if (transport is null) throw new OperationCanceledException("저장을 취소했습니다.");
        await transport.SendAsync(zpl, ct);
    }

    /// <summary>바이트 수를 읽기 쉬운 단위로 (ui.js fmtBytes).</summary>
    public static string FmtBytes(long n)
    {
        if (n <= 0) return "0 B";
        var u = new[] { "B", "KB", "MB", "GB" };
        var i = Math.Min(u.Length - 1, (int)Math.Floor(Math.Log(n) / Math.Log(1024)));
        var v = n / Math.Pow(1024, i);
        return (i == 0 ? v.ToString("0") : v.ToString("0.0")) + " " + u[i];
    }
}

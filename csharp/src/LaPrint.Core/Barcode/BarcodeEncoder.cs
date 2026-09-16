// 바코드 인코딩 — ZXing.Net 으로 모듈 행렬을 만든다. 그리기는 Render 가 맡는다.
using System.Text.RegularExpressions;
using ZXing;
using ZXing.Common;
using ZXing.Datamatrix;
using ZXing.Datamatrix.Encoder;
using ZXing.OneD;
using ZXing.QrCode;
using ZXing.QrCode.Internal;

namespace LaPrint.Core.Barcode;

/// <summary>인코딩 결과. Modules 는 [y,x], 1D 는 ModulesH = 1. 실패하면 Error 에 한국어 문구.</summary>
public sealed record BarcodeSymbol(bool[,]? Modules, int ModulesW, int ModulesH, string? Error, int QuietZone);

/// <summary>심볼로지별 인코더 (gs1datamatrix · gs1-128 · datamatrix · code128 · qrcode).</summary>
public static class BarcodeEncoder
{
    /// <summary>2D 심볼 주변 조용한 영역(모듈). 그리는 쪽이 확보한다.</summary>
    public const int QuietZone2D = 1;

    /// <summary>1D 심볼 좌우 조용한 영역(모듈).</summary>
    public const int QuietZone1D = 10;

    // Code128Writer 가 FNC1 로 해석하는 이스케이프 문자 — GS(0x1D)를 그대로 넣으면 제어문자로 부호화되어 진짜 FNC1 이 아니다
    private const char Code128Fnc1 = 'ñ';

    /// <summary>데이터를 모듈 행렬로 만든다. 실패해도 예외를 던지지 않고 Error 에 문구를 담는다.</summary>
    public static BarcodeSymbol Encode(string symbology, string data, bool humanReadable = false)
    {
        var sym = Symbologies.ById(symbology);
        var quiet = sym.Is2D ? QuietZone2D : QuietZone1D;
        var text = data ?? "";
        try
        {
            BitMatrix m;
            switch (sym.Id)
            {
                case "gs1datamatrix":
                    // 선두 FNC1(]d2)과 GS→FNC1 변환은 DATA_MATRIX_COMPACT(MinimalEncoder) 경로에서만 이루어진다
                    m = new DataMatrixWriter().encode(Gs1.ToFnc1Stream(text), BarcodeFormat.DATA_MATRIX, 0, 0,
                        new Dictionary<EncodeHintType, object>
                        {
                            [EncodeHintType.GS1_FORMAT] = true,
                            [EncodeHintType.DATA_MATRIX_COMPACT] = true,
                            [EncodeHintType.MARGIN] = 0,
                            [EncodeHintType.DATA_MATRIX_SHAPE] = SymbolShapeHint.FORCE_SQUARE,
                        });
                    break;
                case "datamatrix":
                    m = new DataMatrixWriter().encode(text, BarcodeFormat.DATA_MATRIX, 0, 0,
                        new Dictionary<EncodeHintType, object>
                        {
                            [EncodeHintType.MARGIN] = 0,
                            [EncodeHintType.DATA_MATRIX_SHAPE] = SymbolShapeHint.FORCE_SQUARE,
                        });
                    break;
                case "gs1-128":
                    m = new Code128Writer().encode(Gs1.ToFnc1Stream(text).Replace(Gs1.GS, Code128Fnc1), BarcodeFormat.CODE_128, 0, 0,
                        new Dictionary<EncodeHintType, object>
                        {
                            [EncodeHintType.GS1_FORMAT] = true,
                            [EncodeHintType.MARGIN] = 0,
                        });
                    break;
                case "code128":
                    m = new Code128Writer().encode(text, BarcodeFormat.CODE_128, 0, 0,
                        new Dictionary<EncodeHintType, object> { [EncodeHintType.MARGIN] = 0 });
                    break;
                case "qrcode":
                    m = new QRCodeWriter().encode(text, BarcodeFormat.QR_CODE, 0, 0,
                        new Dictionary<EncodeHintType, object>
                        {
                            [EncodeHintType.ERROR_CORRECTION] = ErrorCorrectionLevel.M,
                            [EncodeHintType.MARGIN] = 0,
                        });
                    break;
                default:
                    return new BarcodeSymbol(null, 0, 0, HumanizeError("unknown encoder: " + symbology), quiet);
            }
            return ToSymbol(m, sym.Is2D, quiet);
        }
        catch (Exception e) when (e is WriterException or ArgumentException or IndexOutOfRangeException or InvalidOperationException)
        {
            return new BarcodeSymbol(null, 0, 0, HumanizeError(e.Message), quiet);
        }
    }

    // BitMatrix(x,y) → bool[y,x]. 1D 는 첫 행만 취한다 (막대 높이는 그리는 쪽이 정한다)
    private static BarcodeSymbol ToSymbol(BitMatrix m, bool is2D, int quiet)
    {
        var w = m.Width;
        var h = is2D ? m.Height : 1;
        var modules = new bool[h, w];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                modules[y, x] = m[x, y];
        return new BarcodeSymbol(modules, w, h, null, quiet);
    }

    private static readonly (Regex Re, Func<Match, string> Fn)[] ErrorMap =
    {
        (new Regex(@"GS1valueTooShort.*AI (\d+)"), g => $"AI ({g.Groups[1].Value}) 값이 너무 짧습니다."),
        (new Regex(@"GS1valueTooLong.*AI (\d+)"), g => $"AI ({g.Groups[1].Value}) 값이 너무 깁니다."),
        (new Regex(@"GS1aiMissingOpenParen"), _ => "GS1 데이터는 (01) 같은 괄호 표기로 시작해야 합니다."),
        (new Regex(@"GS1badAI|unknownAI", RegexOptions.IgnoreCase), _ => "알 수 없는 응용식별자(AI)입니다."),
        (new Regex(@"badCharacter|characterNotAllowed|Bad character|non-printable|outside iso-8859-1", RegexOptions.IgnoreCase),
            _ => "이 심볼로지가 지원하지 않는 문자가 포함되어 있습니다."),
        (new Regex(@"emptyText|textIsEmpty|Found empty contents", RegexOptions.IgnoreCase), _ => "바코드 데이터가 비어 있습니다."),
        (new Regex(@"unknown encoder|bcid", RegexOptions.IgnoreCase), _ => "지원하지 않는 바코드 종류입니다."),
        (new Regex(@"Contents length|too long|too big|Can't find a symbol arrangement", RegexOptions.IgnoreCase), _ => "데이터가 너무 깁니다."),
    };

    private static readonly Regex BwippPrefix = new(@"^bwipp\.\w+#\d+:\s*");

    /// <summary>ZXing 오류 메시지를 사용자용 한국어 문구로.</summary>
    public static string HumanizeError(string raw)
    {
        var m = raw ?? "";
        foreach (var (re, fn) in ErrorMap)
        {
            var g = re.Match(m);
            if (g.Success) return fn(g);
        }
        return BwippPrefix.Replace(m, "");
    }
}

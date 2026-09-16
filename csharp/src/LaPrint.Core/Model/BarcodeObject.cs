// 바코드 객체 — 데이터 바인딩 3종(field | expression | object)과 모듈 정수 배율 렌더링 옵션.
namespace LaPrint.Core.Model;

/// <summary>바코드 객체. 심볼로지와 데이터 바인딩, 배치 방식을 가진다.</summary>
public sealed class BarcodeObject : LabelObject
{
    public override string Type => "barcode";

    /// <summary>gs1datamatrix | gs1-128 | datamatrix | code128 | qrcode.</summary>
    public string Symbology { get; set; } = "gs1datamatrix";

    /// <summary>field | expression | object.</summary>
    public string Source { get; set; } = "field";

    /// <summary>source=field 일 때 필드 키.</summary>
    public string Binding { get; set; } = "UDI_FULL";

    /// <summary>source=expression 일 때 플레이스홀더 식.</summary>
    public string Expression { get; set; } = "";

    /// <summary>source=object 일 때 링크한 객체 id.</summary>
    public string? LinkObjectId { get; set; }

    public bool HumanReadable { get; set; }

    /// <summary>module(정수 모듈 배율) | stretch(영역 채움).</summary>
    public string FitMode { get; set; } = "module";

    public string Fit { get; set; } = "center";
    public string VFit { get; set; } = "middle";
    public double Rotation { get; set; }
}

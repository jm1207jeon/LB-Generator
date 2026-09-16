// 이미지 슬롯 객체 — 필드 키("IMG_STENT") 또는 열 직접 참조("@AB")로 행마다 그림을 바꿔 읽는다.
using System.Text.Json.Serialization;
using SkiaSharp;

namespace LaPrint.Core.Model;

/// <summary>이미지 슬롯 객체. 데이터 행마다 다른 그림 파일을 읽어 넣는다.</summary>
public sealed class ImageObject : LabelObject
{
    public override string Type => "image";

    /// <summary>"IMG_STENT" 같은 필드 키 | "@AB" 열 직접 참조 | "" 수동 파일.</summary>
    public string SourceField { get; set; } = "";

    /// <summary>마지막으로 읽은 파일명 (런타임).</summary>
    public string FileName { get; set; } = "";

    /// <summary>left | center | right.</summary>
    public string Fit { get; set; } = "center";

    /// <summary>top | middle | bottom.</summary>
    public string VFit { get; set; } = "middle";

    /// <summary>contain | cover | stretch.</summary>
    public string FitMode { get; set; } = "contain";

    /// <summary>현재 읽혀 있는 비트맵 (직렬화하지 않음).</summary>
    [JsonIgnore]
    public SKBitmap? Bitmap { get; set; }

    /// <summary>슬롯 읽기 오류 문구 (직렬화하지 않음).</summary>
    [JsonIgnore]
    public string Error { get; set; } = "";

    /// <summary>브라우저판 서식 호환용 data URL. 가져올 때 Bitmap 으로 바꾼 뒤 비운다.</summary>
    public string? DataUrl { get; set; }
}

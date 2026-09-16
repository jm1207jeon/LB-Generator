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

    /// <summary>수동 배치 그림의 data URL (서식에 함께 저장됨 · 브라우저판 호환). 슬롯 객체에서는 항상 null.</summary>
    public string? DataUrl { get; set; }

    /// <summary>DataUrl(수동 배치 그림 · 브라우저판 호환)만 있고 비트맵이 없으면 디코드한다. 실패하면 Bitmap 은 null 로 남는다.</summary>
    public void EnsureBitmap()
    {
        if (Bitmap is not null || string.IsNullOrEmpty(DataUrl)) return;
        try
        {
            var comma = DataUrl.IndexOf(',');
            if (comma < 0) return;
            var bytes = Convert.FromBase64String(DataUrl[(comma + 1)..]);
            Bitmap = SKBitmap.Decode(bytes);
        }
        catch
        {
            Bitmap = null;
        }
    }
}

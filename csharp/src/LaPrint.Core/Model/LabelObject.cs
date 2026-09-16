// 라벨 위 객체의 공통 속성 — JSON "type" 판별자(text|image|barcode)로 파생형을 고른다.
using System.Text.Json.Serialization;

namespace LaPrint.Core.Model;

/// <summary>라벨 위에 놓이는 객체의 공통 속성. 좌표·크기는 언제나 mm.</summary>
public abstract class LabelObject
{
    [JsonPropertyOrder(-3)]
    public string Id { get; set; } = "";

    /// <summary>JSON 판별자 — "text" | "image" | "barcode". 저장 시에만 쓰인다.</summary>
    [JsonPropertyOrder(-2)]
    public abstract string Type { get; }

    [JsonPropertyOrder(-1)]
    public string? Name { get; set; }

    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; }
    public double H { get; set; }

    public bool Visible { get; set; } = true;
    public bool Locked { get; set; } = false;
}

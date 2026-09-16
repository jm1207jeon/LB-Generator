// 텍스트 객체 — 플레이스홀더 원문과 조판 속성(자간·어간·행간·장평·커닝).
namespace LaPrint.Core.Model;

/// <summary>텍스트 객체. 플레이스홀더를 포함한 원문과 글꼴·조판 속성을 가진다.</summary>
public sealed class TextObject : LabelObject
{
    public override string Type => "text";

    /// <summary>플레이스홀더 포함 원문.</summary>
    public string Text { get; set; } = "";

    public string Font { get; set; } = "Arial";
    public double SizePt { get; set; } = 8;
    public bool Bold { get; set; }
    public bool Italic { get; set; }

    /// <summary>자간 pt (-5 ~ 30).</summary>
    public double LetterSpacing { get; set; }

    /// <summary>어간 pt (-10 ~ 60).</summary>
    public double WordSpacing { get; set; }

    /// <summary>커닝.</summary>
    public bool Kerning { get; set; } = true;

    /// <summary>행간 배수 (0.4 ~ 6).</summary>
    public double LineHeight { get; set; } = 1.15;

    /// <summary>장평 % (10 ~ 500).</summary>
    public double HScale { get; set; } = 100;

    /// <summary>left | center | right | justify.</summary>
    public string Align { get; set; } = "left";

    /// <summary>top | middle | bottom.</summary>
    public string VAlign { get; set; } = "top";

    public bool Wrap { get; set; } = true;
    public bool AutoShrink { get; set; } = false;
    public string Color { get; set; } = "#000000";
    public bool Clip { get; set; } = true;
}

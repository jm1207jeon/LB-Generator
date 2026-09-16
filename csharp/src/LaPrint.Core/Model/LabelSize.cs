// 라벨 한 장의 실제 크기(mm)와 배경 서식 — 브라우저판 state.label 과 같은 camelCase JSON.
namespace LaPrint.Core.Model;

/// <summary>라벨 한 장의 실제 크기(mm)와 배경 서식 이미지 지정.</summary>
public sealed class LabelSize
{
    /// <summary>내장 리소스(A3 원판 template_bg.png)를 배경으로 쓴다는 뜻의 Bg 값.</summary>
    public const string TemplateBg = "res:template_bg";

    public double W { get; set; }
    public double H { get; set; }

    /// <summary>"res:template_bg" | 파일 경로 | data URL(가져온 서식 호환) | "".</summary>
    public string Bg { get; set; } = "";

    /// <summary>출력물에 배경을 포함할지.</summary>
    public bool BgInclude { get; set; } = true;
}

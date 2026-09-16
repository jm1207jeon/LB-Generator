// 서식 — 라벨 크기와 객체 목록. 브라우저판 { name, label, objects } 와 같은 구조.
namespace LaPrint.Core.Model;

/// <summary>서식 한 벌: 이름 · 라벨 크기 · 객체 목록.</summary>
public sealed class LabelTemplate
{
    public string Name { get; set; } = "";
    public LabelSize Label { get; set; } = new();
    public List<LabelObject> Objects { get; set; } = new();
}

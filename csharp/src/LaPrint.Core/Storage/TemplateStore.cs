// 서식 저장소 — templates 폴더에 이름별 JSON.
using LaPrint.Core.Model;

namespace LaPrint.Core.Storage;

/// <summary>저장된 서식 목록·읽기·쓰기·삭제.</summary>
public sealed class TemplateStore
{
    public IReadOnlyList<(string Name, DateTime At)> List()
        => throw new NotImplementedException("TemplateStore.List — 아직 구현되지 않았습니다");

    public LabelTemplate? Load(string name)
        => throw new NotImplementedException("TemplateStore.Load — 아직 구현되지 않았습니다");

    public void Save(string name, LabelTemplate t)
        => throw new NotImplementedException("TemplateStore.Save — 아직 구현되지 않았습니다");

    public void Delete(string name)
        => throw new NotImplementedException("TemplateStore.Delete — 아직 구현되지 않았습니다");
}

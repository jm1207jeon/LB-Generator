// 출력 파일명 — 패턴 치환 후 금지문자 → '-', 120자 제한, .pdf.
using LaPrint.Core.Data;
using LaPrint.Core.Model;

namespace LaPrint.Core.Export;

/// <summary>출력 파일명 만들기 (exporter.js buildFileName).</summary>
public static class FileNaming
{
    public static string Build(string pattern, Fields f, LabelSize label, DbRow? row)
        => throw new NotImplementedException("FileNaming.Build — 아직 구현되지 않았습니다");
}

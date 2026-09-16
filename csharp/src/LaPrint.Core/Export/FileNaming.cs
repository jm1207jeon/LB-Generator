// 출력 파일명 — 패턴 치환 후 금지문자 → '-', 120자 제한, .pdf.
using System.Text.RegularExpressions;
using LaPrint.Core.Data;
using LaPrint.Core.Model;

namespace LaPrint.Core.Export;

/// <summary>출력 파일명 만들기 (exporter.js buildFileName).</summary>
public static class FileNaming
{
    /// <summary>패턴이 비었을 때의 기본 규칙.</summary>
    public const string DefaultPattern = "{ITEM}_{LOT}_{DATE}";

    private static readonly Regex ForbiddenRe = new(@"[\\/:*?""<>|]", RegexOptions.Compiled);
    private static readonly Regex ControlRe = new(@"[\x00-\x1F]", RegexOptions.Compiled);
    private static readonly Regex SpacesRe = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex EdgeRe = new(@"^[.\s]+|[.\s]+$", RegexOptions.Compiled);
    private static readonly Regex PdfRe = new(@"\.pdf$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>파일명 규칙 치환. 파일명에 못 쓰는 문자는 '-' 로, 제어문자는 제거, 120자 제한, 확장자 .pdf.</summary>
    public static string Build(string pattern, Fields f, LabelSize label, DbRow? row)
    {
        var pat = string.IsNullOrEmpty(pattern) ? DefaultPattern : pattern;
        var baseName = Placeholders.Resolve(pat, f ?? new Fields(), row);
        var name = ForbiddenRe.Replace(baseName, "-");
        name = ControlRe.Replace(name, "");
        name = SpacesRe.Replace(name, " ");
        name = EdgeRe.Replace(name, "");
        name = name.Trim();
        if (name.Length == 0) name = "label";
        if (name.Length > 120) name = name[..120];
        return PdfRe.IsMatch(name) ? name : name + ".pdf";
    }
}

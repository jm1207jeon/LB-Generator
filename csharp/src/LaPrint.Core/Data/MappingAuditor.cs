// 열 매칭 점검 — 표본 행에서 채움 비율·그림 파일명 비율을 세어 매칭이 의심스러운 필드를 찾는다 (data.js auditMapping).
//
// 라벨DB의 머리글 줄은 예전 양식이 남아 있어 실제 값과 맞지 않는 경우가 있다.
// 머리글을 믿지 말고 값을 보고 판단하라는 뜻에서, 이 결과를 화면에 그대로 보여 준다.
namespace LaPrint.Core.Data;

/// <summary>필드 하나의 매칭 점검 결과. Level 은 ok | warn | error | off.</summary>
public sealed record MappingAudit(string Key, string Label, string Group, string Col, int Filled, int Total, int Pct,
                                  int ImagePct, string Sample, string Level, string Note);

/// <summary>데이터 매칭 편집기의 점검표를 만든다.</summary>
public static class MappingAuditor
{
    /// <summary>행을 균등 간격으로 표본 추출해 FIELD_GROUPS 순서대로 점검한다. 행이 없으면 빈 목록.</summary>
    public static IReadOnlyList<MappingAudit> Audit(IReadOnlyList<DbRow> rows, FieldMap map, int sample = 400)
    {
        var outList = new List<MappingAudit>();
        rows ??= Array.Empty<DbRow>();
        var src = new List<DbRow>();
        var step = Math.Max(1, sample <= 0 ? rows.Count : rows.Count / sample);
        for (var i = 0; i < rows.Count && src.Count < sample; i += step) src.Add(rows[i]);
        var total = src.Count;
        if (total == 0) return outList;

        foreach (var (group, keys) in FieldMap.Groups)
        {
            foreach (var key in keys)
            {
                var col = map.Cols.GetValueOrDefault(key) ?? "";
                var label = FieldMap.Labels.TryGetValue(key, out var l) ? l : key;
                var isImg = Array.IndexOf(FieldMap.ImageFields, key) >= 0;
                if (col.Length == 0)
                {
                    outList.Add(new MappingAudit(key, label, group, "", 0, total, 0, 0, "", "off", "사용 안 함"));
                    continue;
                }
                int filled = 0, imgLike = 0;
                var sampleV = "";
                foreach (var r in src)
                {
                    var v = r.Get(col).Trim();
                    if (v.Length == 0) continue;
                    filled++;
                    if (sampleV.Length == 0) sampleV = v;
                    if (LabelIndex.LooksLikeImageName(v)) imgLike++;
                }
                var pct = RoundJs(filled / (double)total * 100);
                var imagePct = filled > 0 ? RoundJs(imgLike / (double)filled * 100) : 0;
                string level = "ok", note = "";
                if (filled == 0) { level = "error"; note = "이 열은 표본 전체가 비어 있습니다"; }
                else if (pct < 20) { level = "warn"; note = $"표본의 {pct}%에만 값이 있습니다"; }
                if (isImg && filled > 0 && imagePct < 50)
                {
                    level = "error";
                    note = $"그림 항목인데 값의 {100 - imagePct}%가 파일명이 아닙니다";
                }
                outList.Add(new MappingAudit(key, label, group, col, filled, total, pct, imagePct,
                    sampleV.Length > 40 ? sampleV[..40] : sampleV, level, note));
            }
        }
        return outList;
    }

    // JS Math.round — .5 는 올림
    private static int RoundJs(double v) => (int)Math.Floor(v + 0.5);
}

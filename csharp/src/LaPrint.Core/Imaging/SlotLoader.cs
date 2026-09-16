// 이미지 슬롯 읽기 — ★ 행마다 호출 (app.js loadSlotImagesInto · slotFileName).
// SourceField 가 "@" 로 시작하면 row[열], 아니면 f[SourceField]. 연속 출력의 beforeJob 은 반드시 이것을 force 로 부른다.
using LaPrint.Core.Data;
using LaPrint.Core.Model;

namespace LaPrint.Core.Imaging;

/// <summary>이미지 객체들의 비트맵을 현재 행 데이터로 채운다.</summary>
public static class SlotLoader
{
    /// <summary>그림 슬롯이 가리키는 파일명 (필드 또는 {@열} 직접 참조).</summary>
    public static string SlotFileName(string? sourceField, Fields? f, DbRow? row)
    {
        var sf = sourceField ?? "";
        if (sf.Length == 0) return "";
        if (sf.StartsWith('@'))
        {
            var col = sf[1..].ToUpperInvariant();
            return row is not null && row.TryGetValue(col, out var v) ? (v ?? "") : "";
        }
        return f is not null && f.TryGetValue(sf, out var fv) ? (fv ?? "") : "";
    }

    /// <summary>하나라도 바뀌었으면 true. 파일명이 아니면 Error = "파일명이 아닙니다: …".</summary>
    public static Task<bool> LoadIntoAsync(IEnumerable<LabelObject> objs, Fields f, DbRow? row, ImageStore? store, bool force, bool autoTransparent, int tol)
    {
        var list = objs?.ToList() ?? new List<LabelObject>();
        return Task.Run(() => LoadInto(list, f, row, store, force, autoTransparent, tol));
    }

    /// <summary>동기 버전. 하나라도 바뀌었으면 true.</summary>
    public static bool LoadInto(IEnumerable<LabelObject> objs, Fields f, DbRow? row, ImageStore? store, bool force, bool autoTransparent, int tol)
    {
        var changed = false;
        foreach (var lo in objs ?? Array.Empty<LabelObject>())
        {
            if (lo is not ImageObject o || string.IsNullOrEmpty(o.SourceField)) continue;
            var fileName = SlotFileName(o.SourceField, f, row);
            if (!force && o.FileName == fileName && (o.Bitmap is not null || fileName.Length == 0)) continue;
            o.FileName = fileName;
            o.Error = "";
            if (fileName.Length == 0) { o.Bitmap = null; changed = true; continue; }
            // 라벨DB 그림 칸에 '그림파일 없음' 같은 메모가 들어 있는 경우가 있다
            if (!LabelIndex.LooksLikeImageName(fileName))
            {
                o.Bitmap = null;
                o.Error = $"파일명이 아닙니다: \"{fileName}\"";
                changed = true;
                continue;
            }
            try
            {
                if (store is null) throw new InvalidOperationException("이미지 폴더가 지정되지 않았습니다. 설정에서 지정하세요.");
                o.Bitmap = store.GetProcessed(fileName, autoTransparent, tol);
            }
            catch (Exception e)
            {
                o.Bitmap = null;
                o.Error = e.Message;
            }
            changed = true;
        }
        return changed;
    }
}

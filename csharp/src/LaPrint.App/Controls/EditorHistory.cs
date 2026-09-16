// 실행취소 이력 — editor.js _snapshot/_restore/beginTx/endTx.
// 스냅샷은 서식 JSON 문자열(TemplateJson.Save). 이미지 비트맵은 JSON 에 없으므로 복원 때 id 로 되살린다.
using LaPrint.Core.Model;
using SkiaSharp;

namespace LaPrint.App.Controls;

/// <summary>스냅샷 기반 실행취소 이력. 트랜잭션(BeginTx/EndTx)으로 드래그 전체를 한 칸으로 묶는다.</summary>
internal sealed class EditorHistory
{
    /// <summary>이력 최대 칸 수.</summary>
    public const int Max = 200;

    private readonly record struct Entry(string Label, string Snap);

    private readonly List<Entry> _undo = new();
    private readonly List<Entry> _redo = new();
    private Entry? _tx;
    /// <summary>중첩 BeginTx 깊이 — 바깥 트랜잭션이 끝날 때만 기록한다.</summary>
    private int _depth;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string UndoLabel => _undo.Count > 0 ? _undo[^1].Label : "";
    public string RedoLabel => _redo.Count > 0 ? _redo[^1].Label : "";
    public bool InTx => _tx is not null;

    /// <summary>서식(객체 + 라벨 크기)의 스냅샷.</summary>
    public static string Snapshot(LabelTemplate t)
    {
        try { return TemplateJson.Save(t); }
        catch { return ""; }
    }

    /// <summary>트랜잭션 시작 — 이미 열려 있으면 깊이만 늘린다.</summary>
    public void BeginTx(string label, LabelTemplate t)
    {
        if (_tx is not null) { _depth++; return; }
        _tx = new Entry(label, Snapshot(t));
    }

    /// <summary>트랜잭션 끝. 바깥 트랜잭션이 끝나고 실제로 바뀌었을 때만 이력에 넣는다.</summary>
    public void EndTx(bool changed, LabelTemplate t)
    {
        if (_tx is null) return;
        if (_depth > 0) { _depth--; return; }
        var tx = _tx.Value;
        _tx = null;
        if (changed && tx.Snap.Length > 0 && tx.Snap != Snapshot(t))
        {
            _undo.Add(tx);
            if (_undo.Count > Max) _undo.RemoveAt(0);
            _redo.Clear();
        }
    }

    /// <summary>실행 취소 — 현재 상태를 다시 실행 칸에 넣고 이전 스냅샷을 복원한다. 복원했으면 true.</summary>
    public bool Undo(LabelTemplate t)
    {
        if (_undo.Count == 0) return false;
        var e = _undo[^1]; _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(new Entry(e.Label, Snapshot(t)));
        Restore(e.Snap, t);
        return true;
    }

    public bool Redo(LabelTemplate t)
    {
        if (_redo.Count == 0) return false;
        var e = _redo[^1]; _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(new Entry(e.Label, Snapshot(t)));
        Restore(e.Snap, t);
        return true;
    }

    public void Reset()
    {
        _undo.Clear();
        _redo.Clear();
        _tx = null;
        _depth = 0;
    }

    /// <summary>스냅샷을 서식에 제자리로 되돌린다 (목록 인스턴스를 바꾸지 않는다 — RenderContext.Objects 가 같은 목록을 본다).</summary>
    private static void Restore(string snap, LabelTemplate t)
    {
        LabelTemplate s;
        try { s = TemplateJson.Load(snap); }
        catch { return; }

        // 비트맵은 JSON 에 없다 — 같은 id·같은 소스의 이미지는 읽어 둔 비트맵을 그대로 잇는다
        var bitmaps = new Dictionary<string, ImageObject>();
        foreach (var o in t.Objects)
            if (o is ImageObject im && im.Bitmap is not null) bitmaps[im.Id] = im;

        t.Objects.Clear();
        foreach (var o in s.Objects)
        {
            if (o is ImageObject im)
            {
                if (bitmaps.TryGetValue(im.Id, out var prev) && prev.SourceField == im.SourceField && prev.FileName == im.FileName)
                {
                    im.Bitmap = prev.Bitmap;
                    im.Error = prev.Error;
                }
                else EnsureBitmap(im);
            }
            t.Objects.Add(o);
        }
        t.Label.W = s.Label.W;
        t.Label.H = s.Label.H;
        t.Label.Bg = s.Label.Bg;
        t.Label.BgInclude = s.Label.BgInclude;
    }

    /// <summary>DataUrl(브라우저판 호환 · 파일 직접 지정)만 있고 비트맵이 없으면 디코드한다.</summary>
    public static void EnsureBitmap(ImageObject im)
    {
        if (im.Bitmap is not null || string.IsNullOrEmpty(im.DataUrl)) return;
        try
        {
            var comma = im.DataUrl.IndexOf(',');
            if (comma < 0) return;
            var bytes = Convert.FromBase64String(im.DataUrl[(comma + 1)..]);
            im.Bitmap = SKBitmap.Decode(bytes);
        }
        catch
        {
            im.Bitmap = null;
        }
    }

    /// <summary>객체 하나의 깊은 복사 (JSON 왕복). 비트맵은 공유한다.</summary>
    public static LabelObject Clone(LabelObject o)
    {
        var tmp = new LabelTemplate { Label = new LabelSize { W = 1, H = 1 }, Objects = { o } };
        var c = TemplateJson.Load(TemplateJson.Save(tmp)).Objects[0];
        if (o is ImageObject src && c is ImageObject dst)
        {
            dst.Bitmap = src.Bitmap;
            dst.Error = src.Error;
        }
        return c;
    }
}

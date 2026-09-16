// 이미지 폴더 — 정확한 이름 → 대소문자 무시 매칭, 처리된 비트맵 LRU 캐시 150 (settings.js readImage · app.js imgCache).
//
// 라벨DB에는 E.JPG / e.JPG / E.jpg 가 섞여 있고 Windows 공유 폴더는 대소문자를 구분하지 않지만,
// 리눅스 파일시스템은 구분한다. 폴더 이름 목록(소문자 → 실제 이름)을 캐시해 두고 찾는다.
using System.Text.RegularExpressions;
using SkiaSharp;

namespace LaPrint.Core.Imaging;

/// <summary>이미지 폴더(UNC 가능)에서 파일을 읽고 처리 결과를 캐시한다.</summary>
public sealed class ImageStore
{
    /// <summary>처리된 비트맵 캐시 상한.</summary>
    public const int CacheLimit = 150;

    private static readonly Regex ImageExt = new(@"\.(png|jpe?g|gif|bmp|webp|svg)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly object _gate = new();
    private Dictionary<string, string>? _nameMap;           // 소문자 이름 → 실제 이름
    private readonly Dictionary<string, LinkedListNode<(string Key, SKBitmap Bmp)>> _cache = new();
    private readonly LinkedList<(string Key, SKBitmap Bmp)> _lru = new();   // 앞이 가장 최근

    public ImageStore(string folder)
    {
        Folder = folder ?? "";
    }

    /// <summary>이미지 폴더 경로.</summary>
    public string Folder { get; }

    /// <summary>파일 바이트. 정확한 이름이 없으면 대소문자만 다른 파일을 쓴다. 없으면 "파일 없음: …" 예외.</summary>
    public byte[] Read(string fileName)
    {
        if (string.IsNullOrWhiteSpace(Folder))
            throw new InvalidOperationException("이미지 폴더가 지정되지 않았습니다. 설정에서 지정하세요.");
        var name = fileName ?? "";
        var exact = SafeCombine(name);
        if (exact is not null && File.Exists(exact)) return File.ReadAllBytes(exact);

        // 대소문자만 다른 파일이 있으면 그것을 쓴다
        var m = NameMap();
        if (m is not null && m.TryGetValue(name.ToLowerInvariant(), out var real) && real != name)
        {
            var path = SafeCombine(real);
            if (path is not null && File.Exists(path))
            {
                try { return File.ReadAllBytes(path); }
                catch (IOException) { /* 아래에서 실패로 처리 */ }
            }
        }
        throw new FileNotFoundException($"파일 없음: {fileName}", fileName);
    }

    /// <summary>읽고 처리한 비트맵 (LRU 150, 키 = 이름|허용치|자동투명). 같은 인스턴스를 돌려주므로 Dispose 하지 말 것.</summary>
    public SKBitmap GetProcessed(string fileName, bool autoTransparent, int tolerance)
    {
        var key = $"{fileName}|{tolerance}|{(autoTransparent ? "true" : "false")}";
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out var node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                return node.Value.Bmp;
            }
        }
        var bytes = Read(fileName);
        var bmp = ImageProcessor.Process(bytes, autoTransparent, tolerance);
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out var existing))
            {
                // 같은 파일을 동시에 읽었으면 먼저 들어간 것을 쓴다
                bmp.Dispose();
                _lru.Remove(existing);
                _lru.AddFirst(existing);
                return existing.Value.Bmp;
            }
            var node = _lru.AddFirst((key, bmp));
            _cache[key] = node;
            while (_cache.Count > CacheLimit)
            {
                var last = _lru.Last!;
                _lru.RemoveLast();
                _cache.Remove(last.Value.Key);      // 객체가 아직 쓰고 있을 수 있으니 Dispose 하지 않는다
            }
            return bmp;
        }
    }

    /// <summary>폴더 목록·비트맵 캐시를 비운다.</summary>
    public void Invalidate()
    {
        lock (_gate)
        {
            _nameMap = null;
            _cache.Clear();
            _lru.Clear();
        }
    }

    /// <summary>폴더의 그림 파일 이름 목록 (정렬, 최대 4000). 폴더를 읽을 수 없으면 빈 목록.</summary>
    public IReadOnlyList<string> ListImages()
    {
        const int limit = 4000;
        var list = new List<string>();
        try
        {
            if (string.IsNullOrWhiteSpace(Folder) || !Directory.Exists(Folder)) return list;
            foreach (var path in Directory.EnumerateFiles(Folder))
            {
                var name = Path.GetFileName(path);
                if (ImageExt.IsMatch(name)) list.Add(name);
                if (list.Count >= limit) break;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return list;
        }
        list.Sort(StringComparer.Ordinal);
        return list;
    }

    /* 소문자 이름 → 실제 이름. 같은 소문자 이름이 여럿이면 먼저 나온 것 */
    private Dictionary<string, string>? NameMap()
    {
        lock (_gate)
        {
            if (_nameMap is not null) return _nameMap;
            try
            {
                if (!Directory.Exists(Folder)) return null;
                var m = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var path in Directory.EnumerateFiles(Folder))
                {
                    var name = Path.GetFileName(path);
                    var k = name.ToLowerInvariant();
                    if (!m.ContainsKey(k)) m[k] = name;
                }
                _nameMap = m;
                return m;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }

    /* 폴더 밖을 가리키는 이름(경로 구분자 포함 등)은 찾지 않는다 */
    private string? SafeCombine(string name)
    {
        if (name.Length == 0) return null;
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
        if (name.Contains('/') || name.Contains('\\')) return null;
        try { return Path.Combine(Folder, name); }
        catch (ArgumentException) { return null; }
    }
}

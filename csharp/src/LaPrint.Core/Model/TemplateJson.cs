// 서식 JSON 읽기·쓰기 — 브라우저판 서식 파일과 호환(camelCase, "type" 판별자, normalizeObject 정규화).
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LaPrint.Core.Model;

/// <summary>서식 JSON 직렬화. 읽을 때 빠진 속성을 기본값으로 채우고, 쓸 때는 알려진 속성만 camelCase 로 쓴다.</summary>
public static class TemplateJson
{
    /// <summary>기본 서식(A3 원판) 내장 리소스 이름.</summary>
    public const string DefaultResource = "LaPrint.Core.Resources.default_template.json";

    /// <summary>A3 원판 배경 PNG 내장 리소스 이름.</summary>
    public const string BackgroundResource = "LaPrint.Core.Resources.template_bg.png";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        Converters = { new LabelObjectConverter() },
    };

    /// <summary>서식 JSON 문자열을 읽어 정규화된 서식으로 만든다.</summary>
    public static LabelTemplate Load(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("서식 JSON 이 비어 있습니다.", nameof(json));

        var dto = JsonSerializer.Deserialize<TemplateDto>(json, Options)
                  ?? throw new JsonException("서식 JSON 을 읽을 수 없습니다.");

        var t = new LabelTemplate
        {
            Name = dto.Name ?? "",
            Label = dto.Label ?? new LabelSize { W = 297, H = 420 },
        };
        if (string.IsNullOrEmpty(t.Label.Bg)) t.Label.Bg = "";
        if (dto.UseTemplateBg == true) t.Label.Bg = LabelSize.TemplateBg;

        var list = dto.Objects ?? new List<LabelObject?>();
        for (var i = 0; i < list.Count; i++)
        {
            var o = list[i];
            if (o is null) continue;
            Normalize(o, i);
            t.Objects.Add(o);
        }
        return t;
    }

    /// <summary>서식을 들여쓰기된 camelCase JSON 으로 쓴다. 내장 배경은 useTemplateBg 로 표시한다.</summary>
    public static string Save(LabelTemplate t)
    {
        ArgumentNullException.ThrowIfNull(t);
        var useTemplateBg = t.Label.Bg == LabelSize.TemplateBg;
        var dto = new TemplateDto
        {
            Name = t.Name ?? "",
            Label = new LabelSize
            {
                W = t.Label.W,
                H = t.Label.H,
                Bg = useTemplateBg ? "" : (t.Label.Bg ?? ""),
                BgInclude = t.Label.BgInclude,
            },
            UseTemplateBg = useTemplateBg ? true : null,
            Objects = new List<LabelObject?>(t.Objects),
        };
        return JsonSerializer.Serialize(dto, Options);
    }

    /// <summary>내장 기본 서식(A3 원판 297×420mm, 배경 포함)을 읽는다.</summary>
    public static LabelTemplate Default() => Load(ReadResourceText(DefaultResource));

    /// <summary>내장 A3 원판 배경 PNG 바이트를 돌려준다.</summary>
    public static byte[] LoadBackgroundPng() => ReadResourceBytes(BackgroundResource);

    /// <summary>객체 정규화 — 브라우저판 normalizeObject 와 같은 규칙으로 빠진 속성을 채운다.</summary>
    public static void Normalize(LabelObject o, int index)
    {
        ArgumentNullException.ThrowIfNull(o);
        if (string.IsNullOrEmpty(o.Id)) o.Id = "o" + (index + 1);

        switch (o)
        {
            case TextObject t:
                if (string.IsNullOrEmpty(t.Text)) t.Text = "";
                if (string.IsNullOrEmpty(t.Font)) t.Font = "Arial";
                if (!(t.SizePt > 0)) t.SizePt = 8;
                if (double.IsNaN(t.LetterSpacing)) t.LetterSpacing = 0;
                if (double.IsNaN(t.WordSpacing)) t.WordSpacing = 0;
                if (!(t.LineHeight > 0)) t.LineHeight = 1.15;
                if (double.IsNaN(t.HScale)) t.HScale = 100;
                if (string.IsNullOrEmpty(t.Align)) t.Align = "left";
                if (string.IsNullOrEmpty(t.VAlign)) t.VAlign = "top";
                t.Color = NormalizeColor(t.Color);
                break;

            case ImageObject im:
                if (string.IsNullOrEmpty(im.Fit)) im.Fit = "center";
                if (string.IsNullOrEmpty(im.VFit)) im.VFit = "middle";
                if (string.IsNullOrEmpty(im.FitMode)) im.FitMode = "contain";
                if (string.IsNullOrEmpty(im.SourceField)) im.SourceField = "";
                if (string.IsNullOrEmpty(im.FileName)) im.FileName = "";
                if (string.IsNullOrEmpty(im.Error)) im.Error = "";
                // 브라우저판은 슬롯의 dataUrl 을 행마다 바꿔 쓰는 캐시로 썼다 — 여기서는 슬롯 그림을 SlotLoader 가 읽으므로
                // 서식에 남은 옛 dataUrl 은 버린다 (안 버리면 다른 품목의 그림이 되살아나고 누락 경고가 묻힌다).
                if (im.SourceField.Length > 0) im.DataUrl = null;
                else if (string.IsNullOrEmpty(im.DataUrl)) im.DataUrl = null;
                else im.EnsureBitmap();          // 수동 배치 그림은 서식에 든 데이터가 곧 그림이다 — 읽자마자 비트맵으로
                break;

            case BarcodeObject b:
                if (string.IsNullOrEmpty(b.Symbology)) b.Symbology = "gs1datamatrix";
                if (string.IsNullOrEmpty(b.Source)) b.Source = "field";
                if (string.IsNullOrEmpty(b.Binding)) b.Binding = "UDI_FULL";
                if (string.IsNullOrEmpty(b.Expression)) b.Expression = "";
                if (string.IsNullOrEmpty(b.Fit)) b.Fit = "center";
                if (string.IsNullOrEmpty(b.VFit)) b.VFit = "middle";
                if (string.IsNullOrEmpty(b.FitMode)) b.FitMode = "module";
                break;
        }
    }

    /// <summary>"#000" 같은 3자리 hex 를 "#000000" 으로 넓힌다. 비어 있으면 검정.</summary>
    public static string NormalizeColor(string? color)
    {
        if (string.IsNullOrWhiteSpace(color)) return "#000000";
        var c = color.Trim();
        if (c.Length == 4 && c[0] == '#' && c.Skip(1).All(Uri.IsHexDigit))
            return new string(new[] { '#', c[1], c[1], c[2], c[2], c[3], c[3] });
        return c;
    }

    private static string ReadResourceText(string name)
    {
        using var s = OpenResource(name);
        using var r = new StreamReader(s, System.Text.Encoding.UTF8);
        return r.ReadToEnd();
    }

    private static byte[] ReadResourceBytes(string name)
    {
        using var s = OpenResource(name);
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    private static Stream OpenResource(string name)
        => typeof(TemplateJson).Assembly.GetManifestResourceStream(name)
           ?? throw new FileNotFoundException($"내장 리소스를 찾을 수 없습니다: {name}");

    /// <summary>파일 최상위 구조. 브라우저판의 _lb · at 같은 부가 속성은 무시한다.</summary>
    private sealed class TemplateDto
    {
        public string? Name { get; set; }
        public LabelSize? Label { get; set; }
        public bool? UseTemplateBg { get; set; }
        public List<LabelObject?>? Objects { get; set; }
    }

    /// <summary>"type" 속성으로 파생형을 고르는 변환기. 속성 순서에 관계없이 판별자를 찾는다.</summary>
    private sealed class LabelObjectConverter : JsonConverter<LabelObject>
    {
        public override LabelObject? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new JsonException("서식 객체는 JSON 객체여야 합니다.");

            var type = "";
            foreach (var p in root.EnumerateObject())
            {
                if (p.Name.Equals("type", StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String)
                {
                    type = p.Value.GetString() ?? "";
                    break;
                }
            }

            return type.Trim().ToLowerInvariant() switch
            {
                "text" => root.Deserialize<TextObject>(options),
                "image" => root.Deserialize<ImageObject>(options),
                "barcode" => root.Deserialize<BarcodeObject>(options),
                _ => throw new JsonException($"알 수 없는 객체 종류입니다: \"{type}\""),
            };
        }

        public override void Write(Utf8JsonWriter writer, LabelObject value, JsonSerializerOptions options)
            => JsonSerializer.Serialize(writer, value, value.GetType(), options);
    }
}

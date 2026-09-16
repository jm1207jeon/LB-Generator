// 출력 전 점검 — 데이터 검증 + 서식 검증(넘침·영역 밖·미해결 플레이스홀더·이미지 슬롯·바코드) 을 한 목록으로 (exporter.js preflight).
using LaPrint.Core.Barcode;
using LaPrint.Core.Data;
using LaPrint.Core.Imaging;
using LaPrint.Core.Model;
using LaPrint.Core.Render;
using LaPrint.Core.Typography;

namespace LaPrint.Core.Export;

/// <summary>점검 결과. All 을 수준별로 나눠 둔다.</summary>
public sealed record PreflightResult(IReadOnlyList<Issue> All, IReadOnlyList<Issue> Errors, IReadOnlyList<Issue> Warnings, IReadOnlyList<Issue> Infos);

/// <summary>출력 전 점검 (exporter.js preflight). Errors 가 있으면 출력을 막는다.</summary>
public static class Preflight
{
    /// <summary>라벨 1건이 출력 가능한 상태인지 전부 검사한다. 객체는 ctx.Objects(비어 있으면 t.Objects).</summary>
    public static PreflightResult Run(LabelTemplate t, RenderContext ctx, ValidationRules rules, double dpi)
    {
        ArgumentNullException.ThrowIfNull(t);
        ArgumentNullException.ThrowIfNull(ctx);
        rules ??= new ValidationRules();
        var objects = ctx.Objects is { Count: > 0 } ? ctx.Objects : t.Objects;
        var label = t.Label ?? new LabelSize();
        var fields = ctx.Fields ?? new Fields();
        var row = ctx.Row;
        Func<string, string> resolve = ctx.ResolveText ?? (s => Placeholders.Resolve(s, fields, row));

        var all = new List<Issue>();
        void Add(string level, string code, string msg, string? objId = null) => all.Add(new Issue(level, code, msg, null, objId));

        // 1) 데이터 자체
        foreach (var v in RecordValidator.Validate(fields, row, rules))
            all.Add(new Issue(v.Level, v.Code, v.Msg, v.Field));

        // 2) 객체별
        var LW = label.W;
        var LH = label.H;
        foreach (var o in objects)
        {
            if (o is null || !o.Visible) continue;
            var name = string.IsNullOrEmpty(o.Name) ? o.Id : o.Name;

            // 라벨 밖으로 나감
            if (rules.WarnOutOfBounds)
            {
                var outside = o.X < -0.05 || o.Y < -0.05 || o.X + o.W > LW + 0.05 || o.Y + o.H > LH + 0.05;
                if (outside) Add("warn", "OUT_OF_BOUNDS", $"\"{name}\" 객체가 라벨 영역을 벗어났습니다.", o.Id);
            }

            switch (o)
            {
                case TextObject tx:
                {
                    var raw = tx.Text ?? "";
                    var txt = resolve(raw);
                    if (rules.WarnUnresolved)
                    {
                        var left = Placeholders.Unresolved(raw, fields, row);
                        if (left.Count > 0)
                            Add("warn", "UNRESOLVED", $"\"{name}\" 의 {string.Join(", ", left)} 항목이 치환되지 않았습니다. 필드 이름을 확인하세요.", o.Id);
                    }
                    if (rules.WarnOverflow && txt.Trim().Length > 0)
                    {
                        var b = TextLayoutEngine.Bounds(tx, txt);
                        if (b.OverflowX || b.OverflowY)
                            Add("warn", "TEXT_OVERFLOW", $"\"{name}\" 텍스트가 영역을 넘칩니다. 영역을 키우거나 자동 축소를 켜세요.", o.Id);
                    }
                    break;
                }
                case ImageObject im:
                {
                    // 슬롯인데 그림이 아직 없다 (브라우저판의 dataUrl = 여기서는 비트맵)
                    var loaded = (ctx.ImageOf is not null ? ctx.ImageOf(im.Id) : null) ?? im.Bitmap;
                    if (!string.IsNullOrEmpty(im.SourceField) && loaded is null && string.IsNullOrEmpty(im.DataUrl))
                    {
                        var fn = SlotLoader.SlotFileName(im.SourceField, fields, row);
                        if (string.IsNullOrEmpty(fn))
                        {
                            if (rules.WarnMissingImage)
                                Add("warn", "IMG_NO_NAME", $"\"{name}\" 슬롯: 이 품목의 {im.SourceField} 파일명이 라벨DB에 없습니다.", o.Id);
                        }
                        else if (!LabelIndex.LooksLikeImageName(fn))
                        {
                            // 라벨DB 그림 칸에 메모가 들어 있는 경우 ('그림파일 없음', 'Coil 주문금지' …)
                            Add("warn", "IMG_NOT_FILE", $"\"{name}\" 슬롯: 라벨DB 값이 그림 파일명이 아닙니다 — \"{fn}\". DB를 확인하세요.", o.Id);
                        }
                        else
                        {
                            Add("warn", "IMG_MISSING", $"\"{name}\" 슬롯: 이미지 파일을 불러오지 못했습니다 — {fn}", o.Id);
                        }
                    }
                    break;
                }
                case BarcodeObject bc:
                {
                    var data = BarcodeBinding.ResolveData(bc, new BindingContext(fields, objects, resolve));
                    if (string.IsNullOrWhiteSpace(data))
                    {
                        Add("error", "BC_EMPTY", $"\"{Symbologies.ById(bc.Symbology).Name}\" 바코드의 데이터가 비어 있습니다.", o.Id);
                        continue;
                    }
                    foreach (var v in Gs1.ValidateData(bc.Symbology, data))
                        Add(v.Level, v.Code, $"바코드: {v.Msg}", o.Id);
                    // 실제 생성이 되는지 (인코더가 최종 판정)
                    var g = BarcodeEncoder.Encode(bc.Symbology, data, bc.HumanReadable);
                    if (g.Modules is null || g.Error is not null)
                        Add("error", "BC_FAIL", $"바코드를 만들 수 없습니다: {g.Error}", o.Id);
                    else
                        foreach (var v in BarcodeBinding.CheckPhysical(bc, data, dpi))
                            Add(v.Level, v.Code, $"바코드: {v.Msg}", o.Id);
                    break;
                }
            }
        }

        if (objects.Count == 0) Add("warn", "NO_OBJECTS", "라벨에 객체가 없습니다.");

        return new PreflightResult(
            all,
            all.Where(i => i.Level == "error").ToList(),
            all.Where(i => i.Level == "warn").ToList(),
            all.Where(i => i.Level == "info").ToList());
    }
}

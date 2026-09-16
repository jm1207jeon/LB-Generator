# LaPrint (C#) — 아키텍처 · 구현 계약

브라우저판(`/js/*.js`)을 **완전한 Windows C# 프로그램**으로 옮긴다.
UDInspect · LaVis 와 같은 패밀리 구조(WPF + Core 라이브러리 + xUnit)와 디자인 체계를 따른다.

```
csharp/
  LaPrint.sln
  src/LaPrint.Core/          net8.0        플랫폼 독립 엔진 — 리눅스 CI에서 테스트 가능
  src/LaPrint.App/           net8.0-windows  WPF 화면 (EnableWindowsTargeting → 리눅스에서 컴파일 검증)
  tests/LaPrint.Core.Tests/  net8.0        xUnit — JS판에서 뽑은 골든 기준값과 대조
  docs/                      이 문서 · 사용설명서
```

패키지 (LaVis 와 같은 계열로 고정)

| 용도 | 패키지 | 근거 |
|---|---|---|
| 라벨DB `.xls/.xlsx/.xlsm` | NPOI 2.8.0 | 실제 DB가 BIFF8 `.xls`. 12,199행 열기 2.07초 확인 |
| 렌더·조판·PDF | SkiaSharp 2.88.9 (+NativeAssets.Linux.NoDependencies) | 화면·PDF·ZPL 이 **렌더러 하나**를 지난다. PDF는 벡터 |
| 바코드 | ZXing.Net 0.16.11 (+Bindings.SkiaSharp 0.16.14) | GS1 DataMatrix · GS1-128 FNC1 왕복 검증 완료 |
| 커닝 | SkiaSharp.HarfBuzz 2.88.9 | 커닝 켬일 때만 HarfBuzz 셰이핑 |
| MVVM | CommunityToolkit.Mvvm 8.4.2 | App 전용 |
| 테스트 | xunit 2.9.3 | LaVis 와 동일 |

---

## 0. 지켜야 할 원칙

1. **JSON 호환** — 서식(`LabelTemplate`)·설정의 속성 이름은 브라우저판과 같은 camelCase 를 쓴다.
   `Resources/default_template.json` 이 그대로 읽혀야 하고, 사용자가 브라우저판에서 저장한 서식 파일을 가져올 수 있어야 한다.
2. **좌표계** — 객체 좌표·크기는 언제나 **mm**. 조판(줄바꿈·정렬) 계산은 mm 로 한 번만 하고, 그리기만 배율(px/mm)을 곱한다.
   그래서 화면·PDF·ZPL 의 레이아웃이 같다. (브라우저판 실측 오차 0.024 mm)
3. **행마다 그림을 다시 읽는다** — 연속 출력에서 `beforeJob` 은 치환기뿐 아니라 **이미지 슬롯도** 그 행의 데이터로 바꿔야 한다.
   (브라우저판에서 발견한 회수급 결함 — 회귀 테스트 필수)
4. **모드 오류 금지** — "이 라벨 1장" 과 "큐 N장" 은 언제나 **서로 다른 명령/버튼**. 상태에 따라 뜻이 바뀌는 버튼을 만들지 않는다.
5. **정크 품목번호 차단** — 품목번호가 비었거나 `0` 인 행은 색인에서 제외. 중복은 첫 행을 쓰고 알린다.
6. **의심스러우면 비운다** — 열 매칭이 확실하지 않으면 '사용 안 함'으로 둔다. 엉뚱한 값이 조용히 찍히는 것이 빈 것보다 나쁘다.
7. 사용자에게 보이는 모든 문구는 **한국어**, 브라우저판 문구를 그대로 옮긴다 (검증된 HFE 문구).

---

## 1. LaPrint.Core 계약

아래 시그니처는 구현 팀 사이의 **약속**이다. 이름·네임스페이스를 바꾸지 않는다.
알고리즘 세부는 괄호의 JS 원본을 그대로 따른다.

### 1.1 Model  (`LaPrint.Core.Model`)

```csharp
public sealed class LabelSize { double W; double H; string Bg = ""; bool BgInclude = true; }
// Bg: "res:template_bg" (내장 리소스) | 파일 경로 | data URL(가져온 서식 호환) | ""

public abstract class LabelObject {            // JSON: "type": "text"|"image"|"barcode"
  string Id; string? Name; double X, Y, W, H;  // mm
  bool Visible = true; bool Locked = false;
}
public sealed class TextObject : LabelObject {
  string Text = "";            // 플레이스홀더 포함 원문
  string Font = "Arial"; double SizePt = 8; bool Bold, Italic;
  double LetterSpacing;        // 자간 pt   (-5 ~ 30)
  double WordSpacing;          // 어간 pt   (-10 ~ 60)
  bool Kerning = true;         // 커닝
  double LineHeight = 1.15;    // 행간 배수 (0.4 ~ 6)
  double HScale = 100;         // 장평 %    (10 ~ 500)
  string Align = "left";       // left|center|right|justify
  string VAlign = "top";       // top|middle|bottom
  bool Wrap = true; bool AutoShrink = false; string Color = "#000000"; bool Clip = true;
}
public sealed class ImageObject : LabelObject {
  string SourceField = "";     // "IMG_STENT" 같은 필드 키 | "@AB" 열 직접 참조 | "" 수동 파일
  string FileName = "";        // 마지막으로 읽은 파일명 (런타임)
  string Fit = "center"; string VFit = "middle"; string FitMode = "contain"; // contain|cover|stretch
  [JsonIgnore] SKBitmap? Bitmap; [JsonIgnore] string Error = "";
  string? DataUrl;             // 브라우저판 서식 호환(가져오기 시 Bitmap 으로 변환 후 비움)
}
public sealed class BarcodeObject : LabelObject {
  string Symbology = "gs1datamatrix"; // gs1datamatrix|gs1-128|datamatrix|code128|qrcode
  string Source = "field";     // field|expression|object
  string Binding = "UDI_FULL"; string Expression = ""; string? LinkObjectId;
  bool HumanReadable; string FitMode = "module"; // module|stretch
  string Fit = "center"; string VFit = "middle"; double Rotation;
}
public sealed class LabelTemplate { string Name; LabelSize Label; List<LabelObject> Objects; }
public static class TemplateJson { static LabelTemplate Load(string json); static string Save(LabelTemplate t);
                                   static LabelTemplate Default(); }   // Resources/default_template.json (A3 원판)
```
객체 정규화(`normalizeObject`, js/app.js 60~100행)는 `TemplateJson.Load` 가 수행한다 — 빠진 속성은 위 기본값으로 채우고,
`"color": "#000"` 같은 3자리 hex 도 받는다. 기본 서식 JSON 의 최상위 `"useTemplateBg": true` 는 `Label.Bg = "res:template_bg"` 로 옮긴다
(내장 리소스 `Resources/template_bg.png`, A3 원판 297×420mm). 저장할 때는 알려진 속성만 camelCase 로 쓴다.

### 1.2 Data  (`LaPrint.Core.Data`)   ← js/data.js

```csharp
public enum DbProfileId { General, Bsc }
public sealed record DbProfile(DbProfileId Id, string Key /*"general"|"bsc"*/, string Name, string DefaultFile,
                               IReadOnlyDictionary<string,string> Map, string Desc);
public static class Profiles { IReadOnlyList<DbProfile> All; DbProfile Get(string key); }

public sealed class FieldMap {                 // 프로필 기본값 + 사용자 수정
  static IReadOnlyDictionary<string,string> DefaultCols;  // data.js DEFAULT_FIELD_COLS (UPN/CATALOG/STENT_TYPE/REF_JP 포함, 값 "")
  static IReadOnlyDictionary<string,string> BscCols;      // data.js BSC_FIELD_COLS
  static IReadOnlyDictionary<string,string> Labels;       // FIELD_LABELS
  static IReadOnlyList<(string Group, string[] Keys)> Groups;  // FIELD_GROUPS
  static string[] ImageFields;                             // IMAGE_FIELDS
  string Profile { get; }  string KeyCol { get; set; } = "H";
  IReadOnlyDictionary<string,string> Cols { get; }         // 현재 유효 매핑
  void SetProfile(string key); void Apply(IDictionary<string,string>? userMap); void Reset();
  Dictionary<string,string> Diff();                        // 프로필 기본값과 다른 것만
  IReadOnlyDictionary<string,string> BaseCols();
}

public sealed class DbRow : Dictionary<string,string> { }  // 열 문자("A".."BC") → 값(trim)
public sealed record LabelDb(List<DbRow> Rows, string Sheet, IReadOnlyList<(string Name,int Count)> Sheets,
                             DbRow? Header, int ColCount);
public static class LabelDbLoader {
  LabelDb Load(Stream s, string fileName, string keyCol);   // NPOI: .xls → HSSFWorkbook, .xlsx/.xlsm → XSSFWorkbook, .csv 도
  // 시트 선택: 키 열에 데이터가 가장 많은 시트, 이름 '라벨DB' 우선. 머리글 감지: 키 열 첫 값이 /product\s*number|품목\s*번호|품번/i
  // 셀 값은 DataFormatter 로 "표시 문자열" 그대로 (숫자 08806… 앞자리 0 보존). 날짜 셀은 ISO 가 아닌 표시 형식 유지.
}
public static class ColumnName { string FromIndex(int i); int ToIndex(string name); }   // 0→A, 26→AA

public sealed record SearchEntry(string Key, string Ref, string Name);
public sealed class LabelIndex {
  IReadOnlyDictionary<string,DbRow> ByRef; IReadOnlyList<(string Key,int Count)> Dups; int Skipped; int Total;
  static LabelIndex Build(IEnumerable<DbRow> rows, FieldMap map);   // isJunkKey · 먼저 나온 행 · productName 폴백
  IReadOnlyList<SearchEntry> Search(string query, int limit = 60); // 품목번호 → 규격 → 제품명 가중치 (data.js searchProducts)
  static bool IsJunkKey(string? k); static bool LooksLikeImageName(string? v); static string ProductName(DbRow r, FieldMap map);
}

public sealed record JobInputs(string Item, string Lot, string Sn, string Mfg /*ISO*/, int Months = 36,
                               bool ExpAuto = true, string Exp = "");
public sealed class Fields : Dictionary<string,string> { DateTime? MfgDate; DateTime? ExpDate; }
public static class FieldComputer {
  Fields Compute(DbRow? row, JobInputs inputs, FieldMap map);   // ITEM/LOT/SN + 모든 열 + MFG/MFG6/EXP/EXP6 + TODAY/NOW/DATE/TIME
  // UDI:  g(ai,v)= v==""? "" : "(" + ai + ")" + v
  //   GTIN01 = g(01,GTIN); UDI_L1 = GTIN01 + g(10,LOT); UDI_L2 = g(17,EXP6)+g(240,ITEM)+g(21,SN); UDI_FULL = L1+L2
  static DateTime Edate(DateTime d, int months);   // 엑셀 EDATE (말일 클램프). EXP = EDATE(MFG, months) - 1일
  static string FormatDate(DateTime? d, string pattern);  // YYYY YY MMM MM M DD D HH mm ss
}
public static class Placeholders {
  string Resolve(string text, Fields f, DbRow? row);   // {FIELD} {FIELD:fmt}(MFG/EXP/TODAY) {@AB} {FIELD|대체}
  IReadOnlyList<string> Unresolved(string text, Fields f, DbRow? row);
  IReadOnlyList<(string Key,string Token,string Label)> List(FieldMap map);
}
public sealed record Issue(string Level /*error|warn|info*/, string Code, string Msg, string? Field = null, string? ObjId = null);
public sealed class ValidationRules { bool RequireItem=true, RequireLot=true, RequireMfg=true, RequireSn=false,
  CheckGtin=true, CheckExpPast=true, CheckExpOrder=true, WarnMissingImage=true, WarnUnresolved=true, WarnOverflow=true, WarnOutOfBounds=true; }
public static class RecordValidator { IReadOnlyList<Issue> Validate(Fields f, DbRow? row, ValidationRules r); string? LotHint(string lot); }
public sealed record MappingAudit(string Key, string Label, string Group, string Col, int Filled, int Total, int Pct, int ImagePct, string Sample, string Level, string Note);
public static class MappingAuditor { IReadOnlyList<MappingAudit> Audit(IReadOnlyList<DbRow> rows, FieldMap map, int sample = 400); }
```

### 1.3 Barcode  (`LaPrint.Core.Barcode`)   ← js/barcode.js

```csharp
public sealed record SymbologyInfo(string Id, string Name, bool Is2D, bool Gs1, string Note);
public static class Symbologies { IReadOnlyList<SymbologyInfo> All; SymbologyInfo ById(string id); }
public static class Gs1 {
  bool GtinValid(string gtin); int GtinCheckDigit(string body);
  IReadOnlyDictionary<string, AiDef> AiTable;                        // '00','01','02','10','11'…'241'
  IReadOnlyList<(string Ai,string Value,AiDef? Def)> ParseAis(string text);   // "(01)…(10)…"
  IReadOnlyList<Issue> ValidateData(string symbology, string text);           // validateData 규칙 그대로
  string ToFnc1Stream(string bracketed);   // "(01)x(10)y(17)z" → "01x" + "10y" + GS + "17z" (가변길이 AI 뒤에만 GS, 마지막은 생략)
}
public sealed record BarcodeSymbol(bool[,]? Modules /*[y,x]*/, int ModulesW, int ModulesH, string? Error, int QuietZone);
public static class BarcodeEncoder {
  BarcodeSymbol Encode(string symbology, string data, bool humanReadable = false);
  // ZXing.Net: gs1datamatrix → DataMatrixWriter + GS1_FORMAT hint + ToFnc1Stream
  //            gs1-128 → Code128Writer + GS1_FORMAT hint (선두 FNC1) ; code128 ; qrcode(Error correction M) ; datamatrix
  // 1D 는 ModulesH = 1 (막대 높이는 그리는 쪽이 정함). 오류 메시지는 HumanizeError 로 한국어.
  string HumanizeError(string raw);
}
public static class BarcodeBinding {
  string ResolveData(BarcodeObject o, BindingContext ctx, HashSet<string>? seen = null);   // 순환 참조 차단
  IReadOnlyList<(string Id,string Label)> LinkableObjects(IEnumerable<LabelObject> objs, string selfId);
  IReadOnlyList<Issue> CheckPhysical(BarcodeObject o, string data, double dpi);   // X-dim ≥0.254mm, GS1-128 높이 ≥ max(6.35, 0.15W), 정수 도트
}
public sealed record BindingContext(Fields Fields, IReadOnlyList<LabelObject> Objects, Func<string,string> ResolveText);
```

### 1.4 Typography  (`LaPrint.Core.Typography`)   ← js/text.js  (가장 정밀하게 옮길 것)

```csharp
public static class FontProvider {
  SKTypeface Get(string family, bool bold, bool italic);   // 폴백: Windows(맑은 고딕/Arial) · Linux(Liberation Sans ≈ Arial 메트릭, WenQuanYi/IPA CJK)
  bool IsAvailable(string family); IReadOnlyList<(string Id,string Name)> Choices;  // text.js FONTS
}
public sealed record LayoutLine(string Text, double XMm, double WMm, double VisWMm, double SpaceExtra);
public sealed record TextLayout(IReadOnlyList<LayoutLine> Lines, double SizePt, double FontMm, double LineHMm, double TotalHMm,
                                double StartYMm, double HScale, bool OverflowX, bool OverflowY, bool Shrunk, double AscMm, double DescMm);
public static class TextLayoutEngine {
  const double Pt2Mm = 25.4/72; const double Ref = 20;   // 측정 기준 px/mm
  TextLayout Layout(TextObject o, string text);           // 캐시. tokenize/foldTokens/NO_LINE_START/CJK 정규식/autoShrink 이분탐색/정렬 모두 JS 와 동일
  TextLayout Draw(SKCanvas c, TextObject o, string text, double scale, double ox, double oy);  // translate(X,Y); scale(hs,1); u = xMm/hs*scale
  (double WMm, double HMm, bool OverflowX, bool OverflowY, double SizePt, bool Shrunk, int Lines) Bounds(TextObject o, string text);
  void Invalidate();
}
```
측정 규칙 — `잉크 폭(mm) = Σ글리프 advance + 자간×(글자수−1) + 어간×(공백수)` 를 REF 배율로 재서 `/REF`.
브라우저의 "후행 자간" 보정은 필요 없다(직접 합산하므로). 그리기는 글리프 단위로 `advance + 자간` 만큼 전진(자간·어간·장평 모두 정확).
커닝 켬: `SKShaper`(HarfBuzz) 로 advance 를 얻고, 끔: `SKPaint.GetGlyphWidths`. 수직 메트릭은 `SKFontMetrics`(Ascent/Descent 절댓값).

### 1.5 Render  (`LaPrint.Core.Render`)   ← js/editor.js drawObject · _fitRect, js/barcode.js draw

```csharp
public sealed class RenderContext {
  Fields Fields; DbRow? Row; Func<string,string> ResolveText; IReadOnlyList<LabelObject> Objects;
  bool ForExport; bool IncludeBg = true; SKBitmap? Background; double Dpi = 300;
  Func<string,SKBitmap?> ImageOf;   // ImageObject → 비트맵 (슬롯 캐시)
}
public static class LabelRenderer {
  void Render(SKCanvas c, LabelTemplate t, RenderContext ctx, double scale /*px per mm*/, double ox, double oy);
  RenderResult DrawObject(SKCanvas c, LabelObject o, RenderContext ctx, double scale, double ox, double oy);
  static SKRect FitRect(double nw, double nh, double X, double Y, double W, double H, string hAlign, string vAlign, string fitMode);
  // 바코드: module 모드 = 정수 모듈 배율(2D: 정사각 유지 / 1D: 가로 정수모듈·세로 영역 채움), stretch = 영역 채움. 안티앨리어싱 끔.
  SKBitmap RenderToBitmap(LabelTemplate t, RenderContext ctx, double dpi);   // 흰 배경, 클립. ZPL·미리보기용
}
```
편집 전용 장식(선택 테두리·고스트 텍스트·눈금·스냅선·링크 오버레이)은 **App** 이 위에 덧그린다. Core 는 `ForExport=false` 일 때도 장식을 그리지 않는다.

### 1.6 Export  (`LaPrint.Core.Export`)   ← js/exporter.js · paper.js · extract.js

```csharp
public sealed record PreflightResult(IReadOnlyList<Issue> All, IReadOnlyList<Issue> Errors, IReadOnlyList<Issue> Warnings, IReadOnlyList<Issue> Infos);
public static class Preflight { PreflightResult Run(LabelTemplate t, RenderContext ctx, ValidationRules rules, double dpi); }
public static class FileNaming { string Build(string pattern, Fields f, LabelSize label, DbRow? row); }   // 금지문자→'-', 120자, .pdf

public sealed record PaperSpec(string Id, string Name, double W, double H);
public sealed record LabelPreset(string Id, string Name, double W, double H, (double X,double Y)? Src);
public sealed class LayoutOptions { string Paper="label"; double CustomW=210, CustomH=297; string Orientation="auto"; double MarginMm=8, GapX=3, GapY=3;
                                    string Align="center"; bool CropMarks, Outline; string Repeat="fill"; }
public sealed record ImpositionPlan(double PaperW, double PaperH, int Cols, int Rows, int PerPage, double OriginX, double OriginY, double StepX, double StepY, bool Fits, bool Direct, string Reason);
public static class Paper { IReadOnlyList<PaperSpec> Papers; IReadOnlyList<LabelPreset> ProductLabels, CommonLabels; LabelPreset Sheet;
  ImpositionPlan Plan(LabelSize label, LayoutOptions o); (double X,double Y) SlotAt(ImpositionPlan p, int i); string Describe(LabelSize l, LayoutOptions o);
  LabelPreset? MatchPreset(double w, double h); void DrawCropMarks(SKCanvas c, ImpositionPlan p, int i, LabelSize l, double pxPerMm); }
public static class SheetExtractor { bool Inside(LabelObject o, SKRect r, double tol=0.8); bool Overlaps(LabelObject o, SKRect r);
  (LabelTemplate Tpl, int Taken, int Partial, int Dropped) Extract(LabelTemplate sheet, SKRect rectMm, bool includePartial, bool keepBackground);
  (LabelTemplate, int, int, int) ExtractPreset(LabelTemplate sheet, LabelPreset preset, bool keepBackground);
  SKBitmap? CropBackground(SKBitmap bg, LabelSize sheet, SKRect rectMm); IReadOnlyList<(LabelPreset, int N, int Partial)> Preview(LabelTemplate sheet); }

public sealed record ExportJob(string Id, int Copies, Fields Fields, DbRow? Row, IReadOnlyList<LabelObject>? Objects);
public sealed class ExportOptions { string Mode="separate"; double Dpi=300; bool IncludeBg=true; string Pattern="{ITEM}_{LOT}_{DATE}";
  string Conflict="increment"; string OutDir=""; LayoutOptions Layout=new(); }
public sealed record ExportResult(bool Ok, string? FileName, long Bytes, int Pages, string? Error);
public sealed class PdfExporter {
  // 벡터 PDF: SKDocument.CreatePdf, 페이지 = 라벨 mm×(72/25.4)pt, LabelRenderer.Render(scale=72/25.4). 이미지는 원본 해상도 그대로 삽입.
  Task<ExportResult> ExportOne(LabelTemplate t, RenderContext ctx, ExportOptions o, CancellationToken ct);
  Task<BatchResult> ExportBatch(LabelTemplate t, IReadOnlyList<ExportJob> jobs, ExportOptions o,
      Func<ExportJob, Task> beforeJob,           // ★ await — 치환기 + 이미지 슬롯을 이 행 데이터로 교체
      IProgress<(int Index,int Total,ExportJob Job,ExportResult? Res)>? progress, Func<bool>? isPaused, CancellationToken ct);
  // Layout.Paper != "label" → 면付: ImpositionPlan 대로 슬롯에 배치, 연속 출력은 칸을 서로 다른 라벨로 채우고 가득 차면 페이지 추가
  // copies > 1 이고 패턴에 {COPY} 없으면 "_{COPY}" 자동 추가 (파일명 충돌 방지)
}
```

### 1.7 Imaging  (`LaPrint.Core.Imaging`)   ← js/imaging.js · settings.readImage

```csharp
public static class ImageProcessor { SKBitmap Process(byte[] data, bool autoTransparent = true, int tolerance = 30); }
// hasTransparency(알파<250 픽셀 17개 이상) → 그대로 / 아니면 가장자리 최빈색(16단위 양자화)을 배경으로 BFS 플러드필 투명화
public sealed class ImageStore {        // 이미지 폴더 (UNC 경로 가능)
  ImageStore(string folder); byte[] Read(string fileName);      // 정확한 이름 → 없으면 대소문자 무시 매칭 (폴더 목록 캐시)
  SKBitmap GetProcessed(string fileName, bool autoTransparent, int tolerance);   // LRU 150
  void Invalidate(); IReadOnlyList<string> ListImages();
}
public static class SlotLoader {        // ★ 행마다 호출
  Task<bool> LoadIntoAsync(IEnumerable<LabelObject> objs, Fields f, DbRow? row, ImageStore? store, bool force, bool autoTransparent, int tol);
  // fileName = SourceField 가 "@" 로 시작하면 row[열] 아니면 f[SourceField]; LooksLikeImageName 아니면 Error="파일명이 아닙니다: …"
}
```

### 1.8 Zpl  (`LaPrint.Core.Zpl`)   ← js/zpl.js

```csharp
public sealed record MonoBitmap(byte[] Bytes, int WidthBytes, int Width, int Height);
public static class Monochrome { MonoBitmap Convert(SKBitmap bmp, int threshold = 160, bool dither = true); } // 회색조 = 0.299R+0.587G+0.114B, 알파는 흰색 합성, Floyd–Steinberg
public static class ZplCompress { string CompressRows(byte[] bytes, int widthBytes, int height); string ToGfa(MonoBitmap m, bool compress = true); }
// G..Y=1..20, g..z=20..400(20단위), ','=남은 줄 0, '!'=남은 줄 F, ':'=윗줄과 동일 — 골든 zpl_golden.json 과 문자열 일치해야 한다
public sealed class ZplOptions { int Dpi=203; int? Darkness; int? Speed; int Quantity=1; string? MediaMode; int HomeX, HomeY; bool Invert; int Threshold=160; bool Dither=true; bool Compress=true; int? LabelWidthDots, LabelLenDots; }
public static class ZplBuilder { string Build(MonoBitmap m, ZplOptions o); int MmToDots(double mm, int dpi); IReadOnlyList<Issue> SanityCheck(string zpl, int widthDots, int heightDots, int dpi, LabelSize label);
                                 string TestLabel(int dpi); string PrintConfig(); string Calibrate(); }
public interface IZplTransport { string Name { get; } Task SendAsync(string zpl, CancellationToken ct); }
public sealed class TcpTransport : IZplTransport { TcpTransport(string host, int port = 9100); }   // 네트워크 프린터
public sealed class FileTransport : IZplTransport { FileTransport(string path); }
[SupportedOSPlatform("windows")] public sealed class RawSpoolerTransport : IZplTransport { RawSpoolerTransport(string printerName); static IReadOnlyList<string> InstalledPrinters(); }
// winspool.drv OpenPrinter/StartDocPrinter(RAW)/WritePrinter — Windows 에 설치된 Zebra 드라이버(USB 포함)로 원시 ZPL 전송
```

### 1.9 Batch  (`LaPrint.Core.Batch`)   ← js/batch.js

```csharp
public sealed class QueueRow { string Id; string Item, Lot, Sn, Mfg, Exp; int Months=36; bool ExpAuto=true; int Copies=1;
                               string Status="pending" /*pending|running|done|error|skipped*/; string FileName="", Error=""; List<Issue> Issues; Fields? Fields; DbRow? Row; }
public sealed class QueueEngine {
  IReadOnlyList<QueueRow> Rows; int Count; int TotalLabels; bool Running, Paused; (int Index,int Total) Progress;
  event Action Changed;
  QueueRow Add(QueueRow r); void AddMany(IEnumerable<QueueRow>); void Update(string id, Action<QueueRow>); void Remove(string id); void Clear(); void ResetStatus();
  IEnumerable<QueueRow> ExpandSerial(QueueRow proto, int from, int to, int pad);      // SN 연번 전개
  static (List<QueueRow> Rows, Dictionary<string,int> Mapping, bool HeaderDetected, string? Error) ParseTable(string text, QueueRow? defaults);  // 탭/쉼표, COLUMN_ALIASES, normDate
  static Task<(…)> ParseFileAsync(string path, QueueRow? defaults);   // csv/txt/tsv 는 ParseTable, xlsx/xls 는 NPOI 첫 시트
  string ToCsv();
  QueueSummary ValidateAll(LabelIndex index, FieldMap map, LabelTemplate t, ValidationRules rules, double dpi, Func<QueueRow,RenderContext> ctxOf);
  Task<BatchResult> RunAsync(LabelTemplate t, ExportOptions o, PdfExporter exporter, Func<QueueRow,Task> prepareJob, IProgress<…>?, CancellationToken ct);
  void Pause(); void Resume(); void Cancel();
}
```

### 1.10 Storage  (`LaPrint.Core.Storage`)   ← js/settings.js · store.js

```csharp
public static class AppPaths { string Root /*%APPDATA%\LaPrint*/; string SettingsFile; string TemplatesDir; string SessionFile; string HistoryFile; string CacheDir; }
public sealed class AppSettings {   // System.Text.Json, camelCase, 들여쓰기, 원자적 저장(tmp → Move). 구조는 js/settings.js DEFAULTS 와 동일
  PathsSettings Paths;     // dbDir, dbFileName, dbFileNameBsc, imgDir, outDir, dbAutoLoad, dbLastModified(Bsc)
  OutputSettings Output;   // dpi, rasterFormat, jpegQuality, includeBg, mode, pattern, target, conflict, confirmBeforeExport
  ImagingSettings Imaging; TextDefaults TextDefaults; BarcodeDefaults BarcodeDefaults;
  PrinterSettings Printer; // dpi, darkness, speed, mediaMode, threshold, dither, invert, method(spooler|tcp|file), printerName, host, port
  UiSettings Ui;           // theme, linkMode, showRulers, showGrid, gridMm, snap, snapPx, onboardingDone
  ValidationRules Validation; TemplateSettings Template /*preset*/; LayoutOptions Layout;
  DataSettings Data;       // profile, profiles{general,bsc}.{fieldMap,keyCol}
}
public sealed class SettingsStore { AppSettings Load(); void Save(AppSettings s); event Action<AppSettings> Changed; }
public sealed class TemplateStore { IReadOnlyList<(string Name, DateTime At)> List(); LabelTemplate? Load(string name); void Save(string name, LabelTemplate t); void Delete(string name); }
public sealed class SessionStore { SessionState? Load(); void Save(SessionState s); }   // inputs, queue rows, locked, savedAt
public sealed class HistoryStore { void Append(HistoryEntry e); IReadOnlyList<HistoryEntry> List(int limit); }   // jsonl
public sealed class DbCache { void Put(string profile, LabelDb db, DateTime lastWrite); LabelDb? Get(string profile, DateTime lastWrite); }  // 12,000행 재파싱 생략
```

---

## 2. 골든 테스트 (tests/LaPrint.Core.Tests)

`Fixtures/golden.json` 은 브라우저판이 실제 라벨DB(12,199행)로 계산한 기준값이다.

| 골든 키 | 검증 대상 | 통과 기준 |
|---|---|---|
| `fields` (123건) | FieldComputer + FieldMap | 모든 키의 문자열 일치 (TODAY/NOW/DATE/TIME 제외) |
| `edate` | Edate 말일 클램프 | 일치 |
| `resolve` | Placeholders.Resolve / Unresolved | 일치 |
| `validate` | RecordValidator | level+code 집합 일치 (오늘 날짜 의존 EXP_PAST 는 기준일 고정) |
| `gtin`, `parseAIs`, `bcValidate` | Gs1 | 일치 |
| `paper` (75건), `presets` | Paper.Plan/Describe/SlotAt | 수치 ±0.001, 문자열 일치 |
| `extract` | SheetExtractor Inside/Overlaps | id 집합 일치 |
| `fileName`, `cols`, `junk`, `imgName` | FileNaming, ColumnName, IsJunkKey, LooksLikeImageName | 일치 |
| `defaultTemplate`, `fieldCols`, `placeholders` | TemplateJson, FieldMap, Placeholders.List | 왕복 직렬화 후 동일 |
| `zpl_golden.json` | ZplCompress.ToGfa | 문자열 완전 일치 |
| `barcode_golden.json` + `barcodes/*.png` | bwip-js 결과를 ZXing.Net 으로 **디코드** → 데이터 일치, GS1 은 FNC1 위치 일치 |
| 자체 | BarcodeEncoder.Encode → 비트맵 → ZXing.Net 디코드 왕복, 심볼로지 식별자 `]d2` / `]C1` | 일치 |
| 자체 | TextLayoutEngine (Liberation Sans) | 자간·어간·장평·줄바꿈·금칙·autoShrink 성질 검사 (폭 단조성, 마지막 글자 뒤 자간 없음 등) |
| `샘플_라벨DB.xlsx` + `images/` | 로딩 → 색인 → 슬롯 로딩 → 연속 출력 3건 | 3장의 그림 영역 해시가 서로 다름 (★) |

---

## 3. LaPrint.App (WPF) — 화면 구성

브라우저판 index.html 의 배치를 그대로 옮긴다. 세부 디자인 토큰·HFE 규칙은 `docs/DESIGN.md`.

```
┌ AppBar: [로고 LaPrint v3] [서식 ▾][저장][관리…]  [🔒 잠금][실물 보기]      ●DB ●이미지 ●저장폴더  [⚙][?] ┐
├ 좌측 Rail (330px)          ┬ 중앙 Stage                                    ┬ 우측 Inspector (308px) ┤
│ 1 작업 입력                │  툴바: +텍스트 +이미지슬롯 +바코드 +이미지파일    │ [속성][객체 N]        │
│   출고 구분 [일반 ▾]       │        정렬·분배·순서·삭제 | 되돌리기 | 스냅 격자 │ 위치와 크기 / 내용 /  │
│   품목번호 [검색 팝업]     │        눈금 링크보기 | 배율 | 라벨 프리셋 W×H     │ 글꼴(자간·어간·행간·  │
│   LOT SN 제조일 유효기간   │  SKElement 캔버스: 줌(휠) 팬(드래그/스페이스)   │ 장평·커닝) / 정렬 /   │
│   유효일 매수 [+큐에 추가] │        선택·마키·이동·리사이즈 핸들·스냅 안내선  │ 데이터 링크 / 이미지  │
│ 2 DB 참조값 [열 매칭…]     │        눈금자 · 격자 · 링크 오버레이(하나만)     │ 소스 / 바코드         │
│ 3 출력 전 점검             │                                                 │                       │
│ 4 출력 대상 · 인쇄 크기    │                                                 │                       │
│   [이 라벨 1장 출력]       │                                                 │                       │
│   [큐 N장 출력]            │                                                 │                       │
├ 하단 Drawer: 연속 작업 큐 (붙여넣기 · 파일 · SN 전개 · 표 · 진행률 · 일시정지/중지) ───────────────────┤
└ StatusBar: 🔒 잠금 · [HH:MM:SS] 메시지 · 173.8×75mm · 113% · 저장됨 HH:MM ─────────────────────────────┘
```

창: 설정(폴더 경로 · 데이터 매칭 · 라벨/용지 · 출력 · ZEBRA 프린터 · 이미지 · 검증 규칙 · 화면 · 출력 이력 · 정보),
데이터 매칭 편집기(별도 창, 좌 항목/우 열 그리드), 용지 배치, 원판에서 떼어내기, 출력 확인, ZEBRA 전송 확인, 서식 관리, 처음 설정 안내.

단축키: Ctrl+P 이 라벨 1장 · Ctrl+Shift+P 큐 전체 · Ctrl+Enter 큐에 추가 · Ctrl+Z/Y · Ctrl+0 화면 맞춤 · Delete · 방향키(0.1mm, Shift 1mm) · Ctrl+D 복제 · Esc 선택 해제.

// 설정 구조 — settings.js DEFAULTS 와 같은 camelCase 구조. 프린터 전송 방식만 Windows 용으로 바뀐다.
using LaPrint.Core.Data;
using LaPrint.Core.Export;

namespace LaPrint.Core.Storage;

/// <summary>폴더·파일 경로 설정.</summary>
public sealed class PathsSettings
{
    public string DbDir { get; set; } = "";
    /// <summary>dbDir 안의 라벨DB 파일명 (일반).</summary>
    public string DbFileName { get; set; } = "";
    /// <summary>BSC 출고(일본)용 라벨DB 파일명.</summary>
    public string DbFileNameBsc { get; set; } = "";
    public string ImgDir { get; set; } = "";
    public string OutDir { get; set; } = "";
    /// <summary>시작 시 자동 로딩.</summary>
    public bool DbAutoLoad { get; set; } = true;
    /// <summary>변경 감지용 마지막 수정 시각 (일반).</summary>
    public DateTime? DbLastModified { get; set; }
    /// <summary>변경 감지용 마지막 수정 시각 (BSC).</summary>
    public DateTime? DbLastModifiedBsc { get; set; }
}

/// <summary>출력 설정.</summary>
public sealed class OutputSettings
{
    public int Dpi { get; set; } = 300;
    /// <summary>auto | png | jpeg.</summary>
    public string RasterFormat { get; set; } = "auto";
    public double JpegQuality { get; set; } = 0.95;
    public bool IncludeBg { get; set; } = true;
    /// <summary>separate | merged.</summary>
    public string Mode { get; set; } = "separate";
    public string Pattern { get; set; } = "{ITEM}_{LOT}_{DATE}";
    /// <summary>pdf | zebra.</summary>
    public string Target { get; set; } = "pdf";
    /// <summary>increment | overwrite | ask.</summary>
    public string Conflict { get; set; } = "increment";
    public bool ConfirmBeforeExport { get; set; } = true;
}

/// <summary>이미지 후처리 설정.</summary>
public sealed class ImagingSettings
{
    public bool AutoTransparent { get; set; } = true;
    public int Tolerance { get; set; } = 30;
}

/// <summary>새 텍스트 객체 기본값.</summary>
public sealed class TextDefaults
{
    public string Font { get; set; } = "Arial";
    public double SizePt { get; set; } = 8;
    public double LetterSpacing { get; set; }
    public double LineHeight { get; set; } = 1.15;
    public double HScale { get; set; } = 100;
    public string Align { get; set; } = "left";
    public string VAlign { get; set; } = "top";
    public bool AutoShrink { get; set; }
    public string Color { get; set; } = "#000000";
}

/// <summary>새 바코드 객체 기본값.</summary>
public sealed class BarcodeDefaults
{
    public string Symbology { get; set; } = "gs1datamatrix";
    public string Source { get; set; } = "field";
    public string Binding { get; set; } = "UDI_FULL";
    public bool HumanReadable { get; set; }
    public string FitMode { get; set; } = "module";
    public string Fit { get; set; } = "center";
}

/// <summary>ZEBRA 프린터 설정.</summary>
public sealed class PrinterSettings
{
    /// <summary>203 | 300 | 600.</summary>
    public int Dpi { get; set; } = 203;
    public int? Darkness { get; set; }
    public int? Speed { get; set; }
    /// <summary>T | P | C. null 이면 프린터 설정 유지.</summary>
    public string? MediaMode { get; set; }
    public int Threshold { get; set; } = 160;
    public bool Dither { get; set; } = true;
    public bool Invert { get; set; }
    /// <summary>spooler | tcp | file.</summary>
    public string Method { get; set; } = "spooler";
    public string PrinterName { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 9100;
}

/// <summary>화면 설정.</summary>
public sealed class UiSettings
{
    /// <summary>system | light | dark.</summary>
    public string Theme { get; set; } = "system";
    /// <summary>on | off — 선택한 객체 하나만 링크 표시.</summary>
    public string LinkMode { get; set; } = "on";
    public bool ShowRulers { get; set; } = true;
    public bool ShowGrid { get; set; }
    public double GridMm { get; set; } = 5;
    public bool Snap { get; set; } = true;
    public int SnapPx { get; set; } = 6;
    public bool OnboardingDone { get; set; }
}

/// <summary>서식 설정.</summary>
public sealed class TemplateSettings
{
    /// <summary>처음 시작할 때 쓸 라벨 규격.</summary>
    public string Preset { get; set; } = "PMFL-001";
}

/// <summary>프로필 하나의 열 매칭 설정.</summary>
public sealed class ProfileSettings
{
    /// <summary>프로필 기본값과 다른 항목만.</summary>
    public Dictionary<string, string> FieldMap { get; set; } = new();
    public string KeyCol { get; set; } = "H";
}

/// <summary>데이터(라벨DB) 설정.</summary>
public sealed class DataSettings
{
    /// <summary>general | bsc.</summary>
    public string Profile { get; set; } = "general";
    public Dictionary<string, ProfileSettings> Profiles { get; set; } = new()
    {
        ["general"] = new ProfileSettings(),
        ["bsc"] = new ProfileSettings(),
    };
}

/// <summary>앱 설정 전체. settings.json 에 camelCase 들여쓰기로 저장한다.</summary>
public sealed class AppSettings
{
    public PathsSettings Paths { get; set; } = new();
    public OutputSettings Output { get; set; } = new();
    public ImagingSettings Imaging { get; set; } = new();
    public TextDefaults TextDefaults { get; set; } = new();
    public BarcodeDefaults BarcodeDefaults { get; set; } = new();
    public PrinterSettings Printer { get; set; } = new();
    public UiSettings Ui { get; set; } = new();
    public ValidationRules Validation { get; set; } = new();
    public TemplateSettings Template { get; set; } = new();
    public LayoutOptions Layout { get; set; } = new();
    public DataSettings Data { get; set; } = new();
}

// 테스트 픽스처 — 출력 폴더의 Fixtures/ 경로와 골든 JSON(브라우저판이 실제 라벨DB로 계산한 기준값).
using System.Text.Json;

namespace LaPrint.Core.Tests;

/// <summary>Fixtures 폴더 접근.</summary>
public static class Fixtures
{
    private static readonly Lazy<JsonDocument> GoldenDoc = new(() =>
        JsonDocument.Parse(File.ReadAllText(Path("golden.json"))));

    /// <summary>Fixtures/ 아래 상대 경로 → 테스트 출력 폴더의 절대 경로.</summary>
    public static string Path(string relative)
        => System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", relative);

    /// <summary>golden.json (한 번만 읽는다).</summary>
    public static JsonDocument Golden() => GoldenDoc.Value;
}

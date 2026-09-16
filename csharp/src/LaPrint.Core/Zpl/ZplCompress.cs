// ZPL ^GF 압축 — G..Y=1..20, g..z=20..400(20단위), ','=남은 줄 0, '!'=남은 줄 F, ':'=윗줄과 동일 (zpl_golden.json 과 일치해야 한다).
namespace LaPrint.Core.Zpl;

/// <summary>^GFA 데이터 문자열 만들기.</summary>
public static class ZplCompress
{
    public static string CompressRows(byte[] bytes, int widthBytes, int height)
        => throw new NotImplementedException("ZplCompress.CompressRows — 아직 구현되지 않았습니다");

    public static string ToGfa(MonoBitmap m, bool compress = true)
        => throw new NotImplementedException("ZplCompress.ToGfa — 아직 구현되지 않았습니다");
}

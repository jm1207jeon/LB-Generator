// ZPL ^GF 압축 — G..Y=1..20, g..z=20..400(20단위), ','=남은 줄 0, '!'=남은 줄 F, ':'=윗줄과 동일 (zpl_golden.json 과 일치해야 한다).
using System.Text;

namespace LaPrint.Core.Zpl;

/// <summary>^GFA 데이터 문자열 만들기.</summary>
public static class ZplCompress
{
    private const string Hex = "0123456789ABCDEF";

    /// <summary>반복 횟수 → ZPL 반복 부호. g=20 … z=400 뒤에 G=1 … Y=20.</summary>
    private static string RepeatCode(int n)
    {
        var s = new StringBuilder(4);
        // 반복 코드는 z(400)까지만 있다. 그보다 긴 연속은 z 를 여러 번 이어 붙인다 (zpl.js 는 '{' 등 잘못된 문자를 냈음)
        while (n > 400) { s.Append('z'); n -= 400; }
        int high = n / 20;
        if (high > 0) s.Append((char)('f' + high));
        int low = n % 20;
        if (low > 0) s.Append((char)('F' + low));
        return s.ToString();
    }

    /// <summary>행 단위 ASCII 압축 문자열. 윗줄과 같으면 ':', 뒤쪽이 모두 0/F 면 ','/'!'.</summary>
    public static string CompressRows(byte[] bytes, int widthBytes, int height)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var outp = new StringBuilder();
        var hex = new StringBuilder(widthBytes * 2);
        ReadOnlySpan<byte> prevRow = default;
        bool hasPrev = false;
        for (int y = 0; y < height; y++)
        {
            int off = y * widthBytes;
            ReadOnlySpan<byte> row = bytes.AsSpan(off, widthBytes);

            if (hasPrev && row.SequenceEqual(prevRow)) { outp.Append(':'); prevRow = row; continue; }

            // 니블 단위 hex 문자열
            hex.Clear();
            for (int b = 0; b < widthBytes; b++)
            {
                hex.Append(Hex[row[b] >> 4]).Append(Hex[row[b] & 15]);
            }
            // 뒤쪽 반복을 , 또는 ! 로
            int i = 0;
            int n = hex.Length;
            while (i < n)
            {
                char c = hex[i];
                int run = 1;
                while (i + run < n && hex[i + run] == c) run++;
                int rest = n - i;
                if (run == rest && (c == '0' || c == 'F'))
                {
                    outp.Append(c == '0' ? ',' : '!');
                    break;
                }
                if (run > 1) outp.Append(RepeatCode(run)).Append(c);
                else outp.Append(c);
                i += run;
            }
            prevRow = row;
            hasPrev = true;
        }
        return outp.ToString();
    }

    /// <summary>^GFA 필드 문자열 — '^GFA,{total},{total},{widthBytes},{data}'.</summary>
    public static string ToGfa(MonoBitmap m, bool compress = true)
    {
        ArgumentNullException.ThrowIfNull(m);
        int total = m.Bytes.Length;
        string data;
        if (compress)
        {
            data = CompressRows(m.Bytes, m.WidthBytes, m.Height);
        }
        else
        {
            var hex = new StringBuilder(total * 2);
            for (int i = 0; i < total; i++) hex.Append(Hex[m.Bytes[i] >> 4]).Append(Hex[m.Bytes[i] & 15]);
            data = hex.ToString();
        }
        return $"^GFA,{total},{total},{m.WidthBytes},{data}";
    }
}

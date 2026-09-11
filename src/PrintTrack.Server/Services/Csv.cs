using System.Globalization;
using System.Text;

namespace PrintTrack.Server.Services;

/// <summary>
/// Minimal CSV writer. Uses <c>;</c> as separator and prepends a UTF-8 BOM so Excel in
/// Spanish locales opens it with columns split correctly.
/// </summary>
public sealed class CsvBuilder
{
    private readonly StringBuilder _sb = new();
    private const char Sep = ';';

    public CsvBuilder Row(params object?[] fields)
    {
        for (var i = 0; i < fields.Length; i++)
        {
            if (i > 0) _sb.Append(Sep);
            _sb.Append(Escape(fields[i]));
        }
        _sb.Append("\r\n");
        return this;
    }

    private static string Escape(object? value)
    {
        var s = value switch
        {
            null => "",
            bool b => b ? "sí" : "no",
            DateTimeOffset dto => dto.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            decimal m => m.ToString("0.00", CultureInfo.InvariantCulture),
            double x => x.ToString("0.00", CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? ""
        };

        if (s.Contains(Sep) || s.Contains('"') || s.Contains('\n') || s.Contains('\r'))
            s = '"' + s.Replace("\"", "\"\"") + '"';
        return s;
    }

    public byte[] ToBytes()
    {
        var body = Encoding.UTF8.GetBytes(_sb.ToString());
        var bom = Encoding.UTF8.GetPreamble();
        var result = new byte[bom.Length + body.Length];
        Buffer.BlockCopy(bom, 0, result, 0, bom.Length);
        Buffer.BlockCopy(body, 0, result, bom.Length, body.Length);
        return result;
    }
}

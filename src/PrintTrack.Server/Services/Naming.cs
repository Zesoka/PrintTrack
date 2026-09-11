namespace PrintTrack.Server.Services;

public static class Naming
{
    /// <summary>
    /// Normalize a Windows login for matching: strip <c>DOMAIN\\</c> or <c>user@domain</c>,
    /// trim and upper-case. <c>ACME\\JDoe</c> and <c>jdoe@acme.local</c> both become <c>JDOE</c>.
    /// </summary>
    public static string NormalizeUser(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var s = raw.Trim();

        var slash = s.LastIndexOf('\\');
        if (slash >= 0 && slash < s.Length - 1) s = s[(slash + 1)..];

        var at = s.IndexOf('@');
        if (at > 0) s = s[..at];

        return s.Trim().ToUpperInvariant();
    }
}

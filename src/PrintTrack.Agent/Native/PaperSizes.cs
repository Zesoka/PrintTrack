namespace PrintTrack.Agent.Native;

/// <summary>Maps the common Windows <c>DMPAPER_*</c> codes to readable names.</summary>
internal static class PaperSizes
{
    private static readonly Dictionary<short, string> Map = new()
    {
        [1] = "Letter", [2] = "Letter Small", [3] = "Tabloid", [4] = "Ledger",
        [5] = "Legal", [6] = "Statement", [7] = "Executive", [8] = "A3", [9] = "A4",
        [10] = "A4 Small", [11] = "A5", [12] = "B4 (JIS)", [13] = "B5 (JIS)",
        [14] = "Folio", [15] = "Quarto", [16] = "10x14", [17] = "11x17",
        [24] = "C size sheet", [25] = "D size sheet", [26] = "E size sheet",
        [27] = "Envelope DL", [28] = "Envelope C5", [43] = "A6", [70] = "A2",
    };

    public static string? Name(short code) => Map.TryGetValue(code, out var n) ? n : (code > 0 ? $"Paper#{code}" : null);
}

namespace HomeLedger.Infrastructure.Import;

public static class ReceiptImageContentValidator
{
    public static bool LooksLikeSupportedImage(ReadOnlySpan<byte> content, string fileName)
    {
        if (content.Length < 4)
            return false;

        var extension = Path.GetExtension(fileName);
        if (!string.IsNullOrEmpty(extension))
        {
            return extension.ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => IsJpeg(content),
                ".png" => IsPng(content),
                ".gif" => IsGif(content),
                ".webp" => IsWebp(content),
                ".bmp" => IsBmp(content),
                ".heic" or ".heif" => IsHeicOrHeif(content),
                _ => MatchesAnySupportedFormat(content)
            };
        }

        return MatchesAnySupportedFormat(content);
    }

    private static bool MatchesAnySupportedFormat(ReadOnlySpan<byte> content) =>
        IsJpeg(content)
        || IsPng(content)
        || IsGif(content)
        || IsWebp(content)
        || IsBmp(content)
        || IsHeicOrHeif(content);

    private static bool IsJpeg(ReadOnlySpan<byte> content) =>
        content.Length >= 3
        && content[0] == 0xFF
        && content[1] == 0xD8
        && content[2] == 0xFF;

    private static bool IsPng(ReadOnlySpan<byte> content) =>
        content.Length >= 8
        && content[0] == 0x89
        && content[1] == (byte)'P'
        && content[2] == (byte)'N'
        && content[3] == (byte)'G'
        && content[4] == 0x0D
        && content[5] == 0x0A
        && content[6] == 0x1A
        && content[7] == 0x0A;

    private static bool IsGif(ReadOnlySpan<byte> content) =>
        content.Length >= 6
        && content[0] == (byte)'G'
        && content[1] == (byte)'I'
        && content[2] == (byte)'F'
        && content[3] == (byte)'8'
        && (content[4] == (byte)'7' || content[4] == (byte)'9')
        && content[5] == (byte)'a';

    private static bool IsWebp(ReadOnlySpan<byte> content) =>
        content.Length >= 12
        && content[0] == (byte)'R'
        && content[1] == (byte)'I'
        && content[2] == (byte)'F'
        && content[3] == (byte)'F'
        && content[8] == (byte)'W'
        && content[9] == (byte)'E'
        && content[10] == (byte)'B'
        && content[11] == (byte)'P';

    private static bool IsBmp(ReadOnlySpan<byte> content) =>
        content.Length >= 2
        && content[0] == (byte)'B'
        && content[1] == (byte)'M';

    private static bool IsHeicOrHeif(ReadOnlySpan<byte> content)
    {
        if (content.Length < 12)
            return false;

        if (content[4] != (byte)'f'
            || content[5] != (byte)'t'
            || content[6] != (byte)'y'
            || content[7] != (byte)'p')
        {
            return false;
        }

        var brand = System.Text.Encoding.ASCII.GetString(content.Slice(8, 4));
        return brand is "heic" or "heix" or "hevc" or "hevx" or "heif" or "mif1" or "msf1";
    }
}

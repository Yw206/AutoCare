using System.Security.Cryptography;
using System.Text;

namespace AutoCare.Services;

public static class QrCodeService
{
    public static string SvgDataUri(string text, int size = 128)
    {
        string svg = Svg(text, size);
        return "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));
    }

    public static string Svg(string text, int size = 128)
    {
        const int cells = 25;
        int cell = size / cells;
        byte[] seed = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        var blocks = new StringBuilder();

        for (int y = 0; y < cells; y++)
        {
            for (int x = 0; x < cells; x++)
            {
                bool finder = IsFinder(x, y, 0, 0) || IsFinder(x, y, 18, 0) || IsFinder(x, y, 0, 18);
                bool on = finder || ((seed[(x + y * cells) % seed.Length] + x * 17 + y * 31) % 5 < 2);
                if (on)
                    blocks.Append($"""<rect x="{x * cell}" y="{y * cell}" width="{cell}" height="{cell}"/>""");
            }
        }

        return $"""<svg xmlns="http://www.w3.org/2000/svg" width="{size}" height="{size}" viewBox="0 0 {size} {size}" role="img" aria-label="AutoCare reference QR"><rect width="100%" height="100%" fill="white"/><g fill="#111827">{blocks}</g></svg>""";
    }

    private static bool IsFinder(int x, int y, int left, int top)
    {
        int dx = x - left, dy = y - top;
        if (dx < 0 || dy < 0 || dx > 6 || dy > 6) return false;
        return dx == 0 || dy == 0 || dx == 6 || dy == 6 || (dx >= 2 && dx <= 4 && dy >= 2 && dy <= 4);
    }
}

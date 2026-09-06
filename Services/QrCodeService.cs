using System.Text;
using QRCoder;

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
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q);
        var qr = new SvgQRCode(data);
        return qr.GetGraphic(6, "#111827", "#ffffff", true);
    }
}

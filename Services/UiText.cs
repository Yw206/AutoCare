using Microsoft.AspNetCore.Http;

namespace AutoCare.Services;

public static class UiText
{
    public static string CurrentLanguage(HttpContext context)
    {
        string language = context.Request.Cookies["AutoCare.Language"] ?? "en";
        return language is "zh" or "ms" ? language : "en";
    }

    public static string T(HttpContext context, string en, string zh, string ms) =>
        CurrentLanguage(context) switch
        {
            "zh" => zh,
            "ms" => ms,
            _ => en
        };
}

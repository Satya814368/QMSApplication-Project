namespace QMSApplication.Models;

public static class PortalTheme
{
    public static string? Normalize(string? theme) => theme?.Trim().ToLowerInvariant() switch
    {
        "light" or "white" => "Light",
        "dark" or "black" => "Dark",
        "system" => "System",
        _ => null
    };
}

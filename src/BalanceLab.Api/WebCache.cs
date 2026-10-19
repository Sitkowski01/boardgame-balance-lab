using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Net.Http.Headers;

namespace BalanceLab.Api;

/// <summary>
/// Cache plikow frontu: nazwy w /assets/ zawieraja hash zawartosci (Vite), wiec moga lezec w przegladarce
/// na zawsze; index.html zawsze jest sprawdzany u serwera, zeby po nowym buildzie wskazywal nowe pliki.
/// </summary>
internal static class WebCache
{
    public static void Apply(StaticFileResponseContext context) =>
        context.Context.Response.Headers[HeaderNames.CacheControl] =
            context.Context.Request.Path.StartsWithSegments("/assets", StringComparison.Ordinal)
                ? "public, max-age=31536000, immutable"
                : "no-cache";
}

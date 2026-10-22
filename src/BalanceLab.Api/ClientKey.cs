using System.Net;
using System.Net.Sockets;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.SignalR;

namespace BalanceLab.Api;

/// <summary>
/// Kto pyta — klucz do limitow (zadan, pokoi, eksperymentow). Adres IPv4 wprost; IPv6 po prefiksie /64,
/// bo jeden klient zwykle dostaje cala siec /64 i moglby zmieniac adres przy kazdym zadaniu.
/// </summary>
internal static class ClientKey
{
    public static string Of(HttpContext context) => Of(context.Connection.RemoteIpAddress);

    public static string Of(IPAddress? address)
    {
        if (address is null)
        {
            return "local";
        }

        if (address.IsIPv4MappedToIPv6)
        {
            return address.MapToIPv4().ToString();
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return address.ToString();
        }

        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return new IPAddress(bytes) + "/64";
    }
}

/// <summary>
/// Limit wywolan huba na klienta. Limit zadan HTTP liczy tylko polaczenia, a po jednym polaczeniu WebSocket
/// mozna wolac hub bez konca (np. zakladac pokoje i dosiadac sie do cudzych).
/// </summary>
internal sealed class HubCallLimiter(PartitionedRateLimiter<string> limiter) : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext invocationContext, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        using var lease = limiter.AttemptAcquire(ClientKey.Of(invocationContext.Context.GetHttpContext()?.Connection.RemoteIpAddress));
        return lease.IsAcquired
            ? await next(invocationContext)
            : throw new HubException("Za dużo próśb naraz - zwolnij na chwilę.");
    }
}

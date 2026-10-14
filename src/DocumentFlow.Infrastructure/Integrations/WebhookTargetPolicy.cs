using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;

namespace DocumentFlow.Infrastructure.Integrations;

public sealed class WebhookTargetPolicy(IConfiguration configuration)
{
    public async Task<(Uri Uri, IPAddress Address)> ValidateAsync(string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment) || uri.Port is < 1 or > 65535)
            throw new ArgumentException("Only HTTPS webhook URLs without credentials or fragments are allowed.");
        var allowLocal = bool.TryParse(configuration["WebhookSecurity:AllowLocalTargets"], out var enabled) && enabled &&
            (configuration["ASPNETCORE_ENVIRONMENT"] is "Development" or "Test" ||
             configuration["DOTNET_ENVIRONMENT"] is "Development" or "Test");
        if (uri.HostNameType == UriHostNameType.Unknown || uri.Host.Length > 253)
            throw new ArgumentException("Invalid webhook host.");
        var addresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost, ct);
        if (addresses.Length == 0 || (!allowLocal && addresses.Any(IsUnsafe)))
            throw new ArgumentException("Webhook target resolves to a prohibited address.");
        return (uri, addresses[0]);
    }

    public static bool IsUnsafe(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            return IPAddress.IsLoopback(address) || address.Equals(IPAddress.IPv6Any) ||
                (bytes[0] & 0xFE) == 0xFC || (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80) ||
                (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0D && bytes[3] == 0xB8);
        }
        if (address.AddressFamily != AddressFamily.InterNetwork) return true;
        var b = address.GetAddressBytes();
        return b[0] is 0 or 10 or 127 || b[0] >= 224 ||
            (b[0] == 169 && b[1] == 254) || (b[0] == 172 && b[1] is >= 16 and <= 31) ||
            (b[0] == 192 && b[1] == 168) || (b[0] == 100 && b[1] is >= 64 and <= 127) ||
            (b[0] == 192 && b[1] == 0) || (b[0] == 192 && b[1] == 0 && b[2] == 2) ||
            (b[0] == 198 && b[1] is 18 or 19) || (b[0] == 198 && b[1] == 51 && b[2] == 100) ||
            (b[0] == 203 && b[1] == 0 && b[2] == 113);
    }
}

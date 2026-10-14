using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using DocumentFlow.Domain;

namespace DocumentFlow.Infrastructure.Integrations;

public interface IWebhookSender
{
    Task SendAsync(WebhookDelivery delivery, WebhookEndpoint endpoint, CancellationToken ct);
}

public sealed class WebhookSender(WebhookTargetPolicy targets, WebhookSecretProtector secrets) : IWebhookSender
{
    public static string Signature(string secret, string payload) => "sha256=" + Convert.ToHexString(
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();

    public async Task SendAsync(WebhookDelivery delivery, WebhookEndpoint endpoint, CancellationToken ct)
    {
        var (uri, address) = await targets.ValidateAsync(endpoint.Url, ct);
        using var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            UseCookies = false,
            ConnectTimeout = TimeSpan.FromSeconds(3),
            ConnectCallback = async (_, token) =>
            {
                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    await socket.ConnectAsync(new IPEndPoint(address, uri.Port), token);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch { socket.Dispose(); throw; }
            }
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
        using var request = new HttpRequestMessage(HttpMethod.Post, uri);
        request.Content = new StringContent(delivery.Payload, Encoding.UTF8, "application/json");
        request.Headers.TryAddWithoutValidation("X-DocumentFlow-Event-Id", delivery.EventId.ToString("D"));
        request.Headers.TryAddWithoutValidation("X-DocumentFlow-Event-Type", delivery.EventType);
        request.Headers.TryAddWithoutValidation("X-DocumentFlow-Signature", Signature(secrets.Unprotect(endpoint.EncryptedSecret), delivery.Payload));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Webhook returned HTTP {(int)response.StatusCode}.");
        if (response.Content.Headers.ContentLength is > 65536) throw new InvalidDataException("Webhook response is too large.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[8192];
        var total = 0;
        while (true)
        {
            var read = await stream.ReadAsync(buffer, ct);
            if (read == 0) break;
            total += read;
            if (total > 65536) throw new InvalidDataException("Webhook response is too large.");
        }
    }
}

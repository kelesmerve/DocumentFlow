using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using DocumentFlow.Application.Timestamps;
using Microsoft.Extensions.Configuration;

namespace DocumentFlow.Infrastructure.Timestamps;

public sealed class ExternalRfc3161TimestampProvider : ITimestampProvider, IDisposable
{
    private readonly HttpClient httpClient;
    private readonly X509Certificate2Collection _trust;
    private readonly bool _trustConfigurationValid;
    private readonly string _url;
    private readonly string _authority;

    public ExternalRfc3161TimestampProvider(HttpClient httpClient, IConfiguration configuration)
    {
        this.httpClient = httpClient;
        _url = configuration["Timestamping:External:Url"] ?? string.Empty;
        _authority = configuration["Timestamping:External:Authority"] ?? "Configured external TSA";
        try
        {
            _trust = LoadTrust(configuration);
            _trustConfigurationValid = _trust.Count > 0;
        }
        catch (Exception exception) when (exception is IOException or CryptographicException or ArgumentException)
        {
            _trust = [];
            _trustConfigurationValid = false;
        }
    }

    public string ProviderType => "ExternalRfc3161";

    public async Task<TimestampIssue> IssueAsync(byte[] signatureBytes, byte[] nonce, CancellationToken cancellationToken)
    {
        if (!_trustConfigurationValid) throw new TimestampProviderUnavailableException("Trusted external TSA certificates are not configured or could not be loaded.");
        if (!Uri.TryCreate(_url, UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttps)
            throw new TimestampProviderUnavailableException("A trusted HTTPS RFC 3161 TSA URL is not configured.");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new ByteArrayContent(Rfc3161Codec.CreateRequest(SHA256.HashData(signatureBytes), nonce))
            };
            request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/timestamp-query");
            request.Headers.Accept.ParseAdd("application/timestamp-reply");
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode) throw new TimestampProviderUnavailableException($"TSA returned HTTP {(int)response.StatusCode}.");
            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is null || !(mediaType.Equals("application/timestamp-reply", StringComparison.OrdinalIgnoreCase) || mediaType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase)))
                throw new TimestampProviderUnavailableException("TSA response has an unexpected media type.");
            var body = await ReadBoundedAsync(response.Content, cancellationToken);
            var token = Rfc3161Codec.ParseResponse(body);
            var verification = Verify(token, signatureBytes, nonce);
            if (verification.Result != "Valid") throw new TimestampProviderUnavailableException($"TSA token failed validation: {verification.Message}");
            return new TimestampIssue(token, _authority, verification.SerialNumber!, verification.Algorithm!, verification.Imprint!, verification.GenTimeUtc!.Value, ProviderType);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimestampProviderUnavailableException("TSA request timed out.");
        }
        catch (HttpRequestException exception) { throw new TimestampProviderUnavailableException($"TSA is unavailable: {exception.Message}"); }
        catch (IOException exception) { throw new TimestampProviderUnavailableException($"TSA response could not be read: {exception.Message}"); }
        catch (CryptographicException exception) { throw new TimestampProviderUnavailableException($"TSA response could not be verified: {exception.Message}"); }
        catch (System.Formats.Asn1.AsnContentException exception) { throw new TimestampProviderUnavailableException($"TSA response is malformed: {exception.Message}"); }
    }

    public TimestampVerification Verify(byte[] token, byte[] expectedSignatureBytes, byte[] expectedNonce) =>
        Rfc3161Codec.Verify(token, expectedSignatureBytes, expectedNonce, _trust);

    public void Dispose() { foreach (var certificate in _trust) certificate.Dispose(); }

    private static X509Certificate2Collection LoadTrust(IConfiguration configuration)
    {
        var collection = new X509Certificate2Collection();
        foreach (var path in configuration.GetSection("Timestamping:External:TrustedCertificates").GetChildren().Select(section => section.Value).Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>())
        {
            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath)) throw new InvalidOperationException($"Configured TSA trust anchor does not exist: {fullPath}");
            collection.Add(X509Certificate2.CreateFromPem(File.ReadAllText(fullPath)));
        }
        return collection;
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        const int maximumBytes = 512 * 1024;
        if (content.Headers.ContentLength is > maximumBytes) throw new TimestampProviderUnavailableException("TSA response exceeds the configured size limit.");
        await using var input = await content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (output.Length + count > maximumBytes) throw new TimestampProviderUnavailableException("TSA response exceeds the configured size limit.");
            await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }
        return output.ToArray();
    }
}

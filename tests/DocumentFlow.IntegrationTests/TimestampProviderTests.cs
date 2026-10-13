using System.Formats.Asn1;
using System.Net;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using DocumentFlow.Application.Timestamps;
using DocumentFlow.Infrastructure.Timestamps;
using Microsoft.Extensions.Configuration;

namespace DocumentFlow.IntegrationTests;

public sealed class TimestampProviderTests
{
    [Fact]
    public async Task External_provider_sends_RFC3161_DER_and_accepts_only_trusted_matching_response()
    {
        var root = TempDirectory();
        try
        {
            using var local = new LocalTimestampProvider(Config(root));
            var certPath = Path.Combine(root, "tsa-cert.pem");
            var signature = RandomNumberGenerator.GetBytes(256);
            var nonce = RandomNumberGenerator.GetBytes(20);
            var issued = await local.IssueAsync(signature, nonce, CancellationToken.None);
            var handler = new DelegateHandler(async request =>
            {
                Assert.Equal("application/timestamp-query", request.Content!.Headers.ContentType!.MediaType);
                Assert.Equal("application/timestamp-reply", request.Headers.Accept.Single().MediaType);
                var requestDer = await request.Content.ReadAsByteArrayAsync();
                Assert.Equal((byte)0x30, requestDer[0]);
                return Reply(issued.Token);
            });
            using var http = new HttpClient(handler);
            using var external = new ExternalRfc3161TimestampProvider(http, ExternalConfig(certPath));
            var accepted = await external.IssueAsync(signature, nonce, CancellationToken.None);
            Assert.Equal("ExternalRfc3161", accepted.ProviderType);
            Assert.Equal(issued.SerialNumber, accepted.SerialNumber);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(signature)), accepted.Imprint);

            var wrongNonce = RandomNumberGenerator.GetBytes(20);
            var wrongNonceToken = await local.IssueAsync(signature, wrongNonce, CancellationToken.None);
            using var mismatchHttp = new HttpClient(new DelegateHandler(_ => Task.FromResult(Reply(wrongNonceToken.Token))));
            using var mismatchProvider = new ExternalRfc3161TimestampProvider(mismatchHttp, ExternalConfig(certPath));
            await Assert.ThrowsAsync<TimestampProviderUnavailableException>(() => mismatchProvider.IssueAsync(signature, nonce, CancellationToken.None));
        }
        finally { DeleteDirectory(root); }
    }

    [Fact]
    public async Task External_provider_rejects_untrusted_bad_DER_and_unavailable_TSA()
    {
        var root = TempDirectory();
        var otherRoot = TempDirectory();
        try
        {
            using var trustedLocal = new LocalTimestampProvider(Config(root));
            using var untrustedLocal = new LocalTimestampProvider(Config(otherRoot));
            var trustedPath = Path.Combine(root, "tsa-cert.pem");
            var signature = RandomNumberGenerator.GetBytes(256);
            var nonce = RandomNumberGenerator.GetBytes(20);
            var untrustedToken = await untrustedLocal.IssueAsync(signature, nonce, CancellationToken.None);
            using var untrustedHttp = new HttpClient(new DelegateHandler(_ => Task.FromResult(Reply(untrustedToken.Token))));
            using var provider = new ExternalRfc3161TimestampProvider(untrustedHttp, ExternalConfig(trustedPath));
            await Assert.ThrowsAsync<TimestampProviderUnavailableException>(() => provider.IssueAsync(signature, nonce, CancellationToken.None));

            using var malformedHttp = new HttpClient(new DelegateHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([0x30, 0x01, 0xFF]) { Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/timestamp-reply") } }
            })));
            using var malformedProvider = new ExternalRfc3161TimestampProvider(malformedHttp, ExternalConfig(trustedPath));
            await Assert.ThrowsAsync<TimestampProviderUnavailableException>(() => malformedProvider.IssueAsync(signature, nonce, CancellationToken.None));

            using var unavailableHttp = new HttpClient(new DelegateHandler(_ => throw new HttpRequestException("offline")));
            using var unavailableProvider = new ExternalRfc3161TimestampProvider(unavailableHttp, ExternalConfig(trustedPath));
            await Assert.ThrowsAsync<TimestampProviderUnavailableException>(() => unavailableProvider.IssueAsync(signature, nonce, CancellationToken.None));

            using var timeoutHttp = new HttpClient(new DelegateHandler(_ => throw new TaskCanceledException("timeout")));
            using var timeoutProvider = new ExternalRfc3161TimestampProvider(timeoutHttp, ExternalConfig(trustedPath));
            await Assert.ThrowsAsync<TimestampProviderUnavailableException>(() => timeoutProvider.IssueAsync(signature, nonce, CancellationToken.None));
        }
        finally { DeleteDirectory(root); DeleteDirectory(otherRoot); }
    }

    [Fact]
    public void Verification_rejects_expired_and_non_timestamping_certificates()
    {
        var root = TempDirectory();
        try
        {
            using var local = new LocalTimestampProvider(Config(root));
            var signature = RandomNumberGenerator.GetBytes(256);
            var nonce = RandomNumberGenerator.GetBytes(20);
            using var badEku = CreateCertificate("1.3.6.1.5.5.7.3.1", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
            using var expired = CreateCertificate("1.3.6.1.5.5.7.3.8", DateTimeOffset.UtcNow.AddYears(-2), DateTimeOffset.UtcNow.AddYears(-1));
            Assert.Equal("Invalid", local.Verify(CreateToken(signature, nonce, badEku), signature, nonce).Result);
            Assert.Equal("Invalid", local.Verify(CreateToken(signature, nonce, expired), signature, nonce).Result);
            Assert.Equal("Unverifiable", local.Verify(CreateToken(signature, nonce, localCertificate: null), signature, nonce).Result);
            Assert.Equal("Unverifiable", local.Verify([], signature, nonce).Result);
        }
        finally { DeleteDirectory(root); }
    }

    private static byte[] CreateToken(byte[] signature, byte[] nonce, X509Certificate2? localCertificate)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        writer.PushSequence();
        writer.WriteInteger(1);
        writer.WriteObjectIdentifier("1.3.6.1.4.1.57264.1.1");
        writer.PushSequence();
        writer.PushSequence();
        writer.WriteObjectIdentifier("2.16.840.1.101.3.4.2.1");
        writer.WriteNull();
        writer.PopSequence();
        writer.WriteOctetString(SHA256.HashData(signature));
        writer.PopSequence();
        writer.WriteInteger(10);
        writer.WriteGeneralizedTime(DateTimeOffset.UtcNow);
        writer.WriteInteger(new BigInteger(nonce, isUnsigned: true, isBigEndian: true));
        writer.PopSequence();
        var cms = new SignedCms(new ContentInfo(new Oid("1.2.840.113549.1.9.16.1.4"), writer.Encode()));
        using var generatedCertificate = localCertificate is null ? CreateCertificate("1.3.6.1.5.5.7.3.8", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1)) : null;
        var certificate = localCertificate ?? generatedCertificate!;
        var signer = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, certificate) { DigestAlgorithm = new Oid("2.16.840.1.101.3.4.2.1") };
        if (localCertificate is null) signer.IncludeOption = X509IncludeOption.None;
        var attribute = new AsnWriter(AsnEncodingRules.DER);
        attribute.PushSequence();
        attribute.PushSequence();
        attribute.PushSequence();
        attribute.WriteObjectIdentifier("2.16.840.1.101.3.4.2.1");
        attribute.WriteNull();
        attribute.PopSequence();
        attribute.WriteOctetString(SHA256.HashData(certificate.RawData));
        attribute.PopSequence();
        attribute.PopSequence();
        signer.SignedAttributes.Add(new Pkcs9AttributeObject(new Oid("1.2.840.113549.1.9.16.2.47"), attribute.Encode()));
        cms.ComputeSignature(signer, silent: true);
        return cms.Encode();
    }

    private static X509Certificate2 CreateCertificate(string ekuOid, DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=Timestamp Test Certificate", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        var eku = new OidCollection { new(ekuOid) };
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(eku, true));
        return request.CreateSelfSigned(notBefore, notAfter);
    }

    private static HttpResponseMessage Reply(byte[] token)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        writer.PushSequence();
        writer.PushSequence();
        writer.WriteInteger(0);
        writer.PopSequence();
        writer.WriteEncodedValue(token);
        writer.PopSequence();
        var content = new ByteArrayContent(writer.Encode());
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/timestamp-reply");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private static IConfiguration Config(string root) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Timestamping:Local:KeyDirectory"] = root
    }).Build();

    private static IConfiguration ExternalConfig(string trustedPath) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Timestamping:External:Url"] = "https://tsa.example.test/timestamp",
        ["Timestamping:External:Authority"] = "Test External TSA",
        ["Timestamping:External:TrustedCertificates:0"] = trustedPath
    }).Build();

    private static string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"documentflow-tsa-provider-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path) { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }

    private sealed class DelegateHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => callback(request);
    }
}

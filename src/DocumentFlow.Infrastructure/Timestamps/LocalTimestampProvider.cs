using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using DocumentFlow.Application.Timestamps;
using Microsoft.Extensions.Configuration;

namespace DocumentFlow.Infrastructure.Timestamps;

public sealed class LocalTimestampProvider : ITimestampProvider, IDisposable
{
    private readonly string _directory;
    private readonly string _authority;
    private readonly X509Certificate2Collection _trust = [];
    private readonly RSA _privateKey;
    private readonly X509Certificate2 _certificate;

    public LocalTimestampProvider(IConfiguration configuration)
    {
        _directory = Path.GetFullPath(configuration["Timestamping:Local:KeyDirectory"] ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DocumentFlow", "tsa"));
        Directory.CreateDirectory(_directory);
        var certPath = Path.Combine(_directory, "tsa-cert.pem");
        var keyPath = Path.Combine(_directory, "tsa-key.pem");
        if (!File.Exists(certPath) || !File.Exists(keyPath)) CreateCertificate(certPath, keyPath);
        _privateKey = RSA.Create();
        _privateKey.ImportFromPem(File.ReadAllText(keyPath, Encoding.UTF8));
        _certificate = X509Certificate2.CreateFromPem(File.ReadAllText(certPath, Encoding.UTF8)).CopyWithPrivateKey(_privateKey);
        _authority = configuration["Timestamping:Local:Authority"] ?? "DocumentFlow Local Test TSA";
        _trust.Add(_certificate);
    }

    public string ProviderType => "Local";

    public Task<TimestampIssue> IssueAsync(byte[] signatureBytes, byte[] nonce, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var imprint = SHA256.HashData(signatureBytes);
        var serial = new BigInteger(RandomNumberGenerator.GetBytes(16), isUnsigned: true, isBigEndian: true);
        var genTime = DateTime.UtcNow;
        var tstInfo = Rfc3161Codec.CreateTstInfo(imprint, serial, genTime, nonce);
        var cms = new SignedCms(new ContentInfo(new Oid(Rfc3161Codec.TstInfoOid), tstInfo), detached: false);
        var signer = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, _certificate)
        {
            DigestAlgorithm = new Oid(Rfc3161Codec.Sha256Oid),
            IncludeOption = X509IncludeOption.EndCertOnly
        };
        Rfc3161Codec.AddSigningCertificateV2(signer, _certificate);
        cms.ComputeSignature(signer, silent: true);
        var token = cms.Encode();
        var verification = Rfc3161Codec.Verify(token, signatureBytes, nonce, _trust);
        if (verification.Result != "Valid") throw new CryptographicException("Locally generated RFC 3161 token failed verification.");
        return Task.FromResult(new TimestampIssue(token, _authority, serial.ToString("X"), "SHA-256",
            Convert.ToHexString(imprint), genTime, ProviderType));
    }

    public TimestampVerification Verify(byte[] token, byte[] expectedSignatureBytes, byte[] expectedNonce) =>
        Rfc3161Codec.Verify(token, expectedSignatureBytes, expectedNonce, _trust);

    public void Dispose()
    {
        _certificate.Dispose();
        _privateKey.Dispose();
        foreach (var certificate in _trust) if (!ReferenceEquals(certificate, _certificate)) certificate.Dispose();
    }

    private static void CreateCertificate(string certPath, string keyPath)
    {
        using var key = RSA.Create(3072);
        var request = new CertificateRequest("CN=DocumentFlow Local Test TSA", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        var eku = new OidCollection { new(Rfc3161Codec.TimestampingEkuOid) };
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(eku, critical: true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));
        using (var stream = new FileStream(keyPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.Write(key.ExportPkcs8PrivateKeyPem());
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(keyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.WriteAllText(certPath, certificate.ExportCertificatePem(), new UTF8Encoding(false));
    }
}

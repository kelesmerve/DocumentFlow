using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace DocumentFlow.Infrastructure.Signatures;

public sealed record SigningKey(string Fingerprint, RSA PrivateKey);

public interface IRsaSigningKeyStore
{
    SigningKey? GetCurrentSigningKey();
    RSA? GetTrustedPublicKey(string fingerprint);
    string? RotateDevelopmentKey();
}

public sealed class RsaSigningKeyStore(bool isDevelopment, IConfiguration configuration) : IRsaSigningKeyStore
{
    private static readonly Regex FingerprintPattern = new("^[A-F0-9]{64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly object DevelopmentKeyLock = new();
    private readonly string? _directory = ResolveDirectory(isDevelopment, configuration);

    public SigningKey? GetCurrentSigningKey()
    {
        if (_directory is null) return null;
        var fingerprint = isDevelopment
            ? ReadOrCreateDevelopmentKey()
            : configuration["SignatureSigning:CurrentKeyFingerprint"];
        if (fingerprint is null || !FingerprintPattern.IsMatch(fingerprint)) return null;

        var privatePath = PrivateKeyPath(fingerprint);
        if (!File.Exists(privatePath)) return null;
        try
        {
            var rsa = RSA.Create();
            rsa.ImportFromPem(File.ReadAllText(privatePath, Encoding.UTF8));
            if (!string.Equals(Fingerprint(rsa), fingerprint, StringComparison.Ordinal))
            {
                rsa.Dispose();
                return null;
            }
            return new SigningKey(fingerprint, rsa);
        }
        catch (CryptographicException) { return null; }
        catch (IOException) { return null; }
    }

    public RSA? GetTrustedPublicKey(string fingerprint)
    {
        if (_directory is null || !FingerprintPattern.IsMatch(fingerprint)) return null;
        var publicPath = PublicKeyPath(fingerprint);
        if (!File.Exists(publicPath)) return null;
        try
        {
            var rsa = RSA.Create();
            rsa.ImportFromPem(File.ReadAllText(publicPath, Encoding.UTF8));
            if (!string.Equals(Fingerprint(rsa), fingerprint, StringComparison.Ordinal))
            {
                rsa.Dispose();
                return null;
            }
            return rsa;
        }
        catch (CryptographicException) { return null; }
        catch (IOException) { return null; }
    }

    public string? RotateDevelopmentKey()
    {
        if (!isDevelopment || _directory is null) return null;
        lock (DevelopmentKeyLock) return CreateKeyPair(makeCurrent: true);
    }

    private string ReadOrCreateDevelopmentKey()
    {
        Directory.CreateDirectory(_directory!);
        var currentPath = Path.Combine(_directory!, "current-key.txt");
        if (File.Exists(currentPath))
        {
            var current = File.ReadAllText(currentPath, Encoding.ASCII).Trim();
            if (FingerprintPattern.IsMatch(current) && File.Exists(PrivateKeyPath(current)) && File.Exists(PublicKeyPath(current))) return current;
        }

        lock (DevelopmentKeyLock)
        {
            if (File.Exists(currentPath))
            {
                var current = File.ReadAllText(currentPath, Encoding.ASCII).Trim();
                if (FingerprintPattern.IsMatch(current) && File.Exists(PrivateKeyPath(current)) && File.Exists(PublicKeyPath(current))) return current;
            }
            using var processLock = new FileStream(Path.Combine(_directory!, ".key-generation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            if (File.Exists(currentPath))
            {
                var current = File.ReadAllText(currentPath, Encoding.ASCII).Trim();
                if (FingerprintPattern.IsMatch(current) && File.Exists(PrivateKeyPath(current)) && File.Exists(PublicKeyPath(current))) return current;
            }
            return CreateKeyPair(makeCurrent: true);
        }
    }

    private string CreateKeyPair(bool makeCurrent)
    {
        Directory.CreateDirectory(_directory!);
        using var rsa = RSA.Create(3072);
        var fingerprint = Fingerprint(rsa);
        WritePrivateKey(PrivateKeyPath(fingerprint), rsa.ExportPkcs8PrivateKeyPem());
        File.WriteAllText(PublicKeyPath(fingerprint), rsa.ExportSubjectPublicKeyInfoPem(), new UTF8Encoding(false));
        if (makeCurrent) File.WriteAllText(Path.Combine(_directory!, "current-key.txt"), fingerprint, Encoding.ASCII);
        return fingerprint;
    }

    private static void WritePrivateKey(string path, string pem)
    {
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.Write(pem);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private string PrivateKeyPath(string fingerprint) => Path.Combine(_directory!, $"{fingerprint}.private.pem");
    private string PublicKeyPath(string fingerprint) => Path.Combine(_directory!, $"{fingerprint}.public.pem");

    private static string Fingerprint(RSA rsa) => Convert.ToHexString(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo()));

    private static string? ResolveDirectory(bool isDevelopment, IConfiguration configuration)
    {
        var configured = configuration["SignatureSigning:KeyDirectory"];
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);
        if (!isDevelopment) return null;
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return string.IsNullOrWhiteSpace(local) ? null : Path.Combine(local, "DocumentFlow", "signing-keys");
    }
}

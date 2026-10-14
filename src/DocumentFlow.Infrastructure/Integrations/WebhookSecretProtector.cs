using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;

namespace DocumentFlow.Infrastructure.Integrations;

public sealed class WebhookSecretProtector(IConfiguration configuration)
{
    private byte[] Key()
    {
        var configured = configuration["WebhookSecurity:EncryptionKey"];
        byte[] key;
        try { key = Convert.FromBase64String(configured ?? string.Empty); }
        catch (FormatException) { throw new InvalidOperationException("Webhook encryption key is not configured."); }
        if (key.Length != 32) throw new InvalidOperationException("Webhook encryption key is not configured.");
        return key;
    }

    public string Protect(string secret)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var clear = System.Text.Encoding.UTF8.GetBytes(secret);
        var encrypted = new byte[clear.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(Key(), 16)) aes.Encrypt(nonce, clear, encrypted, tag);
        return Convert.ToBase64String([.. nonce, .. tag, .. encrypted]);
    }

    public string Unprotect(string encoded)
    {
        var data = Convert.FromBase64String(encoded);
        if (data.Length < 28) throw new CryptographicException("Invalid webhook secret record.");
        var clear = new byte[data.Length - 28];
        using (var aes = new AesGcm(Key(), 16)) aes.Decrypt(data[..12], data[28..], data[12..28], clear);
        return System.Text.Encoding.UTF8.GetString(clear);
    }
}

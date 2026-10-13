using System.Formats.Asn1;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using DocumentFlow.Application.Timestamps;

namespace DocumentFlow.Infrastructure.Timestamps;

internal sealed record ParsedTstInfo(string AlgorithmOid, byte[] Imprint, string SerialNumber, DateTime GenTimeUtc, byte[] Nonce);

internal static class Rfc3161Codec
{
    internal const string Sha256Oid = "2.16.840.1.101.3.4.2.1";
    internal const string TstInfoOid = "1.2.840.113549.1.9.16.1.4";
    internal const string TimestampingEkuOid = "1.3.6.1.5.5.7.3.8";
    private const string SigningCertificateV2Oid = "1.2.840.113549.1.9.16.2.47";
    private const string TsaPolicyOid = "1.3.6.1.4.1.57264.1.1";

    internal static byte[] CreateTstInfo(byte[] imprint, BigInteger serial, DateTime genTimeUtc, byte[] nonce)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        writer.PushSequence();
        writer.WriteInteger(1);
        writer.WriteObjectIdentifier(TsaPolicyOid);
        writer.PushSequence();
        writer.PushSequence();
        writer.WriteObjectIdentifier(Sha256Oid);
        writer.WriteNull();
        writer.PopSequence();
        writer.WriteOctetString(imprint);
        writer.PopSequence();
        writer.WriteInteger(serial);
        writer.WriteGeneralizedTime(new DateTimeOffset(genTimeUtc), omitFractionalSeconds: false);
        writer.WriteInteger(new BigInteger(nonce, isUnsigned: true, isBigEndian: true));
        writer.PopSequence();
        return writer.Encode();
    }

    internal static ParsedTstInfo ParseTstInfo(byte[] encoded)
    {
        var reader = new AsnReader(encoded, AsnEncodingRules.DER);
        var sequence = reader.ReadSequence();
        if (sequence.ReadInteger() != 1) throw new CryptographicException("Unsupported RFC 3161 TSTInfo version.");
        _ = sequence.ReadObjectIdentifier();
        var imprintSequence = sequence.ReadSequence();
        var algorithmSequence = imprintSequence.ReadSequence();
        var algorithm = algorithmSequence.ReadObjectIdentifier();
        if (algorithmSequence.HasData) algorithmSequence.ReadNull();
        algorithmSequence.ThrowIfNotEmpty();
        var imprint = imprintSequence.ReadOctetString();
        imprintSequence.ThrowIfNotEmpty();
        var serial = sequence.ReadInteger();
        if (serial <= BigInteger.Zero) throw new CryptographicException("RFC 3161 serial number must be positive.");
        var genTime = sequence.ReadGeneralizedTime().UtcDateTime;
        if (!sequence.HasData) throw new CryptographicException("RFC 3161 token has no nonce.");
        var nonceValue = sequence.ReadInteger();
        if (nonceValue <= BigInteger.Zero) throw new CryptographicException("RFC 3161 nonce must be positive.");
        var nonce = nonceValue.ToByteArray(isUnsigned: true, isBigEndian: true);
        sequence.ThrowIfNotEmpty();
        reader.ThrowIfNotEmpty();
        return new ParsedTstInfo(algorithm, imprint, serial.ToString("X"), DateTime.SpecifyKind(genTime, DateTimeKind.Utc), nonce);
    }

    internal static byte[] CreateRequest(byte[] imprint, byte[] nonce)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        writer.PushSequence();
        writer.WriteInteger(1);
        writer.PushSequence();
        writer.PushSequence();
        writer.WriteObjectIdentifier(Sha256Oid);
        writer.WriteNull();
        writer.PopSequence();
        writer.WriteOctetString(imprint);
        writer.PopSequence();
        writer.WriteInteger(new BigInteger(nonce, isUnsigned: true, isBigEndian: true));
        writer.WriteBoolean(true);
        writer.PopSequence();
        return writer.Encode();
    }

    internal static byte[] ParseResponse(byte[] response)
    {
        var reader = new AsnReader(response, AsnEncodingRules.DER);
        var sequence = reader.ReadSequence();
        var statusInfo = sequence.ReadSequence();
        var status = (int)statusInfo.ReadInteger();
        if (status is not (0 or 1)) throw new CryptographicException($"TSA returned PKI status {status}.");
        if (statusInfo.HasData && statusInfo.PeekTag().HasSameClassAndValue(Asn1Tag.Sequence))
        {
            var statusText = statusInfo.ReadSequence();
            while (statusText.HasData) _ = statusText.ReadCharacterString(UniversalTagNumber.UTF8String);
            statusText.ThrowIfNotEmpty();
        }
        if (statusInfo.HasData) _ = statusInfo.ReadBitString(out _);
        statusInfo.ThrowIfNotEmpty();
        if (!sequence.HasData) throw new CryptographicException("TSA response has no timestamp token.");
        var token = sequence.ReadEncodedValue().ToArray();
        sequence.ThrowIfNotEmpty();
        reader.ThrowIfNotEmpty();
        return token;
    }

    internal static SignedCms ReadSignedCms(byte[] token)
    {
        var cms = new SignedCms();
        cms.Decode(token);
        if (cms.ContentInfo.ContentType.Value != TstInfoOid || cms.SignerInfos.Count != 1)
            throw new CryptographicException("CMS token is not a single-signer RFC 3161 TSTInfo token.");
        if (cms.SignerInfos[0].Certificate is null) throw new MissingTimestampCertificateException();
        cms.CheckSignature(verifySignatureOnly: true);
        return cms;
    }

    internal static TimestampVerification Verify(byte[] token, byte[] expectedSignatureBytes, byte[] expectedNonce,
        X509Certificate2Collection trustAnchors)
    {
        if (token.Length == 0 || expectedSignatureBytes.Length == 0)
            return new("Unverifiable", "Timestamp token or signature bytes are missing.", null, null, null, null, null);
        try
        {
            var cms = ReadSignedCms(token);
            var tst = ParseTstInfo(cms.ContentInfo.Content);
            if (tst.AlgorithmOid != Sha256Oid || !CryptographicOperations.FixedTimeEquals(tst.Imprint, SHA256.HashData(expectedSignatureBytes)))
                return Invalid("Timestamp message imprint does not match the signed signature bytes.", tst);
            var expectedNonceValue = new BigInteger(expectedNonce, isUnsigned: true, isBigEndian: true);
            var tokenNonceValue = new BigInteger(tst.Nonce, isUnsigned: true, isBigEndian: true);
            if (tokenNonceValue != expectedNonceValue) return Invalid("Timestamp nonce does not match the request.", tst);
            if (cms.SignerInfos[0].SignerIdentifier.Type != SubjectIdentifierType.IssuerAndSerialNumber)
                return Invalid("Timestamp signer identifier is not issuer and serial number.", tst);
            var signer = cms.SignerInfos[0].Certificate;
            if (signer is null) return new("Unverifiable", "Timestamp signer certificate is missing.", null, tst.SerialNumber, "SHA-256", Convert.ToHexString(tst.Imprint), tst.GenTimeUtc);
            if (!HasMatchingSigningCertificateV2(cms.SignerInfos[0], signer)) return Invalid("CMS SigningCertificateV2 does not identify the TSA signing certificate.", tst);
            if (!HasTimestampEku(signer)) return Invalid("TSA certificate lacks the critical timestamping EKU.", tst);
            if (tst.GenTimeUtc < signer.NotBefore.ToUniversalTime() || tst.GenTimeUtc > signer.NotAfter.ToUniversalTime())
                return Invalid("TSA certificate was not valid at the timestamp generation time.", tst);
            if (tst.GenTimeUtc > DateTime.UtcNow.AddMinutes(5)) return Invalid("Timestamp generation time is in the future.", tst);
            if (tst.Imprint.Length != 32 || tst.SerialNumber.TrimStart('0').Length == 0) return Invalid("Timestamp imprint or serial number has an invalid length or value.", tst);
            if (trustAnchors.Count == 0) return new("Unverifiable", "No configured trusted TSA certificate is available.", signer.Subject, tst.SerialNumber, "SHA-256", Convert.ToHexString(tst.Imprint), tst.GenTimeUtc);
            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.AddRange(trustAnchors);
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.VerificationTime = tst.GenTimeUtc;
            chain.ChainPolicy.ApplicationPolicy.Add(new Oid(TimestampingEkuOid));
            if (!chain.Build(signer)) return new("Unverifiable", "TSA certificate chain is not trusted by configured anchors.", signer.Subject, tst.SerialNumber, "SHA-256", Convert.ToHexString(tst.Imprint), tst.GenTimeUtc);
            return new("Valid", "RFC 3161 CMS signature, certificate chain, timestamp EKU, imprint, nonce, serial and generation time verified.", signer.Subject, tst.SerialNumber, "SHA-256", Convert.ToHexString(tst.Imprint), tst.GenTimeUtc);
        }
        catch (MissingTimestampCertificateException exception) { return new("Unverifiable", exception.Message, null, null, null, null, null); }
        catch (CryptographicException exception) { return new("Invalid", exception.Message, null, null, null, null, null); }
        catch (AsnContentException exception) { return new("Invalid", exception.Message, null, null, null, null, null); }
        catch (ArgumentException exception) { return new("Invalid", exception.Message, null, null, null, null, null); }
    }

    internal static bool HasTimestampEku(X509Certificate2 certificate)
    {
        var eku = certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().SingleOrDefault();
        return eku is { Critical: true } && eku.EnhancedKeyUsages.Count == 1 && eku.EnhancedKeyUsages[0].Value == TimestampingEkuOid;
    }

    internal static void AddSigningCertificateV2(CmsSigner signer, X509Certificate2 certificate)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        writer.PushSequence();
        writer.PushSequence();
        writer.PushSequence();
        writer.WriteObjectIdentifier(Sha256Oid);
        writer.WriteNull();
        writer.PopSequence();
        writer.WriteOctetString(SHA256.HashData(certificate.RawData));
        writer.PopSequence();
        writer.PopSequence();
        signer.SignedAttributes.Add(new Pkcs9AttributeObject(new Oid(SigningCertificateV2Oid), writer.Encode()));
    }

    private static bool HasMatchingSigningCertificateV2(SignerInfo signerInfo, X509Certificate2 certificate)
    {
        var attribute = signerInfo.SignedAttributes.Cast<CryptographicAttributeObject>().SingleOrDefault(x => x.Oid?.Value == SigningCertificateV2Oid);
        if (attribute is null || attribute.Values.Count != 1) return false;
        try
        {
            var reader = new AsnReader(attribute.Values[0].RawData, AsnEncodingRules.DER);
            var certificates = reader.ReadSequence();
            var certificateId = certificates.ReadSequence();
            var tag = certificateId.PeekTag();
            if (tag.HasSameClassAndValue(Asn1Tag.Sequence))
            {
                var algorithm = certificateId.ReadSequence();
                if (algorithm.ReadObjectIdentifier() != Sha256Oid) return false;
                if (algorithm.HasData) algorithm.ReadNull();
                algorithm.ThrowIfNotEmpty();
            }
            var expectedHash = certificateId.ReadOctetString();
            if (expectedHash.Length != 32) return false;
            return CryptographicOperations.FixedTimeEquals(expectedHash, SHA256.HashData(certificate.RawData));
        }
        catch (AsnContentException) { return false; }
    }

    private static TimestampVerification Invalid(string message, ParsedTstInfo tst) => new("Invalid", message, null, tst.SerialNumber, tst.AlgorithmOid, Convert.ToHexString(tst.Imprint), tst.GenTimeUtc);
}

internal sealed class MissingTimestampCertificateException() : CryptographicException("Timestamp signer certificate is missing.");

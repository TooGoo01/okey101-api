using System.Security.Cryptography;
using System.Text;

namespace Okey101.Api.Services;

public class PhoneEncryptionService : IPhoneEncryptionService
{
    private readonly byte[] _key;

    public PhoneEncryptionService(IConfiguration configuration)
    {
        var keyString = configuration["PhoneEncryption:Key"]
            ?? throw new InvalidOperationException("PhoneEncryption:Key is not configured.");
        _key = SHA256.HashData(Encoding.UTF8.GetBytes(keyString));
    }

    public string Encrypt(string phoneNumber)
    {
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(phoneNumber);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        var result = new byte[aes.IV.Length + cipherBytes.Length];
        aes.IV.CopyTo(result, 0);
        cipherBytes.CopyTo(result, aes.IV.Length);

        return Convert.ToBase64String(result);
    }

    public string Decrypt(string encrypted)
    {
        var fullBytes = Convert.FromBase64String(encrypted);

        using var aes = Aes.Create();
        aes.Key = _key;

        var iv = fullBytes[..16];
        var cipherBytes = fullBytes[16..];

        aes.IV = iv;
        using var decryptor = aes.CreateDecryptor();
        var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);

        return Encoding.UTF8.GetString(plainBytes);
    }

    public string Hash(string phoneNumber)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(phoneNumber));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}

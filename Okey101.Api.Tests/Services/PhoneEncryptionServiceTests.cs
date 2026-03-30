using Microsoft.Extensions.Configuration;
using Okey101.Api.Services;

namespace Okey101.Api.Tests.Services;

public class PhoneEncryptionServiceTests
{
    private readonly PhoneEncryptionService _service;

    public PhoneEncryptionServiceTests()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PhoneEncryption:Key"] = "test-encryption-key-for-unit-tests!"
            })
            .Build();

        _service = new PhoneEncryptionService(config);
    }

    [Fact]
    public void Encrypt_ThenDecrypt_ReturnsOriginal()
    {
        var phoneNumber = "+905551234567";

        var encrypted = _service.Encrypt(phoneNumber);
        var decrypted = _service.Decrypt(encrypted);

        Assert.Equal(phoneNumber, decrypted);
    }

    [Fact]
    public void Encrypt_ProducesDifferentCiphertextEachTime()
    {
        var phoneNumber = "+905551234567";

        var encrypted1 = _service.Encrypt(phoneNumber);
        var encrypted2 = _service.Encrypt(phoneNumber);

        Assert.NotEqual(encrypted1, encrypted2); // Different IVs
    }

    [Fact]
    public void Hash_ReturnsDeterministicResult()
    {
        var phoneNumber = "+905551234567";

        var hash1 = _service.Hash(phoneNumber);
        var hash2 = _service.Hash(phoneNumber);

        Assert.Equal(hash1, hash2);
        Assert.Equal(64, hash1.Length); // SHA-256 hex = 64 chars
    }

    [Fact]
    public void Hash_DifferentNumbers_ProduceDifferentHashes()
    {
        var hash1 = _service.Hash("+905551234567");
        var hash2 = _service.Hash("+905559876543");

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void Constructor_WithMissingKey_ThrowsInvalidOperation()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        Assert.Throws<InvalidOperationException>(() => new PhoneEncryptionService(config));
    }
}

namespace Okey101.Api.Services;

public interface IPhoneEncryptionService
{
    string Encrypt(string phoneNumber);
    string Decrypt(string encrypted);
    string Hash(string phoneNumber);
}

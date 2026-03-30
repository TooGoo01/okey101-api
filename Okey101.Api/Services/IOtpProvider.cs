namespace Okey101.Api.Services;

public interface IOtpProvider
{
    Task SendOtpAsync(string phoneNumber, string otpCode);
}

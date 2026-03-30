namespace Okey101.Api.Services;

public class DevOtpProvider : IOtpProvider
{
    private readonly ILogger<DevOtpProvider> _logger;

    public DevOtpProvider(ILogger<DevOtpProvider> logger)
    {
        _logger = logger;
    }

    public Task SendOtpAsync(string phoneNumber, string otpCode)
    {
        _logger.LogInformation("[DEV OTP] Phone: {PhoneNumber}, Code: {OtpCode}", phoneNumber, otpCode);
        return Task.CompletedTask;
    }
}

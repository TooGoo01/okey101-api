using Okey101.Api.Endpoints;
using Okey101.Api.Models.Requests;
using Okey101.Api.Models.Responses;
using Okey101.Api.Services;
using Moq;

namespace Okey101.Api.Tests.Endpoints;

public class AuthEndpointsValidationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("abcdefghij")]
    [InlineData("0551234567")] // starts with 0
    public void ValidatePhoneNumber_InvalidFormats_ThrowsArgumentException(string? phone)
    {
        // Use reflection to test the private static method
        var method = typeof(AuthEndpoints).GetMethod("ValidatePhoneNumber",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        var ex = Assert.Throws<System.Reflection.TargetInvocationException>(
            () => method!.Invoke(null, [phone]));
        Assert.IsType<ArgumentException>(ex.InnerException);
    }

    [Theory]
    [InlineData("+905551234567")]
    [InlineData("905551234567")]
    [InlineData("+1234567890")]
    [InlineData("1234567890123")]
    public void ValidatePhoneNumber_ValidFormats_DoesNotThrow(string phone)
    {
        var method = typeof(AuthEndpoints).GetMethod("ValidatePhoneNumber",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        var exception = Record.Exception(() => method!.Invoke(null, [phone]));
        Assert.Null(exception);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("abcdef")]
    public void ValidateOtpCode_InvalidFormats_ThrowsArgumentException(string? code)
    {
        var method = typeof(AuthEndpoints).GetMethod("ValidateOtpCode",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        var ex = Assert.Throws<System.Reflection.TargetInvocationException>(
            () => method!.Invoke(null, [code]));
        Assert.IsType<ArgumentException>(ex.InnerException);
    }

    [Theory]
    [InlineData("123456")]
    [InlineData("000000")]
    [InlineData("999999")]
    public void ValidateOtpCode_ValidFormats_DoesNotThrow(string code)
    {
        var method = typeof(AuthEndpoints).GetMethod("ValidateOtpCode",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        var exception = Record.Exception(() => method!.Invoke(null, [code]));
        Assert.Null(exception);
    }
}

namespace Okey101.Api.Models.Responses;

public class ApiErrorResponse
{
    public ApiError Error { get; set; } = new();

    public ApiErrorResponse() { }

    public ApiErrorResponse(string code, string message, IEnumerable<ApiErrorDetail>? details = null)
    {
        Error = new ApiError
        {
            Code = code,
            Message = message,
            Details = details ?? []
        };
    }
}

public class ApiError
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public IEnumerable<ApiErrorDetail> Details { get; set; } = [];
}

public class ApiErrorDetail
{
    public string Field { get; set; } = string.Empty;
    public string Issue { get; set; } = string.Empty;
}

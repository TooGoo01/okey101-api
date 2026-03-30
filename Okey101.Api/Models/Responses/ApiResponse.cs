namespace Okey101.Api.Models.Responses;

public class ApiResponse<T>
{
    public T Data { get; set; }

    public ApiResponse(T data)
    {
        Data = data;
    }
}

public class ApiListResponse<T>
{
    public IEnumerable<T> Data { get; set; }
    public int Total { get; set; }

    public ApiListResponse(IEnumerable<T> data, int total)
    {
        Data = data;
        Total = total;
    }
}

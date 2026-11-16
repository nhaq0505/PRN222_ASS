namespace CourtBook.Services.Models;

public class ServiceResult
{
    public bool Succeeded { get; init; }
    public string? ErrorMessage { get; init; }

    public static ServiceResult Ok() => new() { Succeeded = true };
    public static ServiceResult Fail(string errorMessage) => new() { Succeeded = false, ErrorMessage = errorMessage };
}

public class ServiceResult<T> : ServiceResult
{
    public T? Data { get; init; }

    public static ServiceResult<T> Ok(T data) => new() { Succeeded = true, Data = data };
    public new static ServiceResult<T> Fail(string errorMessage) => new() { Succeeded = false, ErrorMessage = errorMessage };
}

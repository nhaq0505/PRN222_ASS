namespace CourtBook.Services.Models;

public class ServiceResult
{
    public bool Succeeded { get; init; }
    public string? ErrorMessage { get; init; }
    public string? WarningMessage { get; init; }

    public static ServiceResult Ok(string? warningMessage = null)
        => new() { Succeeded = true, WarningMessage = warningMessage };
    public static ServiceResult Fail(string errorMessage)
        => new() { Succeeded = false, ErrorMessage = errorMessage };
}

public class ServiceResult<T> : ServiceResult
{
    public T? Data { get; init; }

    public static ServiceResult<T> Ok(T data, string? warningMessage = null)
        => new() { Succeeded = true, Data = data, WarningMessage = warningMessage };
    public new static ServiceResult<T> Fail(string errorMessage)
        => new() { Succeeded = false, ErrorMessage = errorMessage };
}

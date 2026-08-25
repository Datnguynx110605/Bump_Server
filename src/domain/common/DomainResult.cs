namespace domain;

public class DomainResult
{
    public bool IsSuccess { get; private set; }
    public string? Error { get; private set; }

    protected DomainResult(bool isSuccess, string? error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public static DomainResult Success() => new(true, null);

    public static DomainResult Failure(string error) => new(false, error);

    public static DomainResult<T> Success<T>(T value) => DomainResult<T>.Success(value);

    public static DomainResult<T> Failure<T>(string error) => DomainResult<T>.Failure(error);
}

public class DomainResult<T> : DomainResult
{
    public T? Value { get; private set; }

    private DomainResult(bool isSuccess, T? value, string? error)
        : base(isSuccess, error)
    {
        Value = value;
    }

    public static DomainResult<T> Success(T value) => new(true, value, null);

    public static new DomainResult<T> Failure(string error) => new(false, default, error);
}

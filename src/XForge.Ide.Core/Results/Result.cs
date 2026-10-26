namespace XForge.Ide.Core.Results;

/// <summary>Resultado de operação sem valor de retorno.</summary>
public class Result
{
    public bool IsSuccess { get; }
    public string? Error { get; }

    protected Result(bool isSuccess, string? error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public static Result Success() => new(true, null);
    public static Result Failure(string error) => new(false, error);
}

/// <summary>Resultado de operação com valor.</summary>
public class Result<T> : Result
{
    private readonly T? _value;

    private Result(bool isSuccess, T? value, string? error) : base(isSuccess, error)
        => _value = value;

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Resultado não contém valor: " + Error);

    public static Result<T> Success(T value) => new(true, value, null);
    public static new Result<T> Failure(string error) => new(false, default, error);
}

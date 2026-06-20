namespace CP6.Core.Services.Fin;

/// <summary>带数据的结果（既有 FinResult 无 Data 时用）。</summary>
public class FinResult<T>
{
    public bool Ok { get; init; }
    public string? Code { get; init; }
    public object[]? Args { get; init; }
    public T? Data { get; init; }
    public static FinResult<T> Pass(T data) => new() { Ok = true, Data = data };
    public static FinResult<T> Fail(string code, params object[] args) => new() { Ok = false, Code = code, Args = args };
}

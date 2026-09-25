namespace CP6.Entity.DTOs.Plm;

public sealed class PlmException : Exception
{
    public string Code { get; }
    public int HttpStatus { get; }

    public PlmException(string code, int httpStatus, string message) : base(message)
    {
        Code = code;
        HttpStatus = httpStatus;
    }

    public static PlmException Conflict(string code) => new(code, 409, code);
    public static PlmException Invalid(string code) => new(code, 422, code);
    public static PlmException NotFound(string code) => new(code, 404, code);
}

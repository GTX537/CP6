namespace CP6.Core.Services.Sys;
public interface ICurrentUserAccessor { Guid? UserId { get; } string? UserName { get; } }

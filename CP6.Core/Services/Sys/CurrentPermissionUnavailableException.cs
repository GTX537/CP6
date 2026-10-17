namespace CP6.Core.Services.Sys;

/// <summary>No current permission conclusion is available; callers must not reuse an earlier allow.</summary>
public sealed class CurrentPermissionUnavailableException : InvalidOperationException
{
    public CurrentPermissionUnavailableException(Exception? innerException = null)
        : base("Current permissions are unavailable.", innerException) { }
}

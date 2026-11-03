namespace CP6.Core.Services.Wf;

/// <summary>One explicit boundary for retired definition writers. Existing version and runtime reads are retained.</summary>
public static class LegacyDefinitionWrites
{
    public const string Code = "DEFINITION_V2_COMMAND_REQUIRED";
    public static Task<T> Rejected<T>() => Task.FromException<T>(new InvalidOperationException(Code));
}

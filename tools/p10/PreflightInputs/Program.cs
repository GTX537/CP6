using CP6.P10.ReleaseVerifier;
using CP6.Platform.Release;

// Test-input materialization only. This executable cannot create a validation/candidate handoff.
try
{
    if (args is not [var requested]) return Refuse("preflight-input-args", 64);
    if (!Path.IsPathFullyQualified(requested)) return Refuse("preflight-input-path", 64);
    var output = Path.GetFullPath(requested);
    if (File.Exists(output) || Directory.Exists(output) || new DirectoryInfo(output).LinkTarget is not null)
        return Refuse("preflight-input-path", 64);
    var parent = Directory.GetParent(output);
    if (parent is null || !parent.Exists) return Refuse("preflight-input-path", 64);
    for (var current = parent; current is not null; current = current.Parent)
        if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
            return Refuse("preflight-input-path", 64);
    var token = Environment.GetEnvironmentVariable("P10_FEED_READ_TOKEN");
    if (token is not { Length: > 0 and <= 4096 } || token.Any(char.IsControl))
        return Refuse("preflight-input-credential", 65);

    var stage = Path.Combine(parent.FullName, ".p10-preflight-inputs-" + Guid.NewGuid().ToString("N"));
    if (OperatingSystem.IsWindows()) Directory.CreateDirectory(stage);
    else Directory.CreateDirectory(stage, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    string[] packageIds =
    [
        "CP6.Platform.Abstractions", "CP6.Platform.AspNetCore", "CP6.Platform.Contracts",
        "CP6.Platform.Deployment", "CP6.Platform.EntityFramework", "CP6.Platform.Messaging", "CP6.Platform.Release"
    ];
    foreach (var id in packageIds)
    {
        var package = await FormalPackageSource.DownloadAndVerifyAsync(id, token);
        var path = Path.Combine(stage, id + ".0.10.1.nupkg");
        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await file.WriteAsync(package.CopyPackageBytes());
    }
    // Same-parent rename hands off only a complete verified set, without overwriting an existing output.
    Directory.Move(stage, output);
    Console.WriteLine("{\"state\":\"PreflightInputsOnly\",\"packageCount\":7,\"candidateAccepted\":false,\"deployable\":false}");
    return 0;
}
catch (Cp6ReleaseContractException error)
{
    return Refuse(error.Code, 1);
}
catch (Exception)
{
    // Do not expose credentials, package bytes, signed redirect URLs or local paths.
    return Refuse("preflight-input-io", 1);
}

static int Refuse(string code, int exit)
{
    Console.Error.WriteLine(code);
    return exit;
}

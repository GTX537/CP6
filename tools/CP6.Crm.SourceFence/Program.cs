using System.Globalization;
using System.Text.Json;
using CP6.Crm.SourceFence;

var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
try
{
    if (args.Length == 1 && (args[0] == "seal-forward-only-actual" || args[0] is "reopen-actual" or "recovery-status-actual"
        && (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("C04A_RECOVERY_REQUEST_PATH"))
            || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("C04A_TARGET_SQL_CONNECTION")))))
        throw new SourceFenceException("C04A_TARGET_ROLLBACK_COORDINATOR_REQUIRED");
    if (args.Length != 1 || args[0] is not ("status" or "preflight" or "freeze" or "reopen" or "seal-forward-only" or "inspect-actual" or "freeze-actual" or "status-actual" or "reopen-actual" or "recovery-status-actual"))
        throw new SourceFenceException("C04A_INVALID_COMMAND");
    var options = new SourceFenceOptions(Required("C04A_SQL_CONNECTION"), Required("C04A_EXPECTED_DATABASE"),
        Guid.Parse(Required("C04A_EXPECTED_DATABASE_GUID")),
        int.Parse(Environment.GetEnvironmentVariable("C04A_LOCK_TIMEOUT_MS") ?? "5000", CultureInfo.InvariantCulture),
        int.Parse(Environment.GetEnvironmentVariable("C04A_COMMAND_TIMEOUT_SECONDS") ?? "30", CultureInfo.InvariantCulture));
    if (args[0] is "reopen-actual" or "recovery-status-actual")
    {
        var request = await ActualSourceFreezer.ReadRecoveryRequestAsync(Required("C04A_RECOVERY_REQUEST_PATH"), Required("C04A_RECOVERY_REQUEST_FILE_SHA256"));
        var freezer = new ActualSourceFreezer(new(options.ConnectionString, options.ExpectedDatabaseName,
            options.ExpectedDatabaseGuid, Required("C04A_EXPECTED_SERVER_NAME"),
            Environment.GetEnvironmentVariable("C04A_EXPECTED_SCOPE_SHA256"), options.LockTimeoutMilliseconds, options.CommandTimeoutSeconds));
        var target = Required("C04A_TARGET_SQL_CONNECTION");
        Console.WriteLine(JsonSerializer.Serialize(args[0] == "reopen-actual"
            ? await freezer.ReopenAsync(request, request.Digest(), target) : await freezer.RecoveryStatusAsync(request, request.Digest(), target), json));
        return 0;
    }
    if (args[0] is "freeze-actual" or "status-actual")
    {
        var request = await ActualSourceFreezer.ReadRequestAsync(Required("C04A_REQUEST_PATH"), Required("C04A_REQUEST_FILE_SHA256"));
        var freezer = new ActualSourceFreezer(new(options.ConnectionString, options.ExpectedDatabaseName,
            options.ExpectedDatabaseGuid, Required("C04A_EXPECTED_SERVER_NAME"),
            Environment.GetEnvironmentVariable("C04A_EXPECTED_SCOPE_SHA256"), options.LockTimeoutMilliseconds, options.CommandTimeoutSeconds));
        Console.WriteLine(JsonSerializer.Serialize(args[0] == "freeze-actual"
            ? await freezer.FreezeAsync(request, request.Digest()) : await freezer.StatusAsync(request.Digest()), json));
        return 0;
    }
    if (args[0] == "inspect-actual")
    {
        var inspector = new ActualSourceInspector(new(options.ConnectionString, options.ExpectedDatabaseName,
            options.ExpectedDatabaseGuid, Required("C04A_EXPECTED_SERVER_NAME"),
            Environment.GetEnvironmentVariable("C04A_EXPECTED_SCOPE_SHA256"), options.LockTimeoutMilliseconds,
            options.CommandTimeoutSeconds));
        Console.WriteLine(JsonSerializer.Serialize(await inspector.InspectAsync(), json));
        return 0;
    }
    var fence = new SourceFence(options);
    SourceFenceStatus status;
    if (args[0] == "status") status = await fence.StatusAsync();
    else if (args[0] == "preflight") status = await fence.PreflightAsync();
    else
    {
        var run = Guid.Parse(Required("C04A_RUN_ID"));
        var generation = long.Parse(Required("C04A_EXPECTED_GENERATION"), CultureInfo.InvariantCulture);
        status = args[0] switch
        {
            "freeze" => await fence.FreezeAsync(run, generation),
            "reopen" => await fence.ReopenAsync(run, generation),
            _ => await fence.SealForwardOnlyAsync(run, generation)
        };
    }
    Console.WriteLine(JsonSerializer.Serialize(status, json));
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(JsonSerializer.Serialize(new { error = error is SourceFenceException failure ? failure.Code : "C04A_INVALID_OPTIONS" }, json));
    return 2;
}

static string Required(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
    ? value : throw new SourceFenceException("C04A_INVALID_OPTIONS");

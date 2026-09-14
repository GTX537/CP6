using System.Globalization;
using System.Text.Json;
using CP6.Crm.SourceFence;

var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
try
{
    if (args.Length == 1 && args[0] == "inspect-container")
    {
        var container = await LocalSqlContainerInspector.InspectAsync(Required("C04A_DOCKER_ENGINE_PIPE"), Required("C04A_DOCKER_CONTAINER_ID"),
            int.Parse(Required("C04A_DOCKER_HOST_PORT"), CultureInfo.InvariantCulture),
            int.Parse(Environment.GetEnvironmentVariable("C04A_DOCKER_SQL_PORT") ?? "1433", CultureInfo.InvariantCulture));
        // This directly reusable bound-file document uses the DTO's PascalCase names.
        Console.WriteLine(JsonSerializer.Serialize(container));
        return 0;
    }
    if (args.Length == 1 && (args[0] == "seal-forward-only-actual" || args[0] is "reopen-actual" or "recovery-status-actual"
        && (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("C04A_RECOVERY_REQUEST_PATH"))
            || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("C04A_TARGET_SQL_CONNECTION")))))
        throw new SourceFenceException("C04A_TARGET_ROLLBACK_COORDINATOR_REQUIRED");
    if (args.Length == 1 && args[0] is "reopen-targets-actual" or "recovery-status-targets-actual"
        && (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("C04A_RECOVERY_REQUEST_PATH"))
            || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("C04A_RECOVERY_TARGETS_PATH"))))
        throw new SourceFenceException("C04A_TARGET_ROLLBACK_COORDINATOR_REQUIRED");
    if (args.Length != 1 || args[0] is not ("status" or "preflight" or "freeze" or "reopen" or "seal-forward-only" or "inspect-actual" or "freeze-actual" or "status-actual" or "reopen-actual" or "recovery-status-actual" or "reopen-targets-actual" or "recovery-status-targets-actual"))
        throw new SourceFenceException("C04A_INVALID_COMMAND");
    var options = new SourceFenceOptions(Required("C04A_SQL_CONNECTION"), Required("C04A_EXPECTED_DATABASE"),
        Guid.Parse(Required("C04A_EXPECTED_DATABASE_GUID")),
        int.Parse(Environment.GetEnvironmentVariable("C04A_LOCK_TIMEOUT_MS") ?? "5000", CultureInfo.InvariantCulture),
        int.Parse(Environment.GetEnvironmentVariable("C04A_COMMAND_TIMEOUT_SECONDS") ?? "30", CultureInfo.InvariantCulture));
    LocalSqlContainerBinding? localContainer = null;
    if (Environment.GetEnvironmentVariable("C04A_CONTAINER_BINDING_PATH") is { Length: > 0 } bindingPath)
    {
        if (args[0] is not ("inspect-actual" or "freeze-actual" or "status-actual" or "reopen-actual" or "recovery-status-actual" or "reopen-targets-actual" or "recovery-status-targets-actual"))
            throw new SourceFenceException("C04A_CONTAINER_INVALID_OPTIONS");
        localContainer = await LocalSqlContainerInspector.ReadBindingAsync(bindingPath, Required("C04A_CONTAINER_BINDING_FILE_SHA256"));
    }
    else if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("C04A_CONTAINER_BINDING_FILE_SHA256")))
        throw new SourceFenceException("C04A_CONTAINER_INVALID_OPTIONS");
    if (args[0] is "reopen-actual" or "recovery-status-actual" or "reopen-targets-actual" or "recovery-status-targets-actual")
    {
        var request = await ActualSourceFreezer.ReadRecoveryRequestAsync(Required("C04A_RECOVERY_REQUEST_PATH"), Required("C04A_RECOVERY_REQUEST_FILE_SHA256"));
        var freezer = new ActualSourceFreezer(new(options.ConnectionString, options.ExpectedDatabaseName,
            options.ExpectedDatabaseGuid, Required("C04A_EXPECTED_SERVER_NAME"),
            Environment.GetEnvironmentVariable("C04A_EXPECTED_SCOPE_SHA256"), options.LockTimeoutMilliseconds, options.CommandTimeoutSeconds, localContainer));
        ActualSourceRecoveryStatus recovery;
        if (args[0] is "reopen-targets-actual" or "recovery-status-targets-actual")
        {
            var targets = await ActualSourceFreezer.ReadRecoveryTargetsAsync(Required("C04A_RECOVERY_TARGETS_PATH"), Required("C04A_RECOVERY_TARGETS_FILE_SHA256"));
            recovery = args[0] == "reopen-targets-actual" ? await freezer.ReopenTargetsAsync(request, request.Digest(), targets)
                : await freezer.RecoveryStatusTargetsAsync(request, request.Digest(), targets);
        }
        else
        {
            var target = Required("C04A_TARGET_SQL_CONNECTION");
            recovery = args[0] == "reopen-actual" ? await freezer.ReopenAsync(request, request.Digest(), target)
                : await freezer.RecoveryStatusAsync(request, request.Digest(), target);
        }
        Console.WriteLine(JsonSerializer.Serialize(recovery, json));
        return 0;
    }
    if (args[0] is "freeze-actual" or "status-actual")
    {
        var request = await ActualSourceFreezer.ReadRequestAsync(Required("C04A_REQUEST_PATH"), Required("C04A_REQUEST_FILE_SHA256"));
        var freezer = new ActualSourceFreezer(new(options.ConnectionString, options.ExpectedDatabaseName,
            options.ExpectedDatabaseGuid, Required("C04A_EXPECTED_SERVER_NAME"),
            Environment.GetEnvironmentVariable("C04A_EXPECTED_SCOPE_SHA256"), options.LockTimeoutMilliseconds, options.CommandTimeoutSeconds, localContainer));
        Console.WriteLine(JsonSerializer.Serialize(args[0] == "freeze-actual"
            ? await freezer.FreezeAsync(request, request.Digest()) : await freezer.StatusAsync(request.Digest()), json));
        return 0;
    }
    if (args[0] == "inspect-actual")
    {
        var inspector = new ActualSourceInspector(new(options.ConnectionString, options.ExpectedDatabaseName,
            options.ExpectedDatabaseGuid, Required("C04A_EXPECTED_SERVER_NAME"),
            Environment.GetEnvironmentVariable("C04A_EXPECTED_SCOPE_SHA256"), options.LockTimeoutMilliseconds,
            options.CommandTimeoutSeconds, localContainer));
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

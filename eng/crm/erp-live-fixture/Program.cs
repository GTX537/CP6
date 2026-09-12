using CP6.ErpLive.Fixture;

using var stopping = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stopping.Cancel(); };
try
{
    switch (args)
    {
        case ["initialize", var input, var parent]:
            await ErpLiveFixture.InitializeAsync(input, parent, stopping.Token);
            break;
        case ["dispatch", var config]:
            await ErpLiveFixture.DispatchAsync(config, stopping.Token);
            break;
        case ["snapshot", var snapshotFixture, var output]:
            await ErpLiveFixture.SnapshotAsync(snapshotFixture, output, stopping.Token);
            break;
        case ["cleanup", var ownedRoot]:
            await ErpLiveFixture.CleanupAsync(ownedRoot, stopping.Token);
            break;
        case ["storage-fault", var faultFixture, var faultTenant, var mode]:
            await ErpLiveFixture.StorageFaultAsync(faultFixture, faultTenant, mode, stopping.Token);
            break;
        case ["replay", var replayFixture, var replayTenant, var message, var operation]:
            await ErpLiveFixture.ReplayAsync(replayFixture, replayTenant, message, operation, stopping.Token);
            break;
        default:
            Console.Error.WriteLine("Usage: erp-live-fixture initialize <input.json> <privateParent> | dispatch <appsettings.Local.json> | snapshot <live-fixture.json> <output.json> | cleanup <ownedDirectory-or-privateParent> | storage-fault <live-fixture.json> <tenantId> <on|off> | replay <live-fixture.json> <tenantId> <messageId> <operationId>");
            Environment.ExitCode = 2;
            break;
    }
}
catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
catch (Exception error)
{
    // SQL/configuration exceptions can contain credentials or private input. Never echo them.
    Console.Error.WriteLine(error is FixtureException failure ? failure.Code : "C03_LIVE_FIXTURE_FAILED");
    await ErpLiveFixture.WritePrivateDiagnosticAsync(args, error);
    Environment.ExitCode = 1;
}

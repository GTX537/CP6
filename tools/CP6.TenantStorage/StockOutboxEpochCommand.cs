using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Core.Persistence.Tenancy;
using CP6.Core.Services.Integration.StockOutbox;
using CP6.Platform.EntityFramework.OutboxV2;
using Npgsql;

internal static class StockOutboxEpochCommand
{
    private sealed record Input(RmaTenantTargetV1 Target, Guid TenantId, Guid OperationId, Guid? DrainId,
        long ExpectedEpoch, string ExpectedControlVersion, string AdminUsername, string AdminPassword,
        string SslMode = "VerifyFull", bool RecoverOnly = false);

    internal static async Task<int> RunAsync()
    {
        var confirmed = false;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            await using var stdin = Console.OpenStandardInput();
            var bytes = new byte[16_385];
            var length = await stdin.ReadAtLeastAsync(bytes, bytes.Length, false, timeout.Token);
            if (length == bytes.Length) throw new TenantProvisioningException("input-rejected");
            using var document = JsonDocument.Parse(bytes.AsMemory(0, length));
            if (document.RootElement.ValueKind != JsonValueKind.Object || !TenantStorageInput.Unique(document.RootElement))
                throw new TenantProvisioningException("input-rejected");
            var input = document.Deserialize<Input>(
                new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow });
            if (input?.Target is null || string.IsNullOrWhiteSpace(input.AdminUsername) || string.IsNullOrEmpty(input.AdminPassword)
                || !Enum.TryParse<SslMode>(input.SslMode, out var ssl) || ssl.ToString() != input.SslMode)
                throw new TenantProvisioningException("input-rejected");
            var address = input.Target.Validate();
            if (ssl != SslMode.VerifyFull && !(ssl == SslMode.Disable && IPAddress.IsLoopback(address)))
                throw new TenantProvisioningException("input-rejected");

            await using var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder
            {
                Host = input.Target.Server, Port = input.Target.Port, Database = input.Target.Database,
                Username = input.AdminUsername, Password = input.AdminPassword, SslMode = ssl,
                Pooling = false, Enlist = false, IncludeErrorDetail = false, SearchPath = "pg_catalog",
                Options = " ", Timeout = 10, CommandTimeout = 45, ApplicationName = "CP6.TenantStorage.StockOutboxEpochV1"
            }.ConnectionString);
            var result = await StockOutboxEpochControlV1.ApplyAsync(connection, input.Target, input.TenantId,
                input.OperationId, input.DrainId, input.ExpectedEpoch, input.ExpectedControlVersion, timeout.Token, input.RecoverOnly);
            confirmed = true;
            Console.WriteLine(result);
            return 0;
        }
        catch (Exception error) when (error is StockOutboxSwitchOutcomeUnknown or OutboxProtocolExceptionV2
            or TenantProvisioningException or JsonException or NpgsqlException or IOException or OperationCanceledException)
        {
            var unknown = confirmed || error is StockOutboxSwitchOutcomeUnknown;
            var code = error switch
            {
                OutboxProtocolExceptionV2 rejected => rejected.Code,
                TenantProvisioningException rejected => rejected.Code,
                PostgresException postgres => "database-" + postgres.SqlState,
                JsonException => "input-rejected",
                _ => unknown ? "retry-same-operation" : "execution-interrupted"
            };
            Console.Error.WriteLine(JsonSerializer.Serialize(new { Outcome = unknown ? "UNKNOWN" : "REJECTED", Code = code }));
            return unknown ? 4 : 1;
        }
    }
}

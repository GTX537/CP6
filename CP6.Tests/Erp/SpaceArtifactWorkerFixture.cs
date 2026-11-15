using System.Data.Common;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.Space.Observability;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CP6.Tests.Erp;

internal sealed class SpaceArtifactWorkerFixture(string root) : IStartupFilter
{
    internal const string RootVariable = "CP6_SPACE_ARTIFACT_FIXTURE_ROOT";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static void Configure(IServiceCollection services)
    {
        if (Environment.GetEnvironmentVariable(RootVariable) is not { } root)
            return;

        services.AddSingleton<IStartupFilter>(new SpaceArtifactWorkerFixture(root));
        services.AddScoped<ISpaceCadParseProvider, ArtifactFixtureCadProvider>();
        services.AddScoped<ArtifactFault>();

        // The genuine lease ledger keeps the original options. Faults target only the artifact owner's context.
        services.AddScoped(provider => new SpaceContext(
            new DbContextOptionsBuilder<SpaceContext>(provider.GetRequiredService<DbContextOptions<SpaceContext>>())
                .AddInterceptors(new ArtifactTransactionFault(provider.GetRequiredService<ArtifactFault>()),
                    new ArtifactSaveFault(provider.GetRequiredService<ArtifactFault>()))
                .Options,
            provider.GetRequiredService<CP6.Space.Application.ISpaceExecutionContext>(),
            provider.GetRequiredService<ISpaceClock>()));
    }

    Action<IApplicationBuilder> IStartupFilter.Configure(Action<IApplicationBuilder> next) => app =>
    {
        next(app);
        var lifetime = app.ApplicationServices.GetRequiredService<IHostApplicationLifetime>();
        lifetime.ApplicationStarted.Register(() => _ = RunAsync(app.ApplicationServices, lifetime.ApplicationStopping));
    };

    private async Task RunAsync(IServiceProvider rootServices, CancellationToken cancellationToken)
    {
        for (var sequence = 1; !cancellationToken.IsCancellationRequested; sequence++)
        {
            try
            {
                var request = await ReadAsync<ArtifactCommand>(root, sequence, "request", cancellationToken);
                var result = await ExecuteAsync(rootServices, request, sequence, cancellationToken);
                Write(root, sequence, "result", result);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception error)
            {
                Write(root, sequence, "result", new ArtifactResult(Guid.Empty, null, null, error.GetType().Name,
                    error.Message, null, false, []));
            }
        }
    }

    private async Task<ArtifactResult> ExecuteAsync(IServiceProvider rootServices, ArtifactCommand command, int sequence, CancellationToken ct)
    {
        await using var scope = rootServices.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<ITenantContext>().CurrentTenantId = command.TenantId;
        var execution = SpaceExecutionContext.ForSystem(command.TenantId, "space-worker:processing-fixture",
            Guid.NewGuid(), ActivityTraceId.CreateRandom().ToHexString(), command.JobId);
        using var owned = services.GetRequiredService<ISpaceExecutionContextManager>().Push(execution);
        var processor = services.GetServices<ISpaceJobProcessor>().Single(item => item.JobType == command.JobType);
        var ledger = services.GetRequiredService<ISpaceJobLeaseStore>();
        SpaceJobLease? lease = null;
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            deadline.CancelAfter(TimeSpan.FromSeconds(15));
            while (lease is null)
            {
                lease = await ledger.TryClaimNextAsync("w613-artifact-worker", processor.ProcessorVersion,
                    TimeSpan.FromSeconds(90), [command.JobType], deadline.Token);
                if (lease is null)
                    await Task.Delay(100, deadline.Token);
            }
        }

        if (lease.JobId != command.JobId)
            throw new InvalidOperationException("The fixture claimed a different Job.");
        var step = processor.StepCodes[0];
        var started = await ledger.StartStepAsync(lease, 1, step, ct);
        lease = started.Lease;
        if (command.Hold == "claim")
        {
            Write(root, sequence, "waiting", lease.AttemptId);
            await ReadAsync<bool>(root, sequence, "release", ct);
        }

        var fault = services.GetRequiredService<ArtifactFault>();
        fault.Arm(root, sequence, command);
        try
        {
            var output = await processor.ExecuteStepAsync(new(lease, 1, step), ct);
            await ledger.CompleteStepAsync(lease, started.StepId, output.CheckpointJson, output.OutputHash, ct);
            return new(lease.AttemptId, output.CheckpointJson, output.OutputHash, null, null, null, fault.AckInjected, fault.PublishedAtFault, fault.ExpiryObservedAt);
        }
        catch (Exception error) when (!ct.IsCancellationRequested)
        {
            var failure = error as SpaceJobProcessingException;
            lease = await ledger.FailStepAsync(lease, started.StepId, ct);
            await ledger.FailJobAsync(lease, failure?.FailureKind ?? SpaceJobFailureKind.Bug,
                failure?.ErrorCode ?? "SPACE_JOB_PROCESSOR_FAILED", "Artifact fixture observed a processor failure.", cancellationToken: ct);
            return new(lease.AttemptId, null, null, error.GetType().Name, error.Message,
                failure?.ErrorCode, fault.AckInjected, fault.PublishedAtFault, fault.ExpiryObservedAt);
        }

    }

    internal static void Write<T>(string root, int sequence, string phase, T value)
    {
        var path = Path.Combine(root, $"{sequence}-{phase}.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(value, Json));
        File.Move(path + ".tmp", path);
    }

    internal static async Task<T> ReadAsync<T>(string root, int sequence, string phase, CancellationToken ct = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (phase != "request") deadline.CancelAfter(TimeSpan.FromSeconds(35));
        var path = Path.Combine(root, $"{sequence}-{phase}.json");
        while (!File.Exists(path))
            await Task.Delay(45, deadline.Token);
        return JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path, deadline.Token), Json)!;
    }

    internal static byte[] SimulatedCadArtifact(SpaceArtifactType type) =>
        JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, type = type.ToString(), fixture = "W613" }, Json);

    internal sealed record ArtifactCommand(Guid TenantId, Guid JobId, SpaceJobType JobType, string FileRoot, string? Hold = null, string? Fault = null);
    internal sealed record ArtifactResult(Guid AttemptId, string? CheckpointJson, string? OutputHash, string? ErrorType, string? Error,
        string? Code, bool AckInjected, string[] PublishedAtFault, DateTime? ExpiryObservedAt = null);

    internal sealed class ArtifactFixtureCadProvider(SpaceContext context) : ISpaceCadParseProvider
    {
        public async Task<IReadOnlyList<SpaceCadGeneratedArtifact>> GenerateAsync(SpaceCadParseProviderRequest request, Stream source, CancellationToken ct = default)
        {
            if (context.Database.CurrentTransaction is not null)
                throw new InvalidOperationException("CAD parsing belongs outside the artifact transaction.");
            var sourceHash = Convert.ToHexString(await SHA256.HashDataAsync(source, ct)).ToLowerInvariant();
            if (sourceHash != request.Payload.SourceSha256)
                throw new InvalidDataException("The simulated CAD input hash differs.");

            return [Artifact(SpaceArtifactType.CadIr), Artifact(SpaceArtifactType.LayerInventory), Artifact(SpaceArtifactType.PreviewSet)];
        }

        private static SpaceCadGeneratedArtifact Artifact(SpaceArtifactType type)
        {
            var bytes = SimulatedCadArtifact(type);
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            return new(type, "1", $"{type}.json", "application/json", ".json", bytes.LongLength, hash,
                _ => ValueTask.FromResult<Stream>(new MemoryStream(bytes, writable: false)));
        }
    }

    private sealed class ArtifactFault
    {
        internal ArtifactCommand? Command { get; private set; }
        internal string Root { get; private set; } = string.Empty;
        internal int Sequence { get; private set; }
        internal bool AckInjected { get; set; }
        internal string[] PublishedAtFault { get; private set; } = [];
        internal bool Holding { get; set; }
        internal DateTime? ExpiryObservedAt { get; set; }

        internal void Arm(string root, int sequence, ArtifactCommand command) => (Root, Sequence, Command) = (root, sequence, command);

        internal void ObservePublished()
        {
            var root = Command!.FileRoot;
            PublishedAtFault = Directory.GetFiles(root, "*.content", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(root, path)).Order(StringComparer.Ordinal).ToArray();
        }
    }

    private sealed class ArtifactTransactionFault(ArtifactFault fault) : DbTransactionInterceptor
    {
        public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection,
            TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
        {
            if (fault.Command?.Hold == "transaction" && !fault.Holding)
            {
                fault.Holding = true;
                Write(fault.Root, fault.Sequence, "waiting", true);
                await ReadAsync<bool>(fault.Root, fault.Sequence, "release", cancellationToken);
            }
            return result;
        }

        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken ct = default)
        {
            if (fault.Command?.Fault == "ack")
            {
                fault.AckInjected = true;
                fault.ObservePublished();
                throw new IOException("W613 injected loss of the native artifact commit acknowledgement.");
            }
            return Task.CompletedTask;
        }
    }

    private sealed class ArtifactSaveFault(ArtifactFault fault) : SaveChangesInterceptor
    {
        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken ct = default)
        {
            if (fault.Command?.Fault is "before" or "expiry")
            {
                fault.ObservePublished();
                if (fault.Command.Fault == "before")
                    throw new SpaceJobProcessingException(SpaceJobFailureKind.Transient, "W613_BEFORE_COMMIT", "W613 interrupted the artifact save before COMMIT.");

                var expiresAt = await ReadAsync<DateTime>(fault.Root, fault.Sequence, "expiry", ct);
                while (DateTime.Now <= expiresAt)
                    await Task.Delay(50, ct);
                fault.ExpiryObservedAt = DateTime.Now;
            }
            return result;
        }
    }
}

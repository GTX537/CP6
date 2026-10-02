using System.Data.Common;
using CP6.Platform.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.DatabaseCompatibility.Probe;

internal sealed class PlatformContext(DbConnection connection, ProbeProvider provider, string schema, bool ownsConnection = false, IInterceptor? interceptor = null) : ProbeContext(connection, provider, schema, interceptor)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddCp6TransactionalMessaging(Schema);
        if (Provider == ProbeProvider.PostgreSql)
        {
            foreach (var type in new[] { typeof(Cp6OutboxMessage), typeof(Cp6InboxMessage), typeof(Cp6InboxAggregateCheckpoint), typeof(Cp6DeadLetterRecord) })
            {
                var property = modelBuilder.Entity(type).Property<byte[]>("RowVersion");
                property.HasColumnType("bytea").IsConcurrencyToken().ValueGeneratedOnAddOrUpdate();
                property.Metadata.SetBeforeSaveBehavior(PropertySaveBehavior.Ignore);
                property.Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
            }
        }
    }

    public override void Dispose()
    {
        var owned = ownsConnection ? Database.GetDbConnection() : null;
        base.Dispose();
        owned?.Dispose();
    }

    public override async ValueTask DisposeAsync()
    {
        var owned = ownsConnection ? Database.GetDbConnection() : null;
        await base.DisposeAsync();
        if (owned is not null) await owned.DisposeAsync();
    }
}

internal sealed class PlatformContextFactory(ProbeDatabase db) : IDbContextFactory<PlatformContext>
{
    public PlatformContext CreateDbContext() => new(db.NewConnection(), db.Provider, db.Schema, ownsConnection: true);
    public Task<PlatformContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
}

internal sealed class ProbeMessageValidator : ICp6OutboxEnvelopeValidator, ICp6InboxDeliveryValidator
{
    public Cp6MessageValidationResult Validate(Cp6OutboxEnvelope envelope) => Cp6MessageValidationResult.Valid;
    public Cp6MessageValidationResult Validate(Cp6InboxDelivery delivery) => Cp6MessageValidationResult.Valid;
}

internal sealed class ProbeClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan duration) => _now = _now.Add(duration);
    public void Set(DateTimeOffset instant) => _now = instant;
}

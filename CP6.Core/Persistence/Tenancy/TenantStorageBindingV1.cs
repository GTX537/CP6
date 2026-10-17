using System.Collections.ObjectModel;
using System.Text;
using Npgsql;

namespace CP6.Core.Persistence.Tenancy;

internal enum TenantStorageState { Provisioning, Ready, Draining, Unavailable, Retired }
internal enum TenantStorageFailure
{
    TrustedContextRequired, ContextChanged, BindingRejected, BindingChanged, ConfigurationRejected,
    CapacityExceeded, CredentialsUnavailable, ConnectionUnavailable, IdentityRejected, TenantInactive, DatabaseRejected, LeaseClosed, OperationInProgress
}

/// <summary>No inner exception, raw database message, identity, or credential is propagated.</summary>
internal sealed class TenantStorageException(TenantStorageFailure failure, string? sqlState = null)
    : Exception("Tenant storage operation rejected: " + failure + ".")
{
    public TenantStorageFailure Failure { get; } = failure;
    public string? SqlState { get; } = sqlState;
}

/// <summary>Host configuration, never a request/wire contract or evidence of current business authorization.</summary>
internal sealed class TenantStorageBindingV1
{
    public const string SupportedLayout = "cp6-pg-tenancy-v1";
    public Guid TenantId { get; }
    public string HostId { get; }
    public string DatabaseRef { get; }
    public string SchemaName { get; }
    public string LayoutVersion { get; }
    public string ModelVersion { get; }
    public string BindingRevision { get; }
    public string CredentialVersion { get; }
    public string RuntimePrincipalRef { get; }
    public IReadOnlyList<string> QueueDomainRefs { get; }
    public TenantStorageState StorageState { get; }
    public string MigrationVersion { get; }

    public TenantStorageBindingV1(Guid tenantId, string hostId, string databaseRef, string layoutVersion,
        string modelVersion, string bindingRevision, string credentialVersion, string runtimePrincipalRef,
        IEnumerable<string> queueDomainRefs, TenantStorageState storageState, string migrationVersion)
    {
        if (tenantId == Guid.Empty || !Enum.IsDefined(storageState)) throw Rejected();
        TenantId = tenantId;
        SchemaName = "t_" + tenantId.ToString("N");
        HostId = Text(hostId); DatabaseRef = Text(databaseRef); LayoutVersion = Text(layoutVersion);
        ModelVersion = Text(modelVersion); BindingRevision = Text(bindingRevision); CredentialVersion = Text(credentialVersion);
        RuntimePrincipalRef = Text(runtimePrincipalRef); StorageState = storageState; MigrationVersion = Text(migrationVersion);
        var queues = queueDomainRefs?.Select(Text).ToArray() ?? throw Rejected();
        if (queues.Distinct(StringComparer.Ordinal).Count() != queues.Length) throw Rejected();
        QueueDomainRefs = new ReadOnlyCollection<string>(queues);
    }

    internal static string Text(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256 || value.Any(char.IsControl)) throw Rejected();
        return value;
    }
    internal static TenantStorageException Rejected() => new(TenantStorageFailure.ConfigurationRejected);
}

// Records contain only immutable values; no caller-owned connection-string builder is retained.
internal sealed record TenantDatabase(string Reference, string Server, int Port, string Database, SslMode SslMode = SslMode.VerifyFull);
internal sealed record TenantPrincipal(string Reference, string Username);

/// <summary>Immutable host-owned directory snapshot. Replacement invalidates every old acquisition/lease.</summary>
internal sealed class TenantStorageCatalog
{
    private readonly Dictionary<Guid, TenantStorageBindingV1> _bindings;
    private readonly Dictionary<string, TenantDatabase> _databases;
    private readonly Dictionary<string, TenantPrincipal> _principals;
    private readonly HashSet<string> _models;
    public string HostId { get; }

    public TenantStorageCatalog(string hostId, IEnumerable<string> knownModels, IEnumerable<TenantStorageBindingV1> bindings,
        IEnumerable<TenantDatabase> databases, IEnumerable<TenantPrincipal> principals)
    {
        HostId = TenantStorageBindingV1.Text(hostId);
        _models = new(knownModels?.Select(TenantStorageBindingV1.Text) ?? throw TenantStorageBindingV1.Rejected(), StringComparer.Ordinal);
        _bindings = []; _databases = new(StringComparer.Ordinal); _principals = new(StringComparer.Ordinal);
        if (_models.Count == 0 || bindings is null || databases is null || principals is null) throw TenantStorageBindingV1.Rejected();
        var actualDatabases = new HashSet<(string Server, int Port, string Database)>();
        foreach (var database in databases)
        {
            if (database is null) throw TenantStorageBindingV1.Rejected();
            TenantStorageBindingV1.Text(database.Reference); TenantStorageBindingV1.Text(database.Server);
            Identifier(database.Database);
            // Exactly one explicitly configured TCP server. No multi-host or socket-path aliases.
            if (Uri.CheckHostName(database.Server) == UriHostNameType.Unknown || database.Port is < 1 or > 65535
                || !Enum.IsDefined(database.SslMode)
                || !_databases.TryAdd(database.Reference, database)
                || !actualDatabases.Add((database.Server.ToLowerInvariant(), database.Port, database.Database))) throw TenantStorageBindingV1.Rejected();
        }
        foreach (var principal in principals)
        {
            if (principal is null) throw TenantStorageBindingV1.Rejected();
            TenantStorageBindingV1.Text(principal.Reference); Identifier(principal.Username);
            if (!_principals.TryAdd(principal.Reference, principal)) throw TenantStorageBindingV1.Rejected();
        }
        var schemas = new HashSet<(string Database, string Schema)>();
        // One actual login name per tenant in this host catalog, including differently named endpoint aliases.
        var assignedPrincipals = new HashSet<string>(StringComparer.Ordinal);
        foreach (var binding in bindings)
        {
            if (binding is null || binding.HostId != HostId || !_databases.ContainsKey(binding.DatabaseRef)
                || !_principals.TryGetValue(binding.RuntimePrincipalRef, out var principal)
                || !_bindings.TryAdd(binding.TenantId, binding)
                || !schemas.Add((binding.DatabaseRef, binding.SchemaName))
                || !assignedPrincipals.Add(principal.Username)) throw TenantStorageBindingV1.Rejected();
        }
    }

    internal TenantStorageBindingV1 Resolve(Guid tenant)
    {
        if (!_bindings.TryGetValue(tenant, out var binding) || binding.StorageState != TenantStorageState.Ready
            || binding.LayoutVersion != TenantStorageBindingV1.SupportedLayout || !_models.Contains(binding.ModelVersion))
            throw new TenantStorageException(TenantStorageFailure.BindingRejected);
        return binding;
    }
    internal TenantDatabase Database(TenantStorageBindingV1 binding) => _databases[binding.DatabaseRef];
    internal TenantPrincipal Principal(TenantStorageBindingV1 binding) => _principals[binding.RuntimePrincipalRef];
    private static void Identifier(string value)
    {
        TenantStorageBindingV1.Text(value);
        if (Encoding.UTF8.GetByteCount(value) > 63) throw TenantStorageBindingV1.Rejected();
    }
}

/// <summary>Injected only by the configuration Owner. Return a password for the exact immutable binding/version.</summary>
internal interface ITenantCredentialResolver
{
    ValueTask<string> ResolvePasswordAsync(TenantStorageBindingV1 binding, CancellationToken cancellationToken);
}

internal sealed record TenantPoolLimits(int MaximumPools, int MaximumConnections, int ConnectionsPerPool,
    int ConnectionTimeoutSeconds = 5, int CommandTimeoutSeconds = 30)
{
    internal void Validate()
    {
        if (MaximumPools is < 1 or > 1024 || MaximumConnections is < 1 or > 10000
            || ConnectionsPerPool < 1 || ConnectionsPerPool > MaximumConnections
            || ConnectionTimeoutSeconds is < 1 or > 120 || CommandTimeoutSeconds is < 1 or > 300)
            throw TenantStorageBindingV1.Rejected();
    }
}

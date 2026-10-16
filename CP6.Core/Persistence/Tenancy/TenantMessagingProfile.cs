using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.ErpIntegration;

namespace CP6.Core.Persistence.Tenancy;

/// <summary>Explicit compatibility set, not a caller-selected arbitrary EF profile.</summary>
internal static class TenantMessagingProfile
{
    internal const string CombinedModelVersion = "erp-identity-messaging-v1";
    internal const string CombinedMigrationVersion = "erp-identity-messaging-tenant-rls-v1";

    internal static bool Supports(TenantStorageBindingV1 binding, bool identity)
    {
        var combined = binding.ModelVersion == CombinedModelVersion
            && binding.MigrationVersion == CombinedMigrationVersion;
        var single = identity
            ? binding.ModelVersion == IdentityTenantTransaction.ModelVersion
                && binding.MigrationVersion == IdentityTenantTransaction.MigrationVersion
            : binding.ModelVersion == ErpTenantTransaction.ModelVersion
                && binding.MigrationVersion == ErpTenantTransaction.MigrationVersion;
        var expected = combined ? new[] { ErpIntegrationContext.Schema, IdentityMessagingContext.Schema }
            : new[] { identity ? IdentityMessagingContext.Schema : ErpIntegrationContext.Schema };
        return (combined || single) && binding.QueueDomainRefs.Count == expected.Length
            && expected.All(domain => binding.QueueDomainRefs.Contains(domain, StringComparer.Ordinal));
    }
}

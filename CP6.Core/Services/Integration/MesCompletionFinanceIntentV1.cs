using System.Text.Json;
using CP6.Entity.DomainModels.Integration;

namespace CP6.Core.Services.Integration;

internal sealed record MesCompletionSessionV1(
    Guid Subject, string AccessId, long ExpiresAt, string AuthenticationVersion, bool PlatformAdmin,
    Guid? Impersonator, Guid? ImpersonatorTenant, string? ImpersonatorVersion, bool HasBrowserFamily);

internal interface IMesCompletionSessionsV1
{
    MesCompletionSessionV1 Capture(Guid subject, Guid tenant);
    Task RequireCurrentAsync(MesCompletionSessionV1 session, Guid tenant, CancellationToken ct);
}

internal sealed record MesCompletionFinanceIntentV1(
    Guid WorkOrderId, decimal Quantity, MesCompletionSessionV1 Session, string PermissionVersion,
    string[]? RemainingMaterials = null)
{
    internal const string Hook = "CompleteWorkOrderFinanceV1";

    internal static bool Matches(IntegrationEvent evt)
        => evt.SourceModule == "MES" && evt.TargetModule == "FIN" && evt.HookName == Hook;

    internal static MesCompletionFinanceIntentV1 Read(IntegrationEvent evt)
        => JsonSerializer.Deserialize<MesCompletionFinanceIntentV1>(evt.PayloadJson)
            ?? throw new JsonException("Missing MES completion intent.");

    internal void StoreIn(IntegrationEvent evt) => evt.PayloadJson = JsonSerializer.Serialize(this);
}

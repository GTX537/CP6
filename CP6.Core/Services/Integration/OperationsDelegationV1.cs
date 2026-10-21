using CP6.Core.EFDbContext;

namespace CP6.Core.Services.Integration;

// In-process capability issued only by a confirmed parent Owner. No controller accepts it.
// Exact parent draft generation, or the fixed completion effects bound to a persisted result.
internal sealed record OperationsDelegationV1(CP6Context Context, Guid Tenant, Guid Subject,
    string Actor, string Resource, string Action, string Number, string PermissionVersion)
{
    internal Guid? CompletionResultId {get;init;}
    internal decimal? CompletedQuantity {get;init;}
}

using CP6.Core.EFDbContext;

namespace CP6.Core.Services.Integration;

// In-process capability issued only by a confirmed parent Owner. No controller accepts it.
// Permits draft generation for this exact parent; never inventory, issue, edit or arbitrary children.
internal sealed record OperationsDelegationV1(CP6Context Context, Guid Tenant, Guid Subject,
    string Actor, string Resource, string Action, string Number, string PermissionVersion);

using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;

namespace CP6.Core.Services.Plm;

// PLM-CHG 保有范围批准。实现须在传入的 Main Owner 事务中核对准确原件及当前权限。
public abstract class EngineeringRevisionChangeAuthorityV1
{
    internal abstract bool Owns(CP6Context context);
    internal abstract Task RequireAsync(OperationsAccessV1 access, EngineeringSourceRefV1 change,
        EngineeringSourceRefV1 parent, EngineeringProductRefV1 product, bool requireApproval, CancellationToken ct);
}

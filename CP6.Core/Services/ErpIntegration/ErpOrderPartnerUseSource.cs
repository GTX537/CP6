using System.Data.Common;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;

namespace CP6.Core.Services.ErpIntegration;

// 当前映射 Owner 在传入的原事务和已持有的 BP Scope S 下完成核证。
// 旧 T_WebBusinessPartner.CrmAccountId 和已发送的 C03 结果均不构成当前映射证明。
internal interface IErpOrderPartnerUseSource
{
    Task<BpUseMappingProof?> ReadAsync(CP6Context db, DbTransaction transaction, ErpOrderRequested original,
        Guid bp, string legalEntityKey, CancellationToken ct);
}

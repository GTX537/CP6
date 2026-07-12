using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Seed;

/// <summary>
/// F1 波G G.1：存量租户 COA 缺行逐租户幂等回填（波A/波D 新增科目只进 <see cref="FinCoaTemplate"/> 模板，
/// 已按旧模板建账的存量租户缺行 → 库存过账 <c>E-FIN-141</c> / 年结 <c>E-FIN-408</c>）。
///
/// 回填码单（从模板取属性，单一真相源，保证"金额科目属性与模板一致"）：
///  - CN-GAAP：<c>2204</c> GRNI 暂估应付（波A 收货未开票）/ <c>3103</c> 本年利润 / <c>3104</c> 未分配利润（波D 年结两凭证依赖）
///  - INTL：<c>2110</c> GRNI / <c>1900</c> PENDING_PROPERTY_LOSS / <c>4800</c> NON_OP_INCOME /
///          <c>6800</c> NON_OP_EXPENSE（波A 盘盈亏/报废）/ <c>3103</c> Current Year Earnings /
///          <c>3300</c> Retained Earnings（波G 年结本年利润/未分配利润）
///
/// 规则（照 <see cref="A3AccountSeed"/> 精神，但升级为**逐租户**——GlAccount 是 BaseTenantEntity）：
///  - 只对**已导入该 scheme**（该租户下有 ≥1 行同 scheme 科目）的租户回填，不给空租户凭空建 COA。
///  - **只插缺失**（按 Code 判存）、**不动已有**（科目"不删只停用"，停用行 Code 仍在→不重插）、不删。
///  - 逐租户显式 <c>TenantId</c> + <c>IgnoreQueryFilters</c>（跨租户可见，避免默认租户作用域误判）。
///  - ParentId 从同租户同 scheme 既有科目按 ParentCode 解析（新增行的父级为存量科目，必已在）。
///
/// 接入：Program.cs 于 <see cref="A3AccountSeed"/> 附近（模板/迁移之后）调用。幂等可重入。
/// </summary>
public static class FinCoaBackfillSeed
{
    /// <summary>scheme → 需回填的科目编码集（属性一律从 <see cref="FinCoaTemplate"/> 取）。</summary>
    private static readonly Dictionary<string, string[]> BackfillCodes = new()
    {
        [FinCoaTemplate.CnGaap] = new[] { "2204", "3103", "3104" },
        [FinCoaTemplate.Intl] = new[] { "2110", "1900", "4800", "6800", "3103", "3300" },
    };

    public static void EnsureSeeded(CP6Context db)
    {
        var tenantIds = db.Sys_Tenants.Select(t => t.Id).ToList();
        if (tenantIds.Count == 0) return;

        var changed = false;
        foreach (var (scheme, codes) in BackfillCodes)
        {
            var template = FinCoaTemplate.Get(scheme).ToDictionary(r => r.Code);

            foreach (var tid in tenantIds)
            {
                // 该租户该 scheme 的既有科目（Code → Id），跨租户过滤器绕过。
                var existing = db.GlAccounts.IgnoreQueryFilters()
                    .Where(a => a.TenantId == tid && a.StandardScheme == scheme)
                    .Select(a => new { a.Code, a.Id })
                    .ToList();
                if (existing.Count == 0) continue;   // 该租户未导入此 scheme → 不凭空建 COA

                var byCode = existing.ToDictionary(x => x.Code, x => x.Id);

                foreach (var code in codes)
                {
                    if (byCode.ContainsKey(code)) continue;   // 已有 → 不动
                    if (!template.TryGetValue(code, out var row)) continue;   // 防御：码不在模板则跳过

                    Guid? parentId = null;
                    if (row.ParentCode != null && byCode.TryGetValue(row.ParentCode, out var pid))
                        parentId = pid;

                    db.GlAccounts.Add(new GlAccount
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tid,                 // 显式设 → StampTenant 不覆盖
                        Code = row.Code,
                        Name = row.Name,
                        Type = row.Type,
                        NormalSide = row.NormalSide,
                        Level = row.Level,
                        IsLeaf = row.IsLeaf,
                        IsControl = row.IsControl,
                        SubLedgerType = row.SubLedgerType,
                        RequirePartner = row.RequirePartner,
                        Role = row.Role,
                        StandardScheme = scheme,
                        IsActive = true,
                        ParentId = parentId,
                        Creator = "system",
                        CreateDate = DateTime.Now,
                    });
                    changed = true;
                }
            }
        }

        if (changed)
            db.SaveChanges();
    }
}

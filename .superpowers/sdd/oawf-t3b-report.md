# M-OA/WF T3b 执行报告：逐租户 MenuAction/RoleAction 权限种子

生成于 2026-07-12。分支 `feat/m-oawf-crosscutting`。样板 = MesPermissionSeed。

## 交付物

| 文件 | 角色 |
|---|---|
| `CP6.WebApi/Seed/OawfPermissionSeed.cs` | 正本：20 元组逐租户幂等种子（MenuAction+RoleAction，RoleId=1） |
| `CP6.WebApi/Program.cs`（OawfMenuSeed 之后） | 接入点：紧随菜单锚定，每启动幂等 |
| `CP6.Tests/OawfPermissionSeedTests.cs` | 5 断言，ExpectedTuples 独立硬编码 oracle |
| `docs/seeds/oawf-permission-seed.sql` | 文档留档（CROSS JOIN + NOT EXISTS，头声明 C# 为正本） |

## 三数闭环对账（31 → 20 → 20，漏 0 多 0）

**① 控制器真写端点 = 31**（grep `Controllers/Oa/*.cs` 21 + `Controllers/Wf/*.cs` 10 的 `[RequirePermission]`）：

Oa 21：Pref edit / Draft add,edit,submit,del / Catalog favorite / Notification read×2 / Designer edit,add /
Inbox read×2,approve,transfer,sendback / ApproverMap add,edit,del / Delegate delegate×2 / FlowAdmin enable。

Wf 10：Task withdraw / Approval submit / Form form-save,submit / AdvancedFlow sendback,addsign,delegate /
Flow edit,submit,approve。

**② 去重 (menu-key, action) = 20**（跨控制器归并消解 11 处重复，真相源 §五）：
- `oa-inbox:read` 覆 Inbox task/cc-read + Notification read/read-all（4→1）
- `oa-inbox:approve` 覆 Inbox batch + Flow act（2→1）
- `oa-inbox:sendback` 覆 Inbox + AdvancedFlow（2→1）
- `oa-form-catalog:submit` 覆 Draft submit + Approval submit + Form data + Flow submit（4→1）
- `oa-settings:delegate` 覆 Delegate add/remove + AdvancedFlow delegate（3→1，T2 委派合一拍板1）
- `oa-designer:edit` 覆 Designer save + Flow def（2→1）

31 − 11 归并 = 20 唯一元组。

**③ 种子元组 = 20**（`OawfPermissionSeed.Actions`）：

| MenuId | menu-key | actions | 计 |
|---|---|---|---|
| 733 | oa-inbox | read, approve, transfer, sendback, addsign, withdraw | 6 |
| 734 | oa-flow-admin | enable | 1 |
| 735 | oa-form-catalog | add, edit, submit, del, favorite | 5 |
| 737 | oa-settings | edit, delegate | 2 |
| 738 | oa-designer | edit, add, form-save | 3 |
| 739 | oa-approver-map | add, edit, del | 3 |
| | | **合计** | **20** |

**闭环：31 真写端点 → 20 去重元组 → 20 种子元组。漏种 0 / 多种 0 ✅**

## 覆盖与豁免

- 覆盖 **6** 个有写端点 menu-key（733/734/735/737/738/739）。
- `oa-form-search`(736) 仅 1 只读 POST（Query search 豁免→view，未贴点），无键可种，故不在本种子——与 7 键总数不矛盾。
- 2 只读 POST 豁免（Forecast preview→`oa-form-catalog:view` / Query search→`oa-form-search:view`）未入种子。
- 资源键总 22（真相源 §七）= 本 20 写键 + 2 view 豁免键 ✅ 自洽。

## 四要件核对（照 MesPermissionSeed）

1. 枚举 `Sys_Tenants` 显式 TenantId ✅（`tenantIds` foreach，每租户各插一份）
2. `IgnoreQueryFilters()` 查重 ✅（跨租户可见，避默认租户作用域误判）
3. MenuAction + RoleAction 双种 RoleId=1 ✅
4. StampTenant 不覆盖显式 TenantId ✅（显式设 tid，拦截器仅盖 Guid.Empty）

## TDD Evidence

| 阶段 | 命令 | 结果 |
|---|---|---|
| 🔴 红 | `dotnet test --filter OawfPermissionSeedTests`（种子未建） | CS0103 `OawfPermissionSeed` 不存在 —— 编译失败 |
| 🟢 绿 | 同上（种子建成 + 接入） | **Passed 5 / Failed 0**（20/租户元组集合相等 + 幂等行数级 40 + RoleId=1 & MenuId 锚定 & oa- 前缀 + 逐租户显式 TenantId + 无租户 no-op） |
| 全量 | `dotnet test CP6.Tests` | **Passed 1769 / Failed 0 / Skipped 5**（基线 1764 + 新增 5，无跌） |

## Oracle 独立性

`ExpectedTuples` 为测试内独立硬编码 20 元组常量（非引用 `OawfPermissionSeed.Actions`），
两侧各自从真相源手抄，集合相等断言（`Assert.Equal(expected, maSet/raSet)`）双向校验，防自证假绿。

## SQL 头声明

`docs/seeds/oawf-permission-seed.sql` 头注明「★正本は C#：OawfPermissionSeed.cs，本 SQL は同一集合の文書留档，乖離時は C# を正とする」。CROSS JOIN Sys_Tenants + NOT EXISTS 幂等，与 C# 20 元组 1:1。

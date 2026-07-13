# Task F-T2 报告：权限点/i18n seed + E-T1 守卫收编

STATUS: DONE（全量 1969 passed / 5 skipped，与基线一致；零迁移；前端未触）

## 交付文件（surgical，5 项）
- 新增 `CP6.WebApi/Seed/I18nOaFlowTriggerScreenSeed.cs`（53 键五语）
- 新增 `CP6.WebApi/Seed/FlowTriggerPermissionSeed.cs`（FlowTrigger.View/Edit 逐租户权限种子）
- 改 `CP6.WebApi/Program.cs`（FlowTriggerPermissionSeed 接入 + i18n concat 追加）
- 移 `Controllers/Integration/FlowTriggerAdminController.cs` → `Controllers/Oa/`（git mv 保历史，命名空间改 + GET 去键）
- 改 `CP6.Tests/OawfPermissionAttributeTests.cs`（词表 + 计数常量 + 计数描述注释）

## 交接注记落实
1. **菜单 734 MenuKey 回填跳过**：`OawfMenuSeed` 已落地 `oa-flow-admin`（M-OA/WF 波），本任务不重做、不加竞争回填。已核对 brief Step-1 内联回填块（RoleId=1 范本）被本任务丢弃。
2. **权限种子逐租户**：未用 brief 的 Program.cs 内联 RoleId=1 块。选择**创建 sibling** `FlowTriggerPermissionSeed.cs`（照 `OawfPermissionSeed` 逐租户 + IgnoreQueryFilters 幂等 + 显式 TenantId 模式），而非扩 OawfPermissionSeed——后者的 XML doc 是 M-OA/WF 波精确台账（20 元组/31 贴点/6 menu-key），注入波③ trigger 会污染该波账本；sibling 隔离波次、git 更 surgical。接入点在 `OawfPermissionSeed.EnsureSeeded` 之后（菜单 734 须先在）。
3. **E-T1 守卫收编**：见下「守卫 re-baseline」。
4. **i18n 键面双向对账**：见下「键面对账」。

## 键面对账（双向 grep，零缺零孤儿）
权威源 = `cp6.web/src/views/oa/admin` 三新文件 + `flowTriggerModel.ts` 的 `t()`/`labelKey`/校验返回键。

| 类别 | 引用键数 | 归属 |
|---|---|---|
| `oa.flowtrigger.*` | 49 | **全新 → 本 seed** |
| `oa.flowadmin.tab.flows` | 1 | **新（E-T2 tab）→ 本 seed** |
| `oa.flowadmin.*` 其余 10（title/uniqueHint/empty/col.×5/enabled/disabled） | 10 | 既有 `I18nOaInboxScreenSeed`，**不重复** |
| `common.*`（add/save/ok/edit/cancel） | 5 | 既有通用键，**不重复** |
| `E-WF-022/023/024` | 3 | 后端错误码，grep 确认全库未 seed → 本 seed |

- 本 seed Items 计 **53** = 49 + 1 + 3。
- 正向（文件→seed）：49 `oa.flowtrigger.*` + `oa.flowadmin.tab.flows` 全部命中本 seed（其余 10 flowadmin + 5 common 命中既有 seed）→ 零缺失。
- 反向（seed→文件）：49 `oa.flowtrigger.*` + tab.flows 均有 `t()`/`labelKey` 引用；3 个 E-WF 为后端 message 错误码（非前端 t()，属故意 LangKey）→ 零孤儿。
- 去重 grep：`oa.flowtrigger.*`/`E-WF-022~024` 在 `CP6.WebApi/Seed/` 均无既存条目；`oa.flowadmin.*` 10 键仅存于 InboxSeed，本 seed 只补 tab.flows。
- ja/ko 为真译（非中文照抄），逐条核对（例：keyOnce ja「このキーは一度しか表示されません…」/ ko「이 키는 한 번만 표시됩니다…」）。
- concat 接在 `I18nOaKernelHardeningScreenSeed.Items` 之后（Program.cs，仿 kernel-hardening 先例）。

## 守卫 re-baseline（E-T1 交接票）+ 一处附加口径修正
控制器 `FlowTriggerAdminController` 由 `Controllers.Integration` 收编回 `Controllers.Oa`（命名空间改、路由 `api/oa/flow-triggers` 不动）。

**跑测得实数（未盲信 brief）**：
- `OawfControllers_AreDiscovered`：16 → **17**（Oa 11→12 + Wf 5）。
- `EveryMutatingAction_IsGuarded` taggedCount：31 → **37**（+6 = FlowTrigger 变更端点：Create/Update/Enable/ResetKey/ManualFire=Edit ×5，CronPreview=View ×1）；33 → 39 非GET端点（37 贴点 + 2 豁免）。
- `ActionVocabulary`：+`FlowTrigger.View`、+`FlowTrigger.Edit`。
- 测试改动**仅限**词表 + 计数常量（16→17 / 31→37）+ 计数描述性注释，**无断言逻辑弱化**、无豁免清单变动。

**附加口径（E-T1 报告 + brief 均未预见的第 4 处锁）**：`NoReadOnlyGetAction_HasRequirePermission` 禁只读 GET 贴 `[RequirePermission]`。原控制器在 Integration 时 3 个 GET（list/{id}/{id}/fires）贴了 `FlowTrigger.View`；收编入 Oa 后该守卫会红。按「不弱化断言 + 全绿」硬门，将这 3 个 GET 去键——与**同菜单 `oa-flow-admin` 兄弟控制器 `FlowAdminController`**（其 GET 仅 `[Authorize]`、只 Enable 写端点贴键）既有约定完全一致。`FlowTrigger.View` 仍保留在 CronPreview（POST）上，故词表与「贴点⊆种子」互锁不受影响（贴点 action 集 {View(CronPreview), Edit×5} ⊆ 种子 {View, Edit}）。

## 「贴点⊆种子」互锁
控制器实际使用 action = {FlowTrigger.View, FlowTrigger.Edit}；种子登记 = {FlowTrigger.View, FlowTrigger.Edit}（menu 734）→ 无孤儿键、无漏种。

## 门槛核验
1. `dotnet test CP6.slnx`：**1969 passed / 5 skipped**（含改后 OawfPermissionAttributeTests 4/4）。与基线 1969/5skip 一致（本任务未加新测试，仅原地改守卫）。
2. `dotnet ef migrations has-pending-model-changes`：No changes（零迁移）。
3. 前端未触（仅后端 seed + 控制器移 + 测试）→ vitest 无需重跑。
4. `git show --stat`：仅 brief 文件 + 记档的收编文件（5 项，含 R100 rename）。

## 偏差/关切
- **[需复核] GET 读授权口径微调**：收编导致 list/{id}/{id}/fires 由「须 FlowTrigger.View（种子仅授 RoleId=1）」放宽为「[Authorize] 登录态 + 租户隔离」。这与同菜单兄弟控制器 FlowAdminController 一致（该控制器注释明言「面向平台管理员，[Authorize] 登录态即可，P1 不加细粒度权限属性」），且 GET 不返回明文 key（key 仅 hash 存、仅 Create/ResetKey 一次性明文回）。fail-closed 守卫现覆盖本控制器 6 写端点（净安全增益）。此为「全绿 + 移动 + 不弱化断言」三约束下的唯一解，E-T1 报告与 brief 均只列了 3 处锁、未预见此第 4 处 GET 锁——特此显式记档供终审裁定。
- 引擎零改动；DI 无新增（服务 E-T1 已注册）。

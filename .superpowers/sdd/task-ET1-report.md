# Task E-T1 + E-T2 报告：WFS 内核 hardening i18n 五语 seed(12键) + gstack QA harness(只写不跑)

> 注：本文件覆盖了同名的旧波(D-wave serviceTask 校验)报告；当前内容为 WFS 内核 hardening E 波。

## Status: 完成（两 commit 均已 push）

| Task | Commit | 内容 |
|---|---|---|
| E-T1 | `d0b7ba0` | `CP6.WebApi/Seed/I18nOaKernelHardeningScreenSeed.cs`（12键×五语）+ Program.cs concat 行 |
| E-T2 | `ab2ea67` | `docs/superpowers/qa/wfs-kernel-hardening/`（README + seed.sql + ps1，只写不跑）|

## 测试摘要
- 后端全量 `dotnet test CP6.Tests`：**1887 passed / 5 skipped**（= 基线，零回退，终审者亲跑）。
- `dotnet build CP6.WebApi`：成功（唯一告警 InboxService CS8601，既有，与本波无关）。
- 前端未改动（E-T1/E-T2 全在后端 seed + docs QA 目录），vitest 基线 420 不受影响。

## E-T1 键清单交叉核对结论
grep 复核（`CP6.WebApi/Seed/` 全库 + 全仓 `.cs` seed）：12 键**零重复，全部首插**。
前端 t() / 后端 throw 实引用逐一核对——**引用了必种、种了必被引用，无裸键、无死键**：

| 键 | 引用点（已实读确认） |
|---|---|
| `oa.designer.gw.inclusiveSplit` / `.inclusiveJoin` | `InclusiveGatewayNode.vue:20` t() |
| `oa.designer.gw.branchReject` / `.cascade` / `.prune` / `.branchRejectHint` | `NodePropertyPanel.vue:292-297` t() |
| `oa.designer.errInclusiveDefault` / `errInclusivePair` / `errBranchReject` | `designerModel.ts:202/217/223` validateClient |
| `E-WF-019` | `AdvancedFlow.cs:141` throw |
| `E-WF-020` | `InclusiveSplitNodeHandler.cs:37` + `FlowSchemaValidator.cs:116` |
| `E-WF-021` | `FlowSchemaValidator.cs:124/130/137` |

- 「引用了没种」：无。前端 6 个 `gw.*` + 3 个 `err*`（D-T2 报告所列）+ 3 个后端错误码，全部命中本 seed 的 12 键。
- 「种了没引用」：无。三个 `E-WF-0xx` 是后端错误码，作 i18n 消息键被引擎/校验器抛出、由前端错误码本地化解析，非死键。
- Program.cs 于 `I18nOaServiceTaskScreenSeed.Items` concat 行后新增 `I18nOaKernelHardeningScreenSeed.Items`；`SeedLangs` 运行期 `Where(!existingKeys)` + `GroupBy` 去重 → 首插即生效，无需 SQL 补丁。

## E-T2 剧本清单（7 条，只写不跑）
三件套落 `docs/superpowers/qa/wfs-kernel-hardening/`（仿 wfs-service-task E-T3 先例）。
seed.sql：5 用户 + 1 FormDef(`khd-demo-form`) + 4 FlowDef；`SET QUOTED_IDENTIFIER ON`、单数表名 `Wf_FlowDef`/`Wf_FormDef`、隔离库 `CP6DB_OA`、`IF NOT EXISTS` 幂等、raw INSERT 绕 DesignerService 校验（schema 均合法，仅为一致性 raw 插）。

| # | FlowKey | 剧本 | e2e 覆盖 |
|---|---|---|---|
| 1 | `khd-inclusive` | inclusive 2/3 真边(goA/goB 真)→ 恰 2 待办、C/default 无待办 → 两支办结 Approved | ps1 |
| 2 | `khd-inclusive` | 全假 → default 兜底唯一待办 → 办结 Approved | ps1 |
| 3 | `khd-prune` | parallelSplit(onBranchReject=prune)；A 驳 → 实例 Running、B 待办健在、发起人收 BranchPruned 通知(Type=5) → B 同意 Approved | ps1（通知经 `/api/oa/notification/list` type==5 断言）|
| 4 | `khd-cascade` | 同拓扑无 onBranchReject；A 驳 → 实例 Rejected、B 待办作废、无 Type=5 通知 | ps1 |
| 5 | `khd-sameback` | (a1→a2, b1)；a2 退回 a1(SameBranch)→ b1 不扰 → 重走 A + b1 办结 Approved | ps1 |
| 6 | `khd-sameback` | a2 退回 b1(SiblingBranch)→ HTTP 400 含 `E-WF-019`、零突变(a2/b1 待办健在、Running) | ps1 |
| 7 | designer 真浏览器 | palette 拖 inclusiveSplit/Join(空心圆菱形)→ 配分支驳回策略 → 删 default 边保存 → 校验报 `oa.designer.errInclusiveDefault`(E-WF-020 镜像) i18n 显示；五语切换验证 | README §5 手动(gstack browse) |

关键机制已实读定案后写入 harness：条件求值(未知字段安全失败=假、空条件=default 边)、动态计票 join、prune vs cascade 语义、退回三规则先校验后写；用户名 + 节点 nodeId 精确选任务(避免跨实例重跑歧义)。

## 落码纪律核对
- 零跨模块污染：改动仅 `CP6.WebApi/Seed/*` + `Program.cs` 一行 + `docs/superpowers/qa/*` 新目录。
- 提交刻意剔除工作树中既存 `.md`(CT2/CT3/DT2/ET1-brief) 的 LF→CRLF 行尾 churn，仅提交本波交付物。
- 两 commit 信息照计划 + 尾加 `Co-Authored-By: Claude Fable 5`。

## 末期 live QA（用户在场，未跑）
隔离库 `CP6DB_OA` 起后端(5181)+前端 → 跑 ps1 HTTP e2e + gstack 真浏览器走剧本 7。抓 bug 当场 TDD 修（回归补 `CP6.Tests/Wf`）。

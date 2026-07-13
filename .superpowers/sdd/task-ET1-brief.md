### Task E-T1: i18n 五语 seed（12 键）

**Files:**
- Create: `CP6.WebApi/Seed/I18nOaKernelHardeningScreenSeed.cs`
- Modify: `CP6.WebApi/Program.cs`（:1819 `I18nOaServiceTaskScreenSeed` concat 行后追加）

- [ ] **Step 1: 实现 seed**（仿 `I18nOaServiceTaskScreenSeed` 静态 `Sys_Lang[] Items` 模式；**先 grep 既有 seed 确认 12 键零重复**：`grep -rn "gw.inclusive\|errInclusive\|errBranchReject\|E-WF-019\|E-WF-020\|E-WF-021" CP6.WebApi/Seed/`）：

```csharp
using CP6.Entity.DomainModels.Sys;

namespace CP6.WebApi.Seed;

/// <summary>内核 hardening 画面词条：inclusive 网关（palette/节点/面板）+ 分支驳回策略 + 前端校验镜像
/// + 后端错误码 E-WF-019/020/021。键面以 cp6.web/src/views/oa/designer 实际引用为权威
/// （InclusiveGatewayNode.vue / NodePropertyPanel.vue / designerModel.ts validateClient）。
/// 去重：12 键在既有 I18nOa* seed 中均无重复（落地前 grep 复核）。</summary>
public static class I18nOaKernelHardeningScreenSeed
{
    public static readonly Sys_Lang[] Items =
    {
        // ── 节点名（InclusiveGatewayNode.vue）──
        new() { LangKey = "oa.designer.gw.inclusiveSplit",          ZhCN = "包容分叉",         ZhTW = "包容分叉",         En = "Inclusive Split",                    Ja = "包含分岐",                     Ko = "포괄 분기" },
        new() { LangKey = "oa.designer.gw.inclusiveJoin",           ZhCN = "包容汇聚",         ZhTW = "包容匯聚",         En = "Inclusive Join",                     Ja = "包含合流",                     Ko = "포괄 합류" },

        // ── 分支驳回策略（NodePropertyPanel.vue）──
        new() { LangKey = "oa.designer.gw.branchReject",            ZhCN = "分支驳回策略",     ZhTW = "分支駁回策略",     En = "Branch Reject Policy",               Ja = "分岐却下ポリシー",             Ko = "분기 반려 정책" },
        new() { LangKey = "oa.designer.gw.branchReject.cascade",    ZhCN = "整单驳回（默认）", ZhTW = "整單駁回（預設）", En = "Reject whole instance (default)",    Ja = "全体却下（既定）",             Ko = "전체 반려(기본)" },
        new() { LangKey = "oa.designer.gw.branchReject.prune",      ZhCN = "仅剪除本分支",     ZhTW = "僅剪除本分支",     En = "Prune this branch only",             Ja = "この分岐のみ剪定",             Ko = "해당 분기만 제거" },
        new() { LangKey = "oa.designer.gw.branchRejectHint",        ZhCN = "剪枝：驳回只终止本分支，兄弟分支继续；全部分支被剪时按上一层策略处理", ZhTW = "剪枝：駁回只終止本分支，兄弟分支繼續；全部分支被剪時按上一層策略處理", En = "Prune: rejection ends only this branch; siblings continue. If every branch is pruned, the parent policy applies.", Ja = "剪定：却下は当該分岐のみ終了し、兄弟分岐は継続します。全分岐が剪定された場合は上位ポリシーを適用します。", Ko = "가지치기: 반려 시 해당 분기만 종료되고 형제 분기는 계속 진행됩니다. 모든 분기가 제거되면 상위 정책이 적용됩니다." },

        // ── 前端校验消息（designerModel.ts validateClient 镜像）──
        new() { LangKey = "oa.designer.errInclusiveDefault",        ZhCN = "包容分叉需至少2条出边且恰好一条无条件默认边", ZhTW = "包容分叉需至少2條出邊且恰好一條無條件預設邊", En = "Inclusive split needs >=2 outgoing edges with exactly one unconditional default edge", Ja = "包含分岐には2本以上の出力エッジと、条件なしのデフォルトエッジがちょうど1本必要です", Ko = "포괄 분기는 2개 이상의 출력 엣지와 정확히 1개의 무조건 기본 엣지가 필요합니다" },
        new() { LangKey = "oa.designer.errInclusivePair",           ZhCN = "包容分叉/汇聚未正确成对",                     ZhTW = "包容分叉/匯聚未正確成對",                     En = "Inclusive split/join are not correctly paired",  Ja = "包含分岐/合流が正しく対になっていません",        Ko = "포괄 분기/합류가 올바르게 짝지어지지 않았습니다" },
        new() { LangKey = "oa.designer.errBranchReject",            ZhCN = "分支驳回策略配置非法",                       ZhTW = "分支駁回策略配置非法",                       En = "Invalid branch reject policy configuration",     Ja = "分岐却下ポリシーの設定が不正です",              Ko = "분기 반려 정책 설정이 잘못되었습니다" },

        // ── 后端错误码（FlowSchemaValidator / SendBackAsync）──
        new() { LangKey = "E-WF-019", ZhCN = "不能退回到兄弟分支内部",             ZhTW = "不能退回到兄弟分支內部",             En = "Cannot send back into a sibling branch",                       Ja = "兄弟分岐内への差し戻しはできません",             Ko = "형제 분기 내부로 반려할 수 없습니다" },
        new() { LangKey = "E-WF-020", ZhCN = "包容分叉出边配置非法（需恰好一条默认边）", ZhTW = "包容分叉出邊配置非法（需恰好一條預設邊）", En = "Invalid inclusive split edges (exactly one default edge required)", Ja = "包含分岐の出力エッジ設定が不正です（デフォルトエッジがちょうど1本必要）", Ko = "포괄 분기 출력 엣지 설정이 잘못되었습니다(기본 엣지 1개 필요)" },
        new() { LangKey = "E-WF-021", ZhCN = "包容网关配对或驳回策略配置非法",     ZhTW = "包容網關配對或駁回策略配置非法",     En = "Invalid inclusive gateway pairing or branch-reject policy",    Ja = "包含ゲートウェイの対応関係または却下ポリシーの設定が不正です", Ko = "포괄 게이트웨이 페어링 또는 반려 정책 설정이 잘못되었습니다" },
    };
}
```

- [ ] **Step 2: Program.cs concat** — :1819 `.Concat(CP6.WebApi.Seed.I18nOaServiceTaskScreenSeed.Items)` 行后加：

```csharp
            .Concat(CP6.WebApi.Seed.I18nOaKernelHardeningScreenSeed.Items)  // 内核 hardening oa.designer.gw.* + errInclusive*/errBranchReject + E-WF-019/020/021
```

- [ ] **Step 3: build 验证 + commit** — `dotnet build CP6.WebApi/CP6.WebApi.csproj`（SeedLangs 运行期幂等去重）。

```bash
git add -A && git commit -m "feat(wfs-hardening): E-T1 I18nOaKernelHardeningScreenSeed 五语12键+concat"
```

---

### Task E-T2: gstack QA harness（只写不跑）

**Files:**
- Create: `docs/superpowers/qa/wfs-kernel-hardening/README.md`（剧本）
- Create: `docs/superpowers/qa/wfs-kernel-hardening/seed.sql`
- Create: `docs/superpowers/qa/wfs-kernel-hardening/qa_kernel_hardening.ps1`（HTTP e2e，ASCII 数据）

- [ ] **Step 1: 写 harness**（参 `docs/superpowers/qa/wfs-service-task/` E-T3 先例：README 剧本 + seed.sql + ps1 三件套；seed.sql 对 OA 表用单数表名 `Wf_FlowDef`/`Wf_FormDef`、`SET QUOTED_IDENTIFIER ON`；隔离库 `CP6DB_OA`）。剧本 7 条：
  1. **inclusive 2/3 真边**：seed 一张 inclusiveSplit（3 条件边 + 1 default）流程；提交 vars 令 2 边为真 → 恰 2 个待办、default 审批人无待办；两支办结 → 实例 Approved。
  2. **全假 default 兜底**：vars 全假 → 仅 default 支收待办 → 办结即 Approved。
  3. **prune 单支剪**：parallelSplit(onBranchReject=prune) 双支；A 支驳回 → 实例仍 Running、B 支待办健在、发起人收到 BranchPruned 站内通知（`Wf_Notification.Type=5`）；B 支同意 → Approved。
  4. **cascade 默认整单驳**：同拓扑不配 onBranchReject；A 支驳回 → 实例 Rejected、B 支待办作废（与现状全等）。
  5. **SameBranch 分支内退回**：A 支两节点，第二节点退回第一节点 → B 支待办不受扰；重走 A 支 + B 支办结 → Approved。
  6. **SiblingBranch 拒绝**：A 支退回到 B 支节点 → HTTP 报错含 `E-WF-019`，五语切换验证文案。
  7. **设计器真浏览器**（gstack browse）：palette 拖 inclusiveSplit/inclusiveJoin（空心圆菱形渲染）→ 属性面板配「分支驳回策略」→ 删 default 边保存 → 校验报错 `oa.designer.errInclusiveDefault`（E-WF-020 镜像）i18n 显示。
- [ ] **Step 2: commit**

```bash
git add -A && git commit -m "test(wfs-hardening): E-T2 gstack QA harness(7剧本+seed+e2e脚本,只写不跑)"
```

- [ ] **Step 3: 末期 live QA（用户在场）** — 隔离库 `CP6DB_OA` 起后端 + 前端 → 跑 ps1 HTTP e2e + gstack 真浏览器走剧本 7。**抓 bug 当场 TDD 修**（对应回归测试补进 CP6.Tests/Wf）。

---

## 落码纪律 / Global Constraints（每个 Task 都遵守）

- **基线锁定**：后端 `dotnet test CP6.Tests/CP6.Tests.csproj` = **1509 通过（5 skip=SQLite 既知）** → 本波只增不减、全绿；前端 `npm run test`（vitest）= **320 全绿** → +N 全绿；`npm run type-check`（package.json 既有命令，含大堆内存参数）+ `npm run build` 全过。
- **EF clean（本波零迁移）**：只改 SchemaJson POCO（`FlowNode.OnBranchReject`）+ 常量（`FlowTokenStatus.Pruned`），**不加实体列、不生成迁移**。每波末跑 `dotnet ef migrations has-pending-model-changes --project CP6.Core/CP6.Core.csproj --startup-project CP6.WebApi/CP6.WebApi.csproj --context CP6Context` 必须 clean。
- **27 个既有 Wf 不变量测试一个断言不许改**（`CP6.Tests/Wf/**` 既有文件只增不改；`ParallelGatewayTests` 4+1 个并行语义测试是动态计票的等价性铁闸）。唯一例外见 D-T1：前端 `designerModel.test.ts:45` 的 palette 类型清单断言随 palette 扩展同步更新（前端测试不在 27 不变量范围，且该断言就是「palette 全集清单」本身）。
- **默认路径行为与现状全等**：无 inclusive 节点 + 无 onBranchReject 配置的流程，token 状态序列 / 任务 / FormTo / 通知 bit 级等价（cascade 一行不改、动态计票旧场景回归锁定、线性退回走既有全清场分支逐字保留）。
- **引擎内写路径三律（黄金模板铁律）**：① 先校验后写（一切结构化拒绝在任何状态突变之前抛出）；② 幂等（join 计数重入安全、已办任务再办 no-op）；③ handler/引擎内部方法**绝不自行 SaveChanges**（统一由 ActAsync/SendBackAsync 等外壳收口，剪枝/退回全部改动随既有 SaveChanges 落库）。
- **E 波紧跟 D 波不留窗口**：D-T2 合入后立即执行 E-T1/E-T2，不允许「有 UI 无 i18n/无 QA」的中间态过夜。
- **零跨模块污染**：不碰 `cp6.web/src/views/space/**`、`Services/*Space*`、Space 迁移/DbSet。每 Task 完成 `git show --stat` 复核。
- **零硬编码色**：前端新增视觉全部走 Design System token（`var(--cp-warn)` 家族等），沿 `DesignerCanvas.vue` `.dot-*` / `GatewayNode.vue` 既有 token 用法。
- **五语 i18n**：ja / zh-CN / zh-TW / en / ko，新 UI 文案全 `t()` 运行时键，键值入 `I18nOa*ScreenSeed` 家族新 seed。
- **隔离 worktree**：建议 `git worktree add C:/CP6-wfs-hardening -b feat/wfs-kernel-hardening main`（off `fb90d75`），不污染 `C:\CP6` 工作区。
- **subagent-driven TDD**：每 Task 全新编码子代理（模型按 model-policy：Opus 4.8）→ 主代理 `git show` diff 复核 → 本地 commit **不 push**。节奏：先写失败测试 → 跑验证 FAIL → 最小实现 → 跑验证 PASS → commit。提交信息 `feat(wfs-hardening): <Task 号> 中文摘要`。

---

## 侦察结论（spec §5.3 各核实项，已实读代码定案 —— 执行者照此实现，不再二次侦察）

| 核实项 | 结论 |
|---|---|
| §4.1 **split 节点定位机制** | `Wf_FlowToken`（`CP6.Entity/DomainModels/Wf/Wf_FlowToken.cs`）**没有** split nodeId 列，只有 `ParentTokenId`/`ForkId`/`StagePlanJson`。选定 **ParentTokenId 上溯**：分叉时 `ParallelSplitNodeHandler.cs:21` 以 `parent: ctx.Token.Id` 生子 token，而 ctx.Token 在被消费前 NodeId 已被 `AdvanceToken`（`FlowEngine.Tokens.cs:102`）置为 split 节点 id、`ConsumeToken` 不改 NodeId ⇒ **`ForkParent(t)`（Id==t.ParentTokenId 的 token）.NodeId 恒等于生成 t.ForkId 批次的 split 节点 id**。join 续 token 的「上弹一层」血缘（`ParallelJoinNodeHandler.cs:25-27`）保证该不变量对每层 fork 成立。零迁移。 |
| §5.3 **SendBackToNodeAsync 重生血缘现状** | `AdvancedFlow.cs:141` — `SpawnToken(inst, target, parent: null, fork: null)`，**血缘归零**（根 token）。SameBranch 规则必须改为携带剥离层血缘 `(parent: strip.ParentTokenId, fork: strip.ForkId)`；BeforeSplit/线性流保留归零重生（现状逐字）。 |
| §5.3 **CancelAllActiveTokens 过滤重载** | `FlowEngine.Tokens.cs:40` 现为全实例清场（含 B-T3 的 Pending Wf_ServiceJob 清场）、无过滤参数。不改它；**新增 `CancelTokenSubtree(Guid instanceId, Guid rootTokenId)`**（C-T2）：按 ParentTokenId 闭包算子树，子树内 Active token→Cancelled、Pending/Suspended 任务→Cancelled、Pending FormTo→Voided、Pending ServiceJob→Cancelled（镜像既有 Local ∪ DB localIds-exclusion 惯用法）。**子树闭包正确性论证**：join 续 token 血缘「上弹一层」（parent=祖父），故任何仍在途的分支延续 token 会重新挂在剥离层同级 —— 作用域分析（C-T1）从 current token 血缘出发选剥离层时选到的正是该延续 token 本身，ParentTokenId 后代闭包捕获全部需清场的活 token（C-T3 嵌套测试锁定）。 |
| §5.3 **StageRound 递增与局部退回相容性** | `NextStageRound`（`FlowEngine.ReadModel.cs:53`）按 `(instanceId, nodeId, tokenId, stageIndex)` 键控取 Max+1。SameBranch 重生的是**新 tokenId** ⇒ 新 token 串簽轮次从 0 起，与现状全清场重生同构，**天然相容，零改**。prevStage 退回不换 token（`AdvancedFlow.cs:161-163`），轮次 +1 语义不受本波影响。 |
| §5.3 **剥离层判定与 fork 栈共用血缘辅助** | 新 `TokenLineage` 静态类（A-T1）：`AncestorChain` / `CrossesFork` / `ForkParent` / `ForkStack`。剪枝递归上弹（B-T2/B-T3）与退回剥离层解析（C-T1）共用，单一口径。token 快照沿用 `ParallelJoinNodeHandler.AllTokens` 的 Local ∪ DB 身份映射去重口径，抽为 `FlowEngine.SnapshotTokens`（A-T1）。 |
| **计票退化护栏（计划期新发现）** | 旧静态计票在「join 被 ForkId==null 的线性 token 进入」的怪异 schema 下按入边数计（等不齐永停）；朴素动态判据会立即放行 ⇒ 行为漂移。定案：`GatewayJoinHelper` 对 **ForkId==null 保留旧静态入边计票路径**，bit 级等价（A-T2 专测锁定）。 |
| **剪后补放行 ≠ 全剪光（计划期新发现）** | 剪枝后 join 若齐批放行，同批 token 全部 Consumed、续 token 属上层批次 ⇒ 「无 Active 穿过本批次」也成立，若不加判别会误判全剪光递归驳回。定案：补放行探测中**检测到任一停泊 token 重入后变为 Consumed（即 join 已放行）则立即返回**，不再走全剪光检查（B-T2 `Prune_JoinBackfill_*` 锁定）。 |

**发现的 spec 与代码现状出入（不改 spec，按下述口径落码）**：
1. spec §5.2 称 BeforeSplit 为「现行为」——实际现状是 `CrossesParallelBlock`（`AdvancedFlow.cs:196`）对一切跨网关退回直接拒绝 E-WF-012，并非允许后全清场。本波 BeforeSplit = **放开该禁令后套用既有全清场机制**（行为上是新放开的能力，机制逐字复用现状代码块）。既有测试无「跨并行块拒绝」断言（已 grep 核实），无不变量冲突。
2. spec §4.2.2 通知联动信箱 spec 偏好矩阵——`PersistentWfNotifier` 现按 `NotificationPrefs` 强类型字段开关，无 BranchPruned 键。本波 `BranchPrunedAsync` **不查偏好开关**（等价信箱 spec「缺键默认 true」三态坍缩），偏好矩阵接管由信箱 spec 落地时统一改造。
3. spec §1 的 FlowEngine 行号锚点有 ±10 行漂移（办理实际在 `ActOnceAsync :136`、会签计票 `:169-177`、驳回连坐 `:221-226`），语义描述全部核实无误。

---

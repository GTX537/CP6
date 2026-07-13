## Task T8: 定时器（timer）到点动作补 webApi 连接器/路径变体 UI（spec §5.3 缺口）

> **票8。** 缺陷：spec §5.3 明列 timer「可选动作（无 / 回写动作 / **webApi 连接器**）」，运行期 `ServiceTaskActionRef.Snapshot`（`:65-73`）也支持 timer + `ConnectorName` → `actionKind="webApi"`（到点外呼）。但 `NodePropertyPanel.vue` 的 timer 分支（`:442-469`）**只提供「到点动作」下拉（=`serviceActionName`，dataWriteback 动作）**，没有连接器/路径入口——「定时到点发一个 webApi」在设计器**无法配置**。更棘手：`serviceKind` 切换清理 watch（`:56-68`）在 `kind !== 'webApi'` 时**清空 `serviceConnectorName/servicePath`**——若 timer 分支直接加连接器字段，会被这个 watch 立刻清掉。修法=(1) timer 分支加「到点动作类型」选择（none / dataWriteback / webApi），据选择显示 动作下拉 或 连接器+路径；(2) 重写清理 watch，使 timer 分支保留其合法字段、只清跨类残留。

**Files:**
- Modify: `cp6.web/src/views/oa/designer/NodePropertyPanel.vue:48-68`（清理 watch 重写）、`:442-469`（timer 分支补变体 UI）
- Modify: `CP6.WebApi/Seed/I18nOaServiceTaskScreenSeed.cs`（加 timer 动作类型三选项键，五语）

- [ ] **Step 1: 加 i18n 键** — `I18nOaServiceTaskScreenSeed.cs` 在 timer 段（`:39` `svc.timerAction` 之后）加：

```csharp
        new() { LangKey = "oa.designer.svc.timerActionKind",       ZhCN = "到点动作类型",   ZhTW = "到點動作類型",   En = "On-Fire Action Type", Ja = "発火時アクション種別", Ko = "실행 시 액션 유형" },
        new() { LangKey = "oa.designer.svc.timerActionKind.none",  ZhCN = "无（纯等待）",   ZhTW = "無（純等待）",   En = "None (pure wait)",    Ja = "なし（待機のみ）",     Ko = "없음(대기만)" },
        new() { LangKey = "oa.designer.svc.timerActionKind.write", ZhCN = "数据回写动作",   ZhTW = "資料回寫動作",   En = "Data-writeback action", Ja = "データ書き戻しアクション", Ko = "데이터 기록 액션" },
        new() { LangKey = "oa.designer.svc.timerActionKind.api",   ZhCN = "接口调用",       ZhTW = "介面呼叫",       En = "API call",            Ja = "API呼び出し",          Ko = "API 호출" },
```

- [ ] **Step 2: 重写清理 watch** — `NodePropertyPanel.vue:56-68` 的 `watch(() => local.value.serviceKind, ...)` 替换为（区分 timer：timer 合法保留 connector/path **和** actionName，二者由「到点动作类型」互斥控制，切走 timer 或 kind 才清）：

```typescript
watch(
  () => local.value.serviceKind,
  (kind) => {
    if (syncing.value) return
    if (local.value.type !== 'serviceTask') return
    // dataWriteback：无连接器/路径/到点。webApi：无到点动作。timer：connector/path/action 均可能合法
    //（由「到点动作类型」互斥控制，见 timerActionKind），故切到 timer 时不清，切离 timer 才由目标 kind 规则清。
    if (kind === 'dataWriteback') {
      local.value.serviceConnectorName = undefined
      local.value.servicePath = undefined
    } else if (kind === 'webApi') {
      local.value.serviceActionName = undefined
    }
    // kind === 'timer'：不在此清理；由 timerActionKind 切换负责清非选中变体（见下）。
  },
)

// 票8：timer「到点动作类型」——从当前已填字段派生，切换时清非选中变体的残留（防 Snapshot 优先级误外呼）。
const timerActionKind = computed<'none' | 'write' | 'api'>({
  get: () => local.value.serviceConnectorName ? 'api'
           : local.value.serviceActionName ? 'write'
           : 'none',
  set: (v) => {
    if (v === 'api') {
      local.value.serviceActionName = undefined            // 互斥：webApi 变体清回写动作
    } else if (v === 'write') {
      local.value.serviceConnectorName = undefined         // 互斥：回写变体清连接器/路径
      local.value.servicePath = undefined
    } else {
      local.value.serviceConnectorName = undefined         // none：全清
      local.value.servicePath = undefined
      local.value.serviceActionName = undefined
    }
  },
})
```

  > `computed` 已在 `:2` import。`Snapshot` 的优先级（timer + ConnectorName 优先判 webApi，见 `ServiceTaskActionRef.cs:65-73`）要求：选 write/none 时必须清空 `serviceConnectorName`，否则到点会静默外呼——上面的 setter 已保证。

- [ ] **Step 3: timer 分支补变体 UI** — `NodePropertyPanel.vue:442-469` 的 timer `<template>` 内，把原「到点动作」下拉（`:459-468`）替换为「类型选择 + 按类型渲染」：

```vue
          <!-- 定时器：延时模式 / 延时值 / 到点动作（none | 回写 | webApi 变体，票8）-->
          <template v-else-if="local.serviceKind === 'timer'">
            <el-form-item :label="t('oa.designer.svc.delayMode')">
              <el-radio-group v-model="local.serviceDelayMode">
                <el-radio value="duration">{{ t('oa.designer.svc.delayMode.duration') }}</el-radio>
                <el-radio value="untilDate">{{ t('oa.designer.svc.delayMode.untilDate') }}</el-radio>
                <el-radio value="untilExpr">{{ t('oa.designer.svc.delayMode.untilExpr') }}</el-radio>
              </el-radio-group>
            </el-form-item>

            <el-form-item :label="t('oa.designer.svc.delayValue')">
              <el-input
                v-model="local.serviceDelayValue"
                :placeholder="t('oa.designer.svc.delayValueHint')"
                clearable
              />
            </el-form-item>

            <!-- 到点动作类型（互斥） -->
            <el-form-item :label="t('oa.designer.svc.timerActionKind')">
              <el-select v-model="timerActionKind" style="width: 100%">
                <el-option value="none"  :label="t('oa.designer.svc.timerActionKind.none')" />
                <el-option value="write" :label="t('oa.designer.svc.timerActionKind.write')" />
                <el-option value="api"   :label="t('oa.designer.svc.timerActionKind.api')" />
              </el-select>
            </el-form-item>

            <!-- 回写变体：动作下拉 -->
            <el-form-item v-if="timerActionKind === 'write'" :label="t('oa.designer.svc.timerAction')">
              <el-select v-model="local.serviceActionName" style="width: 100%" clearable>
                <el-option
                  v-for="a in catalog.actions"
                  :key="a.name"
                  :value="a.name"
                  :label="a.label || a.name"
                />
              </el-select>
            </el-form-item>

            <!-- webApi 变体：连接器 + 路径（票8 补齐 spec §5.3 缺口） -->
            <template v-else-if="timerActionKind === 'api'">
              <el-form-item :label="t('oa.designer.svc.connector')">
                <el-select v-model="local.serviceConnectorName" style="width: 100%" clearable>
                  <el-option
                    v-for="c in catalog.connectors"
                    :key="c.name"
                    :value="c.name"
                    :label="c.label || c.name"
                  />
                </el-select>
              </el-form-item>
              <el-form-item :label="t('oa.designer.svc.path')">
                <el-input
                  v-model="local.servicePath"
                  :placeholder="t('oa.designer.svc.pathHint')"
                  clearable
                />
              </el-form-item>
            </template>
          </template>
```

- [ ] **Step 4: 验证**
```bash
cd cp6.web
NODE_OPTIONS=--max-old-space-size=8192 npm run type-check
npm run build
```
  预期：type-check/build 全绿。

- [ ] **Step 5: commit**
```bash
git add -A && git commit -m "fix(wfs-service-task): T8 定时器到点动作补 webApi 连接器/路径变体 UI + 互斥清理（补 spec §5.3 缺口，防 Snapshot 误外呼）"
```

---

## Global Constraints（每个 Task 都遵守）

- **测试基线不回归：**
  - 后端：`dotnet test CP6.Tests/CP6.Tests.csproj` 全绿——基线 **1509 测试**（5 skip = SQLite 既知限制）。`--filter Wf` 既有 Wf 测试字节等价（除本计划显式改动的测试断言外）。
  - 前端：`npm run test`（vitest run）**320 全绿** + `npm run type-check` 通过。**type-check 须大堆**（vue-tsc 内存密集）：
    - Bash 工具：`NODE_OPTIONS=--max-old-space-size=8192 npm run type-check`
    - PowerShell：`$env:NODE_OPTIONS='--max-old-space-size=8192'; npm run type-check`
- **EF 迁移 clean：**`dotnet ef migrations has-pending-model-changes --project CP6.Core/CP6.Core.csproj --startup-project CP6.WebApi/CP6.WebApi.csproj --context CP6Context` 报无 pending（本计划**不新增迁移**——无实体/DbSet 改动）。
- **零跨模块污染：**只碰 `CP6.Core/Services/Wf/**`、`CP6.WebApi/{Program.cs,Middleware,Seed}`、`cp6.web/src/views/oa/designer/**`、`cp6.web/src/utils/signalr.ts`、对应 `CP6.Tests/Wf/**`。**绝不碰** `views/space/**`、`Services/*Space*`、任何 Space 迁移/DbSet。每 Task 完成 `git show --stat` 复核 diff。
- **零硬编码色：**前端一切颜色走 Design System token（`var(--cp-danger)` 等，见 `cp6.web/src/styles/tokens.css`），禁十六进制字面量。
- **i18n 五语齐全：**任何新增文案键必须五语齐全 `ZhCN/ZhTW/En/Ja/Ko`，加进 `I18nOaServiceTaskScreenSeed.cs`，运行期 SeedLangs 幂等去重。
- **TDD 节奏：**先写失败测试→跑验证 FAIL→最小实现→跑验证 PASS→本地 commit（**不 push**）。提交信息风格：`fix(wfs-service-task): <中文描述>`。
- **独立性：**11 个 Task 互不依赖，可任意顺序 / 并行执行。建议顺序见文末「执行顺序」。


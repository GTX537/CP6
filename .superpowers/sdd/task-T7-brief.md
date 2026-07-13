## Task T7: 服务目录（service-catalog）加载失败的重试边界过窄 → 加显式重试

> **票7。** 缺陷：`NodePropertyPanel.vue:86-99` 用 `watch(isServiceTask, immediate)` + `catalogLoaded` 标记拉服务目录；失败时把 `catalogLoaded=false` 允许「下次重试」——但**该 watch 只在 `isServiceTask` 由 false→true 跳变时再触发**。当用户停在 serviceTask 节点（`isServiceTask` 恒为 true）时目录加载失败，动作/连接器下拉将**永久空白**，除非切到别的节点再切回。重试边界过窄。修法=(1) 把加载抽成 `loadCatalog()`；(2) 目录为空且已加载失败时，在下拉旁露一个「重试」链接（用户主动重拉）；(3) 保留原 `watch(immediate)` 首拉。新增 i18n 键 `oa.designer.svc.reloadCatalog`（五语）。

**Files:**
- Modify: `cp6.web/src/views/oa/designer/NodePropertyPanel.vue:81-99`（抽 `loadCatalog` + 暴露重试）、template 服务任务段加重试链接
- Modify: `CP6.WebApi/Seed/I18nOaServiceTaskScreenSeed.cs`（加 `oa.designer.svc.reloadCatalog` 键，五语）

- [ ] **Step 1: 实现——脚本区抽 `loadCatalog` + 失败态** — `NodePropertyPanel.vue:81-99` 替换为：

```typescript
// ── 服务目录（C-T3）：serviceTask 节点的动作/连接器下拉数据源 ────────
const catalog = ref<ServiceCatalog>({ actions: [], connectors: [] })
const catalogLoaded = ref(false)
const catalogFailed = ref(false)   // 票7：加载失败态，驱动模板露「重试」

async function loadCatalog() {
  catalogFailed.value = false
  try {
    catalog.value = await designerApi.getServiceCatalog()
    catalogLoaded.value = true
  } catch {
    catalogFailed.value = true      // HTTP interceptor 已 toast；此处标失败让用户可主动重试
  }
}

// 首拉：进入 serviceTask 节点时若未成功加载过，拉一次（组件被 Vue 复用无 :key，onMounted 只跑一次不可靠，
// 故用 watch(immediate)）。票7：失败后不再依赖 isServiceTask 跳变——模板提供显式「重试」入口调 loadCatalog。
watch(
  isServiceTask,
  (v) => { if (v && !catalogLoaded.value) void loadCatalog() },
  { immediate: true },
)
```

- [ ] **Step 2: 实现——template 服务任务段加重试链接** — 在服务任务段的「服务类型」下拉之后（`NodePropertyPanel.vue:371` `</el-form-item>` 之后）插入失败重试提示：

```vue
          <!-- 票7：目录加载失败时露显式重试（否则停在 serviceTask 节点将永久空下拉）-->
          <el-alert
            v-if="catalogFailed"
            type="warning"
            :closable="false"
            show-icon
            style="margin-bottom: 8px"
          >
            <template #title>
              <el-button link type="primary" size="small" @click="loadCatalog">
                {{ t('oa.designer.svc.reloadCatalog') }}
              </el-button>
            </template>
          </el-alert>
```

- [ ] **Step 3: 加 i18n 键** — `I18nOaServiceTaskScreenSeed.cs` 在「前端校验消息」段（`:50` 那条之前或之后）加：

```csharp
        new() { LangKey = "oa.designer.svc.reloadCatalog",     ZhCN = "重新加载服务目录", ZhTW = "重新載入服務目錄", En = "Reload service catalog", Ja = "サービスカタログを再読み込み", Ko = "서비스 카탈로그 다시 불러오기" },
```

  > 该 seed 已 `.Concat` 进 `Program.cs` i18n 链（E-T2 完成），无需再改 Program.cs；运行期 SeedLangs 幂等去重。

- [ ] **Step 4: 验证** — 前端类型检查 + 构建（组件改动无独立 vitest，靠 type-check/build 兜）：
```bash
cd cp6.web
NODE_OPTIONS=--max-old-space-size=8192 npm run type-check
npm run build
```
  预期：type-check 无 TS 错、build 成功。

- [ ] **Step 5: commit**
```bash
git add -A && git commit -m "fix(wfs-service-task): T7 服务目录加载失败露显式重试（修复停在 serviceTask 节点时下拉永久空白）"
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


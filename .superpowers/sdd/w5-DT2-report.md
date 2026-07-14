# D-T2 报告：连接器管理 tab（CRUD/启停/凭证不回显 UI）+ Connector.View/Edit 权限点

- **分支**：`feat/wfs-engine-infra`
- **Commit**：`7e49997`（已 push）
- **测试**：后端 2071 → **2075 绿 / 5 skip**（新增 4：WfConnectorControllerTests×4）；前端 453 → **456 绿**（新增 3：wfConnector.spec×3）
- **type-check**：clean（NODE_OPTIONS=8192，vue-tsc --build 零错）／**build**：clean
- **EF**：`has-pending-model-changes` = clean（零迁移零实体改动；引擎/Program.cs 零 diff——服务 DI 由 D-T1 已注册）
- **diff 复核**：8 文件，纯增控制器 1 + 前端 4（api/panel/dialog/spec）+ 改守卫测试 1 + 改 FlowAdmin.vue 挂 tab 1 + 增控制器测试 1。无 Space/WMS/MES/FIN/PUR 污染，无 CP6.Entity/迁移改动。

## 交付文件

| 文件 | 内容 |
|---|---|
| `CP6.WebApi/Controllers/Oa/WfConnectorController.cs` | 薄壳控制器，`[Route("api/oa/wf-connector")]`，list/get/create/update/enabled |
| `CP6.Tests/Wf/WfConnectorControllerTests.cs` | 直 new 控制器 + SQLite + DataProtection provider，4 剧本 |
| `CP6.Tests/OawfPermissionAttributeTests.cs`（改） | fail-closed 守卫重基线（18→19 / 42→45 / 词表加 Connector.*） |
| `cp6.web/src/api/oa/wfConnector.ts` | `WfConnectorItem`/`WfConnectorSaveBody` + api（list/get/create/update/enable） |
| `cp6.web/src/views/oa/admin/WfConnectorPanel.vue` | 列表（掩码 hasAuth 徽标）+ 启停 el-switch + 新建/编辑入口 |
| `cp6.web/src/views/oa/admin/WfConnectorDialog.vue` | 新建/编辑表单，authJson placeholder「已配置（不回显）」当 hasAuth |
| `cp6.web/src/views/oa/admin/FlowAdmin.vue`（改） | 新增「连接器」tab（name="connectors" lazy），最小接线不动既有 |
| `cp6.web/src/views/oa/admin/__tests__/wfConnector.spec.ts` | 掩码列表渲染 + 启停切换 + 新建入口 3 剧本 |

---

## 端点形状（实落，采 brief Step 2 显式 `[Route("api/oa/wf-connector")]`）

| 方法 | 路由 | 服务 | 权限 |
|---|---|---|---|
| GET | `/api/oa/wf-connector` | `ListAsync` → `List<WfConnectorView>`（掩码） | 只读 GET，不贴键（守卫 NoReadOnlyGetAction） |
| GET | `/api/oa/wf-connector/{id}` | `GetAsync` → `WfConnectorView?`（掩码；404=E-WF-018） | 只读 GET，不贴键 |
| POST | `/api/oa/wf-connector` | `CreateAsync` → `{ id }`；E-WF-028→400 | `[RequirePermission("oa-flow-admin","Connector.Edit")]` |
| PUT | `/api/oa/wf-connector/{id}` | `UpdateAsync`；E-WF-028→400；空 AuthJson 保留原密文 | `Connector.Edit` |
| POST | `/api/oa/wf-connector/{id}/enabled` | `SetEnabledAsync(bool)` | `Connector.Edit` |

> **路由取舍**：brief Step 2 明文 `[Route("api/oa/wf-connector")]` 与前端文件名 `wfConnector.ts` 一致 → 采之（D-T1 报告「端点形状（建议）」的 `/api/oa/connectors` 仅为建议，未采）。前后端一致，无歧义。
> **DELETE 未落**：brief Step 2 仅列 list/get/create/update/toggle（无 delete），故不暴露 `DeleteAsync`（服务侧已存在，v1 无删连接器 UI 入口——如需可后续加）。

### 掩码契约遵守（D-T1 交接 §2）
- 读端点恒无明文：控制器仅回 `WfConnectorView`（`AuthJson => null` 恒空，`HasAuth` bool）。前端列表仅渲染 hasAuth 徽标（CpTag `ok`/`muted`），DOM 无凭证明文（spec 测试 `Object.keys(rows[0]).not.toContain('authJson')` 锁定）。
- 写即写、留空保留：Dialog `hydrate()` 编辑态永不回填 authJson；`onSave()` 空串→`authJson:null`（后端 UpdateAsync 空即保留原密文，CreateAsync 空即无认证）；hasAuth 时 placeholder=`oa.connector.form.authConfigured`。
- E-WF-028：TimeoutSec≥租约由 D-T1 服务层抛 `InvalidOperationException("E-WF-028|...")`，控制器 `Err()` 转 400+码，前端 http 拦截器 toast（错误码词条归 F-T1）。

---

## ★ F-T1 交接清单

### 1. 权限键面（Sys_MenuAction / Sys_RoleAction 逐租户播种）
- **menuKey**：`oa-flow-admin`（锚定 oa-flow-admin 菜单 **733**；连接器 tab 挂在流程管理页，波③映射②口径，与 FlowTrigger.*/A-T4 无关的独立 ActionCode）。
- **ActionCode**：
  - `Connector.Edit`（**已贴键**，控制器 create/update/enabled 3 端点）— 高危写：连接器 CRUD/启停/凭证加密写。
  - `Connector.View`（**未贴键**——只读 GET 循守卫约定不贴；F-T1 决定是否只种词表/菜单 View 位）。
- **落库块**：`Sys_MenuAction{MenuId=733, ActionCode="Connector.Edit"/"Connector.View", ActionName, Sort}` + `Sys_RoleAction{RoleId=1(admin), MenuId=733, ActionCode}` 逐租户幂等块（范本 Program.cs:850-856；MenuKey=733 派生 `oa-flow-admin`）。
- **种子落地前中间态**：生产端写端点 fail-closed 403（既定，与 A-T4/F-T2/B-T2 先例一致）。

### 2. i18n 键面（Sys_Lang 五语 ZhCN/ZhTW/En/Ja/Ko，`I18nOaEngineInfraScreenSeed` 追加）
本任务前端 t() 键（回退=键文本，既定中间态，键文本归 F-T1）：

| 键 | 用途 |
|---|---|
| `oa.connector.tab` | tab 标题「连接器」 |
| `oa.connector.new` | 新建按钮 |
| `oa.connector.empty` | 空表提示 |
| `oa.connector.authYes` / `oa.connector.authNo` | 凭证徽标「已配置」/「无」 |
| `oa.connector.col.name` / `.displayName` / `.baseUrl` / `.timeout` / `.auth` / `.enabled` / `.actions` | 列头 |
| `oa.connector.form.name` / `.nameHint` / `.displayName` / `.baseUrl` / `.auth` / `.authHint` / `.timeout` | 表单标签/提示 |
| `oa.connector.form.authConfigured` | 凭证 placeholder「已配置（不回显）」（hasAuth 时） |
| `oa.connector.form.authPlaceholder` | 凭证 placeholder（新建/无凭证时，如 `{"type":"bearer","token":"..."}`） |
| `oa.connector.form.required` | 必填校验（name/baseUrl 空） |
| 复用现有：`common.edit`/`common.cancel`/`common.save` | 通用按钮 |

错误码词条（F-T1 i18n seed 落 LangKey，控制器/中间件按码 400 呈现）：
- `E-WF-028`（TimeoutSec≥租约拒绝，D-T1 已产码）— 五语文案。
- `E-WF-018`（连接器 GET 404 复用此码作 not-found 提示；如需独立 not-found 码由 F-T1 裁定）。

### 3. 守卫重基线数字（本任务已改 `OawfPermissionAttributeTests.cs`，零弱化）
| 断言 | 旧 | 新 |
|---|---|---|
| `OawfControllers_AreDiscovered` 计数 | 18 | **19**（+WfConnectorController） |
| `EveryMutatingAction...` taggedCount | 42 | **45**（+Connector.Edit×3） |
| 非 GET 端点总数 | 44 | **47** |
| exemptHit / ReadOnlyPostExemptions | 2 | 2（不变） |
| ActionVocabulary | — | +`"Connector.Edit"`、`"Connector.View"`（照 A-T4 处理 Calendar.View 方式，View 亦入词表约定） |

---

## 张力与 concerns（须复核人知会）

1. **View 键不贴、只入词表**（守卫口径 vs brief Step 2）：brief Step 2 写 GET 贴 `Connector.View`；但 `OawfPermissionAttributeTests.NoReadOnlyGetAction` 锁「只读 GET 禁贴键」——按守卫口径 + A-T4/F-T2 先例，GET list/get 降 `[Authorize]` 不贴 View 键，仅词表加 `Connector.View`（照 A-T4 把 `Calendar.View` 入词表方式）。**F-T1 决定 View 键面是否只种词表 / 菜单 View 位**。
2. **路由 `api/oa/wf-connector`**（采 brief 明文）：与 D-T1 报告建议的 `/api/oa/connectors` 不同，已采 brief Step 2 显式声明；前后端一致。若 F-T1/live QA 偏好 `connectors`，改 1 处控制器 Route + 1 处 api 前缀即可。
3. **Step 4 seed 未落**（波内既定分工，同 A-T4/F-T1 收口）：本任务只贴 `[RequirePermission]` + 交接键面，不写任何 Sys_MenuAction/RoleAction/i18n seed。种子落地前生产写端点 403。
4. **DELETE/凭证显式清除无 v1 路径**：连接器删除与「显式清空凭证」（区别于留空保留）无 UI；如需由后续任务补独立动作（D-T1 已注明 Update 留空保留=掩码读契约必需）。
5. **真实外呼未在本任务覆盖**：Panel/Dialog 为管理 CRUD，真 HTTP 连接器调用（DbWfConnector 走 IHttpClientFactory）留 live QA harness（F-T2）覆盖连接器全流程+真实外呼。

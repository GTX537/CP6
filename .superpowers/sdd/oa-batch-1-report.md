# OA 迁移批次1 报告（feat/ui-migrate-oa）

接手中断会话：工作区留有 5 个 OA 文件未提交改动（无 commit/无验证）。本报告为逐文件审计 + 补完 + 完整验证的结果。

## 结论摘要

- 接手时原改动**方向正确、质量良好**：FlowAdmin 完整模板化（CpPageShell+CpListPage），其余 4 页按形态正确处置（token 化 / 基础件替换 / 特殊页保留）。硬编码色值已清零，契约用法正确，无 i18n 词条丢失或臆造。
- 我补的：FormQuery 的 `el-empty → CpEmpty`（与同批 FormCatalog/FormInitiate 一致，接手时遗漏）；模板缺口 #23 登账（FormQuery 远程搜索过滤 → CpFilterBar 不支持）。
- 验证：type-check 0 error；test 316/316 全绿；4 页真栈走查无组件错误、交互正常。

---

## 每页盘点表 + 迁移摘要

### 1. FlowAdmin.vue（/oa/flow-admin，菜单734）— 改动最大
| 盘点项 | 迁移前 | 迁移后 | 谁做的 |
|---|---|---|---|
| API | flowAdminApi.list() / enable() | 不变（fetch 包装 list，total=数组长度） | 接手已有 |
| 列 | flowKey/flowName/formKey/version/enable（5列） | ListColumn[]，flowKey→kind:'mono'，enable→col-enable 插槽 | 接手已有 |
| 搜索/分页 | 无 | :paginated="false"（单表全量） | 接手已有 |
| 行操作 | el-switch 启停（toggling 守卫+乐观回滚） | col-enable 行内 el-switch，守卫/回滚保留，切换后 listRef.reload() | 接手已有 |
| v-permission | 无 | 无 | — |
| i18n | oa.flowadmin.* | 全保留；移除的 `t('共 {n} 条')` 由 CpPageShell :count 替代 | 接手已有 |
| 形态 | 查询列表页 → CpPageShell+CpListPage（正确） | ✅ | — |
| 硬编码色 | style 块（#303133 等） | scoped style **整块删除**，归零 | 接手已有 |

摘要：唯一性提示 el-alert 无 Cp 等价物，作为壳内首子项保留（合规，鼓励逃生舱）。@total-change→计数 pill 接线正确，真栈显示「6」。

### 2. ApproverMapView.vue（/oa/approver-map，菜单739）
| 盘点项 | 迁移前 | 迁移后 | 谁做的 |
|---|---|---|---|
| API | keys/roles/rows load + save | 不变 | — |
| 列 | 一致值/承认用户/承认角色/有効/操作（行内 el-input 编辑） | 不变（行内编辑表格，特殊页不套模板） | — |
| 形态 | 行内编辑网格 → 特殊页 token 化（正确） | ✅ | — |
| 硬编码色 | 内联 style="padding/margin/width" | 提取为 class，`.tcard` 卡壳用 --cp-card/--cp-r-md/--cp-shadow-1 | 接手已有 |
| i18n | oa.approverMap.* + t('common.operation') | 全保留 | — |

摘要：处置正确。**注意（非本批引入）**：操作列 `t('common.operation')` 在 ja 词典无对应，真栈显示为原始 key「common.operation」——此为迁移前既有 i18n 词典缺口（原文件第71行即如此），不改 i18n 机制，未动。

### 3. FormCatalog.vue（/oa/form-catalog，菜单735）
| 盘点项 | 迁移前 | 迁移后 | 谁做的 |
|---|---|---|---|
| API | catalogApi.load | 不变 | — |
| 形态 | 卡片目录（el-collapse + 表单卡）→ 特殊页 token 化（正确） | ✅ | — |
| 基础件 | el-empty | CpEmpty | 接手已有 |
| 硬编码色 | --el-border-color-light / --el-text-color-secondary | --cp-line-soft / --cp-muted / --cp-ink | 接手已有 |
| i18n | oa.catalog.* | 全保留 | — |

### 4. FormInitiate.vue（/oa/form-initiate，非菜单子页）
| 盘点项 | 迁移前 | 迁移后 | 谁做的 |
|---|---|---|---|
| API | forecastApi/draftApi/flowAdminApi | 不变 | — |
| 形态 | 动态表单填单页 + 预测时间线 → 特殊页 token 化（正确） | ✅ | — |
| 基础件 | el-empty | CpEmpty | 接手已有 |
| 硬编码色 | --el-text-color-primary / --el-border-color-light（面板标题/左边框/上边框） | --cp-ink / --cp-line-soft | 接手已有 |
| i18n | oa.initiate.* | 全保留 | — |

### 5. FormQuery.vue（/oa/form-search，菜单736）
| 盘点项 | 迁移前 | 迁移后 | 谁做的 |
|---|---|---|---|
| API | queryApi.search / flowAdminApi.list / userApi.getList | 不变 | — |
| 列 | 实例ID/流程类型/发起人/当前节点/状态/发起时间/操作 | 不变（保留 el-table） | — |
| 搜索字段 | 发起人(remote)/处理人(remote)/流程类型/状态/发起日期(daterange)/关键词 | 不变（保留 el-form 查询区） | — |
| 行操作 | 行点击→抽屉详情 + 详情按钮 | 不变 | — |
| 状态色 | el-tag :type=instanceStatusType | **CpTag :tone=instanceStatusTone**（warn/ok/danger/info/info，忠实对齐 inboxModel） | 接手已有 |
| 空态 | el-empty | **CpEmpty**（本批我补，接手时遗漏，与页3/4 一致） | 我补 |
| 形态 | 查询列表页，但**远程搜索过滤 → CpFilterBar 不支持** | 特殊页保留原机制保功能（缺口 #23） | 判定+登账 |
| 硬编码色 | 无 | 无 | — |

摘要：FormQuery 的发起人/处理人为 `el-select filterable remote :remote-method`（异步用户搜索），CpFilterBar 的 FilterField select 只接受静态 options，强套会丢失远程搜索能力；加之 daterange + 6 字段 + 行→抽屉，整体按「特殊页保留原机制」处置，登记模板缺口 #23。仅做无损基础件替换（CpTag/CpEmpty）。

---

## 验证证据

- **type-check**：`vue-tsc --build` 0 error（NODE_OPTIONS=--max-old-space-size=4096）。
- **test**：`vitest run` → Test Files 46 passed，Tests **316 passed**（基线 316，无下降）。
- **真栈走查**（dev 5173，admin/123456，gstack browse）：
  | 页 | 路由 | 结果 | 截图 |
  |---|---|---|---|
  | FlowAdmin | /oa/flow-admin | 标题+计数pill「6」、mono flowKey、6行 switch，refresh 可点无错 | shots/oa-flowadmin.png |
  | ApproverMap | /oa/approver-map | 工具栏+.tcard 表格卡渲染，无组件错误 | shots/oa-approvermap.png |
  | FormCatalog | /oa/form-catalog | collapse 分组 + 表单卡（收藏星/記入する），无错 | shots/oa-formcatalog.png |
  | FormQuery | /oa/form-search | 查询区+表格+CpEmpty 空态，查询/重置可点无错 | shots/oa-formquery.png |
  - 各页 console 无新增组件错误（仅既有 intlify flatten warning / Vue Router next() 弃用 warning / 未登录态 401·403，均与本批无关）。
  - FormInitiate 为非菜单子页（从填单目录进入），走查覆盖其父页 FormCatalog；其改动纯 token 化，type-check + test 已覆盖。

## 新增模板缺口

1 项：**#23 CpFilterBar select 不支持 remote 远程搜索选项**（FormQuery 触发），已记入 `docs/superpowers/plans/2026-07-04-ui-restyle.md`。

## Concerns

- （非本批引入）ApproverMapView 操作列 `t('common.operation')` 在 ja 词典无翻译，真栈显示原始 key。迁移前既有，属 i18n 词典缺口，本批不改 i18n 机制未动，建议后续补词条。
- FormQuery 空态时 el-table 自带「No Data」与页级 CpEmpty 并存（迁移前原设计即如此，非本批引入）。

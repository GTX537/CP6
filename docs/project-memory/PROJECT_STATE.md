# 项目当前状态

最后更新：2026-08-01

## E09-S03 完成状态（2026-08-01）

- E09-S03 外部只读 Portal 与字段策略已完成并进入受控集成基线：功能提交 `88bc42d1`，no-ff 集成提交 `1850b2d8`，起始基线 `13c7b9da`。
- 新增租户权威 `Space_FieldPolicy` 与 `Space_FieldPolicyField`，覆盖 PublishedScene/Stock/Task、显式字段 allowlist、None/Partial/Hash/Redact 掩码、Audience、`CanExport`、版本和终态退休；数据库以复合租户外键、检查约束和过滤唯一索引失败关闭。
- 新增 `/api/space/field-policy` 管理 API，读取/变更沿用 `space:external:read/manage`；Grant 控制行与空间范围，字段策略控制字段、脱敏和导出能力，未知字段默认不可见，导出必须同时得到 Grant 与策略许可。
- 新增 `/api/space/portal/v1` 只读 Organizations/Sites/PublishedScene/Stock/Tasks。外部主体只允许 Portal 的 GET/HEAD；除组织选择外必须提供单一非空 Organization Context，内部主体、未知主体类型、缺失/歧义上下文和外部主体访问其他 Space 路径均拒绝。
- Portal 只读取当前 Published/Active；结构 ID 来自数据库权威候选，业务值字段按策略裁剪。多个合法 Grant 按完整子句 OR，字段采用命中范围内最少限制掩码；其他资源 Grant、运行源身份和 Zone-only 父层字段均不能扩大可见范围。
- OpenAPI 从 29 paths / 38 operations 增至 36 paths / 47 operations，并更新 C#/TypeScript SDK；OpenAPI、C#、TypeScript 工件 SHA-256 分别为 `BCFFEF09...DAF2`、`C7BCC222...6C4B`、`5F5132E7...9ABF`。
- 功能门禁：Space Unit 231/231；Space Integration + KOUSQLSERVER 181/181、0 skipped；CP6.Tests 2711 passed / 17 environment-gated skipped；完整 solution build 0 error；前端 106 files / 607 tests、type-check、production build；EF/SDK drift 与 C#/TypeScript SDK 编译通过。S02→S03 增量 SQL 在临时库连续执行两次通过。
- 合并态聚焦门禁：完整 solution build 0 error、字段策略领域 3/3、Portal/策略/Grant/求值器含真实 SQL 22/22、权限/OpenAPI/中间件/ProblemDetails 84/84、EF/SDK drift、前端 106 files / 607 tests、type-check 和 production build 全部通过。交付报告见 `docs/space/reports/e09-s03-external-portal-field-policy.md`。
- 下一张建议卡为 E09-S04：跨租户越权自动化测试，覆盖猜测 ID、同码组织/仓库/库位、分页/游标、缓存和 Portal DTO；E09-S05 随后补齐外部访问审计与有效期证据。

## E09-S02 完成状态（2026-08-01）

- E09-S02 外部组合授权与访问求值器已完成并进入受控集成基线：功能提交 `cae12c7e`，no-ff 集成提交 `feefa9cd`，起始基线 `8869ac58`。
- 新增 `Space_ExternalGrant` 及 Floor/Zone/Owner/Object 规范化子表；Site 必填，Floor/Zone 固定使用当前 Published Revision 的稳定 LogicalId，数据库以复合租户外键、状态/有效期/版本检查和过滤唯一索引失败关闭。
- 多个 Grant 保持完整子句 OR，单个 Grant 内 Site/Floor/Zone/Owner/BusinessObject 按 AND 匹配；禁止跨 Grant 展平维度造成笛卡尔权限升级。Export 还要求命中子句显式 `CanExport`。
- `ISpaceAccessEvaluator` 验证可信 Tenant/User、单一 Organization Context、Active Organization、有效 Active Membership 与有效 Active Grant；任一缺失、过期、暂停、撤销、歧义或跨组织拼接均拒绝。安全范围携带组织/成员安全戳、GrantVersion 和确定性 AuthorizationVersion。
- 新增 `/api/space/external-organization/{organizationId}/grant` 管理 API，读取/变更沿用 `space:external:read/manage`。`FieldPolicyId` 在 E09-S03 前只作保留字段，非空请求固定 422；外部主体仍被全局拒绝直接进入 `/api/space`，未提前开放 Portal。
- OpenAPI 增加 2 个 route family / 4 个操作并更新 C#/TypeScript SDK；运行时客户端表面哈希为 `FFCE63E749C7653E553A57D32EA85A7FF846F17199AF3436FE787CD26F509259`。
- 功能门禁：Space Unit 228/228；Space Integration + KOUSQLSERVER 169/169、0 skipped；CP6.Tests 2703 passed / 17 environment-gated skipped；完整 solution build 0 error；前端 106 files / 607 tests、type-check、production build；EF/SDK drift 与 C#/TypeScript SDK 编译通过。S01→S02 增量 SQL 在临时库连续执行两次通过。
- 合并态聚焦门禁：完整 solution build 0 error、领域 4/4、访问求值/管理/真实 SQL 10/10、权限/OpenAPI 35/35、EF/SDK drift 通过。交付报告见 `docs/space/reports/e09-s02-external-grant-scope-evaluator.md`。
- 下一张建议卡为 E09-S03：接入 Published-only 外部只读 Portal、资源 DTO allowlist、字段策略/脱敏和导出裁剪；完成前继续保留外部主体的全局拒绝。

## E09-S01 完成状态（2026-08-01）

- E09-S01 外部组织与成员模型已完成并进入受控集成基线：功能提交 `a599cfd7`，no-ff 集成提交 `09538ca3`，起始基线 `0c02fc80`。
- 新增租户权威 `Space_ExternalOrganization` 与 `Space_ExternalMembership`；支持 Customer/Supplier/ThirdPartyLogistics、ERP BusinessPartner 可选关联、用户多组织成员关系、有效期、终态生命周期、SecurityStamp、rowversion 和审计字段。
- 数据库以复合租户外键、过滤唯一索引、枚举/有效期/关联成对检查约束失败关闭；同类型组织编码唯一，客户/供应商/3PL 可以使用相同业务码而保持组织隔离。
- 新增 `/api/space/external-organization` 组织/成员管理 API，读取需要 `space:external:read`，变更需要 `space:external:manage`；跨租户用户、客商与组织引用不泄露存在性。
- OpenAPI、C# 与 TypeScript SDK 已更新；运行时客户端表面哈希为 `6011AA0FC2B4B2A81C5D915B1DEE1D0ADC84BE01BB8D2962A3D087B896E1EF76`。
- 功能门禁：Space Unit 224/224；Space Integration + KOUSQLSERVER 159/159、0 skipped；CP6.Tests 2703 passed / 17 environment-gated skipped；完整 18 项目 solution build 0 error；前端 106 files / 607 tests、type-check、production build；EF/SDK drift、TypeScript SDK 与运行时客户端表面均通过。
- 合并态聚焦门禁：领域 4/4、组织/成员内存与真实 SQL 6/6、权限/种子/ProblemDetails/OpenAPI 49/49、EF/SDK drift 与 TypeScript SDK 编译通过。交付报告见 `docs/space/reports/e09-s01-external-organization-membership.md`。
- 下一张建议卡为 E09-S02：实现 Organization Context、有效 Membership 与 Site/Floor/Zone/Owner/BusinessObject 组合 Grant，并在缺失、歧义或跨组织拼接时失败关闭。

## E08-S05 完成状态（2026-08-01）

- E08-S05 10,000 库位性能基线已完成并进入受控集成基线：功能提交 `cc1d8baf` + `24464fab`，no-ff 集成提交 `7a05c05f` + `675e485c`，起始基线 `5d37865a`。
- 锁定门槛：完整场景 ≤100 draw calls、Medium tier ≥50fps、≤3 秒可交互、标签 P95 ≤16ms/对象池 ≤200、拾取 P95 ≤150ms、10,000 条着色与运行态查询 ≤3 秒。
- 500 个货架框已从逐对象 `LineSegments` 合并为单个 wireframe `InstancedMesh`；完整标准仓 draw calls 从 535 降到 36。库位颜色缓冲在建桶时预分配，库存覆盖层走分桶批量着色并保留旧 ViewerHandle 回退。
- 硬件 WebGL 验收使用 Intel Iris Xe / D3D11：10,000 库位、36 draw calls、275ms 可交互、P95 帧间隔折算 83.3fps、标签 3.5ms、拾取 0.4ms、着色 3.0ms、35 个同屏标签、0 console errors。执行器检测到 SwiftShader 时拒绝形成 GPU PASS。
- 运行态服务现以精确 10,000 个 Published/Active 库位验证库存与任务查询各 20×500 分块且各自 ≤3 秒；既有 10,001 个不同库位在 WMS 调用前 400 拒绝。
- 功能分支门禁：Space Unit 220/220；Space Integration 105 passed + 48 SQL-gated skipped；OpenAPI/SDK 18/18；前端 106 files / 607 tests、CPU/硬件性能门禁、type-check、production build；WebApi/C#/TypeScript SDK build 与 SDK drift 通过。
- 合并态聚焦门禁：Viewer 19/19、CPU 性能 1/1、运行态 10,000/10,001 边界 2/2、follow-up 2/2、type-check 与 range whitespace 通过。交付报告见 `docs/space/reports/e08-s05-10000-location-performance.md`。
- 下一张建议卡为 E09-S01：外部组织与成员模型，使客户、供应商、3PL 可关联用户和租户。

## E08-S04 完成状态（2026-08-01）

- E08-S04 拣货任务与路径验收已完成并进入受控集成基线：功能提交 `9f7e38f8`，no-ff 集成提交 `994339a6`，起始基线 `944e465f`。
- 新增 `GET /api/space/design/v1/sites/{siteId}/runtime/tasks/path?taskId=...`；任务号必填且在 WMS 边界筛选，继续沿用 Published/Active、采纳身份、500 分块、10,000 上限及来源新鲜度。
- 响应提供 WMS 实际顺序、楼层/库区/坐标、跨层/跨区切换、总量与分区工作量，以及当前 Published 巷道拓扑；可用空结果与 `Unavailable` 严格区分，重复实际序号失败关闭为 502。
- Viewer 同时展示实际顺序、仅演示且不回写 WMS 的优化顺序、跨层/跨区和工作量；实际停靠点支持 Locator 跨层定位，残缺坐标不生成不完整优化路径。
- 当前 Design Revision 没有连接体拓扑，跨层段明确降级为近似直连并提示，不把近似路线伪装为精确结果。
- 功能分支门禁：Space Unit 220/220；Space Integration 105 passed + 48 SQL-gated skipped；OpenAPI/SDK 18/18；前端 105 files / 603 tests、type-check、production build；WebApi/C#/TypeScript SDK build 与 SDK drift 通过。
- 合并态聚焦门禁：runtime 47/47、OpenAPI/SDK 18/18、前端 9/9、type-check 与 SDK drift 通过。交付报告见 `docs/space/reports/e08-s04-task-path-acceptance.md`。
- 下一张建议卡为 E08-S05：10,000 库位性能基线，锁定场景交互、标签和批量查询门槛。

## E08-S03 完成状态（2026-08-01）

- E08-S03 物料/批次/容器定位验收已完成并进入受控集成基线：功能提交 `8d8f7e01`，no-ff 集成提交 `dfb6e93b`，起始基线 `faeacd4b`。
- 新增统一运行源端点 `GET /api/space/design/v1/sites/{siteId}/runtime/inventory/locate`；物料、批次、容器至少一个，多个条件固定按精确 AND 匹配。
- 查询当前 Published/Active Space 库位并沿用采纳后的 WMS 身份、500 分块和 10,000 上限；响应按 Space 逻辑库位聚合，显式提供双身份/双编码、楼层、数量、匹配事实、命中库位数、楼层数与 E08-S02 来源新鲜度。
- 可用来源的零命中与 `Unavailable` 来源严格区分；不满足条件、非正库存或同一 WMS 身份多编码的适配器响应以 502 合同违例失败关闭。
- Viewer 搜索支持编码、物料、批次、容器；多结果按楼层分组，由用户选择候选后复用现有跨层 Locator，不再擅自跳第一条；旧并发响应不能覆盖新搜索。
- 功能分支门禁：Space Unit 220/220；Space Integration 101 passed + 48 SQL-gated skipped；OpenAPI/SDK 18/18；前端 103 files / 597 tests、type-check、production build；WebApi/C# SDK 0 warning / 0 error；TypeScript SDK 与 SDK drift 通过。
- 合并态聚焦门禁：runtime 44/44、OpenAPI/SDK 18/18、前端 5/5、type-check 通过。交付报告见 `docs/space/reports/e08-s03-inventory-locate.md`。
- 下一张建议卡为 E08-S04：拣货任务与路径验收，覆盖实际/优化顺序、跨区/跨层和工作量。

## E08-S02 完成状态（2026-08-01）

- E08-S02 库存来源、时间和延迟展示已完成并进入受控集成基线：功能提交 `9a478c7a`，no-ff 集成提交 `d4cd8a82`，起始基线 `bbe77f3e`。
- 3D Viewer 库存覆盖层已从旧楼层库存接口迁移到 E08-S01 统一运行源；按当前楼层 Space 逻辑库位 ID 查询和聚合，不以 WMS 编码漂移替换稳定身份。
- 运行源公开并显示来源类型、来源系统、运行连接、数据观察时间、CP6 接收时间、延迟、时钟超前，以及 Viewer 会话最近成功/最近失败/恢复状态。
- 统一源暂无容量、锁定和拣货流程事实，Viewer 只展示空/有库存，并把利用率明确标为占用估算；不伪造满、锁定或在拣状态。
- 功能分支门禁：Space Unit Release 220/220；Space Integration Release 96 passed + 48 SQL-gated skipped；OpenAPI/SDK 18/18；前端 102 files / 593 tests、type-check、production build；C# SDK 0 warning / 0 error；SDK 无 drift。
- 合并态聚焦门禁：runtime 40/40、OpenAPI/SDK 18/18、前端 20/20、type-check 通过。交付报告见 `docs/space/reports/e08-s02-runtime-freshness.md`。
- 下一张建议卡为 E08-S03：物料/批次/容器定位验收，覆盖多结果、空结果和跨层结果。

## E08-S01 完成状态（2026-07-31）

- E08-S01 统一运行态数据源已完成并进入受控集成基线：最终功能提交 `3df6b1d2`、no-ff 集成提交 `b2bb7a35`、设计提交 `636eb6d5`。
- 功能分支全量验证：Space Unit 220 passed / 0 failed / 0 skipped；默认 Space Integration 94 passed / 0 failed / 48 SQL 环境门禁 skipped；OpenAPI/权限/数据源合同聚焦 45 passed；Release 完整 solution build 0 error / 10 个既有 warning；SDK 无 drift；EF 无待迁移模型变化；feature range `git diff --check` 静默通过。
- 最终复审修复了生成 C#/TypeScript SDK 丢失合法 nullable 响应类型的问题；修复后 OpenAPI/权限 34/34、Client build 0 warning / 0 error、SDK 无 drift，并由原 API 复审者确认关闭。合并态门禁为 runtime/adapter unit 23/23、runtime/adapter/simulator integration 56/56、OpenAPI/权限 34/34、SDK 无 drift。
- 运行权威规则：当前 Published/Active Space 模型是空间与身份权威；生产 `Cp6SpaceWmsAdapter` 是库存/任务运行态权威；模拟器只允许显式选择/测试；Design Revision 不持久化库存、任务等运行事实。
- 已交付 `GET /api/space/design/v1/sites/{siteId}/runtime/inventory` 与 `GET /api/space/design/v1/sites/{siteId}/runtime/tasks`，均要求 `space:model:read`，支持重复 `locationLogicalId` 筛选、Space/WMS 双 LogicalId 与双编码。
- 查询按 500 个位置分块、最多 10,000 个位置；来源/输出合同违例失败关闭为 502，适配器异常为可重试 503；明确 `Unavailable` 返回空 `Items` 并携带 `IsAvailable=false`，不与真实空结果混同。
- 该节记录 E08-S01 完成时的后续建议；E08-S02 现已由上方最新状态接续完成。E08-S01 交付报告见 `docs/space/reports/e08-s01-unified-runtime-source.md`。

## E07-S05 完成状态（2026-07-31）

- E07-S05 存量 WMS 采纳与绑定已完成：独立采纳账本、刷新、分页、单项/批量绑定、空位放置、差异 Issue 同步、rowversion 并发、权限/OpenAPI/SDK 和 Design V1 编辑器侧栏均已闭环。
- 功能提交 `15ccf992`，no-ff 集成提交 `389bf4ec`；交付报告见 `docs/space/reports/e07-s05-wms-adoption.md`。
- 验证：Space Unit 218；默认 Space Integration 56 passed / 48 SQL-gated skipped；WMS 聚焦 11/11，其中 KOUSQLSERVER 3/3；OpenAPI/权限 35/35；前端 98 files / 579 tests；production build、完整 solution build、EF model drift 和 SDK drift 均通过。
- E07-S01 至 E07-S05 已全部进入受控集成基线。该条记录的是 E07-S05 完成时的后续建议；E08-S01 现已由上方最新状态接续完成。

## Git

- Space E07 S05 功能/集成提交：`15ccf992` / `389bf4ec`
- Space E08 S01 功能/集成提交：`3df6b1d2` / `b2bb7a35`
- Space E08 S02 功能/集成提交：`9a478c7a` / `d4cd8a82`
- Space E08 S03 功能/集成提交：`8d8f7e01` / `dfb6e93b`
- Space E08 S04 功能/集成提交：`9f7e38f8` / `994339a6`
- Space E08 S05 功能/集成提交：`cc1d8baf` + `24464fab` / `7a05c05f` + `675e485c`
- Space E09 S01 功能/集成提交：`a599cfd7` / `09538ca3`
- Space E09 S02 功能/集成提交：`cae12c7e` / `feefa9cd`
- Space E09 S03 功能/集成提交：`88bc42d1` / `1850b2d8`

- 交付分支：`main`
- T6 通过 merge commit `d79a39c` 合入并推送；T7 冒烟修复为 `ffca422`
- Space 受控集成分支：`integration/space-v1-20260730`
- Space E00 + E01 S01–S03 集成提交：`539d56de`
- Space E01 S04 功能/集成提交：`bac76444` / `85792161`
- Space E01 S05 功能/集成提交：`3258d47f` / `36f534d9`
- Space E01 S06 功能/集成提交：`6daf1aeb` / `2ccdff7a`
- Space E02 S01 实验门禁功能/集成提交：`fe959066` / `3742fbff`
- Space E04 S01 功能/集成提交：`1d57a3b5` / `e8e84853`
- Space E04 S02 功能/集成提交：`20ee0af0` / `c1043d15`
- Space E04 S03 功能/集成提交：`b322e84a` / `39146c38`
- Space E04 S04 功能/集成提交：`9a87dc30` / `f9c7fd21`
- Space E07 S01–S03 功能/集成提交：`d06a8bd1` / `6e67a9d1`
- Space E07 S04 功能/集成提交：`74577015` / `6d751e0c`
- Space E13 S01 功能/集成提交：`8f7fc25e` / `ea161975`
- Space E13 S02 功能/集成提交：`cff25a25` / `94822669`
- Space E13 S03 功能/集成提交：`cebd401a` / `dca6e19c`
- Space E13 S12 功能/集成提交：`54456946` / `b33929fb`
- Space E05 S01 功能/集成提交：`5bb0cdfb` / `49dbabe3`
- Space E05 S02 功能/集成提交：`2fc03681` / `3d554852`
- Space E05 S03 功能/集成提交：`00021f0a` / `a1edecef`
- Space E05 S04 功能/集成提交：`85b57960` / `888de795`
- Space E05 S05 功能/集成提交：`856f138c` / `a3864d9c`
- Space 历史基线文档提交：`407dcbea`
- Space 后续候选安全检查点：`checkpoint/space-candidate-20260730`（`0d25da4d`，不得整包合入）
- 远端：`origin`（GitHub 私有仓库）
- 换机标签：`migration-2026-07-18-ready`
- 数据备份：Git LFS 三对象，已推送并校验

## 当前波：Space V1 受控集成

| 范围 | 状态 | 证据 |
|---|---|---|
| E00 S01–S04 | 已进入集成基线 | `539d56de`；事实清单、兼容护栏、数据源契约、审计/可观测性 |
| E01 S01–S06 | 已进入集成基线 | `539d56de` + `85792161` + `36f534d9` + `2ccdff7a`；版本/来源文件/Job Ledger、Published→Draft Clone、Design API v1、生成 SDK、文件安全扫描与保留清理 |
| E02 S01 | 部分进入集成基线，最终签收受阻 | `fe959066` + `3742fbff`；中立审计/压力/运行证据/preflight 已集成，正式黄金集、授权、供应商包/凭据和冻结 Worker 尚缺 |
| E04 S01–S04 | 已进入集成基线 | `1d57a3b5` + `e8e84853` + `20ee0af0` + `c1043d15` + `b322e84a` + `39146c38` + `9a87dc30` + `f9c7fd21`；安全底图、坐标标定、通用元素属性、货架/元素统一多选、对齐、等距、旋转、删除、阵列与补偿式撤销/重做 |
| E07 S01–S05 | 已进入集成基线 | `d06a8bd1` + `6e67a9d1` + `74577015` + `6d751e0c` + `15ccf992` + `389bf4ec`；版本化能力合同、CP6 真实适配器、持久化幂等账本、标准模拟器、确定性标准仓与存量 WMS 采纳/绑定 |
| E08 S01–S05 | 已进入集成基线 | `3df6b1d2` + `b2bb7a35` + `9a478c7a` + `d4cd8a82` + `8d8f7e01` + `dfb6e93b` + `9f7e38f8` + `994339a6` + `cc1d8baf` + `24464fab` + `7a05c05f` + `675e485c`；统一 Published 运行源、双身份、来源新鲜度、库存定位、任务路径与 10,000 库位性能基线 |
| E09 S01–S03 | 已进入集成基线 | `a599cfd7` + `09538ca3` + `cae12c7e` + `feefa9cd` + `88bc42d1` + `1850b2d8`；租户权威外部组织/成员、组合 Grant、Organization Context、访问求值、字段策略/脱敏、Published-only 外部只读 Portal、真实 SQL 约束及管理 API/SDK |
| E13 S01–S03、S12 | 已进入集成基线 | Provider/确定性端口、可审计 Run/Proposal/Decision/Usage 模型、可恢复 Worker 控制面，以及数据库并发槽与预算账本 |
| E05 S01–S05 | 已进入集成基线 | 通用元素、逐层货架、统一场景 DTO、版本化资产库及确定性参数化 3D 渲染 |
| E04 S05–S06、E06、E08 S05+ 等剩余范围 | 候选证据或尚未实现 | `0d25da4d` 只作提取来源；不得以候选报告替代集成验收 |

## 上一完成波：GR-VP

| 任务 | 状态 | 证据 |
|---|---|---|
| T1 标准一般用户角色种子 | 完成 | `ddcfa1ac`，7 测试 |
| T2 OA/WF v-permission | 完成 | `15823c38`，40 按钮/17 视图 |
| T3 ERP v-permission | 完成 | `4a48525e`，39 按钮/16 视图 |
| T4 MES v-permission | 完成 | `6e4ade1`，31 指令/12 视图/24 键 |
| T5 FIN v-permission | 完成 | `5732057`，66 指令/16 视图/51 键 |
| T6 PUR/PLAN/PUB v-permission | 完成 | `4bb7512` + `cf20d42`，37 页面级声明/12 视图/33 键；异步加载守权 |
| T7 部署与冒烟 | 完成 | API/Web 双镜像；A1 角色 SQL；`qa_general` 自审批/越权/403 冒烟 |

## 最近验证基线

- E09-S03 已推进至受控集成提交 `1850b2d8`：功能分支门禁为 Space Unit 231/231、Space Integration + KOUSQLSERVER 181/181 且 0 skipped、CP6.Tests 2711 passed / 17 environment-gated skipped、完整 solution build 0 error、前端 106 files / 607 tests、EF/SDK drift、C#/TypeScript SDK 编译与 S02→S03 幂等增量 SQL 双执行通过；合并态聚焦门禁为 3/3、22/22、84/84、前端 607/607 及 EF/SDK drift。
- E09-S02 已推进至受控集成提交 `feefa9cd`：功能分支门禁为 Space Unit 228/228、Space Integration + KOUSQLSERVER 169/169 且 0 skipped、CP6.Tests 2703 passed / 17 environment-gated skipped、完整 solution build 0 error、前端 106 files / 607 tests、EF/SDK drift、C#/TypeScript SDK 编译与 S01→S02 幂等增量 SQL 双执行通过；合并态聚焦门禁为 4/4、10/10、35/35 及 EF/SDK drift。
- E09-S01 已推进至受控集成提交 `09538ca3`：功能分支门禁为 Space Unit 224/224、Space Integration + KOUSQLSERVER 159/159 且 0 skipped、CP6.Tests 2703 passed / 17 environment-gated skipped、完整 solution build 0 error、前端 106 files / 607 tests、EF/SDK drift、TypeScript SDK 和运行时客户端表面通过；合并态聚焦门禁为 4/4、6/6、49/49 及 EF/SDK drift。
- E08-S04 已推进至受控集成提交 `994339a6`：功能分支门禁为 Space Unit 220/220、Space Integration 105 passed / 48 SQL-gated skipped、OpenAPI/SDK 18/18、前端 105 files / 603 tests、type-check、production build、WebApi/C#/TypeScript SDK build 与 SDK drift；合并态聚焦门禁为 runtime 47/47、OpenAPI/SDK 18/18、前端 9/9、type-check 和 SDK drift。
- E08-S03 已推进至受控集成提交 `dfb6e93b`：功能分支门禁为 Space Unit 220/220、Space Integration 101 passed / 48 SQL-gated skipped、OpenAPI/SDK 18/18、前端 103 files / 597 tests、type-check、production build、WebApi/C#/TypeScript SDK build 与 SDK drift；合并态聚焦门禁为 runtime 44/44、OpenAPI/SDK 18/18、前端 5/5 和 type-check。
- E08-S02 已推进至受控集成提交 `d4cd8a82`：功能分支门禁为 Space Unit Release 220/220、Space Integration Release 96 passed / 48 SQL-gated skipped、OpenAPI/SDK 18/18、前端 102 files / 593 tests、type-check、production build、C# SDK build 与 SDK drift；合并态聚焦门禁为 runtime 40/40、OpenAPI/SDK 18/18、前端 20/20 和 type-check。
- E08-S01 已推进至受控集成提交 `b2bb7a35`：功能分支全量门禁为 Space Unit 220、默认 Space Integration 94 passed / 48 SQL-gated skipped、OpenAPI/权限/数据源合同 45、完整 solution build 0 error / 10 个既有 warning、EF/SDK drift 均通过；合并态聚焦门禁为 23/23、56/56、34/34 和 SDK 无 drift。
- 历史 E04-S04 验证快照：当时集成代码提交为 `f9c7fd21`。合并态完整 solution 构建 0 error / 10 个既有 warning；Space Unit 213 passed，默认 Space Integration 48 passed / 45 SQL-gated skipped，Design Scene 真实 SQL 3/3 passed；OpenAPI/权限 25/25 passed；前端 96 files / 575 tests、type-check、production build 通过；EF 无待迁移模型变化，SDK drift 通过。
- E02 S01 实验门禁已推进至 `3742fbff`：中立工具 10/10 测试通过，Aspose 隔离实验适配器构建 0 warning / 0 error；5 个冻结 Seed 完整性通过，50MiB 与 100 万实体压力资产生成通过。严格 readiness 按预期退出 `3`，ODA/APS 模板 preflight 按预期退出 `4`，表明外部签收条件仍未满足。
- E07 S01–S03 已推进至 `6e67a9d1`：Release 全解构建 0 error（7 个既有测试 warning），Space Unit 73 passed，Space Integration 35 passed / 30 SQL 环境门禁 skipped，CP6 主测试 2674 passed / 17 environment-gated skipped，Client 71 passed，EF 模型与 Migration 一致；新增代码精确格式门禁通过。
- E07 S04 已推进至 `6d751e0c`：500 货架、10,000 库位、100 SKU、5,000 库存记录、100 拣货任务和 6 个固定故障样本由同一固定种子生成；两次独立生成的 17 个文件差异为 0，干净检出后的 Manifest 16 个受管文件哈希错误为 0。合并态 Release 全解构建 0 error（10 个既有 warning），Space Unit 79 passed，Space Integration 40 passed / 30 SQL 环境门禁 skipped，CP6 主测试 2680 passed / 17 environment-gated skipped，Client 71 passed。
- E13 S01–S03、S12 已完成 Provider 安全端口、运行审计模型、可恢复 Worker 控制面、三并发槽和日/月预算原子账本；外部 Provider、CAD IR、输出校验与 Apply 仍未提前启用。
- E05 S01–S05 已完成通用元素、非均匀逐层货架、Design Revision 权威场景、System/Tenant 版本化资产库和 `space-parametric-v1` 确定性前端渲染链；资产不加载外部 URL 或脚本。
- E04 S01–S04 已完成 PDF/PNG/JPG 底图、安全扫描、挂接、标定、通用元素属性，以及货架/元素共享的框选、对齐、等距、旋转、删除、阵列和补偿式撤销/重做；命令继续保持 Draft/revision 失败关闭、协议幂等、整批原子性和逐命令 before/after 审计。默认扫描器继续失败关闭，多副本生产环境必须配置真实扫描引擎与共享耐久卷。
- Space 集成前端：type-check 通过，96 files / 575 tests passed，production build 通过；仅有既有大 chunk 提示。
- CP6.Tests 全量本轮为 2682 passed / 6 个既有 RFQ 固定日期失败 / 17 environment-gated skipped；同一 RFQ 失败已在 S03 前的 `f8dff096` 基线复现，不是 Space 回归。
- 后续候选检查点 `0d25da4d` 已独立通过更大范围候选回归，但它仍不是实现真相，也不授权整包合并。
- 后端在 GR-VP T1 报告中：2220 passed / 5 skipped。
- 前端在 T7 干净 `main` 重新验证：73 files / 488 tests passed，type-check 0，2649 modules production build 通过；在线 Web 与新 chunk 均为 200。
- T6 后端权限 oracle：11/11 passed。
- T7 真实权限链：4 菜单、8 动作；本人待办 200、他人待办 400、无权端点 403；测试流程数据已清理。
- 这些是最近任务报告基线，不代表生成本知识库时重新运行了全量测试。

## 数据状态

- `CP6DB`、`CP6DB_OA`、`CP6DB_SpaceQA` 已于 2026-07-18 备份。
- 三份均通过 SQL Server `RESTORE VERIFYONLY WITH CHECKSUM`。
- 新机恢复后需重新轮换 Secrets 并做登录、权限、i18n 与关键业务冒烟。
- 当前运行库 `CP6DB` 的租户注册表只有 `DEFAULT/A1`；RoleId=10 为 1 角色/4 菜单/8 动作，admin 为 148 菜单/323 动作。`qa_general` 保留为 A1 常驻测试用户。

## 下一动作

以 `1850b2d8` 为当前 Space 代码集成基线。E07-S05、E08-S01～S05 与 E09-S01～S03 已完成；下一张建议卡为 E09-S04 跨租户越权自动化测试，覆盖猜测 ID、同码组织/仓库/库位、分页/游标、缓存和 Portal DTO，随后执行 E09-S05 外部访问审计与有效期。E04 S05 仍等待 E02 S07，E04 S06 已具备依赖但应独立排卡；E13 S04 等待 E02 S03，E13 S05 等待 S04 与正式供应商证据；E02 S01 等待正式黄金集、授权和冻结 Worker。禁止把剩余候选整包合入。GR-VP T1–T7 已完成，不要重做。

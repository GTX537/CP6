# AI 可读变更日志

> 依据 Git log 汇总，不替代完整 Git 历史。重点记录影响接手判断的里程碑。

## 2026-08-06：Space V1 E13-S11 Generation Run 恢复产品化

- `dcbbfca8` / `c695850f` / `d3c2da75`：实现、记录并 no-ff 集成取消、同输入安全重试、废弃、Apply CommandBatch 对账、Failed/Stale replacement Run、RuleOnly 降级、OpenAPI/SDK 与前端恢复操作面。
- 取消由 Worker 安全点确认且不拆分 S10 原子事务；重试沿用同一 Job/Run/冻结输入/检查点/ApplyPlan，仅接受 Transient/Resource/Bug。对账只信任匹配当前 Draft revision、RunId 和计划哈希的已提交 CommandBatch。
- replacement 保留 basedOnRunId、源/映射/货架/规则/策略快照与 Decision 审计，旧 Run 不原地 rebase。Failed current 源在同一事务中先退役，同键并发正确 replay；RuleOnly 只对 BuildScene Provider 故障建议。
- 验证为状态机/分类 42/42、OpenAPI/权限 52/52、真实 SQL 14/14、前端 129 files/695 tests、聚焦 6/6、type-check、production build、SDK 生成和 WebApi build 通过。
- 清理 34 个可重建目录并回收约 0.939 GiB；`main` 未修改。生产 BuildScene executor、外部 Provider、真实 CAD/黄金集与发布证据继续失败关闭/待办。

## 2026-08-06：Space V1 E13-S10 原子 AI Apply

- `43dc5534` / `fbc59fb3` / `5be724cf` / `0c587d4c`：实现、纠偏、记录并 no-ff 集成 Staging + ApplyPlan、ApplyGeneration Worker、原子 Draft 写入、Run/Apply Design V1 API、OpenAPI/SDK 和前端轮询刷新。
- Apply 支持新增和更新既有审核基线；Zone/Aisle/Rack/Element 原位更新，RackLevel/Location 确定性协调。跨类型/跨楼层/资产 Element 冲突、WMS 绑定库位移除、陈旧 revision/review、引用/边界/碰撞失败均保持零部分 Draft。
- Queue 使用 Serializable + tenant/run `sp_getapplock`；最终事务固定锁序并只推进一个 Floor Revision/ContentRevision，记录唯一 CommandBatch 与 before/after。Published、WMS 和设备控制数据保持隔离。
- 验证为真实 SQL 7/7、Space Unit 413/413、默认 Space Integration 248 passed / 71 SQL-gated skipped、CP6.Client 71/71、CP6.Tests 2783 passed / 17 environment-gated skipped、前端 129 files / 694 tests；完整 solution、EF/SQL/SDK drift、type-check、production build 与 diff check 通过。
- 功能分支已推送远端备份，`main` 未修改；下一张独立卡为 E13-S11 取消、重试、降级和 Stale 恢复产品化。

## 2026-08-05：Space V1 E13 S04–S09 受控开发链

- E13-S04～S08 已依次交付 CAD IR 最小化与脱敏、本地/Mock Provider 与失败降级、不可信输出校验、规则/AI 确定性融合及只读提案审核工作台；各切片均以独立功能/证据提交 no-ff 进入唯一 Space 集成分支。
- `c87289f2` / `382d5722` / `396ee38b`：实现、记录并 no-ff 集成 E13-S09 追加式 Accept/Reject/Modify 决策、单条/批量 API、rowversion/ReviewEtag 并发、幂等账本、问题解决血缘、同源人工锁定继承、Migration、OpenAPI/C#/TypeScript SDK 和实时前端决策面板。
- S09 继续关闭批量 High 自动 Accept，不写 Draft、Published、WMS 或设备状态；异源建议继承、真实 Worker 接线、外部 Provider、授权真实 CAD 与正式黄金集未被提前宣称完成。下一张独立卡为 E13-S10 Staging + 原子 Apply。
- S09 验证：Space Unit 413/413、Space Integration + KOUSQLSERVER 312/312、CP6.Tests 2779 passed / 17 environment-gated skipped、CAD 25/25、前端 128 files / 692 tests，完整 solution Release 0 error / 10 条既有 warning；EF/迁移/幂等 SQL、SDK drift、type-check 和 production build 通过。
- 功能 tip 已进入远端集成祖先链，本地/远端临时功能分支已删除；清理 38 个可重建目录并回收约 1.42 GiB。`main` 未修改。

## 2026-07-31：Space V1 E04 S04 受控集成

- `9a87dc30` / `f9c7fd21`：实现并 no-ff 集成货架/通用元素统一多选、套索、对齐、等距、旋转、批量删除、货架阵列与保存后补偿式撤销/重做。
- schema v1 命令扩展 `MoveObject`、`RotateObject`、`RestoreLogicalObject` 和 `GenerateRackArray`；继续使用 S03 的 Serializable 批次、Floor/Version revision、请求哈希幂等与 append-only before/after 审计。
- 阵列把模板计入总数，复制 Active RackLevel；生成库位使用新 LogicalId、空编码、Generated/Unbound，不复制 WMS 绑定语义。编码冲突、缺失目标和坐标/数量越界均在整批写入前失败关闭。
- 合并态验证：完整 solution 0 error / 10 个既有 warning，Space Unit 213 passed，默认 Integration 48 passed / 45 SQL-gated skipped，Design Scene 真实 SQL 3/3 passed，API/OpenAPI/权限 25/25；前端 96 files / 575 tests、type-check 和 production build 通过；SDK/EF drift 通过。
- E07 S05 的 E04 S04 前置依赖已解除；下一张建议卡为存量 WMS 采纳与绑定。E04 S05 仍等待 E02 S07，E04 S06 应独立排卡。

## 2026-07-30：Space V1 E00 / E01 S01–S06 / E02 S01 / E04 S01–S03 / E05 S01–S05 / E07 S01–S04 / E13 S01–S03、S12 受控集成

- `0d25da4d`：把 542 个文件的 Space 后续候选固化到 `checkpoint/space-candidate-20260730`；安全审计未发现真实凭据、私钥或异常构建产物。该提交仅作可回退候选，不是正式实现基线。
- `539d56de`：从 `dcc1ac9a` 建立 `integration/space-v1-20260730`，no-ff 合入 E00 S01–S04 与 E01 S01–S03。
- `bac76444`：从候选重建 E01 S04 最小切片，实现 Published→Draft Clone、八类版本快照、LogicalId 保留与 RowId 重映射、幂等预留、租约围栏和失败清理；未夹带后续 E05/E06/E12 能力。
- `85792161`：以 no-ff 方式把 S04 合入唯一 Space 集成分支。
- `3258d47f`：从候选依赖边界重建 E01 S05 最小切片，实现独立 Design API v1 的 6 条路径/8 个操作、Problem Details、权限/外部主体/cutover 闸、cursor 分页、持久化幂等，以及 OpenAPI/C#/TypeScript SDK 生成闭环；未夹带 Scene、Asset、Planning、Publish 或 S06 文件安全能力。
- `36f534d9`：以 no-ff 方式把 S05 合入唯一 Space 集成分支。
- `6daf1aeb`：从候选依赖边界重建 E01 S06 最小切片，实现失败关闭的文件安全扫描、XLSX/ZIP 中央目录安全检查、扫描 Job 原子终态、保留期限、引用感知墓碑与对象删除补偿；未新增上传 HTTP、CAD/Excel 解析命令或后续 Scene/Asset/Planning/Publish/WMS 能力。
- `2ccdff7a`：以 no-ff 方式把 S06 合入唯一 Space 集成分支。
- `fe959066`：重建 E02 S01 非生产实验门禁，加入黄金集/版本矩阵审计、确定性 50MiB 与 100 万实体压力生成、子进程证据、ODA/APS fail-closed preflight、隔离 Aspose 26.6.0 复现及 DWG/DXF 字节稳定属性；未实现生产 CAD 适配器或 E02 S02。
- `3742fbff`：以 no-ff 方式把 E02 S01 可交付实验切片合入唯一 Space 集成分支；最终技术选型继续由正式黄金集、授权、供应商包/凭据和冻结 Worker 阻塞。
- `d06a8bd1`：按 E07 S01–S03 边界重建版本化 WMS 能力合同、CP6 真实适配器、持久化幂等操作账本和标准模拟器；加入跨站点操作键隔离、脏 EF 上下文失败关闭、真实/模拟来源标记及五类故障注入，未夹带 S04/S05、E08、E13、Workload 或发布 Saga。
- `6e67a9d1`：以 no-ff 方式把 E07 S01–S03 合入唯一 Space 集成分支。
- `74577015`：按 E07 S04 冻结边界重建确定性标准仓数据集、加载器、生成器和验收包；同一模型生成 500 货架、10,000 库位、WMS seed、DXF/底图/期望答案和 6 个故障样本，并以逐字节 Git 属性保护 Manifest 哈希。
- `6d751e0c`：以 no-ff 方式把 E07 S04 合入唯一 Space 集成分支。
- `8f7fc25e`：按 E13 S01 冻结边界实现 Provider/确定性端口、Schema v1 强类型契约、租户/Site/别名/数据策略/外部开关门禁、原子配额租约端口，以及默认 Disabled/无 Provider/配额失败关闭；未实现外部适配器、运行数据模型、输出校验或调用外部网络。
- `ea161975`：以 no-ff 方式把 E13 S01 合入唯一 Space 集成分支。
- `cff25a25` / `94822669`：实现并受控集成 E13 S02 的 Run/Proposal/Decision/Usage 可审计数据模型。
- `cebd401a` / `dca6e19c`：实现并受控集成 E13 S03 的 Import/BuildScene 可恢复 Worker 控制面。
- `54456946` / `b33929fb`：实现并受控集成 E13 S12 的数据库三并发槽、预算预留和 Provider 请求幂等对账。
- `5bb0cdfb` / `49dbabe3`：实现并受控集成 E05 S01 通用元素与类型化属性。
- `2fc03681` / `3d554852`：实现并受控集成 E05 S02 非均匀逐层货架规格。
- `00021f0a` / `a1edecef`：实现并受控集成 E05 S03 Design Revision 权威统一场景 DTO。
- `85b57960` / `888de795`：实现并受控集成 E05 S04 System/Tenant 版本化资产库。
- `856f138c` / `a3864d9c`：实现并受控集成 E05 S05 `space-parametric-v1` 确定性前端渲染器。
- `335659b2`：记录 E05 S05 完成报告并把唯一集成基线推进到 S05。
- `1d57a3b5` / `e8e84853`：实现并受控集成 E04 S01 PDF/PNG/JPG 底图上传、安全扫描、Ready/Clean 楼层挂接、受权 Blob 内容读取和 PDF.js/Konva 渲染。
- `b721468c`：记录 E04 S01 完成报告并把唯一集成基线推进到 S01。
- `20ee0af0` / `c1043d15`：实现并受控集成 E04 S02 两点等比标定、第三控制点动态误差验证、坐标确认、append-only 审计、revision、来源复合外键与 Clone 保真。
- `96113ea3`：记录 E04 S02 完成报告并把唯一集成基线推进到 S02。
- `b322e84a` / `39146c38`：实现并受控集成 E04 S03 通用元素单选、属性面板、RemoveRequested 删除、schema v1 原子命令批次、Floor/Version revision、协议幂等与逐命令 before/after 审计。
- `407dcbea`：记录 E04 S03 完成报告并把唯一集成基线推进到 S03。
- 冲突解决保留 WMS 序列追踪不可降级、Definition 不可变、Space 审计追加写三套保存护栏，并在 `CP6.slnx` 同时保留 Mobile 与六个 Space 项目。
- S06 功能态验证：CP6 主测试 2674 passed / 17 environment-gated skipped；SDK drift、C# build、TypeScript strict compile、触及文件格式和范围污染审计通过。合并态全解构建 0 error / 10 existing warnings，Space Unit 52 passed、Space Integration 17 passed / 29 SQL-gated skipped，EF 模型无待迁移变更。前端产品代码未受影响，沿用此前 type-check、86 files / 539 tests 和 production build 通过基线。
- E02 中立工具 10/10 测试通过，Aspose 实验适配器构建 0 warning / 0 error；严格 readiness 与 ODA/APS preflight 分别按预期失败关闭为退出码 `3` / `4`。Aspose 25 次复验中 L5 5/5 崩溃，成功样本 20/20 图层退化为 `0`，因此保持淘汰。
- E07 验证：Release 全解构建 0 error，Space Unit 73 passed，Space Integration 35 passed / 30 SQL-gated skipped，CP6.Tests 2674 passed / 17 environment-gated skipped，Client 71 passed，EF 模型无待迁移变更，新增文件精确格式门禁通过。
- E07 S04 验证：两次独立生成 17 个文件、0 differences，干净检出 Manifest 哈希错误为 0；合并态全解构建 0 error / 10 existing warnings，Space Unit 79 passed，Space Integration 40 passed / 30 SQL-gated skipped，CP6.Tests 2680 passed / 17 environment-gated skipped，Client 71 passed。
- E13 S01 验证：合并态 Release 全解构建 0 error / 10 existing warnings，Space Unit 97 passed，Space Integration 41 passed / 30 SQL-gated skipped，CP6.Tests 2680 passed / 17 environment-gated skipped，Client 71 passed；Provider 契约 18 passed、权限聚焦 17 passed，新增/修改 C# 精确格式门禁通过。
- E05 S05 合并态验证：聚焦渲染 2 files / 7 tests、完整前端 88 files / 546 tests、type-check 和 production build 全部通过；仅保留既有大 chunk 提示。
- E04 S01 验证：聚焦 API/OpenAPI/权限/存储 22/22，Space Unit 205/205，默认 Integration 48 passed / 42 SQL-gated skipped，真实 SQL 6/6，CP6.Tests 2685 passed / 17 environment-gated skipped；前端聚焦 2 files / 11 tests、全量 90 files / 557 tests、type-check 与 production build 通过；合并态完整 solution 0 warning / 0 error。
- E04 S02 验证：Space Unit 210/210，默认 Integration 48 passed / 43 SQL-gated skipped，真实 SQL 9/9，CP6.Tests 2687 passed / 17 environment-gated skipped，API/权限 20/20；前端聚焦 3 files / 15 tests、全量 91 files / 561 tests、type-check 与 production build 通过；SDK/EF drift 通过，合并态完整 solution 0 warning / 0 error。
- E04 S03 验证：Space Unit 213/213，默认 Integration 48 passed / 44 SQL-gated skipped，命令闭环真实 SQL 1/1，API/OpenAPI/权限 21/21；前端聚焦 4 files / 8 tests、全量 95 files / 569 tests、type-check 与 production build 通过；SDK/EF drift 通过。合并态完整 solution 0 error / 10 个既有 warning；CP6.Tests 的 6 个 RFQ 固定日期失败已在 `f8dff096` 基线复现。
- 下一张可独立推进的 3D Space 卡为 E04 S04 多选、对齐、分布与阵列命令；E13 S04/S05、E07 S05 和 E02 S01 继续遵守各自依赖与外部门禁。未独立提取的剩余候选禁止整包合入。

## 2026-07-19：GR-VP T7 部署与真实权限冒烟

- `d79a39c`：T6 以 no-ff 合入并推送 `main`。
- `ffca422`：修复 OA 发起页提交链误先调用 `draft/save(add)`；改为直接调用既有 `wf/flow/submit(submit)`，草稿保存权限保持不变，并新增组件回归测试。
- 干净 `main` 验证：73 files / 488 tests、type-check 0、Vite 2649 modules production build。
- API/Web 双镜像运行指纹分别为 `2ee04fc0…` / `0271d4af…`；HTTP 与最近 API 错误日志检查通过。
- 当前租户注册表仅 `DEFAULT/A1`：一般用户 4 菜单/8 动作，admin 148 菜单/323 动作；`qa_general` 本人审批 200、他人待办 400、无权端点 403。
- 两条测试流程实例及临时自审批定义清理归零，保留 `qa_general`。四租户原计划因 B1/C1/D1 不存在改按全部现存租户验收并记档。

## 2026-07-19：GR-VP PUR / PLAN / PUB T6

- `4bb7512`：PUR/PLAN/PUB 扫描 12 个目标视图，37 个页面级权限声明覆盖 33 个唯一后端写权限键；2 个只读 POST 明确豁免。
- `VolTable` 通过可选 add/edit/delete permission props 接入 Seq CRUD，覆盖桌面与移动入口；受限移动端不再显示空菜单，也不会由卡片点击越权打开编辑框。
- `cf20d42`：修复 `app.mount()` 后异步加载权限导致首屏指令不重判的问题；移动下拉项改用响应式条件渲染并增加事件二次守权。
- T6 验证：后端 oracle 11/11、type-check 0、72 files / 487 tests、聚焦权限测试 6/6、生产 build、真实 Chrome 权限矩阵与正式复审全部通过。
- 下一任务为 T7：合入 main、双镜像部署、四租户种子 SQL 验证与 `qa_general` 端到端冒烟。

## 2026-07-18：GR-VP FIN T5

- `5732057`：FIN 16 个视图完成 66 条 `v-permission` 铺设，覆盖 51 个真实权限键；所有键逐字命中 FIN Controller 贴点。
- 预算 M1–M12、控制模式和控制口径实现“有 edit 权可写、无 edit 权只读”，修复独立复审发现的 view-only 空白风险；最终复审 0 blockers。
- T5 前端验证：type-check 0、71 files / 481 tests、生产 build、系统 Chrome 权限矩阵与预算 view-only/edit-only 复测全部通过，console error 0。
- 下一任务改为 T6 PUR/PLAN/PUB；T7 全量验收、部署与一般用户冒烟随后执行。

## 2026-07-18：GR-VP 合入 main 与 MES T4

- `8e696d2`：`feat/general-role-vperm` 以 no-ff merge 合入并推送 `main`；合并后后端 build 通过，2220 passed / 5 skipped / 0 failed。
- `6e4ade1`：MES 31 条 `v-permission` 覆盖 12 个视图、24 个真实写权限键；设备、工单、质检的新增/编辑模式精确分流。
- T4 前端验证：type-check 0、71 files / 481 tests、生产 build、Chrome denied/edit-only/add-only 三角色场景全部通过；独立复审 0 findings。
- 下一任务改为 T5 FIN；T6 PUR/PLAN/PUB 与 T7 部署冒烟随后执行。

## 2026-07-18：开发环境迁移与知识固化

- 三个 SQL Server 数据库生成压缩 checksum 备份并通过 VERIFYONLY。
- `.bak` 改由 Git LFS 管理并推送私有 GitHub。
- 保存 GR-VP SDD 工作区，建立迁移标签和恢复说明。
- 新增 `docs/project-memory`，固化架构、状态、风险和 AI 接手步骤。

## 2026-07-17：普通角色与全模块权限 UX

- 建立 GR-VP 七任务计划。
- 新增每租户标准一般用户角色 RoleId=10：4 菜单、8 OA 动作，insert-only。
- OA/WF 40 个按钮、17 个视图完成 `v-permission`。
- ERP 39 个按钮、16 个视图完成 `v-permission`。

## 2026-07-17：权限与工作流安全收口

- WF 引擎四类审批动作加入归属复验 E-WF-029，admin 不豁免。
- 跨模块 HttpPatch 反射扫描补齐。
- 建立六模块“权限贴点必须存在于种子”互锁测试。
- PLAN/PUB、MES、ERP、OA/WF、PUR、WMS 多轮授权横切完成并部署验证。

## 2026-07 上旬：WFS、Space 与平台波

- WFS 完成内核硬化、信箱 UX、事件触发、基础设施、服务任务和子流程等系列波。
- Space 完成发布闭环、前端交互、生命周期、库存覆盖、路径/多楼层与收口波。
- 完成平台安全、租户配置、财务集成和 UI 设计系统/迁移等工作。

## 更早阶段

- 建成 ERP/MES/WMS 核心业务与闭环集成。
- 扩展采购、财务、OA/WF、权限平台、计划中台与 Space 3D。
- 建立多语言、操作日志、实时通信、容器部署、测试体系和完整领域文档。

## 维护规则

每完成一个可交付任务，追加日期、提交、业务影响、验证结果和遗留票。不要复制整个 commit 列表；细节用 `git log --all --decorate --oneline` 查询。

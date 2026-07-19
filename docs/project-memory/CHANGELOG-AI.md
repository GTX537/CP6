# AI 可读变更日志

> 依据 Git log 汇总，不替代完整 Git 历史。重点记录影响接手判断的里程碑。

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

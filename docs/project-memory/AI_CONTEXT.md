# CP6 AI 核心上下文

这是整个项目最重要的长期 AI 约束摘要。

## 架构原则

- 单向依赖：WebApi → Core → Entity；前端只经 HTTP/SignalR 访问后端。
- 业务逻辑进 Service，Controller 保持边界职责。
- 领域目录和命名空间保持对齐，不创建跨域“杂物层”。
- 跨模块写入走既有 Bridge Hook / IntegrationEvent，不在 Controller 硬串。

## 数据库原则

- SQL Server + EF Core；复杂读报表可用 Dapper。
- 多租户、软删除、审计、乐观锁不可绕过。
- WMS 库存只走台账；WF 状态只走引擎。
- 迁移追加而非篡改历史；种子幂等且语义明确。
- `.bak` 用 LFS，Secrets 不进 Git。

## API 与权限原则

- 写端点必须有认证、租户和动作权限。
- 后端是安全真相，前端 `v-permission` 只是 UX。
- 权限键必须与 `docs/seeds` 和反射 oracle 逐字一致。
- admin 可见全部按钮，但不能绕过 WF 归属等业务不变量。

## 前端与代码风格

- Vue 3 Composition API + TypeScript；复用 api/types/store/composable。
- 文案使用 i18n；公共视觉遵守 Design System。
- 小步提交、测试先行、禁止夹带无关重构。
- 用户工作区可能有未提交修改，必须先保护再工作。

## 当前负责人意图

- 把 CP6 做成纸箱行业完整可售 SaaS，而非一次性 demo。
- 优先完整性、权限安全、多租户隔离、可恢复数据和可审计证据。
- 以代码、测试和最新计划为事实，不盲从陈旧文档数字。
- 每一波都要实现、审查、全量门禁、部署冒烟和跟踪票收口。

## 当前主线

GR-VP 普通角色 + 全模块前端权限波。T1–T3 完成，下一任务 T4 MES。详见 `PROJECT_STATE.md` 和 `10-AI-Handoff.md`。

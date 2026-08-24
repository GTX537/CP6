# Design V1 空白 Draft 初始化报告

日期：2026-08-15
任务分支：`codex/space-design-blank-draft`

## 结论

Design V1 的版本创建接口现在接受 `createMode=Blank`。该模式创建一个不继承 Published 内容、`BasedOnVersionId=null`、`ContentRevision=0` 的可编辑生产草稿，同时保留当前 Published 指针。创建继续占用每个模型唯一的活动 Draft 槽，并通过初始化 Operation、Job ledger、SQL 事务和既有 Idempotency-Key 记录失败关闭。

这只关闭 LM-FR-001 的“空白版本初始化”纵切。新版本不会猜测或复制楼层，也没有平台/租户整仓模板目录；用户可见的楼层初始化/选择和模板创建入口仍待后续任务。因此 LM-FR-001 与 WP1 均保持 `Partial/Pending`，核心 GA 继续为 72% / `NoGo`。

## 合同与数据行为

- `POST /api/space/design/v1/sites/{siteId}/versions` 支持 `Blank` 与既有 `PublishedVersion`；其他值稳定返回请求错误。
- `Blank` 必须省略 `basedOnVersionId`；传入任何基线版本稳定返回 `SPACE_REQUEST_INVALID`，且零写入。
- 初始化直接产生 `Draft`，使用新的 `InitializeVersion` Job 类型和 `space-blank-v1` 处理器身份；Job/Attempt 在同一事务内完成并可通过既有 Job URL 审计。
- Version 保存初始化 Operation fence；相同 Operation、名称和请求 Hash 返回同一 Version/Job，不同输入不能复用。
- 当前 Published 指针和已有设计快照不变；空白 Draft 不创建 Floor/Zone/Aisle/Rack/Location/Element。

## 验证

- `SpaceVersionCloneTests`：7/7 通过，覆盖空白 Draft 无基线、零 Content Revision 和 Operation fence。
- SQL Server LocalDB：2/2 通过，覆盖无 Published 的底层初始化、Design V1 公开接口、活动 Draft 指针、完成态 Job/Attempt、幂等重放、不同输入拒绝和零 Floor 写入。
- Space Unit 全量 534/534 通过，0 skipped。
- Space Integration 在配置 SQL Server LocalDB 后全量 437/437 通过，0 skipped。
- `CP6.slnx` Release 全量构建通过，0 warnings、0 errors；Design V1 OpenAPI 聚焦测试 48/48 通过，C#/TypeScript SDK 无漂移。
- EF `SpaceContext` 无待生成模型变化；GA 证据结构校验通过，派生状态仍为 `NoGo`（5 项外部输入、9 个接受门禁、5 位签字人待完成）。
- 该任务不修改数据库结构；`SpaceJobType` 仍按既有 smallint 存储，不需要 Migration。

## 后续

1. 增加设计态楼层初始化/选择合同和用户入口，使新 Blank 版本可直接进入 Space Studio。
2. 建立版本级 System/Tenant 仓库模板目录与不可变模板版本；不得把单构件 Asset 或旧运行态模板冒充整仓模板。
3. 为四种创建模式补齐统一创建向导、来源/创建者/更新时间/Blocking 摘要和 E2E。

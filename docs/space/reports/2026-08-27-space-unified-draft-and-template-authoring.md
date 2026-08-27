# Space Studio 统一 Draft 创建与模板制作

日期：2026-08-27
任务分支：`codex/space-v1-completion-20260827`
基线：`main@d6cd50056a751f9ebb1077d98634d6f5833ca78b`

## 结论

WP1 的仓库实现缺口已关闭，`implementationStatus` 从 `Partial` 更新为
`Complete`；`acceptanceStatus` 继续为 `Pending`。本次不修改 72% 基线，不填写
`acceptedEvidence`，也不把本地 SQL、自动化或模板数据冒充真实 CAD、Provider、
WMS、Published Viewer、黄金集或 Pilot 证据。

## 已交付行为

- Site 起始页使用同一入口创建 `Blank`、`PublishedVersion`、
  `SystemTemplate` 和 `TenantTemplate` 四类 Draft。
- 模板模式必须先取得当前不可变模板版本的密封预览；服务端核对 Scope、版本和
  ProposalHash 后幂等创建全部楼层，并逐层通过 Lease、Floor/Content Revision 和
  CommandBatch 写入。
- Draft 持久保存创建来源，以及模板 ID、模板版本 ID 和内容 SHA-256；数据库约束拒绝
  不一致的 Blank、Published 和模板来源组合。
- 仓库人员可对当前生产 Draft 做零写入模板预览，再以模板代码、名称、说明和密封
  ProposalHash 创建租户私有不可变整仓模板。当前仅接受能无损表达的矩形楼层/区域、
  两点巷道和规则货架网格；不规则内容以 422 失败关闭。
- 空白建模首层显式采集宽度、深度和层高，避免隐式默认；模板初始化中断可用同一
  Idempotency-Key 继续，且不会覆盖重试前已被修改的未完成楼层。

## 验证快照

| 门禁 | 结果 |
|---|---|
| SQL Server LocalDB Version Clone 全类 | 17/17 passed，0 skipped |
| SystemTemplate 大体量纵切 | 2 floors / 500 racks / 10,000 locations passed |
| TenantTemplate 租户 Scope 纵切 | passed；错误 Scope 失败关闭 |
| Design V1 OpenAPI 契约 | 57/57 passed |
| Web 聚焦测试 | 3 files / 19 tests passed |
| Vue strict type-check / production build | passed；仅既有大 chunk 提示 |
| 完整 `CP6.slnx` Release build | 0 warning / 0 error |
| EF pending model / SDK drift / diff check | passed |

## 状态边界

本报告只证明可重复的仓库实现和本地 SQL 纵切。WP1 正式接受仍需 DeliveryOwner 对测试
提交留下可重复自审记录；WP0、WP2～WP8、三类外部输入和最终签署仍按
`ga-evidence-index.json` 失败关闭。下一条工程主线是 WP3 的真实主备 CAD Provider 与
隔离 Worker；缺少授权供应商、凭据和 Worker 时不得使用模拟适配器改写 GA 状态。

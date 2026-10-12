# WP05-01 协调审查与补充

接收原候选 `306b1f3ba61456b5dc117189710b37031bf57a19`，从已确认主线 `2a8dd0eeae27b885f136991b2b215ee7539677d6` 合并保留其祖先；实际窗口见 [intake.json](intake.json)。共享状态只由本协调分支更新。

[原审查](review.md)识别已知工作簿、CP22 和 R1 义务映射不足。保留原 [建议 JSON](wp05-review-supplement.json) 与 [建议说明](wp05-review-supplement.md) 的历史内容；正式待采纳补充位于 [1.1.0-proposed](../../supplements/review-r1/README.md)。后继特别修正 R1-09：按固定来源拒绝重复输入引用，不能将旧建议中的“去重复”作为行为规范。

原六份规范资产和 basis-manifest.json hash 不变，原39来源、221可恢复场景、295正文定位及历史30/48缺口检查按原作者与独立审查范围复用。新增补充的语义审查与静态检查单列；全部业务仍 NOT_RUN。正式采纳尚未发生，WP01-01 工具门未改。

检查器24项测试复用 [真实摘要](checker-tests-reused.json)，运行时检查器 hash、补充输入 hash 和范围见 [当时静态记录](checker-original-static.json)。对应脚本已保存至补充目录，代码与当次运行相同；本次只修补充文案中的拒绝重复语义，不影响测试所验证的检查器分支和固定来源。该旧结果不能证明新文案已做业务测试。当前补充资料静态检查另记 supplement-static.json，语义关闭另记 followup.md。源码/提交变化本身不触发重复全仓测试。

后续远端包含性在保护合并后另记；合并与本任务 DONE 均只表示设计交付。无数据库、迁移、部署或 GitHub Actions 运行。

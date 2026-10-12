# S02-00 修正后的定向复查

日期：2026-10-12。依据初审 `review-s02.md` 的 R1（P2）；复查对象是协调工作树 `D:/CP6-worktrees/fw-s02-1012` 相对原候选 `e8303d3dd75e9607f4705a1cbcd6e181d63d0388` 的以下两文件修改，未重做整项审查，也未执行任何业务或数据库操作。

结论：**R1 已关闭；本设计候选加以下修正可合入。** 这是静态文档审查结论，不是数据库运行验收或远端交付确认。

| 复查文件 | 当前 UTF-8/LF SHA-256 |
|---|---|
| docs/design-handbook/architecture/02-postgresql-tenancy.md | dfcaea44365d92f7457db783908dd8eb6f1eaa7651c67016bdff68281a0148c3 |
| docs/development/acceptance/postgresql-tenancy.json | b56363bd547034d45e5556bf112b41f71cbb4d7b9dc565de25876d297deefbd5 |

设计第 88 行已明确 UPDATE 同时具备旧行 USING 与新行 WITH CHECK，二者都约束到 session_user 的可信租户绑定；不依赖 SELECT 策略补齐旧行保护，并要求复核 ALL/专属、permissive/restrictive 的有效组合。符合 [PostgreSQL CREATE POLICY](https://www.postgresql.org/docs/18/sql-createpolicy.html) 对旧行与新行的区别。

PGT-008 已加入非零 A/B 行 fixture、普通获准可写列的无 WHERE/RETURNING 常量 UPDATE 正例（成功且影响数等于 A 预置行数 >0，B 不变），能够揭示漏 USING 导致的全零行更新；另有常量 TenantId=A 的 B→A 改属反例及独立观察员核对。TenantId 列不可更新时可保 ACL 拒绝，但不能替代普通列合法更新正例；不会以全部拒绝冒充隔离成功。

实际定向检查：两文件差异已阅读；限定两文件的 `git diff --check` 通过；JSON 可解析；仍为 27 个唯一场景，全部 NOT_RUN 且 execution_evidence 为空；设计文档本地链接存在。未改变原运行验收状态。初审来源摘要/源码定向核验沿用初审实际结果，未把复查冒称为重新运行全部验证。

初审 M1（完整写出 Registration 前置保护门）仍为不阻止合入的表达建议；本次没有发现新增阻塞问题。后续协调提交、状态页同步、保护合并和远端 main 包含性核对由协调流程继续完成。

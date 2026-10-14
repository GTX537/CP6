# WP02-02 原始验收边界

本轮 PREPARATION_ONLY；没有真实 Main 宿主、数据库或业务执行。原始 17 项全部保持 NOT_RUN，execution_evidence=[]。下表直接来自固定 Main 的 acceptance/postgresql-tenancy.json；完整 actor/preconditions/request/steps/expected/evidence_required 保留在 inventory-final.json.required_pgt_cases，未删减或重写原验收。

| Case | 原标题 | 状态 | 原证据要求 |
|---|---|---|---|
| PGT-001 | 可信绑定和正向读写 | NOT_RUN | session_user/database/schema；bindingRevision；同连接/事务证明；前后行摘要；原receipt |
| PGT-002 | 进程内 A/B 交替池复用 | NOT_RUN | 每轮路由/物理连接映射；读写摘要；scope 清理观测 |
| PGT-003 | 强制向 B 出借 A 物理连接 | NOT_RUN | 同 backend PID；拒绝点；业务命令计数为零；前后摘要 |
| PGT-005 | 标识符长度和碰撞 | NOT_RUN | 目录唯一约束；规范字节；对象 Owner；拒绝前后清单 |
| PGT-006 | 显式跨 schema 读写 | NOT_RUN | SQLSTATE 42501 或相应明确权限拒绝；session_user/ACL；前后摘要 |
| PGT-007 | 角色与 GUC 伪造 | NOT_RUN | 角色成员链；SQLSTATE；GUC/session_user；RLS 行结果 |
| PGT-008 | 共享表 RLS 与目录最小权限 | NOT_RUN | 逐动作有效策略 USING/WITH CHECK/FORCE 及组合表达式；全部角色属性；每条结果；前后数据及 B 行未改属 A 的独立见证 |
| PGT-009 | public/临时对象污染 | NOT_RUN | 函数定义/Owner/EXECUTE ACL；schema/default ACL；拒绝日志 |
| PGT-010 | 连接取消失败与保存点 | NOT_RUN | 原始异常/SQLSTATE；pool/session 设置；后续读写及清理证据 |
| PGT-012 | EF 模型和显式映射 | NOT_RUN | 模型键；ToTable/store identifier；实际 SQL；对象 inventory |
| PGT-018 | 逐租户迁移互斥与事务中断 | NOT_RUN | 进程/锁等待；实际脚本hash；历史和目录差异；B 冒烟 |
| PGT-019 | 非事务步骤和共享升级失败 | NOT_RUN | pg_index 等对象证据；失败日志；恢复步骤/版本路由矩阵 |
| PGT-021 | 单租户完整恢复和他租保护 | NOT_RUN | 快照水位/manifest/哈希；行计数；外键/权限结果；B 前后及并发合法差量 |
| PGT-022 | 不完整备份与旧消息恢复 | NOT_RUN | 缺件报告；现行/备份水位；原消息/receipt 对账；零自动发送 |
| PGT-023 | 缓存/池容量与凭据轮换 | NOT_RUN | 环境/source/package pin；原始测量；模型/池计数；预算与限额决定 |
| PGT-024 | 混合版本与回退 | NOT_RUN | 版本矩阵；路由拒绝；迁移历史；旧 writer 清单和零旁路写 |
| PGT-027 | 租户列与共享引用完整性 | NOT_RUN | 约束DDL；原生错误；bulk/raw前后完整写集 |

## 私有探针与后续真实采用

- 本轮：归档元数据、20 个上下文类型及公共签名、32 份 schema 原字节比对；合成 source verifier / binding authority 下的 DI、Http/message/context 清理与拒绝。没有真实 Owner 决策，不是鉴权或租户隔离验收。
- Main 宿主：真实认证来源及当前 authority、Owner/revision/revocation、入口与后台/消息 adapter、遗留 scope 边界、取消和清理，均须后续独立采用。
- PostgreSQL：可信 tenant→database/schema/role 映射、A/B 最小权限角色与独立连接池、事务本地上下文/reset、shared RLS、显式 TenantId/跨租户键、模型缓存键、必要伴随记录同事务、逐租户迁移/恢复/容量与灾难恢复，均未运行。schema 不是权限隔离，不删原授权/约束。
- 正式信任：待新精确版本、最终源码、当前签名/时间戳/撤销和可信 feed 原字节查回；本轮旧 publication 只提供历史身份，不证明当前密码学信任。

WP02-02 仍 BLOCKED / NOT_CHECKED，WP02-28 未被满足；125 业务目标、247 运行 NOT_RUN、41 历史 REFERENCE_ONLY、ITER/REL 缺件均未由此改变。协调对话独占总台账和四份项目状态文件；执行者不在准备目录另造主状态。

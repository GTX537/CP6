BUG155 已由 PR156 正常交付；远端 main 为 `2e1f90c629340944d95fadd7aee428f639304ed6`，包含候选 `5a868092891d079f5234407f397f47a31cfc7f75`，完整文件树一致。

根因与修复：Serializable 在资源锁等待前建立旧快照；原生 40001 后必须释放失败事务，以新事务完整重读并重试校验申请。最多三次，保留同输入唯一 run/job 与复用结果；调用方事务/待提交状态不被接管，未知错误、取消、耗尽继续传播。

本地证据：修复前确定性原生 40001 RED；修复后两库校验各 9/9、完整设计/发布各 41/41，零 skip；构建零 warning/error，集中源码审查无实质阻断。9 项是 41 项子集，不累加。原始失败、编译失败及同步超时均保留于 [105 份原件及范围说明](https://github.com/GTX537/CP6/blob/2e1f90c629340944d95fadd7aee428f639304ed6/docs/audits/2026-10-03-bug-155-space-validation-retry/README.md)。控制注入不代称原生死锁，ambient/enlisted 和 commit-time 分支只有源码审查证据。

合并后在已核对的同一源码/运行物上重新执行原并发和协调并发：PostgreSQL 2/2、SQL Server 2/2，实际 exit 0、零 skip；PG 再次观察实际 40001 后成功恢复。两个精确 receipt-owned 临时库均完成 owner/task/物理身份及无会话核对、普通 DROP 和不存在确认；没有强制断开，凭据 receipt/PG 角色保留。清理报告 SHA-256：`1998C3C2D93C6F6D701966F5D8C3DBA167A98BFDF3E776CD924E88EFB57CA471`。交付后的原件将在父任务 WP6 中同步归档。

BUG 完成条件已满足，关闭此 Issue。父任务 #134 仍 Open，WP6 接入修复后继续最终双库 Matrix、新发布物 Application 与交付；本地修复通过不代替整体兼容验收。Actions 启动/取消均 0，无工作流、保护或生产部署变更。

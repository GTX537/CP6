# BUG161：CAD Provider 配置替换的完整事务恢复

活动状态以 [Issue #161](https://github.com/GTX537/CP6/issues/161) 为准。本任务由 [DB-COMPAT-01 / #134](https://github.com/GTX537/CP6/issues/134) 的 WP6 最终本地矩阵发现，从已确认远端 main `4a654320c7c41cbdf6ed5bf7bd4fcba459f720f0` 建立独立分支。当前为 LocalVerifiedRemotePending：双库本地回归、构建和一次专项审查通过；远端交付和父任务仍未完成。

## 根因与实现

正式 PostgreSQL Matrix 的 7 个相关项目构建成功、28 个入口 Passed；第 29 个 CAD/资产/协作入口原 15 项为 14P/1F，随后五个入口未执行。原 `Concurrent_replace_preserves_one_current_revision_and_immutable_evidence` 在配置保存时报出实际 PostgreSQL `40001`，该次运行保持 Failed。

Serializable 事务等待 tenant/site 的 advisory lock 不会更新已建立的快照。旧服务只回滚后重抛，没有完整事务恢复，也未显式 dispose 事务。[PostgreSQL 隔离文档](https://www.postgresql.org/docs/18/transaction-iso.html)要求重新执行完整事务；[EF 事务文档](https://learn.microsoft.com/en-us/ef/core/saving/transactions)说明手工事务的回滚和生命周期。

修复保留隔离级别、tenant/site 锁、认证证据不可变、配置版本和 principal/operation/key/request 幂等身份。仅对现有 classifier 识别的序列化或死锁冲突，最多执行三次完整业务事务；每次先回滚、异步释放失败事务，再清除该次跟踪状态并重新校验访问、请求、幂等记录和版本。不同请求返回既有版本冲突；同一 actor/key/request 重放已持久化响应。未知错误继续传播，取消结束恢复，三次耗尽传播原失败。

关系数据库入口要求独立事务和无待保存更改的 Context；调用方显式、ambient 或已 enlisted 事务不得由服务提交、回滚或释放。未保存更改被拒绝并保留。InMemory 路径维持原行为。没有迁移、依赖、公开 DTO 或生产环境变更。

## 实际验证

| 运行 | 结果 | 原始 TRX SHA-256 |
| --- | --- | --- |
| 原 WP6 PostgreSQL 入口 | 14P / 1F，实际 40001 | `32E0C9CD7990B0EE5ACD8B7F20C19EA351C3F9465D13FAEAABE711B7D275E364` |
| 修复前协调 PostgreSQL 回归 | 0P / 2F，均实际 40001 | `97B4946E4B0E911C1A6608393E5651DC37FA08A9DCEDC536DEE22C0018091A14` |
| 修复后 PostgreSQL 精确组 | 25/25，零 skip | `9598DD054712780D5654A509CE0F44911005B2F8EC0ACA71BCE45BBE0DA99BAB` |
| 修复后 SQL Server 精确组 | 25/25，零 skip | `56455CC70EF54EF1C9981F1534D012571375355880BB572AEB29E28B9C2B8C27` |
| 既有 InMemory 路由/配置 | 17/17，补充回归 | 见原件 manifest |

精确组为原 15 项加新增十项：协调并发的版本冲突与同键重放、完整事务的序列化/死锁恢复、未知约束、三次上限、取消、调用方未保存更改、显式事务和 ambient 事务。两个 PostgreSQL 协调 GREEN 都观察到实际 40001 后再恢复。五个失败控制在真实事务第三次保存前注入异常；supersede、新配置和认证已写入，回归检查完整回滚、不同重试事务和单次持久化。注入控制不称为原生死锁验收。直接 enlisted 事务仅经入口保护源码审查，没有专门 native 用例。

修复后一次 locked restore/build 为 0 warning / 0 error；SQL Server 和补充 InMemory 用例复用相同产物。当前源码 manifest `3D8A0E51E1C015F6F10830A9B75D68C34390876DAAD07965ADE160EE391BFE60`、runtime manifest `455ACB762F61A88D7F7DA7ED17B7558FC3EA624BFE81B3D440DB290991AFDB18` 绑定实际输入。原失败、RED、GREEN、精确 case 集、实际命令、源码/产物清单及审查已按字节归档；提交 SHA 改变不重称新构建。

失败 Matrix 的七个精确自有库经 owner/物理身份/无会话检查后使用普通 DROP 清理，全部不存在，原失败 summary 字节不变。本 BUG 两个临时库保留供合并后冒烟。没有 dispatch/cancel Actions；17 个 workflow/依赖输入与远端 main 相同，排队/运行中 Actions 为零。


专项 [源码审查](native/execution/bug161-review.md) 无 P0/P1/P2 阻断，绑定三个源文件 SHA256；root 完成文档/原件/范围核对。71 份公共原件见 [manifest](native/manifest.json)，SHA256 A230CE092F3C7ACBEA37FA9994137596E0BB50FF0199A312DD6D4FA1E236EEBC；原始失败保持原状态。凭据、连接/owner receipt、备份和运行私有状态不归档。

## 交付边界

BUG161 的正常 PR、远端 main 包含性核对、合并后冒烟、两库清理和 Issue 关闭待完成。WP6 完整 Matrix、最新发布物 Application、恢复验收和父任务交付另行完成。本地验证不作为生产验收，不替换既有业务库，不执行生产部署。

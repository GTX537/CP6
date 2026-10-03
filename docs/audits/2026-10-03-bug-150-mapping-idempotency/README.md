# BUG150：映射模板同键并发保存

[Issue150](https://github.com/GTX537/CP6/issues/150)，P1。基线为已确认远端 main `c6c662f5b744b44a51427e26fb2faff96472fcc2`，独立分支 `codex/bug-150-mapping-idempotency`。问题在 DB-COMPAT-01 WP5 的真实双库验证中发现；本修复不依赖尚未交付的 WP5 代码。

## 原因与修复

CAD 和 Excel 映射保存采用 Serializable 事务。两个独立会话同时使用同一幂等键创建模板时，原 SQL Server 路径发生死锁，PostgreSQL 路径发生 CurrentName 唯一约束冲突；原 SQL 专用捕获没有完成回放，且其回滚后的读取仍位于尚未释放的事务作用域内。

两个服务共用一个小型事务恢复 helper。SQL1205、PG40001/40P01 最多执行三次完整自有事务；每次失败先回滚并释放，再开始下一次。已确认的模板名称、模板版本及幂等记录唯一约束，以及乐观并发冲突，只在事务释放后读取回放；无回放时保持原领域409。未知约束和其他错误继续抛出。

真实写入要求干净的 context，且不能接管调用方、ambient 或 enlisted 事务。已存在的幂等回放仍先返回，不保存或丢弃调用方待提交变化。请求规范化、哈希、租户/主体条件、版本和原 InMemory 行为保留，未修改模型或迁移。

## 实际验证

| 来源 | 结果与范围 |
| --- | --- |
| `bug150-sql-red` | 独立分支原生产代码，两项真实并发均因 SQL 死锁失败，0 skip |
| `bug150-pg-red` | 两项均因原生23505、对应 CurrentName 唯一约束失败，0 skip |
| `bug150-sql-green` | 4/4、0 skip；观察到原生1205后完成回放 |
| `bug150-pg-green` | 4/4、0 skip；并发创建及调用方状态保护通过 |
| `bug150-mapping-related-unit` | 原两映射 ServiceTests 共12/12、0 skip；不是原生数据库验收 |

两个并发事实分别验证 CAD/Excel：两实际不同 SPID/PID 在首次 SaveChanges 前汇合，确认 Serializable 事务；最终一个创建、一个回放，完整响应除回放标志一致；数据库各只有一个 profile、version、ledger。相同键改 payload 返回409，数据库完整快照保持。测试没有自动重试。

另外两个事实验证待保存实体保持原对象、完整值及 Added 状态且没有 SaveChanges；调用方事务及预建 savepoint 保留可用。已有回放在 dirty context 与调用方事务同时存在时仍无写返回，原数据库快照保持。

使用新建并验证 owner 的双库 `CP6Bug150_20261003_a73bd1b9`，SQL Core140/Space47、PG Core1/Space1 实际迁移且 pending=0。fixture 只验证和使用预建的 loopback 临时库，不创建或删除数据库。连接与所有权 receipt 仅保留本机未跟踪文件。

最终构建0 warning/error。初次 RED 构建的六项断言风格警告与原失败记录保留；等价断言调整和诊断日志增强未改写 RED。三次成功运行与最终七个任务源文件及加载的 CP6 程序集哈希一致。一次独立任务级代码审查无实质阻断，原件和范围见 [manifest](native/manifest.json)及[审查与输入核对](native/bug150-task-review-and-inputs.json)。

复现时先按 fixture 约定创建新的自有临时库，并在本地配置 `CP6_BUG150_TEST_PROVIDER`、`CP6_BUG150_TEST_CONNECTION` 和 `CP6_TEST_DATABASE_OWNER`；选择任一 provider 后缺少配置或所有权不符会失败，不能跳过必需测试。

```powershell
dotnet test CP6.Space.IntegrationTests/CP6.Space.IntegrationTests.csproj --filter 'FullyQualifiedName~CP6.Space.IntegrationTests.SpaceMappingProfileConcurrencyTests'
```

当前为本地验证完成、远端交付待核对，Issue150 仍 Open。后续正常 PR 合并、远端包含性核对与原步骤冒烟后才关闭。没有 Actions 运行/取消、工作流或保护变更，也没有生产部署；完整 WP5/6 仍待完成。

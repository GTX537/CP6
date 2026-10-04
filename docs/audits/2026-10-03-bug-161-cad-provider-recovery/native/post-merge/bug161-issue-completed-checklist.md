### 优先级

P1 - 下一项处理（核心功能异常，但有绕行）

### 环境

DB-COMPAT-01 / #134 的 WP6 本地 PostgreSQL 18 最终矩阵；执行源码 ac5452557f6a9f0aa0606627f8a450f8eec58882，实际工作树输入另有哈希绑定；已确认远端 main 为 4a654320c7c41cbdf6ed5bf7bd4fcba459f720f0。

### 复现步骤

1. 独立测试库应用 Space 迁移。
2. 两个独立 DbContext / actor 同时调用 SpaceCadProviderCapabilityService.ReplaceAsync，同一 tenant/site，预期配置版本均为 0，使用不同幂等键。
3. 运行原有真实数据库回归 Concurrent_replace_preserves_one_current_revision_and_immutable_evidence。

### 预期与实际结果

预期：一方创建配置版本 1，另一方获得 CAD 配置版本冲突（409）；只存在一个 current 配置、两项认证和一个幂等响应；保留认证证据不可变。

实际：一方抛出原生 PostgreSQL 40001 SerializationFailure，由 EF SaveChanges 包装后逸出。服务在 Serializable 事务内等待 site advisory lock，等待不会更新已有快照；当前代码只回滚后重抛，没有完整事务重试，也缺少显式事务 disposal。

### 证据与临时绕行

原件 tmp/wp6-pg-formal-matrix-claim-fix：28 个入口通过，第 29 个 space-cad-assets-collaboration 的 15 项为 14P/1F，后五入口未执行。Summary SHA256 9A788F5E6450F4B4FE4258FEDA9FFC99B959EB7A7E997F2A8EB2BEAD49F6284C；失败 TRX SHA256 32E0C9CD7990B0EE5ACD8B7F20C19EA351C3F9465D13FAEAABE711B7D275E364。保留原件，不计作通过。临时串行执行同一站点配置替换可避开同时竞争。

### 完成条件

- [x] 记录根因，增加稳定协调的真实并发回归；修复前观察到实际原生错误。
- [x] 可重试的事务失败完整回滚、释放事务并重新读取；保留配置版本、幂等重放、认证证据不可变和 tenant/site 隔离。
- [x] 对取消、重试上限、未知错误及调用方事务/未保存更改有明确边界和相关回归。
- [x] PostgreSQL / SQL Server 原 CAD/资产/协作 15 项及新增相关测试全部通过、零 skip；一次任务级审查及原步骤复测完成。
- [x] 正常合并并核对远端 main；必要合并后冒烟及自有临时库清理完成后再关闭。

不触发 GitHub Actions，不修改现有业务数据库或生产环境。父 Issue #134 保持 Open；最终 WP6 双库验收另行完成。
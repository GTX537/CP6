## 关联

Refs #161；父任务 #134 的 WP6 验收缺陷。合并后冒烟及两个独立临时库清理完成后再关闭 #161。

## 改动

同一站点两个 CAD 配置请求同时提交时，PostgreSQL Serializable 等待锁后的旧快照会触发 40001。现在回滚并释放完整失败事务后重新校验访问、幂等响应和配置版本，最多尝试三次；不同请求得到既有版本冲突，同一幂等请求重放已提交结果。

保留认证证据不可变、租户/站点锁和公开 DTO；拒绝借用调用方事务或保存/丢弃调用方待保存更改。未知错误、取消和重试耗尽继续传播。无迁移、依赖、workflow 或生产环境变更。

## 验证

- [x] 修复前两项稳定协调的真实 PostgreSQL 回归均失败，并各自观察到原生 40001；原 WP6 14P/1F 失败保留。
- [x] PostgreSQL 和 SQL Server 原 15＋新十项各 25/25，零 skip；同一源码/运行产物，locked restore/build 0 warning/error，SQL 复用产物。
- [x] 既有 InMemory 路由/配置补充回归 17/17，明确不计作原生数据库验收。
- [x] 一次专项源码审查无 P0/P1/P2 阻断；root 检查完整文档/证据 diff、81 个文件范围和 71 份原件的实际 index 字节。
- [x] 原复现步骤已在精确组通过。注入失败只证明真实事务的回滚控制，不冒称实际 SQL Server 死锁；直接 enlisted 入口仅经源码审查，覆盖限制在证据文档。

证据：docs/audits/2026-10-03-bug-161-cad-provider-recovery/README.md 和 native/manifest.json。失败 Matrix 七个自有库已普通 DROP 并核对全部不存在；本 BUG 两库保留供合并后冒烟。未触发 GitHub Actions。完整 WP6 双库 Matrix/Application、恢复验收和父任务交付仍待完成。
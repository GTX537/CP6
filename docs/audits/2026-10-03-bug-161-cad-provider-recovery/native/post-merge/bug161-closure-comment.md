BUG161 已通过正常 PR #162 合并到远端 main ce49012d37da4b1b01f074d5b5b7fd227780e656，包含候选提交 15ee2055e1f8930907eadcee6323ba3181f68033，完整 tree 与候选相同。

根因是 Serializable 等待站点锁后的旧快照；已实现最多三次完整事务回滚/释放/重读，保留版本冲突、幂等重放和证据不可变。修复前两项稳定回归均为真实 PostgreSQL 40001 失败；修复后原15＋新十项两库各25/25、零skip，补充InMemory路由17/17。一次专项源码审查无P0/P1/P2；71份原件保留原失败状态并按字节归档。相关 locked restore/build 零warning/error，SQL复用同一源码/产物。

合并后实际重跑原并发步骤和两项协调测试：PostgreSQL 3/3、SQL Server 3/3，实际源码/产物与原25项适用输入一致；PG协调用例再次观察真实40001后恢复。两个精确自有临时库已在owner/物理身份/无会话检查后普通DROP，全部不存在；receipt字节及专用角色保留。没有Actions运行、生产部署或既有业务库改动。

证据已在main：docs/audits/2026-10-03-bug-161-cad-provider-recovery/README.md；native/manifest.json SHA256 A230CE092F3C7ACBEA37FA9994137596E0BB50FF0199A312DD6D4FA1E236EEBC。后合并交付/冒烟/清理原件接入父WP6归档。注入控制不是实际SQL死锁验收；直接enlisted保护仅源码审查，未称独立native覆盖。

关闭本BUG。父任务#134保持Open，WP6最终双库Matrix/Application和整体交付继续。
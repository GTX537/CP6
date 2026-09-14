# C04A 源端组件本地验证证据

本记录只验收源端控制组件及**本机真实空来源恢复演练**，完整 C04A/CRM11/C04B 仍未关闭。[操作入口](../../../../eng/crm/source-fence-rehearsal/README.md)、[实施计划](../../../superpowers/plans/2026-09-13-c04a-source-fence.md)、[逐项输入及原始记录摘要](./local-validation.json)。执行发生于本机 2026-09-13 晚间，JSON 使用 UTC，日期可能显示为次日。

| 验证 | 实际结果 | 适用代码与证据 |
| --- | --- | --- |
| SQL 组件基础回归 | 30 通过、0 失败、0 跳过 | 使用真实 SQL provider 和链接的 Foundation；数据为测试 fixture。`original-execution-records.zip/sql-tests/release-complete.trx` |
| Reopen 审查修复后的定向回归 | 10 通过、0 失败、0 跳过 | 包含恢复写入后的立即重试、审计不重复、非空/过期/状态漂移拒绝；`sql-tests/green-reopen-replay.trx` |
| 备份/恢复输入保护 | 13 通过 | [input-guards.json](./input-guards.json)；拒绝身份不符、原目录覆盖、非本机实例及 UNC/设备/相对存储路径 |
| 原始数据库的真实备份恢复 | COPY_ONLY、CHECKSUM、HEADERONLY、VERIFYONLY 成功 | [restore-receipt.json](./restore-receipt.json)；新数据库及 MOVE 文件，不用 REPLACE；20 表全部为空，305 列及相关元数据与源观察相同 |
| 真实恢复副本完整生命周期 | 39 项通过 | [restored-acceptance.json](./restored-acceptance.json)；全部 20 表普通/直接 SQL 写入被拒绝，ERP 探针与读取正常，混合事务回滚，恢复/封口/再次恢复拒绝 |
| 最终 Release 工具真实副本冒烟 | 3 项通过 | [final-release-smoke.json](./final-release-smoke.json)；在已封口的真实副本验证 status、preflight 和 reopen 拒绝 |
| 原始数据库事后只读核对 | 20 表仍为空，元数据未变，无控制 Schema | [original-source-after.json](./original-source-after.json)；并非持续围栏或所有写入方盘点 |

30 项基础回归及 39 项真实副本生命周期使用 Reopen 重试修复前的 Release DLL `9313b59a…147ff`。随后审查发现“成功 Reopen 后已有正常写入，再立即重试同一 Reopen”会错误拒绝；修复的最终 DLL 为 `7860b3cc…d4d0c`，新增红测试、10 项相关绿回归及三项真实副本冒烟分别留存。未受影响的先前测试按原代码/程序集身份复用，**没有把它们改称最终 DLL 的整套重跑**。完整摘要在 JSON，修复前源码快照也在归档中。

独立审查的另一项 P2 是 `IsPathFullyQualified` 接受 UNC：现在三类 SQL 默认存储路径须为已存在的固定本机磁盘目录，且不经过重解析点。保护发生在连接 SQL、读取默认目录之后、备份之前。修复后已再次执行真实备份恢复和 39 项生命周期验证；并非仅静态声明。

## 原始记录与失败

[原始执行归档](./original-execution-records.zip)保留开发红测试、两次测试连接/错误编号修正、原子性与取消/约束失败、缺少 SQL 环境变量的预期失败、Reopen 重试红/绿记录、实际恢复与 CLI 输出。归档逐成员 SHA-256 在 `local-validation.json` 中；公开文件与源码同时记录原始及 LF 归一化摘要，跨 Git 换行转换使用后者，实际执行引用使用归档中的原始字节。机器路径与唯一测试库名称仅出现在原始执行记录，不作为配置输入。

恢复助手最初因读取时丢失 Foundation 的 UTF-8 BOM 而拒绝摘要；随后一次只读观察后遇到 PowerShell 连接字符串索引赋值问题，两者均在备份前停止。首次生命周期调用还曾因 Release DLL 尚未生成而在数据库探针前停止。失败没有被删除或计为通过。

公共合同扫描曾命中已在 main 中的四份 C03 状态摘要重复测试金额。本次删去摘要中的重复金额，保留对账结论及原始技术证据链接；没有改写 C03 报告、批准合同、哈希或扫描规则。失败及后续结果均归档。

## 未验收范围

源库当前检查操作身份是 sysadmin；SQL DENY 无法限制该身份的管理操作。工具固定报告 `completeWriteFenceVerified=false`，只允许本机特定名称与显式身份匹配的隔离副本；原库未安装触发器或权限拒绝。备份文件及私有资源定位记录保留本机，没有提交或公开。

`seal-forward-only` 是开放未来目标写入之前的保守封口；本次没有目标业务写入，也没有生产切换。实际旧写入方/作业/特权盘点与隔离、停止排空、CRM11 目标物理 Schema 和迁移校验、首笔新写入接线、真实路由切换及回退仍待完成。旧实体映射、已有预览、C01/C02、ERP、工作流与生产配置未修改。

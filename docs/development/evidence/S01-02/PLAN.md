# S01-02 实施与验证步骤

开工主线/任务HEAD为`4ab979e2f391c5a5645da0d1586781e824a9f8cd`，使用COORD已创建的`codex/s01-02-strict-json-20261012`与`D:/CP6-worktrees/s01-02-20261012`。本任务准入为READY/CONFIRMED；准确执行范围见[分配包](../../dispatch/20261012-wave2/S01-02.md)，原任务义务见[任务计划](../../../superpowers/plans/2026-10-11-s01-task-breakdown.md#s01-02严格-jsonnullonly-与结构相等mainm)。本文件细化已有合同，不重新定义业务输入。

1. 核对S01-01 DONE、本人assignment、已确认窗口和七份来源pin，记录于[准入检查](entry-check.json)。读取Handoff协调固定提交`a0429a8975367aa8a8dae5e7492bbddd35425da6`及本人新旧任务回复；START已发布为`23e84612dc3562ece912f03af2d0fcd04873b47b`，路径`handoffs/CP6-DEV-20261012/tasks/S01-02/messages/20261012T104701Z-start/MESSAGE.md`并逐字节回读。COORD负责登记IN_PROGRESS；消息上传不等于收到ACK。
2. 在`eng/contracts/exact-values-tests/`创建net8.0叶子xUnit项目，只引用CP6.Entity及指定Test SDK/xUnit版本。直接读取已固定的21个case ID和原预期，另写原设计明确的边界断言，保留真实失败执行记录，再实现生产逻辑。
3. 在`CP6.Entity/Contracts/Wire/`实现NullOnly及转换器、严格JSON读取/反序列化边界和精确结构相等。重复键按解码后的名字在任意层检查，先于类型转换；必填字段存在性由required元数据保障。数字系数保留准确十进制数字串，指数使用BigInteger，不用浮点、decimal或展开巨大指数。
4. 保留原始UTF-8字节和JSON结构语义的独立用途。补查深度超过默认64、超大指数、转义等价属性、嵌套数组/对象、JSON类型区分和字节保持。不得静默新增合法域限制；类型反序列化的实际能力与严格语法解析分开报告。
5. 执行任务要求的`dotnet test eng/contracts/exact-values-tests/CP6.ExactValues.Tests.csproj --filter FullyQualifiedName~StrictJsonTests`。记录实际环境、退出码、TRX、确切case ID、源码/制品hash和失败修正；没有运行/跳过不能记为通过。普通过程仅验证此叶子项目及CP6.Entity必要依赖，不编译全仓。
6. 实现完成后以完整任务为单位集中审查规范与代码质量。问题修复仅复查受影响部分并跑相应回归；无新实质风险不扩大检查。复用输入未变的成功结果并保留来源。
7. 核对改动仅位于三个授权目录，确认无敏感信息/临时制品/共用预期修改；审计候选与受保护主线工作流后保存提交并发布任务分支，通过Handoff提供DELIVERY。总台账、生成页、四份状态文件和串行合并由COORD负责，本执行对话不写这些共享文件。

合同工具运行通过只代表本任务边界实现。业务Owner、数据库、页面、真实权限与事务、CRM/Platform消费者采用仍各自NOT_RUN；不发布NuGet、不建库/迁库、不部署、不触发GitHub Actions。

# S01-06 交付报告

实现和本地验证已形成候选，尚未宣称远端 Main 包含或任务 DONE。协调对话负责读取固定 Handoff DELIVERY、同步 roadmap/生成页/四状态页、正常 PR 合并及远端包含性核对。

- 业务仓：`GTX537/CP6`（Main）。任务分支：`codex/s01-06-atp-stable-id-20261012`。
- 实际 main/origin/main 基线：`8569b669fed56ddc7b53b7bb24666c7fb93d544a`；输入基线：`d7a210256783e45291a5e724ea068ed4fc165ecc`。
- 实现及全部本地验证证据提交：`f13da00e8470200877c715658891f09833280592`。最终候选是其后的本任务证据提交，由固定 Handoff 消息附完整 SHA；后继只追加交付/审查/发布核对证据，没有改变受测源码、测试、Schema 或依赖。
- 可消费版本：`ATP-STABLEID-LEXICAL/20261011-R1`。C# `CP6.Entity.Contracts.PlanAtp.StableId.Parse`、JSON string converter；TS `stableId(unknown): StableId`；独立局部 Schema `urn:cp6:plan-atp:stable-id:20261011-r1`。

实际行为、固定源/patch pin、五个 PostgreSQL UOM 列、复现命令与限制详见 [IMPLEMENTATION.md](IMPLEMENTATION.md) 和 [source-provenance.json](source-provenance.json)。全部源码/test/Schema 提交字节与 GREEN 执行快照一致，shared 输入、依赖和 DLL 哈希也保持一致。完整 diff 只涉及分配的五类路径；未加入其他对话文件、构建缓存、共享配置或机器专属产品配置。

| 本地门禁 | 实际结果 | 原始证据 |
|---|---|---|
| C# `FullyQualifiedName~StableIdTests` | 60 执行 / 60 通过 / 0 失败 / 0 跳过 | [TRX](green-csharp.trx)、[日志](green-csharp.log) |
| TS `src/contracts/plan-atp/stableId.test.ts` | 81 执行 / 81 通过 / 0 失败 / 0 跳过 | [JSON](green-ts.json)、[日志](green-ts.log) |
| Web `type-check` | 退出 0 | [日志](green-type-check.log) |
| 固定 22 条向量 | 每条 C#、TS、Schema 断言均通过 | [逐 ID 矩阵](case-matrix.json) |
| 测试先行 RED | C# 57 失败 / 3 通过；TS 41 失败 / 40 通过；均退出 1，无跳过 | red-csharp.trx / red-ts.json / 对应日志 |
| 来源组合核对 | 四份原件/派生件及完整 DDL patch 组合 hash 相等；五列映射明确 | source-provenance.json |

Schema 结果只覆盖源片段四个断言的执行，不是通用 Schema 引擎或完整 ATP wire 验收。载体测试复用生产 StrictJson，验证字符串解码/发射与原 UTF-8 分开处理。

## 集中审查与发布

集中审查范围为 `8569b669fed56ddc7b53b7bb24666c7fb93d544a..f13da00e8470200877c715658891f09833280592`；独立审查结论为可交协调集成，Critical/Important 均 0；一处创建/执行基线措辞已按 reflog 修正，见 [REVIEW.md](REVIEW.md)。原始日志按证据保留原字节，任务 diff 的格式检查通过。收尾证据只做增量/链接/发布字节核对，复用上述未改变输入的本地验证，不把提交 SHA 变化当作重新执行。

[publication-precheck.json](publication-precheck.json) 核对候选/受保护 main 的 13 个工作流、事件过滤、uses 依赖及 Git blob；工作流字节完全相等。普通工作流仅手动，R2 `v*.*.*` Tag gate 保留；本任务只推送任务分支，不创建 Tag/PR、不触发 Actions、不调整保护。接手及交付准备阶段 queued/in_progress 查询均为空；取消 0、触发变更 0、Actions 执行 0。

Handoff START 固定提交 `b3fb82ab18e993cea992331f9f193dad036e91df`，路径 `handoffs/CP6-DEV-20261012/tasks/S01-06/messages/20261013T002811Z-c-start/MESSAGE.md`。其远端分支 SHA 及按固定提交读取的逐文件原字节相等，见 [运输核对](start-transport-verification.json)；该证明只代表运输，不是协调 ACK、业务接受或 DONE。最终 DELIVERY 继续在本人资料分支新目录追加，不覆盖历史。

## 真实未覆盖范围

数据库/DDL/迁移、ATP R11 Owner 接入、全量业务/E2E、完整 ATP wire、包发布、生产候选/签名、部署均未执行。PG 完整来源保留给 S01-10；当前 ISupplyService 仍是旧 MRP 标量供应接口。本次没有修改原包 `NOT_RUN`、41 条 `REFERENCE_ONLY`、247 个完整业务场景、历史接受或旧 30/48 原件不可恢复记录，也没有把上游 S01-02 的 77/77 改称本次新执行。

协调对话收到后请按本候选执行串行交付窗口，统一状态并核对远端 Main 包含性。本报告与运输成功均不自行将任务改为 DONE。

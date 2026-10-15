# 第七轮协调交付

WP02-12 与 WP02-26 两项限定库实现完成审查、正常 PR 合并和远端 main 核对，登记 DONE。WP01-01 与 WP02-02 的新准备成果完成归档，主任务分别保持 PLANNED / NOT_CHECKED 和 BLOCKED / NOT_CHECKED。累计 **24/375** 个细分任务，S01 为 **12/12**；这是任务数量，不能当作工时完成率。实际业务仍 **0/125**。

| 任务 | 本轮结果 | 证据 |
|---|---|---|
| WP01-01 | 修复 Issue211 后准备成果合并；PLANNED / NOT_CHECKED | [远端合并](WP01-01/remote-main.json)、[集中审查](WP01-01/review.md) |
| WP02-02 | 完整依赖诊断准备成果合并；BLOCKED / NOT_CHECKED | [远端合并](WP02-02/remote-main.json)、[集中审查](WP02-02/review.md) |
| WP02-12 | TransportV1 限定库任务 DONE；40 原业务 AC NOT_RUN | [远端合并](WP02-12/remote-main.json)、[集中审查](WP02-12/review.md) |
| WP02-26 | SLO/精确断言/故障工具限定库任务 DONE；56 原业务 AC NOT_RUN | [远端合并](WP02-26/remote-main.json)、[集中审查](WP02-26/review.md) |

## 审查修复与实际验证

WP01-01 初审发现六个 ITER 实体遗漏原始 Id 的强制身份绑定，代理 storage_id 与业务号唯一键不足以阻止原 Id 重复。登记 [Issue211](https://github.com/GTX537/CP6/issues/211)，从最新主线建立独立修复分支并显式接入原候选，再补原字节、非空与原唯一作用域约束。两个原文明确的全局主键与四个租户/迭代组合身份分别保留，不能从非空推导裸Id全局唯一；中间过宽方案被复查纠正，其证据作为历史保留。修复保留原候选/清单、静态反例和 RED/GREEN；[定向复查](WP01-01/fix-review.md)与[关闭回执](issues-closed.json)分别记录修复范围及远端身份。没有数据库或 DDL 执行。正式采纳字段和生产启用仍关闭，旧30/48缺件不因新设计消失。

WP02-02 的138文件、106原始归档、实际三入口构建和31私有宿主断言通过原始证据核对。29工具测试按原输入复用，协调重新做只读程序集策略核验。完整图与指定运行路径结论分开；旧 MSB3277、长路径、Scriban 和清理失败均保留。Main Program/真实认证/Owner/PG和17PGT仍未运行，现有NU1903未被抹去。源码采用候选不是生产ProjectReference例外或正式信任授权。

WP02-12 的库44、定向27、旧合同101、架构98，以及真实Kafka/Dapr/PG22和定向7按准确输入复用；定向与旧用例重叠，不相加成独立总数。真实producer边界限专属SASL/ACL、认证sidecar和独立broker记录；接收成功证明fixture观察提交。40原业务AC保持NOT_RUN，C02新采用继续关闭，未宣称真实Owner/Inbox业务同事务或生产TLS/SLO已完成。

WP02-26 的108本地、6原生PG及98架构结果按源码/依赖/程序集/TRX哈希复用。精确算术、独立总体、不可覆盖修订、有界捕获、完整DAG与原生锁/40001/42501/崩溃证据已审查；应用ACK抑制与原生故障分开。56原业务AC保持NOT_RUN，26项仅附限定局部证据，生产SLO声明恒false。

两项平台合流后只追加一次[共同主线架构冒烟](combined-architecture-validation.json)，核对新增公共类型与传输实现共存时的编译/依赖边界，未扩为全仓或真实业务重跑。全部原成功、失败和复用范围见[验证汇总](validation-summary.json)。原始自有资源已经停止；协调没有重启数据库或容器。

## 状态与剩余条件

[实际接收](intake.json)保留本轮8条START/DELIVERY；worker_ack_at来自消息实际启动时间，coordinator_received_at是当前读取时间，没有补造历史协调ACK。[Handoff字节核验](handoff-input-verification.json)区分原候选附件和运输元数据。前轮准备成果保存在preparation_delivery_history；当前准备归档不计主任务DONE。

WP01-01 后续仍需正式具名采纳、生产完成门接线与逐消费者证据。WP02-02 已有可审查源码采用方案，但正式feed包身份/信任或明确源码采用例外尚未具备；实际Host、Owner、PostgreSQL及17PGT仍需独立准入。未自动开放WP02-28或下一轮。

125目标、247运行NOT_RUN、41历史REFERENCE_ONLY、123/125原件盘点、ITER/REL不可恢复缺口及WP05 PROPOSED保持。逐次push/PR/merge均核候选和受保护main的工作流/过滤/依赖；无Actions运行、取消、触发配置变更、正式发包、迁移或部署。原分支、工作树和失败数据保留。四份项目状态及生成进度页与唯一台账一同更新；本地开发文档随最终main同步，Handoff回执另行固定提交并回读。

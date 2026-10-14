# 第七轮：推进传输与验收工具，补齐采纳决定和依赖诊断

本轮沿用四个对话。C/D两个完整任务已具备实施准入；A/B交付新的决定候选和实际依赖诊断，继续推进阻塞项，不重复第六轮来源盘点和空白模板。

| 沿用对话 | 本轮工作 | 仓库 | 主任务准入 |
|---|---|---|---|
| A（原WP01-01） | [WP01-01 技术决定收口与禁用完成门候选](WP01-01.md) | Main | PLANNED / NOT_CHECKED |
| B（原WP02-02） | [WP02-02 完整依赖与固定源码消费诊断](WP02-02.md) | Main | BLOCKED / NOT_CHECKED |
| C（原WP02-08） | [WP02-12 producer资格、真实传输与死信原字节](WP02-12.md) | Platform | READY / CONFIRMED |
| D（原WP02-25） | [WP02-26 SLO重算、确定性故障与精确断言](WP02-26.md) | Platform | READY / CONFIRMED |

A把DP01–08逐项分清：可由已接受合同确定的设计、仍待具名业务决定的政策、尚未实现的真实提供者；形成可审查的新版本候选和独立禁用完成门验证器。原采纳仍未签署，生产台账门不变。B使用固定源码快照和完整传递依赖做真实构建及隔离宿主诊断，解释原裸DLL探针警告，提供可复现源码采用方案供下一窗口审查；这不是正式包发布或可信消费准入。

C实现 producer/原字节/键/header/死信的消息合同，并在自有 Kafka/Dapr 上做真实传输验证。D实现可重算SLO、确定性故障注入、完整DAG与精确断言，配合独占PG验证原生故障和并发证据边界。都不自动证明Main/CRM/业务Owner已采用。

[唯一台账](../../roadmap.json)、[机器执行包](dispatch.json)与[准入证据](../../evidence/wave7-dispatch-20261014/README.md)固定写入清单和资源。C预约Kafka19092、Dapr13500/15001、测试端点18012/18013及必要时PG55430；D独占PG55429；B仅随机loopback HTTP端口，无DB。路径按owner分离，既有容器、5432、旧55428资源和业务环境不在窗口。本次仅只读检查和目录/分支准备，尚未启动这些服务。

22/375任务DONE，S01 12/12，业务0/125、247运行NOT_RUN、41历史REFERENCE_ONLY不变。旧准备交付和assignment历史完整保留；还没有本轮START，不虚构正在开发。WP01-01仍PLANNED/NOT_CHECKED，WP02-02仍BLOCKED/NOT_CHECKED，WP02-28继续等待可消费的可信身份。

恢复原四个对话后读取各自执行包和[Handoff入口](https://github.com/GTX537/CP6.AI-Handoff/blob/codex/dev-coordination-20261012/handoffs/CP6-DEV-20261012/README.md)，发布实际START或START_PREPARATION。没有创建/自动唤醒聊天或后台轮询；本轮不授权Actions、正式发包、采纳签署、业务迁库或部署。

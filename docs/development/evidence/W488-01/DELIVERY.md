# W488-01 · 隐私导出原消息恢复

按用户接受的[12h连续执行](../../planning/20261111-codex-12h-business-chain.md)及W487实际出口登记，COORD单对话串行，无子代理。官方status/start完成；Main DEV-1 / CRM DEV-2复用，集成席位空闲，两仓分支`codex/w488-01`。基线Main `a4324b0df2c7cdaef221b77b16e40fd01deb49d4`、CRM `526817f7a9fa05ea4391b683f35466f360348927`。

[W487](../W487-01/DELIVERY.md)已正常采用Main #955 / CRM #178，Issue #177关闭；准确任务分支删除、固定目录干净detached、两席官方释放，两个主目录安全快进且Main原14,932未跟踪路径保留。

## 准入与限定范围

新增privacy-export路由真实Owner原事件到HTTP sidecar确认/丢回执恢复；原一分钟租约实际到期前后保持原Request和已发送事实，不重复Job/H01/业务事务。已有其他路由成功按原范围复用。

当前已有InquiryOutboxTests覆盖配置、提交、审核、撤回及原保留消息；W482新增privacy-export路由虽按原合同排队，但尚缺该路由实际HTTP sidecar丢回执并跨原租约到期的证据。本窗口只补直接业务链缺口，先实际Owner/PG/网络；真实产品失败再最小修复，不预设需要改动。模拟外部sidecar/IAM允许，真实应用/数据库/消息字节与结果必须执行；HTTP204只证明模拟sidecar收件，不冒Kafka/企业ACL或下游业务确认。

原隐私申请202和唯一Job/审计/原消息、Source/TargetLocal事件字节、原键和H01计数须保持；unknown不能解释为失败、无效果或盲目再发送。等待原一分钟租约真实到期，不改DB时钟或原截止。重建Worker实例只声称该层恢复，不冒操作系统进程重启。

受影响行为验证加一次任务级集中SPEC/QUALITY审查、正常两仓采用/远端核对和准确分支/工作区/官方席位收尾；11h后聚焦当前任务及本轮结算。原AC37/60、修订2/247、375和正式PG16/11保持，满足完整原义务才采纳。

唯一报告本文件，原始输出在`D:/CP6-local-test/CP6-12H-20261111/W488-01`。两个开发席位由本任务独占、尚未释放；无Actions或部署，12h继续ACTIVE。

## 实际结果

新增`InquiryPrivacyExportOutboxTests`，通过真实公开提交/H01和当前模拟IAM权限调用正式隐私申请Owner，取得原202/唯一Job及原Effects/Outbox事件；独立受限writer实际采用privacy-export路由并经HTTP发送完整原CloudEvent字节。外部Dapr sidecar协议由明确本地模拟服务提供，应用/数据库/租约/网络和结果真实运行，不冒Kafka broker、企业ACL或下游消费成功。

最终`privacy-outbox-03/results.trx`为2 PASS/0跳过，共1m25s：

1. 正常204：原消息Published，完成1，后续Worker IDLE；发送的payload与Owner Effects/原Outbox完全相同，原事件/因果/租户/资源、topic和partition保持。
2. 服务已读取原POST后主动断开回执：OUTCOME_UNKNOWN，重建Worker实例仍用原RequestId、仅一次发送。原租约截止`2026-11-11T23:16:31.8736410Z`，观察`23:15:34.7412880Z`后真实等待59.132秒，实际`23:16:36.7655940Z`越过截止；再次重建仍OUTCOME_UNKNOWN/原Request `47f3d178-aaf8-47f1-990f-b5d91347154c`/发送1次，Native AttemptCount=1，未把租约失效解释成无发送或改发新请求。

两场景均以原申请键恢复完全相同202字节，六项业务计数、四项隐私Job/Artifact/Audit/Effect计数及H01调用不变；原Job仍Status20/Version1。传输不会重新执行申请/提交事务。该场景刻意使隐私生成worker间隔86400秒以隔离消息恢复；W482/W485/W487实际后台生成/下载证据仅按原范围复用，不把本项单独消息测试冒生成整链。

最初`privacy-outbox-01`两项失败是夹具把Effects.JobId写成CommandId（42703），原日志保留，不作为产品缺陷；正确列名后的02两项成功。随后只补原Job状态/版本不变这一遗漏风险，03对两分支定向再验成功，02/03不重复累计为四项。产品源码与依赖未变，无产品修复Issue。

## 集中审查与范围

执行者完成一次任务级集中SPEC/QUALITY审查：测试确实调用原Owner、当前权限/受限writer和正式Dispatcher，原字节/因果/事件/分区绑定，unknown前后实际租约时钟和原Request/SendAttempt、原Job及业务零重复均核对；不模拟数据库时间、不回改deadline、不把无回执冒失败。受影响行为已通过，文档只检查diff/链接与台账结构，不跑无关产品测试。

实际.NET SDK8.0.424/PostgreSQL18，本地隔离端口55570，明确模拟IAM/外部sidecar。原FX24尚含实际消费者Inbox去重、第五次确定失败的poison隔离等完整义务；本窗口未将unknown当确定失败，也未运行该消费者全链，因此AC-INQ-023保持NOT_RUN。原询盘37 PASS/23 NOT_RUN、修订2 PASS/245 NOT_RUN、正式PG16 PASS/11 NOT_RUN和375项38 DONE/1 IN_PROGRESS/336 PLANNED保持。

自有PG已核settings/Owner/目录/无外来客户端后smart stop，原数据/日志/失败保留，HTTP/Worker子宿主均退出，固定DEV-2只保留node_modules有效缓存。

## 远端采用与生命周期

CRM [PR #179](https://github.com/GTX537/CP6.CRM/pull/179)已将源`69d0621c705a574a6720a909aa300b46e73ca3bf`正常合入远端main`65993e5e48c9b7a2d3a4b7d2ae11fea9fe1fefdd`，fetch核祖先及完整树`bd85c719f28de0fc5075dfce3b4752dc61a751be`一致。官方finish成功，准确本地/远端任务分支删除，DEV-2干净detached供复用、席位已释放，证明private `crm-finish.json`。

两仓工作流树与受保护main相同、相关依赖未变：CRM `0c2d74f9fdc63f5178f97baf33950fb3d4dad751`、Main `3355235636fd45116f5445fca32fc65e49f13003`，复用既有手动入口/生产tag触发分析，本次非tag路径不命中生产流程。CRM任务Actions列表为空，无取消项、触发配置变更、远程运行或部署。

Main本提交同步唯一报告、roadmap/生成视图、四状态及W487最终采用/生命周期；Main自身PR/远端核对及官方finish待本PR最后核销，DEV-1由W488独占、当前尚未释放，不提前称两席释放。12h原生目标继续ACTIVE。

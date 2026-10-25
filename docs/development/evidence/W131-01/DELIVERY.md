# W131-01 原生身份增量、提交后确认及撤权传播交付

原生CrmInquiryNativeHostV2新增默认关闭的IdentityIngress配置，采用原PostgreSqlIdentityIngressV2、正式0.10.3可信消息上下文和CallerTransactionInbox共同事务。保留原C02 topic cp6.platform.events.v1、冒号partition及structured原文，不把旧EF消费者或通用斜杠消息协议当作新入口。

本地只读登记文件固定版本、明确UTC有效起止、启用状态、服务凭据SHA-256、实际peer IP、当前component文件hash、运输版本/证据引用及逐租户可信来源。每次投递重新读取登记与component；不信任转发IP、不从消息自报租户权限或登记，不创建租户/数据库登记，不自动DDL/授权。生产HTTPS或明确回环HTTP，真实连接peer和单个dapr-api-token共同校验；实际sidecar的__topic、__key、pubsubname与原C02内容比对。操作方文件是配置权威，不能单凭该文件证明broker ACL；更换/撤销须配合实际侧车受控停发和重新登记，文件与数据库并非分布式原子事务。

通过配置门的请求以新上下文/Serializable事务处理，沿用原七锁、PreCheckpoint、完整原文/当前及历史身份、Inbox原结果、冲突和已有会话撤权共同提交。PendingCommitResult不能产生ACK；确认COMMIT后另启只读事务核对持久投递结果，再映射SUCCESS或持久冲突DROP。未知提交、依赖/读取失败和撤销登记返回503 RETRY；原消息重投仍完整校验，不能新造操作ID。未持久隔离的格式/来源错误返回非2xx重试结果，不伪称已隔离DROP。登记在上下文、提交前后及读取/ACK前复核。1MiB正文、15秒正文/数据库截止和有界严格JSON保护保持。

增量沿原数据库guard撤回publication，W130后台再收集发布；publication仍PendingInspection、CanAuthorize=false。实际HTTP撤权与业务最终持锁门使用同一Owner和PG事务，观测到撤权等待实际锁；原业务提交后撤销会话及publication，下一次持锁访问被42501拒绝。此结果不扩大为生产传播SLO或即时授权承诺。

Main增加跨进程驱动：两个真实租户的原IAM Owner提交初始用户和禁用变更，独立仍启用的管理员重试原操作；从普通和priority上下文收集实际原Outbox的两条事件，不改字节、来源或版本。CRM独立进程经原生HTTP与普通PG写入核对每租户原文/hash、重复幂等和匹配会话撤权。Main Owner目录查询0、连接池借用归零。用户/credential epoch、IAM认证上下文、保护会话内容和侧车凭据/peer均为明确隔离夹具，不称企业登录/传输验收。

12项唯一相关本地检查通过：CRM11、Main1；必需0跳过。实际原生HTTP、普通PostgreSQL写入、提交后确认、最终持锁门撤权及实际Main原文跨进程采用均通过；未变成功来源复用，原失败保留。

最终证据保存在D:/CP6-local-test/wave131/foundation/final-validation.json。主要来源：ingress-complete-profile-20261025T073634Z的九项成功行为、held-caller-regression-20261025T073716Z的同Owner持锁回归、actual-main-fresh-source-20261025T074118Z的Main父驱动及独立CRM子TRX；最终审查补强缺失validFromUtc拒绝，仅以registration-validity-final-20261025T074745Z替换相应登记边界结果，不重复计数。完整原失败和定向复测保留。

最初新类型编译、只有ingress的旧夹具安装权限profile、错误的同Owner实例、禁用操作者后重试、遗漏priority outbox、未显式设置初始epoch和重复利用已改变来源hash的CRM夹具均查明并定向修复。没有放宽数据库安装/来源变更检查；换用本轮新建隔离完整profile数据库。审查发现遗漏validFromUtc会接受JSON默认时间，已在本PR拒绝并通过对应回归。没有新增需另立Issue的既有产品缺陷。

当前Docker Desktop因既有sailor-ingest.sock启动失败，未获得daemon或启动任何容器/网络。自动审批以“blocked by policy”拒绝含socket移除的命令，命令未执行；未再尝试删除/改名或其他移除路径。随后核对路径和启动时间，仅停止本次启动的Docker进程，全部已退出，原socket保留。原2026-09-10 transport-byte attempt3 3/3证据仅引用于原IdentityDaprPublisher rawPayload=true、订阅rawPayload=false、保留原文及Kafka保留header过滤，不冒称当前新宿主跑过broker；本轮实际运输为NOT_RUN。准入范围已明确先交付独立PG/HTTP路径并记录运输可执行性。

本轮四个自有CRM PostgreSQL实例（55457/55458/55459及重新使用55457的新实例）、Main55435隔离夹具、HTTP和子进程均已停止，数据及失败记录保留。无Web变更/浏览器验收、Actions、公共合同/依赖/迁移变动或生产部署；缓存保留，无新junction。一次任务级SPEC/QUALITY审查覆盖各PR完整差异，文档只查差异/链接和台账一致性；不因提交或文档修改重复产品验证。

CRM [PR155](https://github.com/GTX537/CP6.CRM/pull/155)：58c4ac76f5517c8d432290fde081d21f9ca2bacb → 8513d075518227bde3e79787559e5977e38d4095；[CRM单份报告](https://github.com/GTX537/CP6.CRM/blob/58c4ac76f5517c8d432290fde081d21f9ca2bacb/docs/development/evidence/W131-02/DELIVERY.md)。Handoff [PR107](https://github.com/GTX537/CP6.AI-Handoff/pull/107)：041afd052a83b873de856eddf01027e4645a6786 → de6e896e9dcca302802d315bc2fc45ca9fa718c3。两仓完整树采用、准确本地/远端分支删除及官方finish释放均已核对，DEV-2已重绑Handoff留作复用。Main最后PR/finish待同轮完成，以wave131-final-verification.json核销，下轮统一登记最终快照。

原估115–190分钟，截至准备33.93分钟；最终以Main START至最后finish计一次，跨仓不叠加。D5原生API可观测性仍待；本轮实际Dapr/Kafka链路NOT_RUN，真实企业传输ACL/凭据/登记、协调撤销和生产传播SLO未证明。D2、D3、D6、D7继续原计划必要出口，完整D5与正式验收不提升。

七天目标ACTIVE；D1/D4已关闭，2/7=28.6%仅表示整阶段出口数，不能作为工作量完成率。正式375卡24DONE、2IN_PROGRESS、349PLANNED；完整125目标0关闭，247NOT_RUN+41REFERENCE_ONLY保持。W129剩余42–70小时是当时低置信估计，已交付后不能当作重新精算值；外部真实政策/日历/登记/密钥等待另列。

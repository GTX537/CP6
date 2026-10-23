# W51 / 七天一期 D5：CRM PostgreSQL 会话与撤权共同事务基础

COORD本聊天单人，2026-10-23T01:07:18.332618+00:00。Main `a33ae89e9779d1e8701c52cc955de50e0f37737f`，CRM `c420cf9e0711bc0c5529d2f709708a57533b341a`。W50产品PR416、状态PR417、Handoff26已采用；准确任务分支删除，工作区干净detached，全部席位finish。实际约50.27分钟为排除中断的分段证据估算。W37–W50不重复，七天目标ACTIVE。

## 前置核对与范围

最新AGENTS、统一POLICY、S02-00共享身份/认证域设计、WP02-06/09直接卡、INT-ID当前补充、Platform WP02-07实际调用方事务与锁约束、Main W21/W22可信基线交付以及CRM现行源码已核。Main身份基线和Platform原生Inbox已存在；CRM仍使用SQL Server投影/会话/锁与0.10.2，不能直接将Main能力当CRM采用。CRM远端main已fetch核对，工作区无他人改动，容量无告警，两个开发席位内串行复用；集成席位不作实现。

本窗先交付CRM实际PostgreSQL会话存储和调用方事务内撤权接缝：保留登录状态一次消费、加密payload、准确tenant/subject/issuer/jti/epoch、会话CAS、数据库并发限流；撤权与session失效同一Npgsql连接/事务，旧读取或重建不得复活被撤销token。使用PG原生RETURNING、事务锁和独立版本化CAS，不把SQL Server8字节rowversion当PG xmin。SQL历史实现/迁移保留，不新增双库验收。

这是WP02-06必要基础窗口，不宣称完整身份投影/Published/PreCheckpoint/Owner最终授权已采用。实际可信Main基线导入、全writer锁覆盖、CRM完整PG业务存储、正式0.10.3宿主采用与WP02-09仍须后续窗口。没有完整PG身份门时不把新存储接成生产登录旁路，不启第二身份消费者。不迁移现有数据、不部署环境、不触发Actions。

## 实际执行预算与顺序

预计实际90–150分钟：存储/原生约束及会话接缝35–55，撤权/并发/CAS专项验证30–50，一次集中作者SPEC/QUALITY和三仓交付收尾25–45。按任务实际完成推进，不等待日历；最终按真实记录修正。

DEV-1 Main `codex/w51-coord`仅总控台账；DEV-2先Handoff `codex/w51-handoff`发布START并可靠保存后finish deferred释放，bind至CRM `codex/w51-01`实现，CRM合并finish后bind回Handoff resume同一原分支补交付并合并。仅两开发席位，不创建第三开发树，不清理独有文件或复用未释放席位。

验证使用隔离自有PG18、本地相关.NET，覆盖一次消费/绑定、过期、CAS与删除后重建、真实共同事务回滚、提交后原状态、跨tenant/subject/token、并发撤权与建会话/续期、限流、只读启动不建库。新Schema职责限宿主认证/受控身份写者，不授普通业务角色跨租户访问；无完整角色安装时保持未部署。固定仅一份CRM交付报告，Main台账/生成页/四状态在同一协调分支闭环，Handoff只做固定消息证据。

允许写入：CRM Api/Authentication、Infrastructure/Identity/PostgreSql、必要项目依赖、相关PostgreSQL测试和docs；Main docs；Handoff本任务消息。独占CRM身份/会话存储、原生schema、PG55435。原125目标/247运行/完整卡片状态不提升。

## CRM产品采用 / 当前收尾

CRM [PR96](https://github.com/GTX537/CP6.CRM/pull/96)已正常合并，source `83017508cb2c4a702ab8f5fb3bb62d73d12c96af`，远端main `48be253dfe3c4b451a9be7eaf9b7ef5f1199086a`，13个任务文件精确blob采用核对通过。14个不同相关PG/.NET用例通过；实际共同事务、独立并发连接、原生约束、CAS、只读启动及现有AuthService兼容；上游明确使用test double，不冒充Main/OIDC或Owner最终门。唯一[产品报告](https://github.com/GTX537/CP6.CRM/blob/48be253dfe3c4b451a9be7eaf9b7ef5f1199086a/docs/development/evidence/W51-01/DELIVERY.md)。初始编译/analyzer和观察器缓存导致的测试失败保留；一次完整作者审查及相关修正复查完成。

新PG存储保存完整UTC ticks，28字节版本化CAS含实例/递增版本；global issuer+jti身份绑定与原生撤权删除同事务，旧Serializable快照40001。现有Program不选择新存储，完整source/Projection/Published/Inbox/final authorization和全部writer仍待后续。不运行旧SQL双库/前端/全仓，不触发Actions/部署。

自有PG已确认停止，76,150,437字节可重建data因自动审批拒绝删除而保留（仅返回blocked by policy，无更具体原因，无替代删除）；证据、TRX、日志和构建缓存保留。CRM finish后复用DEV-2回Handoff原分支，Main总控分支最后合并收尾；最终真实席位/准确分支状态见本轮PR和本机wave51-final-verification.json。整卡24/375、125目标0交付、247运行NOT_RUN不变。

## 资料采用与实际生命周期

Handoff [PR27](https://github.com/GTX537/CP6.AI-Handoff/pull/27)已采用`db5a5c486e46aaf9944df812d036c33ddb086ef8`，准确任务分支已删除、固定树干净detached、finish释放；CRM PR96同样完成准确分支删除及finish，DEV-2串行bind/resume全过程未超额。Main仅剩本协调分支正常PR和finish；当前记录不预先声明Main已释放。最终proof为`C:/Users/tt/CP6-planning-reports/20261017-foundation/wave51-final-verification.json`。本轮登记开工至此约23.17分钟，前置只读核对另约8分钟；最终实际耗时随finish核定。下一轮继续CRM真实PG基线/投影/前置检查，七天目标ACTIVE。

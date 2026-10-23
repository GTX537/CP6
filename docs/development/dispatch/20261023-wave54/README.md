# W54 / 七天一期 D5：CRM正式Platform包与PG共同锁协议

COORD本聊天单人，2026-10-23T03:03:44.501552+00:00。Main `21d401dd34f6a7ca80b5cafbfe310a27f2dafc66`，CRM `7c74f9c9471415df4f4f6429671b8665fafbb4cb`，Handoff `80737129714840a079cde5ea7a015ff2b4f4a016`。W53 CRM98/Handoff29/Main422已采用，准确任务分支删除、固定树干净detached、全部席位finish；登记实际28.28min，前置另约2min，22个不同相关用例通过。七天目标ACTIVE，W37–W53不重复。

## 前置与限定准入

已核AGENTS/POLICY哈希未变、台账WP02-06/07/09、W51–53实际PG存储和全部当前写入口、Platform正式0.10.3发布清单、CallerTransactionInboxV1源码/原生锁协议及已交付测试。Platform当前main `a89e90eb4f078f37a6f8610882079073185fbcf8`，正式包源`1277da5e0a20c6d96f0a95d0e9b4f20e0008332a`；旧文档NOT_RUN的宿主边界不冒充当前包未发布。全卡仍不因局部窗口关闭。容量无告警，主目录用户内容保留。

W51行fence与Platform七阶段advisory协议不同。先显式建立CRM版本化共同锁profile：Registration→Reconciliation→Published→Projection→Inbox→Checkpoint→Observation，固定tenant/stage键；现有会话创建/touch/退出、token撤权、基线保存、对账/当前引用均在原行fence前进入同一协议。新增安装层和原生守卫，不改写W51–53冻结schema；已有环境不自动安装。新profile面向已登记单tenant实际事务，跨tenant批量与任何未知预持锁路径不冒充已支持；普通login/rate独立路径保持原边界。

原生检查与C#计划一致，拒绝未知/部分预持锁和无共同锁的受保护写入；正式Inbox自己获得七锁后可调用实际CRM存储且保持至caller commit。锁profile不提供身份/来源授权，不是Main–CRM分库最终门，不使PendingInspection变成Allow。

采用已正式发布0.10.3的实际CRM Platform引用，固定缓存nupkg与正式发布hash核对，必要Npgsql EF provider采用PG已有版本；不发布新包、不改全局feed/证书/Actions、不恢复旧自动工作流。相关编译/测试按实际依赖变化选择，旧报告保留原0.10.2身份，不伪装历史重验。

本窗验证真实正式CallerTransactionInboxV1原生七锁与实际CRM写入竞争/回滚、跨tenant/未知预锁拒绝、原会话与基线API在新profile下兼容。transport/adapter测试身份明确fixture，不冒充CRM正式内容消费者。完整PreCheckpoint内容/历史冲突/AuthoritySelection、事件原始来源存储与投影升级、持久Published和Owner共同事务在后继连续窗口完成；Program和旧消费者未切换。

## 实际预算与席位

预计实际60–100min：正式包核验/采用与PG profile实现25–40；相关原生竞争/故障和接口兼容验证20–35；一次作者SPEC/QUALITY、三仓合并/清理/finish15–25。按实际记录，不等待日历。

DEV-1 Main `codex/w54-coord`维护台账/计划/四状态；DEV-2 Handoff `codex/w54-handoff` START保存后finish deferred，bindCRM `codex/w54-01`实现/交付/finish，再bindHandoff resume原分支交付/finish，Main最后收尾。无新增物理工作树，集成席位不用作实现。独占CRM身份PG/schema与包/项目配置；隔离PG55437，复用固定编译缓存。

允许CRM Identity/PostgreSql新版本锁profile/安装/清单、必要当前PG入口兼容、实际Platform包引用和Npgsql EF配置、相关PG/接口测试、docs/四状态；Main仅docs/台账/生成页/四状态，Handoff消息。不迁移既有库、不删除历史SQL、不运行Actions或部署。W51被自动审批拒绝的准确data删除保持停止/保留，不重试绕过。125目标/247真实运行验收保持未提升。

## W54实际交付

CRM [PR99](https://github.com/GTX537/CP6.CRM/pull/99)已采用，source `9ad4d54a9b18c87c105c887355074cfb02c741e1`，远端main `46dfd16e0052eab44c1020970767f4f47fc5cf0d`，26任务路径exact blob核对通过。[单份产品报告](https://github.com/GTX537/CP6.CRM/blob/46dfd16e0052eab44c1020970767f4f47fc5cf0d/docs/development/evidence/W54-01/DELIVERY.md)。101个不同相关本地用例通过：14个profile/正式Inbox互通，34个既有PG身份回归，53个当前Platform消费行为；受影响项目编译，C01真实身份/SQL Server执行NOT_RUN，无Actions。

当前CRM项目图采用正式Platform0.10.3，七个nupkg与正式发布清单SHA-256一致，保留旧固定报告/消费者范围；C01当前工具改用0.10.3版本与原始发布清单副本，不伪称真实身份重新验收。新增显式PG共同锁profile在W51行fence前按Registration→Reconciliation→Published→Projection→Inbox→Checkpoint→Observation取七锁。当前会话/撤权/基线/对账/投影受原生守卫约束，未安装profile的旧基础可用，三个冻结schema未改写；不自动切换既有环境。

正式Inbox同连接Serializable实际取得共同锁并调用CRM行fence与PG图读取，指定backend原生阻塞证明当前写入口竞争同一锁，caller提交/回滚后释放。图仍CanAuthorize=false，测试fixture返回DependencyUnavailable/空receipt，不写Checkpoint/Owner效果。预持部分/额外/外tenant锁拒绝；完整预持锁的历史顺序依赖可信委托，单事务单tenant，不声称跨tenant批量/旧消费者已采用。

一次作者SPEC/QUALITY审查通过；原生回归准备已遵循新协议，原防护断言不放宽。所有失败日志保留，离线source mapping在任务目录调整，未改全局或仓库信任配置。owned PG已核身份和零外部会话后停止，数据/证据保留；W51被自动审批拒绝的准确data删除仍保留，无重试绕过。CRM准确本地/远端分支删除，固定工作区干净detached，manage finish释放后DEV-2复用Handoff。

正式CRM原始事件来源、全路径PreCheckpoint/历史冲突/AuthoritySelection、持久Published/当前授权、真实Owner共同事务和Program采用继续后继。锁协议不提供认证/授权，不形成Ready/Allow或Main新鲜度。无Actions、生产部署或既有库迁移，不提升24/375整卡、125目标0交付或247运行NOT_RUN。七天目标ACTIVE，继续D5。

Handoff [PR30](https://github.com/GTX537/CP6.AI-Handoff/pull/30)已采用 `51eb830369d0580d16feab2c6999a21187e5e569`，准确本地/远端分支删除、工作区干净detached、finish释放。Main仅剩当前协调分支正常PR及finish，不提前声明释放。登记开工至此约29.07min，原预算60–100min，实际最终时长以finish为准，不重复累计子任务；此前只读核对约3min。

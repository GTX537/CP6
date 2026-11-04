# W301 — 原可信撤回卡审计与准确剩余

W301原WP12-19审计发现BUG751：ITER普通BLOCKED重试只锁实际人，未锁原业务grant/授权方式；原卡暂不关闭，下一独立BUG修复后重新核销。

Main基线`cff40fb720d8d15e8969d794ae97a0fd33346ad9`，COORD单人。原375任务、原卡实施范围/正反验收均未删改。此次审计不能据五Owner接入齐全而直接关闭原卡。

## 逐项对应

| 原条款 | 当前实现与有效原证据 | 结论 |
| --- | --- | --- |
| 原actor、subject/binding、scope和可信事实；worker不得选人 | ApprovalBusinessEvidenceV2显式路由ITER、DOC、ReqSet、ToolDisposition、LocationRule；各Owner仅接收commandId/reason，认证实际人/准确本轮grant由Owner冻结，事实/Outbox/原日志原子提交。W200/W277/W297–300的PG/真实HTTP与页面恢复 | 当前五Owner已采用；未知Owner明确拒绝 |
| withdraw/apply同control，两种提交顺序 | ITER、DOC、ReqSet共享控制头；库位复用原提案revision/token。先应用保原结果，先撤回晚决定STALE；对应W200、W297、W298、W300两连接/回滚。Tool实际同control、原请求/物理停用/义务不变，W299 | 已有合法效果的Owner两种顺序已运行；Tool尚无法合法APPLIED，不虚构报废赢家 |
| BLOCKED连续receipt与终态不改写 | 各真实ApplicationStore追加revision/previousReceiptId，原终态返回原件；ApprovalReceiptIngestionV2.Drain与Recovery拒绝终态后继。W184/W185协议与W200/W277/W278真实ITER闭环 | 原回执链与真实恢复已采用；普通重试的原授权连续性仍有下述BUG |
| 原角色/原grant当前有效才继续，换人须Owner新请求 | OperationsOwnerV1按当前独立动作、字段/Scope和最终fence授权；ITER普通重试锁ActionActorId，ReauthorizeApplicationAsync显式换人并保存新grant。W277/W278覆盖换人 | **未全部满足：BUG751未校验普通重试的原grant/授权方式** |
| callback.replay不授apply，管理员不能强制，worker不得换人 | 当前申请入口要求独立review-apply/dispose/review-bind，封闭DTO不接受force/actor。OperationsOwnerV1没有管理员绕过；服务运输仅传不可变Owner事实/回执。W185/W200/W277/W297–300相关权限/原件验证 | 当前代码与已运行边界支持；不声称本窗新跑特定名为callback.replay的场景 |

复用报告：[W185](../W185-01/DELIVERY.md)、[W200](../W200-01/DELIVERY.md)、[W277](../W277-01/DELIVERY.md)、[W278](../W278-01/DELIVERY.md)、[W297](../W297-01/DELIVERY.md)、[W298](../W298-01/DELIVERY.md)、[W299](../W299-01/DELIVERY.md)、[W300](../W300-01/DELIVERY.md)。合法RETURNED运输后续修正由[BUG747](https://github.com/GTX537/CP6/issues/747)保持真实适用范围；旧CONFLICT原件未被改写。

## 唯一活动缺陷与下一步

[BUG751](https://github.com/GTX537/CP6/issues/751)为源码审计确认的授权连续性缺口，尚未声称运行复现：IterationReviewOwnerV1.ApplyCore只比较BLOCKED的ActionActorId，BusinessAct重新选择当前grant；未采用原receipt.audit_json已有的behalf/grantBody。因此同一实际人在grant撤销/替换后，或切换本人/受托方式时，可能以普通重试绕过显式再授权。

按仓库BUG规则，W301保存本审计和真实未完成状态，紧接着从最新main建`codex/bug-751-iteration-application-grant`，用本地PG18回归先复现再最小修复。检查同原grant可续、准确版本撤销/修订/替换拒绝、本人/受托切换拒绝、显式再授权追加正确前驱且保原结果。无需新资格表、迁移或更改公共wire。独立修复交付完成后，再按此审计映射判断原卡DONE。

## 直接前置与范围边界

WP12-07已DONE，复用原决定/回执协议。WP02-10的wave43_delivery/wave44_delivery提供实际Main CP6Context同PG事务及原命令结果；WP02-13的wave153_nested_source_fence提供共同最终权限与同事务审计。已核对准确报告与远端采用，可以按原TASK_DELIVERY需求采用，不要求其全部租户连接/隐私卡完成；本窗尚不写原卡DONE采用字段。

本卡是可信撤回与恢复协议，原文明确WP05-20是消费者而非底座前置。Tool义务参与方/物理报废、库位规则激活，以及尚未开发的其他业务域不能冒称完成，也不将它们增设为当前协议原卡全部业务出口。DOC/ReqSet/Rule现不支持的委派应用没有被声明为支持。

## 验证与交付

原卡/当前五Owner源码与既有报告逐项审计；原验证的远端采用祖先关系、准确前置交付、台账生成和文档链接已核对。本窗纯审计，无新增产品运行；旧PG/HTTP/Chrome证据按原范围复用。

COORD单人集中审查完整原卡与当前源码：五Owner可信事实/同control、当前实际人/精确grant、原日志/Outbox、连续回执/终态不改写、运输与业务授权分离。发现的授权缺口登记唯一GitHub Issue751，拒绝提前DONE；按BUG规则先保存审计，随即从最新main独立修复。

原375仍30DONE/2IN_PROGRESS/343PLANNED；原59局部6闭环/53余项，125完整目标及247正式验收不变。五Owner撤回已采用，当前明确原卡缺口为BUG751；版模实物终结、规则激活属于后续业务卡。 三天目标CP6-24H-20261104继续ACTIVE；下一BUG751优先，其后W302隐藏字段受控处置。W301发现缺口后的审计收尾不等于该缺口已修复。

单一核对索引`D:/CP6-local-test/wave301/foundation/final-validation.json`。没有产品代码/依赖/workflow变化，没有新跑全仓测试、Actions、部署或既有数据库迁移。模拟业务数据的既有证据保留原环境与结果，未转换为生产部署声明。

报告提交时两仓远端采用、准确分支清理及官方finish仍待完成。最终分支处置、工作区与席位证明：`C:/Users/tt/CP6-planning-reports/20261017-foundation/wave301-final-verification.json`。

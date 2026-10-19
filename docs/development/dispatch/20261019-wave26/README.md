# 第二十六轮：完整认证政策与会话公共基础

## 第二十六轮三个限定交付已集成

| 窗口 | 正常PR | Issue | 单份交付/有效验证 |
|---|---|---|---|
| W26-01 | [PR328](https://github.com/GTX537/CP6/pull/328) | #327 Closed | [政策、原proof与共同PG完成门](../../evidence/WP02-03/authentication-policy-gate/DELIVERY.md)：187通过，99原生PG |
| W26-02 | [PR330](https://github.com/GTX537/CP6/pull/330) | #329 Closed | [Web/Native交互入口](../../evidence/WP02-03/authentication-entry-completion/DELIVERY.md)：101通过，63原生PG，type-check通过 |
| W26-03 | [PR332](https://github.com/GTX537/CP6/pull/332) | #331 Closed | [会话/设备/精确完成恢复](../../evidence/WP02-03/authentication-session-continuity/DELIVERY.md)：153通过，106原生PG |

产品远端main `0b65bb396c1704c41a5d2ddc8740aaff06edaab2`，各source祖先与各自合并时任务范围blob已核。最终产品W26-03范围一致；其相关政策、原交互、SSO、当前会话、模拟、CSRF和设备/并发/旧库迁移结果构成受影响组合验证。三个窗口用例有重叠，**不得相加当作独立总数**。每项一次作者SPEC/QUALITY，发现问题只局部复查；全部原失败保留，无剩余失败或跳过，无独立审查代理或全业务验收。

三个窗口CLOSED，资源释放；22次自有PG全停止、55435无监听。复用唯一工作目录、wave18构建、NuGet和已核锁文件的零复制node_modules junction，零新树/代理/Actions，原7项用户资料保留。D余约38.85GiB。初估本代理5–8小时、余量10小时，至产品集成/本次收尾准备约174分钟；最终耗时记录在完成检查点，24小时是完成窗口，提前完成不空等。

后续优先：客户端保存原交互秘密/跨Tab恢复与UNKNOWN协调；SSO首次身份明确准入和批准UI；剩余IAM引用CAS及CRM投影/本地Owner业务最终门。现有DEV采用本轮前向迁移并重新登录、普通/管理员真实业务冒烟另设环境窗口。本轮未操作既有PostgreSQL副本或生产。完整IAM/CRM/Owner/真实业务不宣称完成，14包/125目标/375任务/247运行NOT_RUN保持。

最终文档main、Handoff和检查点核对后关闭本轮持续目标；未自动开第27轮。下方过程记录保留其原时点，不代表当前仍待合并或运行资源。


> W26-03候选完成：[DELIVERY](../../evidence/WP02-03/authentication-session-continuity/DELIVERY.md)。#331原缺口修正，153有效通过/106原生PG；一次作者SPEC/QUALITY无剩余阻断。正常合并后总收尾，goal active。

> W26-02已通过[正常PR](https://github.com/GTX537/CP6/pull/330)合入`7833a58db53531181da13fd656e4837e71222e0d`，#329关闭。W26-03真实START，原refresh/current-access/device与精确完成状态恢复接续，随后总收尾，goal active。

> W26-02候选完成：[DELIVERY](../../evidence/WP02-03/authentication-entry-completion/DELIVERY.md)，#329修正，101相关有效通过（63原生PG/HTTP），一次作者SPEC/QUALITY无剩余阻断。正常合并后接续W26-03，goal仍active。

> W26-01已通过[PR328](https://github.com/GTX537/CP6/pull/328)合入`15b61f577e56fbff99acd98c066c49cec9d2ebd5`，#327关闭。W26-02真实START，含一次性交互/完成回执必要PG模型及Web/Native输出适配；随后W26-03和总收尾，goal active。

> W26-01候选完成：[DELIVERY](../../evidence/WP02-03/authentication-policy-gate/DELIVERY.md)，#327修正，187相关通过/99原生，一次作者SPEC/QUALITY无阻断。待正常合并，随后W26-02/03及收尾，goal仍active。

当前聊天单总控持续执行，三个实施窗口及收尾全部完成才关闭goal。基线`4682b0d8a82efa6cc7a267fc69068bcb6bc3c839`，第二十五轮已完成，活动Issue0、Handoff无新交付。当前用户要求优先完整认证政策，沿S5 SYS-IAM-01 v0.2 §6.1–6.5/§7/§9；不重新登记Target/原任务身份，不提升14包/125目标/375任务/247运行NOT_RUN。

目标开始2026-10-19T10:58:51.471858+00:00，24小时检查点2026-10-20T10:58:51.471858+00:00；估计**本代理实际5–8小时**，故障修正余量约10小时。上一轮实际约1小时43分；本轮跨入口、政策、一次性凭据与事务，估计包含实现、定向验证和交付，不是用户工时，提前完成不空等。

| 顺序 | 交付 | 代理实际估时 |
|---|---|---|
| W26-01 | 版本化政策、原认证证据、PG共同完成门与writer并发边界 | 45–90分钟 |
| W26-02 | Web/Native密码、SSO、2FA验证/绑定采用共同门 | 2–3小时 |
| W26-03 | refresh原证明、共享设备当前政策、FullAuth与失败隔离 | 1–2小时 |
| 集成收尾 | 相关验证/一次审查/逐项正常合并、台账/Handoff/资源/检查点 | 45–75分钟 |

## W26-01 政策与完成门

- [x] 核实际TwoFactorMode/SSO/PasswordPolicy/AllowPasswordFallback/QuickSwitch政策来源，缺失或非法关闭，不以默认0/AutoProvision=true等于批准。
- [x] 新增Main受控AuthenticationCompletionGateV1与不可由客户端补造的原proof/政策版本。明确tenant/user/client/device、primary方法/时间、factor、SSO配置及restricted各身份；认证版本不冒业务权限epoch。
- [x] PostgreSQL门覆盖tenant/user/凭据/2FA/SSO/政策实际writer；按原平台root/行锁顺序协调，不仅在签JWT时加锁。重复读/Serializable旧快照也不能越过变化。必要PG前向迁移只在自有已填隔离库验证，非PG历史范围保留。
- [x] 定向实际PG政策变化、撤权/凭据变化竞争、事务取消/回滚/未知边界，合法路径和来源缺失；一份DELIVERY、一次作者SPEC/QUALITY，正常合并。此窗提供门，入口采用由W26-02/03接续，不提前宣称全部生效。

预期修改CP6.Core/Services/Sys/AuthenticationCompletion*、认证policy/proof服务及CP6Context映射、PG迁移/快照；仅必要Sys实体扩展，测试CP6.Tests/Platform/AuthenticationPolicyPostgreSqlTests及相关Sys/模型。报告evidence/WP02-03/authentication-policy-gate/DELIVERY.md。

## W26-02 全部相关发行入口

- [x] 从最新main复用原目录；先核AuthController、ClientAuthController、AuthSessionService、SsoService/state、NativeSsoGrantStore、PendingTokenStore/2FA现行为。
- [x] 密码/SSO第一因子生成真实proof后统一决定；外部SSO不替本地2FA，Mode2未绑定需enroll，pending无普通session。SSO明确MustChange返回补救，不能以占位密码年龄迫使设置本地密码。
- [x] pending原policy/版本/交互绑定、一次性完成及专属enrollment旧新epoch转换；准备密钥被替换/管理员reset/并发消费拒绝。code/state/grant/OTP已消费不能自动重放，未知不回传历史秘密。
- [x] session注册/refresh记录/必要回执确定提交后才输出Web三Cookie或Native会话；restricted遵现已采用白名单，不冒ACTIVE/FullAuth。必要Web挑战landing适配同站安全状态，保NativeAuthResult pending Session=null合同。
- [x] 实际Program HTTP和受控SSO适配、PG故障/并发、相关原前端/Native合同；一份DELIVERY、一次作者审查，正常合并。

报告evidence/WP02-03/authentication-entry-completion/DELIVERY.md。新SSO身份准入政策、完整FE跨Tab/安全状态恢复、CRM本地session仍以实际覆盖范围记录，不擅自虚构批准或原始proof。

## W26-03 会话与设备连续性

- [x] refresh持久原SessionAuthenticationEvidence并原值传递；当前policy/SSO/凭据变化或缺证据重新认证，不在刷新端开启新MFA，不洗原验证时间/强度。
- [x] 原family→token锁序、单胜轮转、精确pending/rejected/UNKNOWN隔离复用W25；仅受影响风险增加验证，不误撤其他健康设备。
- [x] QuickSwitchAllowed显式版本化Owner来源缺失则关闭；目标MFA/Mode2/强制SSO/改密拒绝quick-switch，保原共享窗口截止。完整新交互且设备当前active才同完成责任设置FullAuth；refresh/切人绝不延长。
- [x] 当前access政策消费/模拟合法绑定按实际影响接续；注册/完成失败和设备竞争专项测试，保当前Cookie/DTO合法形态。一份DELIVERY、一次审查、正常合并。

报告evidence/WP02-03/authentication-session-continuity/DELIVERY.md。最终核已合入范围组合风险、远端main/Issue、台账四状态/生成页/Handoff；资源停止与检查点后关闭goal。完整IAM组织授权、CRM/Owner采用及真实业务验收不在本轮冒称完成。

## 执行与存储

只有COORD持有Main:identity-authority。每项从当时最新main建独立codex分支，唯一目录`D:\CP6-worktrees\wave17-dispatch-20261018`；不新增worktree或代理，原7项用户资料/旧失败不动。PG18自有55435串行，证据D:/CP6-local-test/wave26/foundation，复用wave18构建和既有NuGet缓存。没有既有库操作或生产部署。

按精简规则复用相同输入有效证据，只因实际未覆盖风险扩大；文档仅diff/台账生成检查。每次push/PR核当前工作流及路径过滤，绝不触发Actions；正常PR合并/远端核对后才关闭Issue。授权已明确，连续窗口实际前置集成后直接START，无重复ACK或许可问题。新实证BUG入Issues，同范围修正后继续余下窗口。

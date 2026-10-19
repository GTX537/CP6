# W26-03：原认证会话、设备窗口与准确完成恢复

> 已集成：https://github.com/GTX537/CP6/pull/332 / source `4d75c61d824d934d0fe665307b229c0e8df107c6` / main `0b65bb396c1704c41a5d2ddc8740aaff06edaab2`，#331 已关闭。下文保留候选交付时点；本轮最终状态见[总记录](../../../dispatch/20261019-wave26/README.md)。

基线main `7833a58db53531181da13fd656e4837e71222e0d`，分支`codex/wave26-03-authentication-continuity-20261019`。**Ready to merge: Yes**；一次作者集中SPEC/QUALITY及发现问题的局部复查，无剩余阻断，非独立审查。[P0 #331](https://github.com/GTX537/CP6/issues/331)原4项真实PG/HTTP失败已修正。153个去重有效相关结果通过，含106原生PostgreSQL、47相关单测，0剩余失败或跳过。旧失败、复用来源及采用范围保留。

## 改变的行为

实际Program原来在租户改为强制MFA后仍接受旧access/refresh，缺原始完成回执的refresh仍可轮转，共享设备快捷切人能绕过目标用户MFA。现在`AuthenticationSessionAuthorityV1`消费持久完成回执，核当前user/tenant资格、原认证/政策/SSO配置版本、原方法/因子/client/expiry和restricted。缺证明一律重新认证，不把当前用户状态回填为历史证明。已完成会话不受初次5分钟挑战期限限制，但不重写原验证时间/强度；初次enroll唯一旧→新版本转换沿原专属回执保留。

Web/Native refresh使用Core `RotateWithAuthenticationAsync`实际Owner事务：根门→当前tenant/user→family→token，重判原证明，失效源、建立replacement、原证据及父代绑定、当前access/画像/Cookie准备和必要审计同提交。原期限上限与方法/时间/因子/版本完整保留；refresh不发起新MFA、不放行restricted、不设置FullAuth。普通旧Rotate/二阶段适配亦不能绕过原证明门。真正重用仍按原风险语义撤用户会话；并发败者、pending和本次交付拒绝保持普通失败，不能误撤其他健康登录。

所有PG当前access需要原回执并核当前政策。`access_sessions`只为已有真实receipt建立jti映射，允许模拟登录开始/结束沿原平台操作者的认证证据，另核当前目标身份，既不虚构目标用户primary，也不重建操作者证明。原jti黑名单、有效权限与S6业务最终门仍独立。旧无receipt会话须重新登录；W26-02已有Web回执可准确迁移映射，缺足够原生设备元数据的旧Native会话不回填历史而须重新认证。

## 共享设备与实际写入者

新迁移`20261019125500_AuthenticationContinuityV1`在私有认证schema增加会话父代/access映射、设备注册与设备准入策略revision、共享窗口记录；实际T_ClientDevice、T_WmsFeatureFlag、refresh/family与认证记录普通DML参加同一根门。注册禁用/恢复、软删/恢复、策略启用/恢复保留版本变化。已注册禁用/删除或平台不符始终拒绝；未启用设备管控时原本允许普通未注册Native登录的行为保留，不擅自改成全员强制设备激活。

FullAuth只由新完整非受限交互在同发行责任内设置原12小时截止并记录原完成/设备revision/policy/window；私有窗口仅在原输出完成且未拒绝时可用。quick-switch有自己的真实PIN验证和原认证版本，显式QuickSwitchAllowed缺失关闭；目标MFA、Mode2、强制SSO、锁定及改密/重认证限制拒绝。refresh和quick-switch从不续长窗口。

集中审查实际复现“截止延长后恢复原值仍能切人”，增加普通设备writer对FullAuth改变/五次失败的持久窗口失效。仅恢复字段或清计数不能复活旧窗口，必须新完整认证产生新窗口回执。错误PIN计数在根门内重载后原子保存，五次失败仍按原策略阻断。设备注册与窗口是当前Owner的准入记录，本窗不宣称新增物理设备持有证明协议或完整WMS设备业务验收。

## 输出与安全恢复

实际HTTP先注册持久`CompletionPending`会话，普通access/refresh不能消费。MVC结果过滤器先把实际formatter/JSON输出写入缓冲区，Owner `CompleteOutputAsync`再核当前资格、原receipt、源期限及设备后确认输出完成，才复制到网络。首次Web受限改密登录按原白名单保留；这一步不能误按禁止restricted refresh的规则拒绝首次登录。

准备失败在Owner事务内回滚源失效与replacement；formatter失败不会输出缓冲中的秘密，即使准确拒绝写入失败也保留持久pending隔离。确认后的取消/已知输出异常封闭准确generation及后续代次。明确注销关闭所属原登录链；其他健康浏览器/设备保留。普通terminal revoke的实际DB写者亦使该origin回执失效。必需提交或关闭结果不明返回COMPLETION_UNKNOWN，不返回raw secret或复用旧OTP/code。

网络接收仍不等于服务器提交：不能保证客户端一定收到最后一个字节。已经实际拿到有效会话可用profile核当前状态；否则用原交互绑定查安全结果并先关闭无法交付的准确attempt。新增POST `/api/auth/completion/status`、`/completion/close`及Native对应接口只返回安全状态，不返回主体资料或历史raw token。Web消费原refresh/pending Cookie并保CSRF，或独占消费客户端保留的256位`X-CP6-Authentication-Completion`秘密；Native消费原refresh或challenge且核client/device，已消费SSO grant凭原grant+PKCE verifier映射原交互。不能凭deviceId/completionGuid查询或关闭别人的会话。查不到记录返回UNAVAILABLE，不能据此证明另一个在途请求永远不会注册；关闭结果unknown仍等待确认，不提供新ID重放建议。

初次password/quick/WebSSO authorize可接受该可选客户端持有秘密；Native SSO原grant绑定可定位原完成。**完整前端跨Tab恢复UI、客户端自动保存/清除/协调该秘密尚未采用**；未保存原交互秘密且无有效Cookie/Token的旧客户端不能凭设备ID恢复初次丢失响应。本窗提供并验证Owner安全查询/关闭和实际输出边界，不冒完整客户端恢复验收，也不恢复任何历史秘密。

## 验证与复用

本地原始JSON/TRX/log在`D:/CP6-local-test/wave26/foundation`。有效153项按下列最后相应用例结果去重；不是把125/39/21/12直接相加：

| 记录 | 实际结果/作用 |
|---|---|
| continuity-red-20261019T124057Z / continuity-quick-red-20261019T124228Z | 原政策access/refresh与无回执3真实失败，加纠正真实登录入口后的quick目标MFA1真实失败；原quick匿名早拒绝不冒安全证明 |
| continuity-wired-20261019T125209Z | 原4项修正后4通过 |
| continuity-device-20261019T125957Z | 30通过；连续性、设备/窗口、原入口和受控SSO |
| continuity-recovery-20261019T130516Z | 33通过；增加实际HTTP安全查询/关闭/错误设备/CSRF |
| continuity-output-20261019T131105Z | 33通过；实际HTTP缓冲输出/持久pending采用 |
| continuity-boundaries-20261019T131953Z | 125执行，124通过1失败0跳过；发现首次restricted输出误当refresh，已修正 |
| continuity-output-recheck-20261019T132347Z | 39通过；失败用例及相关Web/Native输出、准备回滚、formatter失败/准确拒绝UNKNOWN/确认后取消、enroll原factor转换续传 |
| continuity-writers-20261019T132656Z | 21通过；实际device/feature/close writer通过pg_stat_activity证明等待Owner根门；SSO原方法/过期占位密码/因子续传及已消费grant安全关闭；相关单测 |
| continuity-window-aba-red-20261019T132822Z | 2执行，1合法路径通过1字段恢复真实失败；保留复现 |
| continuity-window-final-20261019T133202Z | 12通过；字段恢复及五次PIN失败后恢复仍拒绝、合法fresh FullAuth恢复、设备/策略ABA、实际writer并发和旧填充库前向迁移 |

153项中106原生PG：模拟26、原会话/故障/并发30、原政策16、连续性14、受控SSO10、原交互10；47相关单测覆盖现CSRF及原安全/refresh行为。旧测试的有效认证起点已改为新鲜验证密码/必要TOTP后由真实Owner门登记新会话，**不补造已有token的证明**；缺旧证明负例保留。ActualProgram正向改为实际密码登录；受控模拟测试沿真实初始回执，SSO测试真实验签/PG/控制器但IdP、权限画像/部分设备服务仍为接缝。不是企业IdP/真实设备或生产验收。原成功证据在输入未变处复用，未重跑全仓、前端build或远程门禁。

一次作者SPEC/QUALITY检查整个任务相对确认main的完整diff，覆盖原proof与当前政策区分、全相关issuer/current access/模拟、family与普通轮转/风险重用、设备实际writer和窗口ABA、输出/UNKNOWN/准确关闭、安全状态的原秘密绑定与CSRF、迁移/权限及历史记录。发现的首次restricted输出回归与窗口字段恢复问题均在本任务内修正并局部验证。新迁移仅自有PG执行，不改SQLServer/双库合同，既有警告与失败记录保留，无Actions。

## 交付边界与资源

本窗闭合W26-01/02已交付门和相关发行入口的会话/设备/失败恢复后端消费。仍不提升完整IAM组织授权、全FE跨Tab恢复、未批准SSO新身份准入UI、CRM本地Owner/跨仓采用、真实DEV/UAT/业务或生产发布。原14包/125目标/375任务/247运行NOT_RUN不变，认证门不代替业务最终提交门。

唯一原工作目录、wave18构建/NuGet与既有零复制前端依赖复用；22个本轮自有PG验证根均有停止记录且无postmaster.pid，实际API子进程按夹具释放。未新增worktree/代理、触发Actions、安装既有数据库或部署。候选正常合并及远端核对后才关闭#331；随后仍须W26总台账/Handoff/资源/检查点收尾，当前持续goal尚未结束。

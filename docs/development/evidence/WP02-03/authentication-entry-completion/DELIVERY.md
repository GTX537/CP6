# W26-02：Web / Native 交互认证完成

> 已集成：https://github.com/GTX537/CP6/pull/330 / source `9b6ca06bbc40390024bbc8919c5a6dfc05289bc3` / main `7833a58db53531181da13fd656e4837e71222e0d`，#329 已关闭。下文保留候选交付时点；本轮最终状态见[总记录](../../../dispatch/20261019-wave26/README.md)。

基线main `15b61f577e56fbff99acd98c066c49cec9d2ebd5`，分支`codex/wave26-02-authentication-entries-20261019`。**Ready to merge: Yes**，一次作者集中SPEC/QUALITY及修改部分复查无剩余阻断，非独立审查。[P0 #329](https://github.com/GTX537/CP6/issues/329)原步骤已修正。101项去重有效相关结果：63原生PostgreSQL/HTTP、33相关单测、5前端组件/continuation，0剩余失败或跳过；类型检查通过。原失败和复用范围保留。

## 改变的行为

原实际Program在密码、政策或fallback变化后仍允许旧pending enroll完成，Native challenge也能由另一设备完成，4项原生PG/HTTP真实复现。现在Web/Native密码验证前读取原政策，验证成功立即建立原认证证明；SSO返回实际验签后的原第一因子证明。所有这些初次发行、SSO exchange、pending verify/enroll走W26-01共同完成门，重新读取当前user/tenant资格、原认证/政策/SSO版本及原Web/native交互绑定。

原state/cache pending的user/tenant三字段不足以代表原认证。本次前向迁移`20261019114500_AuthenticationInteractionsV1`建立私有PG interactions、session_evidence、sso_states、consumed_totp、preparation_versions；撤销PUBLIC权限。不存raw JWT/refresh/密码/OTP，proof不能从HTTP绑定。旧记录不补造认证历史；现有登录需重新认证，安装前需要前向迁移。没有运行既有CP6DB/DEV副本或生产迁移。

pending先持久领取服务端lease，再于共同门核当前原证明及因子，成功因子消费/回执独立持久化后，最后一个门内事务完成专属enroll旧→新epoch、当前决定、token/profile准备、refresh/family注册、session证据及必要成功审计。OTP已成功消费而后续失败不会恢复消费资格。错误因子按现策略保存失败计数/锁定并允许该未消费pending重试；计数写入也核原user/tenant/认证版本。TOTP按user、密钥指纹和真实匹配时间步持久单次消费；不能用另一个challenge重放同一步。Email只在当前政策允许且verify用途消费，不能代替enroll。

准备密钥单独使用数据库revision：普通Sys_Users INSERT/UPDATE实际改变TwoFactorSecret就推进私有revision，包含替换后恢复原值。pending绑定本次准备材料及revision；初始准备仍不冒已生效2FA或无故失效原登录。enroll的旧证明完整保留，只有本次lease及准备材料才能记录新认证版本和专属transition，不以当前版本洗掉旧证明。

`IssueWithinAuthenticationAsync`是显式同事务adapter，不包裹旧Issue自提交方法冒原子性。原refresh实际期限按PG微秒规范保存并作为SessionAuthenticationEvidence的original expiry；证据保留primary方法/时间、原policy/SSO/auth版本、client、factor和enroll transition、restricted及会话发行时间。Web在事务内准备临时Response Cookie，确定提交后输出；Native保留NativeAuthResult，pending Session=null且无认证Cookie。受限Web仍按现实际白名单；现Native不支持受限会话，明确503拒绝，不返普通authenticated/FullAuth。

## SSO与前端

SSO authorize在Owner根门读取当前政策/配置快照；state在PG compare-and-consume后才换外部code，不把非原子的cache Get/Remove当唯一防重放。Web以短期HttpOnly、Lax、限定callback路径的`cp6_sso`绑定原浏览器；Native绑定原client/device/app/platform。错误绑定不消费正常客户端state。

真实SsoService保原ID Token签名、issuer、audience、lifetime、nonce验证；原稳定provider/sub映射后带出原SSO证明。缺显式Owner批准的新身份准入政策时，首次邮箱绑定/JIT返回`SSO_IDENTITY_POLICY_UNAVAILABLE`，不把AutoProvision缺省当批准。本窗没有实现或虚构租户批准策略；已绑定身份可继续。

Native callback仅交付带原证明的PKCE grant，不再提前记SsoLoginSuccess；exchange才进共同本地因子流程。SSO不是本地MFA豁免，Mode2未绑定先enroll，已启用2FA仍verify；SSO不看占位本地密码年龄，但显式MustChangePassword返回ACCOUNT_REMEDIATION_REQUIRED。Web pending只写pending/CSRF并转同站safe challenge状态，SsoLanding先去既有挑战页，不读profile或安装业务登录态。新完整SSO的profile按持久原方法处理密码年龄。

## 验证和审查

原始JSON/TRX/log位于`D:/CP6-local-test/wave26/foundation`，无远程Actions：

| 记录 | 实际结果 / 范围 |
|---|---|
| entries-red-20261019T112832Z | 4执行4失败0跳过，原Issue329实际Program/PG复现 |
| entries-first-20261019T114848Z | 编译失败：using、Cookie接口名、DI完整命名空间；修正后继续，未发生PG执行 |
| entries-wired-20261019T115321Z | 15通过；初批7实际PG/API及原grant/authorize单测 |
| entries-sso-20261019T120227Z | 测试夹具编译失败：Guid.Value和无Dispose的cache；修正，仅测试代码 |
| entries-sso-bound-20261019T120547Z | 15通过；原生PG SSO真实签名/政策/新身份关闭/绑定/重放及历史控制器 |
| entries-final-20261019T121529Z | 48通过；Web SSO浏览器绑定、原生SSO矩阵、TOTP跨challenge单次、错误因子重试/并发enroll/审计回滚、原相关2FA/认证 |
| entries-boundaries-20261019T122331Z | 62通过；新准备revision恢复旧值、消费先提交、W25实际Web/native/自助/模拟/轮转边界、W26-01原政策竞争和已填旧库升级/model snapshot无漂移 |
| entries-review-20261019T122657Z | 20通过；只复查新交互与SSO受影响部分，含Native受限拒绝、实际refresh行与证据original expiry一致 |
| entries-web-tests.log | 锁定Vitest4.1.11：5通过，4个SSO landing行为和1个既有OIDC continuation |
| entries-web-typecheck.log | vue-tsc --build退出0；没有全前端测试或生产build |

按最新相应用例结果去重，96个.NET有效通过（63原生PG，33相关单测）加5前端，共101；没有把所有结果冒成最后一次运行。实际API使用完整Program和自有PG；SSO使用真实SsoService签名验证、PG和Web/Native控制器，但IdP、权限画像/设备服务由受控测试接缝提供，**不是外部企业IdP、已注册真实设备或生产SSO验收**。前端复用与当前lock规范化内容相同的私有依赖，Node20.19.0，Vitest4.1.11；未重装依赖。

集中作者SPEC/QUALITY覆盖当前政策与原proof区分、所有本窗入口、持久单次与原client绑定、准备ABA、错误因子锁定、enroll唯一版本转换、事务输出/必要审计、SSO原真实验证和新身份拒绝、受限路由、PG升级/权限及秘密范围。发现的TOTP重复、准备恢复旧值及期限差异已在本任务内修正并定向复查。没有新独立代理或逐文件审查。既有NU190x/静态告警保留，未运行漏洞扫描。

## 采用边界与后续

本窗交付初次交互发行/因子与SSO的共同门和原会话证据。**W26-03仍必须完成**：旧refresh adapter全面采用原证据与当前policy/device、原方法在每次轮转连续传递、缺旧证明重新认证、当前access政策消费、设备writer及FullAuth共同边界、quick-switch限制、精确完成隔离和安全状态/恢复采用。当前有receipt的access会拒已标记拒绝及身份/版本不符；不能把缺receipt仍在历史消费路径的旧token冒完整政策已采用。

Web临时Cookie和Native DTO在commit后返回，只能证明服务端提交与输出准备；不能证明客户端网络接收。已消费pending而完成失败会关闭该lease，不能重放；完整safe CompletionStatus、准确Owner关闭未交付attempt、formatter/网络UNKNOWN及FE跨Tab恢复按W26-03/对应采用范围继续，不提供历史raw token恢复。MarkFullAuthentication现已加入发行事务，但全设备writer、当前资格和原12小时窗口约束由W26-03专项完成。本窗不关闭持续goal，也不提升完整IAM组织授权、SSO准入批准UI、CRM/Owner或真实业务验收。

原目录与wave18 build/NuGet复用；全部自有PG已停止。临时frontend node_modules是指向已核对私有依赖的零复制junction；删除命令被工具策略拒绝后保留供后续复用，并要求分支切换核唯一目标/当前lock，只放行这一已知ignored路径。没有删除资料、缓存、旧失败或其他资源；没有新增worktree/代理/Actions、既有环境或生产操作。

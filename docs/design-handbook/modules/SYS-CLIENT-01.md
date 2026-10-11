# SYS-CLIENT-01 · 客户端设备、认证与升级

整理状态：`core_semantics_consolidated`。当前静态规范展开；候选字段/行为不冒现代码或运行已采用。

## 1. 业务目的、操作者与边界

Windows/Android保持原技术栈。设备管理员发行票据/禁用/改scope，设备私钥证明设备，人类完整认证或获准quick-switch证明用户，业务Owner另判业务动作，发布Owner管版本政策。bootstrap、设备Active、人类session、业务许可四轴独立；心跳不是任务完成。[原文B:5](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:5) [原文B:11](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:11) [原文B:102](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:102)

CLIENT只消费IAM v0.2 AuthenticationCompletionGateV1，不自算认证政策。旧源码Wms/ProductionMoveEnabled/WM-V2命名不解除V2/T06冻结，旧flag兼容不能豁免当前tenant/user/policy最终认证门。[原文B:5](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:5) [原文B:67](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:67) [原文B:71](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:71)

## 2. 当前正文与接受范围

正文v0.2，SHA256 `e86bc515d64622afed314aa67776fa5b26e3c35e234ad1c0ffaa4d067da1f26d`，128行，Library版本0；IAM对应v0.2 Library版本1，不能混。当前S5十稿中本册仅SPEC04认证修订，另四SPEC原字节复用；22AC/13DEV原ID未增。[原文A:175](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/922445a61163701d__UA-20261008-S5-SYS-CLIENT-01-STATIC-MD02.json:175) [原文B:7](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:7) [原文R:21](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e3/e3522cf5a9c30a6a__S5_v0.2_认证双根独立复审与最小问题报告_20261008.txt:21)

delegate接受非亲签，原02:18:29Z逐字文本未重建，04:09:04Z恢复确认独立。作者STOPPED保历史；22AC NOT_RUN、实施false、设备/运行/Owner采用UNPROVEN。[原文A:8](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/922445a61163701d__UA-20261008-S5-SYS-CLIENT-01-STATIC-MD02.json:8) [原文A:328](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/922445a61163701d__UA-20261008-S5-SYS-CLIENT-01-STATIC-MD02.json:328)

## 3. 设备、票据、认证与发布对象

|对象|关键字段/状态|约束|
|---|---|---|
|设备|tenant内稳定DeviceId；Platform Windows/Android；Mode Shared/Personal；WarehouseCd/AreaCd；Status Active/Disabled；RowVersion|ONLINE/OFFLINE/UNKNOWN是心跳派生，不第二主状态；CurrentTaskNo只是遥测。[原文B:13](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:13)|
|激活票据/输入|hash+scope+expires；ValidMinutes2..120；TenantCode/Token/DeviceId≤128/PublicKey≤2048/AppVersion|raw仅发行一次；candidate activationOperationId/ticketRef/expectedTicketVersion/deviceAttestationPolicyVersion；服务端不收private key。[原文B:19](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:19) [原文B:21](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:21)|
|ActivatedClientDeviceDto|DeviceId/TenantCode/DeviceMode/WarehouseCd/AreaCd/ActivatedAt|激活不是登录或业务许可。[原文B:23](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:23)|
|管理命令/View候选|deviceRef/expectedRowVersion/operationId/reason/status-mode-warehouse-area白名单；policyVersion/revocationStatus/pendingTaskRefs/requiredNextAuth/lastObservedAt|DeviceId/TenantId/publicKey不普通改；实际管理HTTP映射未全取得。[原文B:35](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:35) [原文B:39](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:39)|
|HeartbeatObservation候选|deviceProofStatus/transportReceivedAtUtc/declaredTelemetry/authenticatedUserRef/deviceRowVersion/businessTaskRefUnverified|声明task不等Owner receipt；签名仅实际覆盖字段。[原文B:49](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:49)|
|NativeCompletionView候选|completionRef/state/safeReasonCode/nextAction/policyVersion/deviceReadiness/factorRequirement/originalInteractionStatus|真实primary/tenant/user/policy/evidence由server持，不信前端。[原文B:75](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:75)|
|ClientReadinessView候选|bootstrapCompatibility/deviceState/authenticationState/tenantBinding/policyReady/languageReady/blockedCapabilities +safe completion/适配能力|先SESSION_ALLOWED再取业务权。[原文B:104](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:104)|
|ReleasePolicy候选|platform/channel/releaseVersion/minimumSupported/api-schemaCompatibility/languageManifest/artifactRef/expectedHash/publisherSignatureRef/releaseApprovalRef/effectiveFrom/rollbackPolicy/mandatoryReason|URL/hash存在不等已可信/安装成功。[原文B:114](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:114) [原文B:116](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:116)|

## 4. 激活、管理、心跳与认证状态推进

激活：当前管理员provision及tenant/warehouse scope→发行精确用途期限票据→设备绑定自身身份→完整公钥格式/算法验证→票据未用/未过期/tenantCode启用/platform吻合/设备不重复→原子consume+register+目标audit/receipt→Activated DTO。旧consume/register同SaveChanges已读，候选增强不要全冒现状。[原文B:19](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:19) [原文B:21](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:21) [原文B:23](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:23)

设备管理：当前Ref/RowVersion→影响预览→源/目标warehouse与manage/disable→撤权共同fence+CAS→状态/refresh处置/审计→authority与传播分开。Active→Disabled旧实现清FullAuthExpiresAt/CurrentUser并同SaveChanges RevokeAllForDevice；不证明已发access/离线队列即时停。恢复需新完整认证，Shared→Personal清共享资格，未完任务由原WMS/Owner定安全切换点，不设备页自动迁移。[原文B:33](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:33) [原文B:35](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:35) [原文B:37](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:37)

心跳：当前tenant/user→注册设备→Active/proof/nonce/时间新鲜性→保存真实观察→server time/重认证升级提示；失败不刷新成功LastSeenAt。不能改warehouse/授权/业务command；时差不自动放宽窗口。[原文B:47](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:47) [原文B:49](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:49) [原文B:51](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:51)

native认证入口固定如下（政策唯一来自IAM）：[原文B:71](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:71) [原文B:77](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:77)

|入口|唯一结果|FullAuth|
|---|---|---|
|密码login|第一因子后当前门；enrollmentRequired/twoFactorRequired仅ChallengeToken，Session空|仅完整新交互SESSION_ALLOWED commit可开。[原文B:79](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:79)|
|SSO callback|只准备原client绑定第一因子/grant，不首页已登录|不开/不延长。[原文B:80](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:80)|
|SSO exchange|ConsumeGrant后同CompletionContext；需本地因子返回pending，旧客户端不支持则未就绪/升级|pending/拒绝/未知不开。[原文B:81](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:81)|
|pending enroll|准确准备/原版本；本交互旧→新epoch登记再最终门；绑定已commit而session失败后新登录verify|不因enroll保存开。[原文B:82](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:82)|
|pending verify|当前tenant/user/锁定/policy/primary/device重核，一次性完成|仅完整最终成功。[原文B:83](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:83)|
|refresh|原SessionAuthenticationEvidence+当前门；缺旧证据/需因子/改密REAUTH；成功仍TokenSessionDto|不新开/延长。[原文B:84](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:84)|
|quick-switch|Shared原窗口/失败<5/明示QuickSwitchAllowed，且目标无2FA、Mode0/1、无强制SSO/改密限制；否则完整登录|不借前人MFA，永不延长。[原文B:85](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:85)|
|logout/取消|准确session/interaction撤销，unknown保真，不自动重登|UI关闭不证明server已撤。[原文B:86](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:86)|

SSO只第一因子，外部acr/amr不豁免本地2FA；Mode2未绑定enroll，任意Mode已绑定verify，Mode0/1未绑定才无挑战，Mode未知不默认0。首次SSO映射按IAM五分支，不因初绑拒就创建备用账号；identity已绑定/JIT成功不是session成功。[原文B:71](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:71) [原文B:73](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:73)

## 5. API、原合同与客户端兼容

POST `/api/client/devices/activate`匿名因使用激活票据认证，不等无身份；POST `/api/client/devices/heartbeat`需Authorize。管理员具体HTTP route没有完整来源，留Required，不虚构URL。[原文B:19](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:19) [原文B:25](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:25) [原文B:47](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:47)

现 `/api/client-auth/login`、`2fa/setup/enroll/verify/email-otp`、`sso/start/callback/exchange`、`refresh/logout`、`quick-switch`沿用。旧SSO exchange直接IssueSession与现refresh先Rotate后device门不能冒统一当前门；候选保持正常成功DTO，扩pending/受限状态须能力协商，不支持就明确拒绝/升级，不fallback authenticated。[原文B:63](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:63) [原文B:65](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:65) [原文B:81](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:81) [原文B:106](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:106)

GET `/api/client/bootstrap`匿名platform/currentVersion；回ApiVersion1/ServerUtc/Latest/Minimum/UpgradeRequired/DownloadUrl/Sha256/LanguageManifestVersion，仅公开配置无tenant秘密/授权。RequiredGate声明新设备依赖能力，旧ProductionMoveEnabled return兼容不能伪证所有tenant强制激活。[原文B:67](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:67) [原文B:102](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:102)

## 6. 幂等、会话提交与UNKNOWN恢复

激活unknown查原activationOperation/票据已消费关联/设备，不换DeviceId循环注册，不再次返回raw。设备管理CAS冲突WM-CONFLICT-ROW-VERSION，unknown查版本，不暗恢复；heartbeat cache先查再写nonce缺跨实例原子证明，必须补实际实现证据。[原文B:23](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:23) [原文B:33](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:33) [原文B:49](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:49)

目标refresh在原Main session事务先持IAM门再family/token互斥，原子轮转后输出；旧adapter先Rotate后拒时，禁止新access/refresh和FullAuth，撤准确replacement并封闭attempt；处置unknown隔离准确session代次，不复活旧token、不误全撤别正常family。旧无可信认证证据强制完整重新登录，不能现在回填历史MFA。[原文B:92](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:92)

一次性code/state/grant/pending/OTP不能重放；grant已消费只从原client交互查safe状态，无法安全继续失效原交互新SSO；identity/JIT已提交新交互用tenant+issuer+sub复用原人，enroll已提交保真实绑定后verify。session响应unknown：已有有效凭据先profile当前核，无新凭据则Owner先封闭无法交付session/attempt再重认证；封闭未知继续隔离，不换ID绕开。[原文B:94](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:94) [原文B:96](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:96)

原12小时窗口只在当前完整新交互SESSION_ALLOWED、设备Active、session/device协调commit后开启；refresh/PIN/SSO换码/pending/受限改密不延长，当前tenant/device/policy拒则剩余窗口也不可用。重新登录后的业务UNKNOWN仍先查原operation，认证重启不变业务键。[原文B:96](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:96) [原文B:98](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:98)

## 7. 权限、秘密与业务范围

secret/ticket/token/PIN/private key不普通日志/URL/诊断；private key留设备安全存储。更新DeviceId/TenantId/key是另安全身份动作，warehouse源目标scope及在途业务Owner门独立。设备Active/用户登录不授所有仓库；心跳task声明不是签名业务结果。[原文B:15](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:15) [原文B:21](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:21) [原文B:35](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:35) [原文B:47](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:47)

Main认证相关writer共同门缺真实采用时AUTH_SESSION_AUTHORITY_UNAVAILABLE禁新session，客户端cache/profile不能放行。SSO不忽略MustChangePassword，SSO-only补救不强迫任意本地密码；restricted改密仅IAM实际最小路由，不能普通业务/FullAuth。无网络按Owner离线政策保存意图，不凭最后心跳绕业务当前门。[原文B:53](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:53) [原文B:88](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:88) [原文B:106](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:106)

## 8. 页面、升级流程与服务端检查

激活显范围不直接进业务；管理页Active不等在线，Disabled不等擦设备，CAS保草稿差异；心跳显示最后真实成功，旧queue heartbeat不当前在线。登出/切人清旧人资料/遥测并隔离草稿，不能给下个人自动执行；晚响应按completion+tenant+client+user丢弃。[原文B:25](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:25) [原文B:39](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:39) [原文B:53](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:53) [原文B:98](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:98)

readiness顺序：公开引导兼容→必要设备→第一因子→本地因子/受限补救→SESSION_ALLOWED→当前业务权限。UNAVAILABLE显示未就绪不退无MFA，REAUTH完整认证，不无限refresh/重放OTP。[原文B:104](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:104) [原文B:106](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:106)

升级：policy校格式、latest≥minimum、来源签名/包/compatibility→download stage→确切digest/签名/platform→用户批准或已授权发布机制安装→重启实际版本再readiness。下载失败不卸安全可用旧版；低于minimum禁不兼容新业务，但保安全logout/状态/原operation查询/升级。rollback只获批仍兼容且不低于安全minimum版，不回撤回版；原离线意图保键、重新核schema/当前门，不盲重发。[原文B:116](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:116) [原文B:118](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:118)

页面分建议更新/必须更新/配置未就绪/下载错/验证错/安装待确认/已升级待验证，hash缺/错无“忽略继续”自动执行；点击下载不绿勾，实际新版本观察才成功。[原文B:120](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:120)

## 9. 固定实现差距及顺序

固定Main90c871...ClientDeviceService/Controllers/Auth/Bootstrap和Client.Core引导；管理员路由/完整warehouse校验、公钥算法、跨实例nonce、认证全writer、原版本解析边界有明确差距。旧非法currentVersion Compare=-1、非法minimum=1不能当policy合法；URL/hash配置不证明签名/审批/安装rollback。[原文B:5](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:5) [原文B:21](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:21) [原文B:33](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:33) [原文B:49](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:49) [原文B:114](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:114)

13任务：①票据、公钥、operation；②实际管理权限scope和禁用fence；③心跳安全观察；④IAM唯一完成门和所有native入口适配；⑤轮转后置拒绝/FullAuth/切人草稿；⑥readiness、SSO pending/受限/已消费交互；⑦发布policy、包验证、原意图/rollback与页面。全部PLANNED。[原文B:29](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:29) [原文B:43](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:43) [原文B:57](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:57) [原文B:110](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:110) [原文B:124](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:124)

## 10. 验收场景与状态

22AC NOT_RUN：CL01票据scope/原子consume+register、公钥/到期拒、原键unknown、激活不登录；CL02CAS/scope、禁用refresh与效果门分证、恢复新认证、切仓不移业务；CL03proof/签名覆盖、跨实例nonce证据、task不完成、离线/晚响应；CL04四轴与全入口、SSO本地2FA、flag不豁免、refresh精确隔离、FullAuth不延长/不借MFA、JIT/session分段和unknown原键；CL05policy合法、digest签名、mandatory仍可原operation查、rollback原键、实际版本核实。[原文B:27](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:27) [原文B:41](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:41) [原文B:55](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:55) [原文B:108](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:108) [原文B:122](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:122)

## 11. 未完成与真实采用边界

实际管理员route/动作scope、公钥/nonce保证、IAM共同writer和policy、restricted白名单、SSO-pending客户端能力、发布签名/批准/安装回退仍UNPROVEN。IAM规则已唯一，CLIENT不能为旧客户端另选弱认证；缺能力就对应未就绪。没有操作真实设备/密钥/session或安装包。[原文B:25](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:25) [原文B:49](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:49) [原文B:88](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:88) [原文B:104](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:104) [原文B:114](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:114) [原文B:128](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:128)

## 12. 原件与阅读覆盖

本次全文1–70、71–128；IAM全文1–375按六段补读，CLIENT只引用其同一算法；5SPEC/22AC/13任务实读。S5复核1–128全文；各接受关键完整JSON键读。阅读不等登录或测试，未运行设备/升级/认证请求、项目脚本、构建/Actions。[原文B:1](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e8/e86bc515d64622af__S5_SYS-CLIENT-01_完整设计候选_v0.2.md:1) [原文R:1](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e3/e3522cf5a9c30a6a__S5_v0.2_认证双根独立复审与最小问题报告_20261008.txt:1) [原文A:8](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/92/922445a61163701d__UA-20261008-S5-SYS-CLIENT-01-STATIC-MD02.json:8)

共享合同见 [platform-engineering-contracts.json](D:/CP6/docs/CP6_开发设计文档_20261010/contracts/platform-engineering-contracts.json)，阅读范围见 [platform-engineering-reading.json](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)。

**当前必要材料归并状态。** 本册为 `required_materials_consolidated`：选定正文、规范附件及准确接受/复核范围已按直接实读、结构化语义或精确复用归并。身份清单核对不等底层源码全文阅读；历史草稿、候选实现和Office/HTML视觉未核范围仍单列。可复查[逐件阅读与处置](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)及[本组闭包台账](D:/CP6/docs/CP6_开发设计文档_20261010/worklogs/platform-engineering-progress.json)。当前必需规范未读清单为空；具体Owner采用、参数/物理接口、真实运行和本节已有来源缺口不因整理闭包而改变。

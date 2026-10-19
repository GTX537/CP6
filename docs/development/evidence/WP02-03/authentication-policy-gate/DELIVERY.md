# W26-01：认证政策版本与 PostgreSQL 完成门

> 已集成：https://github.com/GTX537/CP6/pull/328 / source `2a093ba3386cb8d9f7b74ea99749215ebcbfd5a0` / main `15b61f577e56fbff99acd98c066c49cec9d2ebd5`，#327 已关闭。下文保留候选交付时点；本轮最终状态见[总记录](../../../dispatch/20261019-wave26/README.md)。

基线main `afc94bf18e447fdc819b88cb4fc048d9d9eb6258`，分支`codex/wave26-01-authentication-policy-20261019`。**Ready to merge: Yes**；[P1 #327](https://github.com/GTX537/CP6/issues/327)已修正。一次作者集中SPEC/QUALITY无剩余阻断，非独立审查。187去重有效相关结果通过，99原生PG/HTTP、88相关单测/历史模型元数据，0剩余失败/跳过。所有原失败保留。

## 改变的行为

原生PG复现租户不存在、Mode=-1、Mode=3时TwoFactorService不拒绝，未知政策可流入无需挑战分支。现在显式AUTH_POLICY_UNAVAILABLE；合法0/1/2和已启用用户的强制本地因子语义保留。

新增Main AuthenticationPolicyServiceV1、AuthenticationProofV1/FactorEvidenceV1和AuthenticationCompletionGateV1。政策读取实际tenant Mode、nullable QuickSwitchAllowed及SSO配置，并绑定本进程不可变Security.Password/TwoFactor配置快照；缺失/非法来源关闭。proof由受控已验证第一因子adapter建立，绑定completion、tenant/user、Web/native类型与交互/设备、原认证方式/时间/期限、政策/认证/SSO配置版本及非秘密证据引用，禁止HTTP直接绑定或用当前数据补旧证明。

共同门要求PG、自有干净事务和准确tenant上下文；持现有平台root到回调必要写入与最终commit，当前user/tenant启用、锁定和实际到期、原认证版本、当前政策、强制SSO/fallback都在门内核对。等待后不复用旧tracked user。回调输出仅在ExecuteAsync确定commit后返回；取消/失败回滚并还原EF跟踪，commit/rollback不明使用AUTHENTICATION_COMPLETION_OUTCOME_UNKNOWN，不自动重试或重放秘密。保存后再按实际墙钟检查交互/tenant期限。

决策严格区分SessionAllowed、EnrollmentRequired、SecondFactorPending、PasswordChangeRequired及拒绝/重新认证/不可用：SSO不替本地因子，Mode2未绑定需入会；factor证据同completion/tenant/user/client/current认证版本且时效有效。SSO忽略占位本地密码年龄，但显式MustChangePassword返回ACCOUNT_REMEDIATION_REQUIRED。quick-switch需明确允许，目标启用MFA、Mode2、强制SSO或改密/本地凭据过期均不可继续；refresh决策不得启动新因子或延续受限状态。

**本窗交付的是受控服务和并发基础，尚未宣称所有入口已采用。** 初次发行/pending/SSO运输与一次性完成回执由W26-02实施；refresh原完整证据、当前access政策及设备/FullAuth采用由W26-03接续。当前门的callback不是公开HTTP端点，也不能把一个政策决定当成可重放登录凭据。

## 数据库版本与真实 writer 边界

前向迁移20261019111000_AuthenticationPolicyGateV1增加Sys_Tenants.AuthenticationPolicyRevision、nullable QuickSwitchAllowed及Sys_Users.CredentialRevision。旧QuickSwitchAllowed保持null，不能当批准。凭据版本v3包含数据库生成的CredentialRevision和AllowPasswordFallback，延续v2原凭据字段及PG微秒时间规则。版本故意不兼容旧access/refresh/family；旧记录未回填、未冒原MFA证明。

| 当前表/实际入口 | 同门/修订方式 | 本窗证据范围 |
|---|---|---|
| Sys_Users：登录成功/失败、改密/reset、2FA、External/fallback/PIN、用户增删启停 | INSERT/UPDATE/DELETE/TRUNCATE statement trigger先获取共享平台root；实际凭据/启停变化由row trigger生成新CredentialRevision | 原生DML双向竞争、凭据恢复旧值、2FA合法准备/激活、实际Program登录/refresh/模拟 |
| Sys_Tenants：租户管理、2FA政策及明确QuickSwitchAllowed | 同root；Mode/QuickSwitch/Enable/ExpireDate真实变化生成新政策修订 | 变更又改回、tenant writer竞争、原租户actor最终门 |
| Sys_TenantSsoConfigs：Upsert/删除/原生写入 | 同root；insert/update/delete及truncate推进受影响tenant政策修订 | 原生insert竞争，client变更又改回仍拒旧proof |
| Refresh issue/rotate/complete | 先获取新认证root，再沿原tenant→user→family→token行锁 | W25轮转竞争/当前证明/故障隔离和HTTP兼容结果 |
| 平台管理员/模拟 | 复用同一root；不创建相反锁序或第二平台权威 | 原平台末名、原生撤权竞争、真实HTTP及模拟审计/Cookie组合 |
| 进程Security配置 | 服务构造时不可变快照，准确内容纳入policy hash；变化后旧proof不匹配 | 配置变化/非法配置拒绝；没有声称多节点部署配置已统一 |

root采取写revision，重复读/Serializable旧快照不能穿过已提交变更，实际40001拒绝且无自动重试。未启用2FA的准备secret不是生效凭据，不推进CredentialRevision以保自助setup→enroll；pending准备材料专属绑定由W26-02接续。数据库触发器覆盖这些表的普通DML，不宣称超级用户禁触发器、恢复/迁移工具或跨库业务Owner已受本门保护。受限数据库角色无内部函数权限时写入拒绝，不静默绕过。DBA/环境权限安装仍属正式采用验证。

## 验证、故障与复用

原始JSON/TRX/log均在`D:/CP6-local-test/wave26/foundation`：

- policy-red-20261019T110330Z：3执行/3失败/0跳过，实际缺失/非法政策未拒绝。
- policy-gate-20261019T111040Z：70通过/1测试断言不符；PG确已40001拒绝，但EF包装为InvalidOperationException，改查GetBaseException后复验。
- policy-boundaries-20261019T111312Z及111332Z：测试代码编译失败记录保留（旧库夹具变量名/using和泛型推断）；没有数据库执行。
- policy-boundaries-20261019T111401Z：66通过/1夹具失败。直接SQL启用2FA后模拟“新登录”仍用旧CredentialRevision；仅让新登录读取真实持久revision，后续因子变化拒绝断言保留。
- policy-final-20261019T111648Z：51通过/1旧平台升级夹具用当前模型写旧schema失败；切换准确旧模型，不提前补新列。新政策已填库升级、model snapshot无漂移、旧proof不回填、Down拒绝全部通过。
- policy-review-20261019T111902Z：30通过，修正后的旧平台已填库升级及29项原CRM/OIDC桥回归通过。

以上按最新相应用例取有效结果，去重187/99原生。受影响的W25实际Program Web/native、pending/restricted、自助2FA、模拟往返与原source过期/取消/精确隔离均本轮相关执行；未变模型/早先相关通过直接复用，不把所有结果冒成最后一次新执行。历史SQL模型仅元数据构造，无SQL Server运行要求。

作者集中审查最终diff，覆盖准确政策来源、server proof、当前态与原证明分离、版本ABA、root/行锁顺序、原始错误与UNKNOWN、跟踪恢复、合法2FA准备、数据库原语及旧数据升级、相关原平台边界。未发现阻断。既有NU190x/静态告警保留，未执行漏洞扫描。

## 采用与剩余范围

安装新PG应用前须先执行前向迁移并重新登录；没有操作本机既有CP6DB/DEV PG副本或任何生产环境。五次自有PG均停止，无postmaster.pid；实际API由夹具释放，D余约41.06GiB。原目录/build/NuGet复用，零新worktree/代理/Actions，原资料/失败未删。

W26-02/03及总收尾仍待完成。全入口一次性完成、SSO身份准入/映射政策、完整安全状态回查及FE跨Tab、统一family恢复、设备FullAuth、完整IAM组织授权、CRM/Owner采用和真实业务未提升；14包/125目标/375任务/247运行NOT_RUN不变。本窗正常合并/远端回读后关闭#327并连续推进下一窗口，不能结束总goal。

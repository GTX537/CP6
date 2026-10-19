# 第二十五轮：三个连续公共基础交付

> W25-02候选完成：[DELIVERY](../../evidence/WP02-03/impersonation-final-gate/DELIVERY.md)，#321及锁等待过期边界已修正，83有效相关通过/49原生；一次作者审查无阻断。待正常合并，随后W25-03，长目标继续。

> W25-02实证P0 #321：5项实际HTTP RED。为保原备注容量并明确actual/effective/session，登记可空PG jsonb审计字段及前向迁移/快照；仅自有已填隔离库验证，不操作既有库。原认证版本/tenant/会话引用通过已认证签名来源传递，不能回填旧证明。

> W25-01已通过[PR320](https://github.com/GTX537/CP6/pull/320)合入远端`1974bcb4ee9f179c6539dd032b09329037cd5002`，#319关闭。W25-02已真实START，复用原目录；W25-03和总收尾仍待执行。

> W25-01候选：[DELIVERY](../../evidence/WP02-04/current-actor-final-gate/DELIVERY.md)，#319原步骤修正、79相关通过/60原生PG。作者集中审查通过，待正常合并；W25-02/03尚未开始，长目标继续。

当前聊天单总控长任务，使用executing-plans顺序执行；用户明确不创建其他对话/代理，复用原工作目录。目标起点2026-10-19T08:54:40Z，24小时检查点2026-10-20T08:54:40Z；预计**代理实际执行5–9小时**，不是用户工时。三个实施窗口及最终收尾全部完成才关闭goal，不能只交付第一项。

基线main/origin/main `93738f84265d3199e8da2129436f23669da8a5e7`，W24已完成，当前Issue0。PostgreSQL唯一，原14包/125目标/375任务/247运行NOT_RUN不因本计划提升。

| 顺序 | 交付 | 预计代理耗时 |
|---|---|---|
| W25-01 | 租户创建/修改/暂停/恢复的当前平台actor共同事务，必要审计保原子 | 45–90分钟 |
| W25-02 | 模拟进入/退出当前真身及目标检查、必要审计、提交后Cookie输出 | 1–2小时 |
| W25-03 | 访问/refresh原认证状态绑定，凭据变化、缺证据、轮转竞争与合法类型兼容 | 2–4小时 |
| 收尾 | 各项相关验证/集中审查/正常集成，组合风险核对与台账/Handoff/资源/检查点 | 1–2小时 |

## W25-01：租户管理共同事务

修改TenantAdminService/TenantController；复用PlatformAdminMutationV1及PlatformAdminAuthorization，必要泛型结果支持限定在原helper。测试TenantManagementCurrentActorPostgreSqlTests，复用/适配TenantManagementPostgresFixture与TenantManagementAuditPostgreSqlTests。单份报告WP02-04/current-actor-final-gate/DELIVERY.md。

- [ ] 实际HTTP filter之后撤权/停用/删除actor，创建/更新/暂停/恢复不得产生效果，先RED并登记实际P0。
- [ ] PostgreSQL根→actor当前检查与行锁→目标当前读取→一次业务/必要审计，持有到commit；调用方事务savepoint、取消、跟踪恢复沿W23/24现有实现。合法临时密码只在成功后返回；actor审计来自核实持久主体。非PG历史实现保留，不加双库要求。
- [ ] 双向竞争、caller rollback/失败审计、合法跨tenant管理和原错误边界定向验证；一次作者SPEC/QUALITY、正常PR/远端核对与Issue闭环。

## W25-02：模拟进入退出

前置W25-01正常集成。核对完整SYS-IAM-01模拟接受段和实际ImpersonationService/Controller、JWT/Cookie/blacklist合同，在同一原目录最新main分支实施。允许路径按台账；开工时按实际依赖细化，不能并行改共享门。单份报告WP02-03/impersonation-final-gate/DELIVERY.md。

- [ ] 原生复现入口过滤器与模拟服务之间当前真身/目标变化、审计失败、失败后Cookie或blacklist半完成边界；不把未运行推测写成BUG通过。
- [ ] 在PG既有平台门内核实actor和目标/tenant，必要审计与可事务化状态一致；输出Cookie/响应在确定提交后。外部blacklist非同事务时明确失败关闭及未知结果，不伪造原子性或重放一次性凭据。
- [ ] 保留模拟期间平台权挂起、真身/目标tenant各自绑定、退出重新核实平台权、合法无Name输入及既有cookie形态；为后续认证版本校验提供真实发行来源，不用当前状态回填旧认证证明。
- [ ] 相关实际HTTP/PG正反向、取消/并发/故障验证，一次作者审查和正常集成。

## W25-03：原认证状态连续性

前置W25-02集成。精读S5 SYS-IAM-01 §6/§9、实际Web/Native/AuthSessionService、RefreshTokenService、会话模型及当前JWT消费；已有CRM family保护直接复用，不能重做冒新交付。当前无family分支不能冒已有认证版本保证；旧无足够证据按已接受合同重新认证，禁止以当前user信息补造历史MFA。单份报告WP02-03/session-authentication-current-state/DELIVERY.md。

- [ ] 固定发行/消费入口与既有合法输入；实际验证凭据/AuthenticationEpoch变化、旧refresh及并发轮转问题，登记实证P0。
- [ ] 补真实发行时的认证状态绑定并在访问/轮转消费时校验，兼容Web/Native/模拟/合法pending和restricted路由；缺证据不洗成当前版本。数据库字段若确需新增，以PG前向迁移和已填隔离副本升级证据交付，既有库不安装。
- [ ] 维持token-family/device/subject/tenant各自身份，轮转条件更新与replacement同事务，拒绝不返回raw token/正常Cookie，不因普通后置拒绝全撤其他会话；未知提交不自动重放。
- [ ] 整个实际改变范围一次集中SPEC/QUALITY，当前入口/版本/并发及受影响原native/Web验证；未落实的完整政策/SSO/2FA证据、Owner写门明确保持未完成，不能宣称整个IAM完成。

## 执行与存储规则

每项从当时最新main新建codex分支，唯一目录D:/CP6-worktrees/wave17-dispatch-20261018；只有当前窗口持有Main:identity-authority，后续等前置实际集成直接开工，无重复ACK。共享状态四文件由当前COORD同步，不新增代理。任务各一报告、一作者审查；提交或SHA变化不重跑相同验证。

PG18自有55435串行，证据D:/CP6-local-test/wave25/foundation；构建D:/CP6-local-test/wave18/w18-01/artifacts和NuGet缓存复用。保留原7项用户资料及历史失败记录，任何清理前核归属/远端和可重建范围。无新worktree、Actions、既有数据库操作或生产部署。每次push/PR核工作流和路径过滤，正常合并/远端核对后才关闭Issue/窗口。发现同范围P0优先修正，连续推进余下窗口，不因第一项成功结束目标。

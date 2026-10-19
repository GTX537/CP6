# 第二十七轮：浏览器认证恢复与SSO首次身份准入实施计划

执行者：当前COORD聊天，使用executing-plans；不派代理、不新建物理工作树。目标：闭合S5 SYS-IAM-01 v0.2 §6.5、§7.1/7.3的前端恢复与首次身份准入，必要的当前权限/角色引用和真实writer共同门随对应窗口完成。架构沿W26 PostgreSQL认证root、原proof、完成回执与IdentityChange，不另造身份权威。技术为Vue/TypeScript、ASP.NET Core/EF、PostgreSQL18及本地定向验证。

最新确认main `82a3440137eddf71bcc75fd1e5e39ea440207b52`；第26轮三窗已闭环，Handoff `6de4f959`已回读，活动Issue0、无在跑Actions。原S5设计位于根目录用户归档（只读），§6.5规定UNKNOWN需原交互查询/准确封闭，§7.1规定显式准入和五分支。此计划不把原DEV/AC标NOT_RUN变成通过，也不预批准任何真实tenant/issuer/role。

登记2026-10-19T14:00:06.813957+00:00，24小时检查点2026-10-20T14:00:06.813957+00:00。估计**本代理实际4–7小时**，故障余量约10小时；上轮实际2小时58分钟。估时包含代码、相关验证、任务级审查和正常合并，提前完成不空等。

| 顺序 | 可交付范围 | 代理耗时 |
|---|---|---|
| W27-01 | 统一前端认证交互与跨Tab/迟到响应/UNKNOWN安全恢复 | 75–120分钟 |
| W27-02 | 首次身份准入policy、明确批准UI、配置CAS/当前actor/强审计 | 60–100分钟 |
| W27-03 | 初绑/JIT五分支、角色引用/并发唯一性、同PG映射回执/审计/Outbox | 60–100分钟 |
| 集成收尾 | 受影响组合验证、逐项正常合并、台账/Handoff/资源/检查点 | 30–45分钟 |

## W27-01 浏览器认证完成恢复

文件：新增cp6.web/src/auth/集中协调与必要UI，采用api/http.ts、api/sys/auth.ts、LoginView、SSO landing、2FA challenge/enroll、App/router与登出入口。必要后端修改限定AuthenticationCompletionRecoveryV1/AuthenticationRecoveryBindingV1、AuthController/完成服务、相关PG迁移/HTTP测试；只为安全恢复绑定/输出状态所需，不重写W26主门。

- [ ] 先用现HTTP拦截器/实际页面复现每Tab独立refresh、401重放写请求、旧响应复活已退出状态及初始响应丢失没有交互凭据的问题；实证BUG按Issues记录，原有效后端证据复用。
- [ ] 全部相关认证写入口共用浏览器协调器：同源跨Tab互斥、共享epoch/attempt身份、可信profile当前确认、退出/切人先使旧epoch失效，迟到响应不能写菜单或进入业务。凭据不进URL/日志/广播；不把localStorage UI旗标当授权。锁/存储不可用有明确关闭恢复路径，不以不原子localStorage锁假装互斥。
- [ ] 原客户端256位交互秘密安全保存后才发初始请求；登录/SSO/pending/refresh生命周期与server安全status/close匹配。优先用实际收到的session核profile；未交付则封闭准确attempt后才重新登录。UNKNOWN/UNAVAILABLE保持隔离，不新ID重放成功OTP/code；因子明确未消费错误可按原政策重试。
- [ ] 通用HTTP仅在明确未提交且身份未变的安全读取恢复，不自动重放业务写；原operationId/业务UNKNOWN保留给业务Owner。401/403/503和pending/restricted/active分开。多Tab退出/切人清用户、菜单、平台/代理等旧数据。
- [ ] 相关Vitest、typecheck、真实多页面浏览器夹具；必要实际Program/PG丢响应/绑定回归。单份authentication-browser-recovery/DELIVERY.md、一次作者SPEC/QUALITY、正常PR集成。

核心结果模型按服务端安全状态消费，不能返回JWT/refresh历史秘密：
```ts
type CompletionDecision = 'ACTIVE' | 'SECOND_FACTOR_PENDING' | 'ENROLLMENT_REQUIRED'
  | 'PASSWORD_CHANGE_REQUIRED' | 'REAUTH_REQUIRED' | 'DENIED'
  | 'UNAVAILABLE' | 'COMPLETION_UNKNOWN'
```
实际API字面值/错误由源码核对后适配，不因本地枚举绕开后端状态。跨Tab协议应先产生共享操作记录再网络发送，业务页面的迟到读取亦核身份epoch。

## W27-02 明确SSO准入批准与配置管理

文件：Core/Sys/SsoIdentityAdmissionPolicyV1、TenantSsoConfigService与输入合同，WebApi/Sys/SsoConfigController，PG私有policy/配置操作回执及revision迁移；FE SsoConfigView、api/types/sys/sso、相关测试。采用已存在sys-sso-config:query/edit资源，在当前可信tenant/actor受控配置边界显式批准；不凭前端管理员旗标。新增审批权限若原合同要求则沿权威权限定义统一登记，不能暗设真实批准者。

- [ ] 新准入保存policyRef/version、approved issuer/client、EmailClaim可信邮箱语义、allowInitialEmailLink/allowJit/approvedJitRoleIds、邮箱身份权威认可；默认未批准/禁用。界面明确展示批准与普通配置区别，必须显式勾选范围和理由，不将AutoProvision默认true当批准，不回传secret。
- [ ] 当前actor权限/租户与原认证版本在同PG root内复核；expectedConfigVersion/policyVersion、operationId/reason形成配置CAS和准确回执。空secret保留；角色必须当前同tenant可用且被批准。冲突412重载，结果未知查原操作，不自动换ID提交。
- [ ] policy/配置/role/user实际writer遵共同门及revision；启用新JIT/改默认role需当前批准，撤销准入阻止在途新身份；强审计与变更同事务。普通配置变动不会静默复制旧批准到新issuer/client。
- [ ] 定向PG权限撤销/配置竞争/审计失败/角色非法/secret保留、FE批准提示/错误恢复和类型检查。单份sso-admission-policy/DELIVERY.md、一次作者审查、正常合并。此窗交付可供W27-03消费的政策，不冒初绑已采用。

## W27-03 原SSO映射五分支完整采用

文件：SsoService.cs及新SsoService.IdentityAdmissionV1.cs，政策/映射receipt服务、PG唯一性与实际writer guard、身份捕获/强审计适配、Web/Native安全结果展示、真实PG受控IdP测试。

- [ ] 精确(tenant,issuer,sub)已绑只取原账户并查唯一性，不跟外部新email改绑；缺policy不挡合法精确分支。唯一same-tenant email初绑须明确允许、email_verified=true、可信claim/当前空绑定，保角色/平台权。残缺冲突/多邮箱/另一绑定拒绝，不转JIT。
- [ ] 零匹配JIT须AutoProvision+当前批准policy+同tenant启用批准role+业务唯一性。服务端赋tenant/issuer/sub/角色，IsPlatformAdmin=false、无fallback权、随机占位密码，不赋真实本地密码。非法role/用户名冲突不回落Role1。
- [ ] 配置policy→稳定身份root→当前user/role顺序，在实际Main同事务闭合用户/关联、IdentityChange版本与Outbox、必要审计和映射receipt；实际role/配置/身份改绑writer参加相同门，含普通SQL并发证明。新增迁移只自有旧填充隔离PG。
- [ ] 映射提交与认证完成两阶段，映射成功非ACTIVE；后续MFA/策略/响应失败保真实映射，下次新SSO精确复用不重复建户。提交UNKNOWN按原绑定/支持权限查准确receipt，未确认不重放code/state、不新ID绕过或删除重建。两个subject抢邮箱/同subject抢用户有单一结果。
- [ ] 真实验签受控IdP+PG/HTTP五分支、并发writer/回滚/审计/映射成功后认证失败恢复，前端拒绝不暴露候选账户。单份sso-identity-admission/DELIVERY.md、一次作者审查、正常合并；不是企业真实IdP/生产验收。

## 共同验证、目录与收尾

相关前端使用既有锁定Vitest4.1.11和typecheck；PG沿S/run_wave26.py的隔离模板派生wave27，复用wave18产物/NuGet，PG55435逐次独占且停止后再用。UI依赖为已核目标/锁文件的既有零复制junction，不复制node_modules、不同时运行另一个可写分支。证据目录D:/CP6-local-test/wave27/foundation；旧失败与发布绑定文件保留。

三窗串行从各自最新确认main建codex独立分支，唯一物理目录`D:\CP6-worktrees\wave17-dispatch-20261018`，只COORD持Main:identity-authority。真实START一次，不重复ACK；已授权正常PR/Issue/Handoff。每次push/PR核工作流内容/当前路径过滤，不触发Actions。文档只diff/必要链接/台账生成检查，不跑产品测试。

- [ ] 各任务相关验证+一次SPEC/QUALITY、局部问题局部复查；合并核任务source与远端main，输入未变复用。
- [ ] 最后核组合边界、Issues和远端；台账/四状态/生成页/Handoff同步，关闭已交付窗口并释放资源，写检查点后结束goal。

完整IAM剩余组织/引用CAS、CRM投影/本地Owner/真实业务仍按后续窗口；现有PG副本迁移/生产部署未执行。本轮不会自动为真实tenant批准issuer、角色或身份链接。14包/125目标/375任务/247运行NOT_RUN保持原范围。

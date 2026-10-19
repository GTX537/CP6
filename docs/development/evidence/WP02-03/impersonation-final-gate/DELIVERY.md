# W25-02 / BUG321 模拟进入退出最终门

> 已集成：https://github.com/GTX537/CP6/pull/322 / source `f02edd9eb99c3d72be404b25130c1d4e4be40aba` / main `ee11bc845ac2581b19b14d218568c8136d5bf7da`，# 321 已关闭。最终组合核对复用本报告有效范围；下文候选措辞保留原交付时点。

## 结论

基线 `1974bcb4ee9f179c6539dd032b09329037cd5002`，分支 `codex/wave25-02-impersonation-final-gate-20261019`。[P0 #321](https://github.com/GTX537/CP6/issues/321)本地修正，**Ready to merge: Yes**。一次作者集中SPEC/QUALITY无阻断，非独立审查；83个有效相关结果通过（49原生PG/HTTP、34单测），无剩余失败/跳过。正常PR/远端核对后关闭Issue并继续W25-03；本轮长目标未完成。

## 原生复现和修正

实际HTTP最初5项RED：平台filter后撤权仍开始模拟；开始/结束安全审计被PG约束拒绝仍HTTP200并写access Cookie；真身Password在原认证后改变，开始/结束仍签发新身份。后续边界测试又确认原access在等待管理员根时已过期仍可换新身份，已局部修正并复验。

PostgreSQL开始/结束现在在既有管理员根事务内处理。开始复核持久真身，结束以签名中的原真身ID/tenant/认证版本恢复且重新核Enable/IsPlatformAdmin/非强制改密；actor/target用户和双方tenant以FOR SHARE锁定，持到审计提交，直接凭据/tenant绑定修改也不能穿过边界。有效目标仍同tenant且启用，过期/停用tenant拒绝。原token有效期在取得根后及写入审计后再次检查；缺失或矛盾原认证证据拒绝，不用当前状态回填旧证明。

新imp access绑定target当前认证版本，以及已核实来源的impersonator tenant/认证版本；可选原cp6_session引用以独立impersonator_session携带，结束恢复原引用。这里传递原引用，不宣称完成family锁或全认证政策验证；W25-03继续消费。目标须改密时，模拟仍按原合同must_change_password=false、平台权挂起；不给目标refresh。原refresh/CSRF保持不变，只换access Cookie。旧无足够证明的模拟/平台来源需重新认证。

菜单仅取目标tenant内的有效主/附角色和启用菜单，错误tenant附角色和停用角色不混入。保留legacy未指定user时选有效主角色1用户（按ID稳定选择）；这不等于批准代办业务或人工审批。

## 必要审计、Cookie和故障结果

原安全审计服务的best-effort合同保持。PG模拟单独持久化必要Sys_SecurityLog，与管理根同一事务；actor采用持久Id/Name，日志落平台tenant。新增可空原生jsonb `ContextJson`，明确actual/effective身份及tenant、imp jti引用、准确JWT到期时刻、操作范围和完整reason；原Reason的256字符容量/历史记录不缩小。JSON不含access/refresh、密码、密钥或认证证明原文。迁移 `20261019093000_SecurityAuditContextV1` 仅添加可空列，不回填历史证明；Down拒绝删除证据。非PG模型忽略新字段，未增加SQL Server实现。

实际Cookie writer先在暂存响应上生成头，菜单/签名/必要审计均成功后，先拉黑被替换access，再提交PG，确认commit后才把暂存的唯一access Cookie写入真实响应。不接受外层调用方事务，因为保存点释放不能证明可以对外发Cookie。当前产品消费者是HTTP控制器，自有事务满足该边界。

黑名单属于外部缓存，**不是PG原子资源**。写缓存结果不明或其后DB提交失败返回503/IMPERSONATION_SWITCH_OUTCOME_UNKNOWN，不发新Cookie、不恢复旧access、不自动重放；原refresh/CSRF仍按原来源保留。审计失败/取消/签名Cookie准备失败在消费旧access之前回滚。极端提交后写响应头失败只尝试隔离新jti，不全撤其他会话，结果仍UNKNOWN。实际commit后网络送达未知、完整CompletionStatus/session恢复UI仍未实现，不假称客户端已收到身份。

同一源access的并发切换在根内重新查黑名单，只提交一份切换。缓存仍沿既有Redis或进程内实现，其持久化/多节点配置与重启失效范围没有在本次改变；这不是新的持久会话撤销权威。

## 实际验证及复用

证据根 `D:/CP6-local-test/wave25/foundation`，日志/JSON/TRX均保留：

| 前缀 | 真实结果 |
|---|---|
| imp-red-20261019T091953Z | 5失败/0跳过，实际HTTP错误成功复现 |
| imp-green-20261019T092658Z | 原5项通过 |
| imp-boundaries-20261019T093053Z | 测试代码误用MenuRow.MenuId造成编译失败，未运行/未计通过；改为原合同Id |
| imp-boundaries-expiry-red-20261019T093223Z | 72通过/1失败/0跳过；唯一失败为等待锁期间source过期仍签发 |
| imp-final-20261019T093448Z | 修正后的26项模拟原生PG/HTTP全部通过/0跳过 |

最终26项替代此前模拟16项；其余未受局部到期检查影响的57项成功结果复用，构成83个有效去重结果，原失败不改写。49原生=26模拟+11租户actor+12租户审计；34单测=14旧模拟服务+20平台filter。真实覆盖actor撤权/停用/移租户/凭据变化、目标tenant/资格/绑定、长reason及明确审计、无Name主体、旧证明缺失、legacy目标选择、两向access拒复用、同源并发、root等待过期、真实行锁阻塞、取消审计锁、Cookie准备故障、缓存写后未知、PG延迟约束在commit拒绝、外层事务拒绝。真实cookie只写access，原refresh/CSRF不被改写。

迁移在同一自有PG集群内新建独立已填旧基线数据库：先安装截至W23的既有迁移、插入256字符原审计，再前向迁移两次，核无待迁移、旧reason完整且ContextJson仍null，新增jsonb实际写读成功。既有DEV/生产未运行迁移。

HTTP使用实际JWT验证/黑名单、平台filter、产品中间件/控制器/PG服务；缓存故障用明确fixture替身注入，PG提交故障为真实DEFERRED触发器。不是完整Program/浏览器DEV验收。已有依赖漏洞、空引用等构建警告与受控fixture DDL的EF1002提示均保留；未宣称漏洞扫描或全仓通过。

## 一次作者集中SPEC/QUALITY及剩余范围

对完整产品/测试/迁移diff集中核对：SYS-IAM-01 §8.2–8.3来源和原Cookie语义、平台权挂起、当前真身/target可信绑定、原认证版本不得洗新、root与行锁持到必要审计提交、快照/失败跟踪恢复、取消、外部缓存与DB非原子结果、精确Cookie输出和无raw token正文。模型/PG快照仅新增nullable jsonb；旧SQL Server模型保原合同。共享helper只增加已读actor的version/tenant信息，原资格检查顺序不变，相关filter/租户测试已验证；没有重复整个架构或全部业务验收。

新签名claims不证明完整PrimaryProof/2FA/SSO策略、原FullAuth期限或凭据所有writer最终门；session/family/current版本消费在W25-03继续。模拟FE横幅、跨Tab安全恢复、历史查询API和完整业务AC仍NOT_RUN；不能将本报告当完整WP02-03、模拟产品或生产验收。

4次自有PG运行均停止且无postmaster.pid（编译失败未创建集群），HTTP释放；原目录/build/NuGet缓存复用，零新worktree/代理/Actions。原7项用户资料与全部失败证据保留，未操作既有库或部署。

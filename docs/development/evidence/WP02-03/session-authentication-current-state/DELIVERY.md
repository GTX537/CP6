# W25-03 / BUG323 原认证状态连续性

> 已集成：https://github.com/GTX537/CP6/pull/324 / source `1a8d7ac383c6da95abf4d4cdbd8aa5f82a51e3aa` / main `f978d8de9ae90a17bd5655e80662afe0b7aca0bd`，# 323 已关闭。最终组合核对复用本报告有效范围；下文候选措辞保留原交付时点。

## 交付结论

基线 `ee11bc845ac2581b19b14d218568c8136d5bf7da`，分支 `codex/wave25-03-authentication-state-20261019`。[P0 #323](https://github.com/GTX537/CP6/issues/323)已本地修正，**Ready to merge: Yes**。一次作者集中SPEC/QUALITY无剩余阻断（非独立审查）。167个去重有效相关结果通过：53原生PG/实际HTTP/故障组件，61既有认证单测与组件HTTP，53模型元数据检查；无剩余失败或跳过。测试计数只陈述实际范围，不代表全IAM或业务完成。

## 原问题及当前行为

原生PG最初6项观察中，旧refresh在改密、账号停用、主体tenant变化、tenant停用、强制改密或密钥变化后仍轮转；实际完整Program也复现改密后旧access访问 `/api/auth/profile` 返回200。最终factor回归明确使用**已启用**2FA密钥：未入会的准备密钥尚不是生效凭据，不能切断setup→enroll-self。新增此兼容路径曾实际401，已修正并通过真实Program/PG复验；保留全部RED和中间失败记录。

PG发行保存来源用户的认证版本，先锁定并比对当前持久user/tenant，阻止发行期间凭据变化洗成新证明。refresh的AuthenticationVersion在轮转中原值传递，不由当前user补写。旧nullable行缺证明直接重新登录，旧family记录也不回填。版本v2以确定性JSON域编码绑定AuthenticationEpoch、密码及其持久时间、2FA启用/入会时间/生效密钥、外部issuer/subject、QuickPinHash及MustChangePassword；原密码等敏感材料只参与哈希，不写JWT或报告。PG时间按已持久化微秒规范化，避免刚保存的.NET 100ns尾位使合法2FA发行失败；不改变用户输入或数据库时间存储。

轮转拥有独立事务，拒绝调用方事务或待写EF上下文。tenant FOR SHARE→user FOR UPDATE（同账号发行/轮转串行，防真正重用的撤销漏掉并发新令牌）→原family FOR UPDATE→token FOR UPDATE。当前user启用、tenant一致、未锁定、tenant启用且未过期、原版本匹配后，才条件撤销source并插入replacement。保留Web/Windows/Android与device绑定，不允许Web凭证换成native。source、tenant有效期在等待/保存后再次核对，避免使用PG事务起始now()冒充当前墙钟。凭据writer在此边界等待，后续消费拒绝旧证明。

并发初次观察未撤销的失败者返回E-SEC-007，不撤销胜出者或其他会话。缺证据、当前状态不符、客户端不符、family已登出、普通退出/后置拒绝均不进入全用户重用处置。已事先观察为撤销、有已完成replacement、当前原证明仍有效的真正旧令牌重用维持独立E-SEC-008风险处置；不自动重放或恢复source。

## 实际入口和完成边界

实际Program的JWT OnTokenValidated调用AccessTokenAuthentication：保留jti黑名单和family撤销检查，PG在权限/缓存与控制器前校验单一认证身份、subject/tenant、原版本、当前启用/未锁定user及tenant；pending不能作为普通Bearer，缺证据/权限源读取失败关闭。模拟token同时核effective和原actor版本、当前平台actor资格，原actor family引用按actor身份消费；平台权不在模拟token恢复，target须改密的原模拟合法形态保留。真正Program/PG已验证开始→profile→结束→profile及actor凭据变化拒绝。

Web/native refresh使用持久CompletionPending。轮转提交后，设备、画像、签名、审计及Cookie准备完成，再在独立完成事务复核当前user/tenant/family、source期限并解除pending。pending凭证不能消费或触发全用户重用。Web先在临时response准备三Cookie，再一次写入真实response；native保持DTO body，绝不写认证Cookie。准备、完成或取消失败仅给精确replacement写CompletionRejectedAtUtc并撤销，后续旧source重试不误伤其他会话。隔离失败或提交结果不明确返回503 AUTHENTICATION_COMPLETION_OUTCOME_UNKNOWN，无凭据输出/自动恢复/自动重试。取消在完成前后均检查，持久隔离不使用已取消request token。

完成状态是服务端已知边界，**不证明浏览器/网络一定收到响应**；原始token不持久保存供重放。进程在轮转后退出会留下不可消费pending，需重新登录；没有新增自动恢复、凭据重放或全会话清理。这里实现refresh完成边界，未声称全部首次登录/SSO/2FA writer已有统一AuthenticationCompletionGate。

## PostgreSQL采用与迁移

新增前向迁移20261019101500_RefreshAuthenticationStateV1：nullable text AuthenticationVersion、nullable UTC timestamptz CompletionRejectedAtUtc、default false CompletionPending。采用既有UTC映射，旧SQL Server/InMemory模型忽略新列，未增加SQL Server迁移或真实双库验收。升级已填旧基线数据库验证保原TokenHash/行，原证明保持null且消费拒绝；快照与模型元数据相关检查通过。

**采用新PG应用前须安装迁移，并安排重新登录**：v2与旧认证版本故意不兼容，旧访问/refresh/family不得被静默提升。只在自有隔离库安装；现有本机DEV/CP6DB副本及其他环境没有操作，没有生产发布或回退许可。Down拒绝删除认证/拒绝证据，故障按前向恢复处理。

## 验证和复用来源

证据目录 `D:/CP6-local-test/wave25/foundation`，各次JSON/TRX和原日志保留：

- auth-red-20261019T095947Z：6项原生失败观察。
- auth-first-20261019T100437Z：6通过，实际Program夹具缺Space文件根导致1启动失败，补自有目录后继续；access-red-20261019T100539Z实际Program确认旧access改密后仍200。
- auth-green-20261019T100958Z：12通过/1夹具Cookie清除与重写同名解析失败；按浏览器最后一项生效修正。
- auth-completion-20261019T101220Z：20通过。
- auth-boundaries-20261019T101451Z：109通过/1旧数据夹具缺必填FailedLoginCount；改为旧基线实际实体保存，无产品故障掩盖。
- auth-enrollment-red-20261019T101726Z：真实Program自助2FA准备后401；只绑定已生效factor后修正。
- auth-final-20261019T101919Z：113通过/0失败/0跳过，含原生凭据与tenant变化、无证明、client/device、family/非family并发、真实提交拒绝、锁、过期、完成故障和UNKNOWN、迁移、Web/native/pending/restricted/2FA及W25-02组合。
- auth-review-20261019T102150Z：80通过/0失败/0跳过；27原生认证用例（补完成后取消），53已有模型元数据检查（含历史SQL模型构造，无SQL Server实例）。与前次去重为167；未受局部取消/UTC映射改动影响的原结果直接复用。

实际HTTP使用当前编译的CP6.WebApi.dll启动完整Program，临时loopback端口和自有PG数据库，关闭种子初始化及后台任务；没有用仅复制JWT事件的测试宿主冒完整入口。其他历史HTTP/InMemory用例按其真实范围列示，不冒PG或真实业务。现有NU190x及编译/fixture DDL告警保留，未声明漏洞扫描通过。

## 集中审查和剩余范围

一次作者SPEC/QUALITY核对最终产品、测试、迁移及共享状态diff；复核来源/当前状态分离、不可回填旧证明、tenant/user/family/token锁顺序、事务回滚与EF新增实体分离、ordinary与reuse处置、pending/rejected精确持久化、Cookie/DTO输出、真实Program接线、PG迁移/UTC和合法2FA准备。审查中补UTC分类及提交后取消，只复查相应影响并运行相关验证。

这里的认证版本是凭据状态一致性，不是完整认证政策版本或历史MFA证明。SSO配置版本、tenant 2FA政策切换、原认证方式/完成时间/强度/FullAuth期限、全writer共同门、统一浏览器family登出、CompletionStatus恢复界面及Owner业务最终门仍属后续范围。未把本项登记为完整WP02-03、CRM/Owner、既有DEV安装、生产或业务验收。原14包/125目标/375任务/247运行NOT_RUN保持。

本项9次自有PG根均stopped且无postmaster.pid，实际API child由fixture释放；工作目录/build/NuGet缓存复用。无新worktree/代理/Actions、无删除、既有库操作或部署。正常PR/远端核对后关闭#323并做本轮最终协调收尾。

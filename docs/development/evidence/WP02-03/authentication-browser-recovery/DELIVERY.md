# W27-01：浏览器跨标签页认证与准确恢复

> 已集成：https://github.com/GTX537/CP6/pull/336 / source `e24a3366e609c9d1af7ed564f5623af84cb699c8` / main `098264a08ee25e3f044d202e0e5ade3c98b6dc3a`，#335 已关闭。下文保留候选交付时点；本轮最终状态见[总记录](../../../dispatch/20261019-wave27/README.md)。

基线main `9dd9943033397a5a6604ad307498dfd3aebc519f`，分支`codex/wave27-01-browser-auth-recovery-20261019`，关联[P1 #335](https://github.com/GTX537/CP6/issues/335)。**Ready to merge: Yes**。一次作者集中SPEC/QUALITY及发现问题的局部复查，无剩余阻断；不是独立审查。原3项前端真实失败和2项PG/HTTP失败已修正。

## 行为与边界

登录、SSO发起/落地、pending 2FA、刷新、改密、注销及平台身份切换共用`browserAuthentication`。先把256位随机原交互秘密提交到IndexedDB，再发送请求；localStorage只保存公开UI状态、epoch和不具授权能力的引用，storage事件不传秘密。密码、OTP、绑定secret、JWT/refresh均不写浏览器可读持久存储。IndexedDB/Web Locks或安全上下文不可用时，新协议在发送凭据前拒绝；原恢复秘密丢失不假造另一份或猜测成功。

认证动作持同源Web Locks排他锁；业务HTTP请求持共享锁至收到响应，使已准备的旧业务载荷不在身份切换后才带新Cookie发出。多个标签页轮转排队，后到者先核实际当前Cookie，避免重复消费同一refresh。localStorage不是互斥锁，更不作后端授权。

退出和切人同步推进共享epoch，立即清用户/菜单/权限、平台/代理态并卸载旧路由视图；其他页经storage、focus、pageshow及visibilitychange同步。身份切换状态按实际共享Cookie广播公开标记，修正旧“每标签隔离”的错误假设。原业务响应和权限加载以身份/代次校验，迟到成功不能恢复旧权限或导航。身份切换不确定时不能靠仍可用的旧profile推断成功；目标身份过期不悄悄refresh成平台操作者。

通用HTTP不自动重放POST/PUT/PATCH/DELETE。仅同一身份内的安全读取可在明确401后协调刷新并重试一次；403/503不作401处理。业务操作标识和业务UNKNOWN留给原Owner，注销不再清空全部localStorage。已经发出的业务写不能被浏览器撤回，本协议不把退出或身份变化解释为业务回滚。

`active`、受限改密、pending、working、SSO等待、UNKNOWN和closing分开处理。只有与原交互/准确轮转匹配的实际profile可安装会话；导航query、Cookie以外的UI旗标、COMPLETED安全状态都不足以证明当前会话可用。UNKNOWN只查原秘密对应的安全状态或准确关闭；关闭未确认保持隔离，不新ID重放密码/OTP/code。明确未消费的错误因子仍可输入新验证码。2FA页面重载保留原期限、已发送邮件方法和setup已生成标记；不保存绑定材料，不自动覆盖原secret，丢失材料可结束原登录后重来。

## 后端绑定、关闭与审计

新增精确POST `/api/auth/completion/browser/status|close`，仅消费单个64字符十六进制（256位）原秘密header，不消费环境Cookie作为该能力的授权。只有这些精确路径及合法header享有对应CSRF豁免，近似路径、缺失/非法/重复header仍拒绝。原通用恢复端点和Native合同保留；未知原通用交互仍返回UNAVAILABLE。

`BrowserAuthenticationRecoveryV1`复用PG认证根门与原交互/session receipt。当前profile可选原秘密绑定；原交互匹配原completion及合法后代，轮转秘密只匹配准确replacement。pending因子Cookie必须匹配提供的原交互。旧但有效Cookie、其他健康登录及其他轮转均不能冒充本次结果。真实Program平台代入/切出沿原actor receipt alias验证，关闭原登录链后恢复出的actor会话亦拒绝。

新前向迁移`20261019141000_BrowserAuthenticationRecoveryV1`增加私有关闭屏障和轮转映射，普通DML参加现有PG根门；实际INSERT触发器拒绝已关闭或跨类别复用的key。关闭未登记的原attempt会先提交持久屏障，迟到注册不能再发行；单纯查询不到仍为UNAVAILABLE。原登录关闭覆盖所属代次链，轮转关闭只覆盖准确本代/后代，其他健康登录不受牵连。能力、profile匹配和安全状态不返回历史凭据。

正常注销先保留现有注销审计/Cookie清理，再凭原能力确认关闭；已知交互或轮转的恢复关闭与安全审计同一PG事务，审计失败不报告CLOSED。重复关闭幂等且不重复该关闭审计；未登记随机attempt只建立屏障，不能虚构用户审计。关闭状态、身份切换UNKNOWN和改密UNKNOWN均不以旧profile猜结果。浏览器本地信息只改善交互，后端当前权限、租户和原认证证明仍是实际闸门。

## 实际验证与复用

原始日志/JSON/TRX及明确标注的浏览器夹具在`D:/CP6-local-test/wave27/foundation`。不把多次执行数相加成用例目标：

| 记录 | 实际结果与范围 |
|---|---|
| browser-red | 3失败：401重放业务写、退出后旧读取成功、权限reset后迟到响应复活 |
| browser-binding-red-20261019T140602Z | 2真实PG/HTTP失败：错误原能力仍读旧有效profile、无法先封闭迟到注册 |
| browser-binding-green-20261019T140930Z | 4通过，含原失败及未改的通用Web安全关闭/环境Cookie CSRF |
| browser-recovery-boundaries-20261019T142147Z | 68通过、0失败/跳过；原Web/Native入口、受控SSO、CSRF及相关HTTP，含准确轮转、pending能力绑定 |
| browser-actual-switch-20261019T144015Z | 2追加真实PG/实际Program通过：精确CSRF边界、平台身份往返及原能力关闭 |
| browser-closure-audit-review-20261019T144747Z | 7浏览器后端边界通过；包含追加审计拒绝回滚、重试不重复审计及前述绑定/关闭/模拟局部复查 |
| browser-component-final | 55通过；协调器、HTTP、权限、SSO落地与受影响菜单/仪表盘组件 |
| browser-lock-review / browser-logout-reviewed | 21/18相关前端通过，复查共享请求锁、正常注销及恢复路径；未变的其余组件复用55项来源 |
| browser-real-tabs-final / browser-lock-review / browser-logout-reviewed 日志 | 5/2/3执行通过，覆盖6个不同真实Chrome场景；后两次为审查修正后的局部复查 |
| browser-typecheck-delivery | vue-tsc --build退出0，另有退出码JSON；锁定Vitest4.1.11及原依赖，未全量打包 |

真实Chrome使用两个独立页面与真实Web Locks/IndexedDB/storage/reload，验证一次轮转、退出抢先、丢响应/关闭未知不新登录、实际Vue 2FA重载不重建材料、实际应用两页隔离及业务写锁。API为受控夹具，截图中的语言API亦为空夹具，不宣称企业IdP、真实业务数据或完整语言包视觉验收。后端另以真实PostgreSQL18和实际Program验证合同；浏览器夹具不冒PG端到端。

最初浏览器运行因缓存缺对应Playwright headless版本未执行，改用已安装Chrome而未下载新浏览器；2FA页面夹具误把`/src/api/...`当API，改为精确`/api/`路径后通过。最初typecheck的RouterView key可空错误已修正。全部失败日志保留。原相关NU190x/静态告警保留，未触发远程Actions或声称漏洞扫描通过。

作者集中审查全部任务diff：原秘密持久化/泄露面、同源锁和epoch、旧Cookie/迟到响应、正常/pending/restricted/UNKNOWN、共享Cookie的代入身份、原业务写不重放、数据库当前证明/准确代次关闭、CSRF与审计原子性及缓存复用。审查发现的业务请求发送锁与注销审计边界均在本窗修正，并按影响局部复查。

## 采用与剩余范围

本窗仅安装到自有隔离PG。正式采用须先执行该前向迁移，再使用支持Web Locks/IndexedDB的安全同源浏览器；旧客户端若未保存原秘密且缺可用Cookie，不能恢复初次丢失输出。关闭屏障不可任意TTL删除，否则迟到请求可能重新注册；容量/保留及外部入口流控仍属实际环境运维安装范围，本窗没有生产流控或清理任务。

5个本轮自有PG根均停止且无postmaster.pid，实际API由夹具释放；独占本机58427的Web进程核对身份后已停止。复用唯一工作目录、原node_modules junction及wave18/NuGet缓存，无新增工作树/代理。候选正常合并并回读远端后才关闭#335。

W27-02首次SSO身份审批配置与W27-03初绑/JIT事务消费仍待执行，goal继续。完整IAM组织授权、CRM本地Owner/跨仓采用、现有DEV/企业IdP/真实业务及生产发布不提升；14包/125目标/375任务/247运行NOT_RUN保持原范围。

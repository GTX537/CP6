# W130-01 原生身份基线后台与实际跨仓来源交付

CRM实际CrmInquiryNativeHostV2现可显式启用每租户C2凭据、完整身份目录收集和原生PostgreSQL恢复/发布后台。配置默认关闭，独立身份写入角色与业务会话处于同一数据库；不安装schema或授予角色权限。可信配置可首次登记来源，已有不同来源/登记拒绝覆盖。租约与注册、HTTP采集、恢复发布分属两个Serializable事务和事务外网络；保留两分钟租约、90秒收集、最长20分钟publication与原始来源/图校验。

新鲜current直接复用，实例间共享PG租约；取消能停止，单租户故障不阻塞其他租户。明确40001/40P01回滚下次轮询重试；COMMIT连接/超时/取消返回未知，不盲目再次提交同一结果，下一次或重启读取持久current/lease恢复。刷新开始依既有guard撤回旧current，采集期间可能暂时拒绝业务，不放宽guard。publication仍PendingInspection、CanAuthorize=false，Owner继续当前权限与共同事务检查。

凭据客户端只向冻结issuer的/connect/token发送client_credentials/cp6.services；生产HTTPS、禁止重定向/cookie/解压，头/体/时间有界，拒绝重复JSON/错误scope/过期响应；仅单次采集内缓存，不信任JWT载荷为业务身份。日志只含租户、结果/错误类型及SQLSTATE，不含秘密或原身份内容。配置冻结与刷新参数溢出边界已检查。

Main增加真实跨进程驱动，使用实际C2控制器/目录/令牌记录、普通租户目录角色和原身份collector，将临时服务秘密仅通过子进程环境传给CRM。CRM实际BackgroundService通过真实HTTP把A/B完整原始来源写入PG，并核对publication与原始baseline的hash、数量和内容；错误秘密被拒绝，Main Owner目录查询0、连接池借用归零。测试宿主仅登记身份目录/C2两个实际控制器。其他业务的无关依赖不参与该夹具。

34项唯一相关本地检查通过：CRM29、Main5；必需0跳过，原失败及针对性复测保留，未变化成功证据按原适用范围复用。

CRM包括27项本地宿主/C2/既有publication行为、实际旧collector与新native worker两个子进程；Main包括4项独立BUG543前置与新native父驱动。来源为scoped-final-20261025T064339Z、changed-regression-20261025T064550Z、configuration-boundary-20261025T070357Z、bug543-fixed-20261025T065101Z、bug543-source-retest-20261025T065156Z、bug543-cross-final-20261025T065451Z和native-host-scoped-controllers-20261025T070207Z以及对应独立子TRX；final-validation.json保留逐例成功来源。90秒截止已在本轮运行通过，未因提交/SHA重复运行。

最初编译、后台等待/登记错误匹配、Main当前目录安装、共享端口和测试过滤器碰撞、全站控制器注册均有原失败记录；查明原因后仅定向修复复测。真正产品前置缺陷[Issue543](https://github.com/GTX537/CP6/issues/543)用独立分支修复精确函数清单，经[PR544](https://github.com/GTX537/CP6/pull/544)正常采用到2e287861179be170cf6013e311eb608378871597。当前迁移原函数hash核对、篡改/权限负例及原步骤通过后关闭Issue；没有删迁移或放松未知函数检查。BUG分支准确删除、官方finish释放后，W130恢复原分支并合入该前置。

本地HTTP/TLS终止、服务注册/密钥和数据库均为明确隔离夹具。证明实际组件/源码互通，不证明企业TLS、真实租户注册、即时撤权或生产验收。无Web变更或浏览器验收、Actions、公共线合同/项目依赖/迁移变更或部署。三次自有CRM PG55456实例与Main PG55435夹具、HTTP和后台均正常停止，数据、失败记录和复用缓存保留，无新junction。

一次任务级SPEC/QUALITY审查通过，Main生产修复已在独立BUG PR审查；本PR完整变更审查涵盖测试宿主秘密/租户/生命周期以及台账范围。工作流引用与已审查W118内容一致、实际路径不触发远程普通验证。未变成功结果复用，合并后只核对采用与必要集成状态。

CRM [PR154](https://github.com/GTX537/CP6.CRM/pull/154)：1c08f74f089d07c70a226360a32a7f548904a876 → 04bf47e1078c8f741ba79eee94f3e94b44fa84f3；[CRM单份报告](https://github.com/GTX537/CP6.CRM/blob/1c08f74f089d07c70a226360a32a7f548904a876/docs/development/evidence/W130-02/DELIVERY.md)。Handoff [正常交付PR](https://github.com/GTX537/CP6.AI-Handoff/pull/106)：600b3e42ab2e4b9035bde6bf72d0d55da42fafc0 → 82fef5963d59ed9e3994be46bf948e76c61eeca5。两仓完整树采用、准确本地/远端分支删除、官方finish释放均已核对。DEV-2重绑回Handoff留作复用。Main最后PR/finish待同轮完成，以wave130-final-verification.json核销；下轮登记统一写回最终快照。

原估90–150分钟，发现独立BUG时增列20–35分钟风险；截至准备48.69分钟，最终从Main START到最后finish计一次，跨仓/嵌入BUG不叠加，外部等待并非纯编码时间。D5可信delta入口、提交后ACK、及时跨仓撤权和原生API可观测性仍待；基线轮询不等于即时撤权。完整D5及正式目标/验收不提升；D2、D3、D6、D7继续原计划必要出口。

七天目标ACTIVE；D1/D4关闭，2/7=28.6%仅为整阶段出口数，不代表工作量完成率。正式375卡24DONE、2IN_PROGRESS、349PLANNED；完整125目标0关闭；247NOT_RUN+41REFERENCE_ONLY保持。W129剩余42–70小时为当时按必要余项拆分的低置信估计，外部真实政策/日历/注册/密钥不能伪造。

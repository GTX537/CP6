# W105 询盘配置预览、激活与数据库时间生效

本聊天COORD单人按既定writing-plans / executing-plans执行；七天持续目标ACTIVE，不另开代理、聊天或定时任务。

## 最新前置

Main `14a623ff4cd0c069fe0fb1d2e834c41987d62026`、CRM `77dd02db308c5a746fe0ae672e0a8fc13e108185`、Handoff `6cbbde316a20d7b5eb85f6180d59b9743e420ecc` 已核对。W104 Main516/CRM131/Handoff80全部正常合并、准确分支删除、官方finish，实际65.43min；63项相关本地检查的实际范围保留，不重复。当前固定DEV-1/DEV-2干净，旧node_modules junction已由用户移除且依赖缓存完整。

冻结R3 V15正文SPEC04/222–223/507、具名修订合同及FX26子语义要求：独立preview/activate目的、当前query+configure和实际Site/Form、同一保存内容/渲染摘要、短时一次性actor绑定证明、原操作结果、审计及outbox同事务；激活锁定同一表单，关闭前驱区间并打开后继，数据库时钟按[from,to)选唯一版本。Main InquiryPermissionIssuerV1已实现这两种用途；CRM客户端目前只请求read/write。W102/103真实原会话、当前身份、PGOwner和W104页面为直接采用前置。整卡WP03-01仍PLANNED/NOT_CHECKED，本窗口不据此关闭未完成直接前置或正式验收。

## 任务与实际执行估计

|任务|范围|估计|
|---|---|---|
|W105-01 Main|登记窗口、核对W104最终证据、同步台账和四状态|15–25min|
|W105-02 CRM|真实preview/activate Owner、持久证明/原结果/审计/outbox、精确时间区间、BFF/页面、定向验证|140–215min|
|W105-HANDOFF|一次START、保存释放后原分支恢复、交付与收尾|25–30min|

总估180–270min，Main START `2026-10-24T09:54:29.720124+00:00`；最终按Main finish墙钟并单列等待，不累加仓库耗时。持续目标仍为CP6-7D-20261022。

## 实施计划

- [x] CRM原受保护会话显式请求configuration:preview/activate，仅接受当前对应用途；实际Main已有签发逻辑复用，并验证新用途互通。网络均在业务持锁之前，Owner最终重新核对身份/信任/精确pair及部门。
- [x] 在原配置Owner加入两个冻结命令及HTTP/BFF路由。新增独立版本化PG生命周期存储，保存的内容、source registration和原save回执不改；预览、激活追加事实和原结果，版本与ETag各守其身份，更新状态使用原生行仲裁。每个成功命令含当前权限有效期、保护后的原结果、真实审计和正式Platform outbox同一事务，无远程发送。
- [x] 预览重读实际保存内容、日历与渲染摘要；生成随机256-bit非公开proof，数据库只存带版本的摘要和明确actor/bundle/version/content/rendered/时限绑定，原回执加密；预览不签发公开Attempt。激活重新验proof、ETag、内容/日历/权限，原子消耗并登记不重叠后继区间。精确UTC令牌保留原合法精度，不能强制DateTime/PG timestamp微秒截断。
- [x] 以数据库clock_timestamp为选择时间，验证相邻边界、未来生效和不可改写历史；禁用后继也保留历史身份。公开Attempt/提交仍由后继专属Owner接入，不以内部生效选择宣称公开提交完成。
- [x] 页面只对保存且未修改内容提供真实预览与激活。渲染实际保存notice和字段，不带真实提交入口；proof仅在内存，未知回执固定原body/key/CSRF/ETag；401/403清空、迟到响应不恢复、412要求刷新。必要的失效重新预览不放宽proof时限。
- [x] PG/HTTP验证原结果重放、异payload冲突、并发单赢家、错actor/用途/版本/ETag、proof过期/重复、权限撤回和事务回滚；浏览器覆盖保存→预览→激活及未知回执。只运行受影响编译和相关检查，集中完整diff审查一次。
- [ ] CRM、Handoff、Main逐一正常PR合并，核对远端采用与准确分支，停止本轮自有进程，官方finish释放席位；保留一份CRM交付报告和实际失败记录。

固定席位仅DEV-1+DEV-2，跨仓官方bind；缓存外部复用，不创建junction/源码副本。CRM隔离PG55446，实际Main互通必要时55435，输出D:/CP6-local-test/wave105。新存储仅显式安装在本轮隔离数据库，无既有环境迁移。无Actions/保护变更/生产部署。

## 未完成范围

企业实际政策/审批与部署配置不虚构；源登记发布、公开Attempt/提交、正式outbox发送、常驻C2及即时跨仓撤权仍需对应采用。24/375、0/125与247NOT_RUN保持真实；本地行为验证不冒充真实业务/生产验收。

## 实际交付与生命周期

CRM [PR132](https://github.com/GTX537/CP6.CRM/pull/132) 已采用 `14ca04883e5aae7b856b13674d00b6777d1567d5`，来源 `aa4a97ceaac5b312af868657ffd3fd19429708bb`；[单份交付](https://github.com/GTX537/CP6.CRM/blob/14ca04883e5aae7b856b13674d00b6777d1567d5/docs/development/evidence/W105-02/DELIVERY.md)。

原PG配置Owner与页面接通冻结preview/activate，原受保护会话请求对应Main当前目的签名，事务内当前身份/持久信任/实际Site/Form/query+configure/部门与最终有效期重核。随机短时一次性proof绑定真实actor、保存版本、内容与渲染摘要；原save回执和内容保持不变，预览/激活原结果、审批引用、审计与正式Platform outbox同事务。当前有权限的另一actor不能消费原proof，可以自行重新预览；未知回执保持原键重放，当前撤权拒绝披露。

独立版本化PG生命周期存储追加前驱/后继区间；数据库时钟选择唯一[from,to)，原UTC令牌和任意合法精度保留，年0000/闰秒/等价尾零比较不截断。未来Scheduled、到点Active或禁用Retired、前驱Superseded；原操作结果仍不变。内部选择器不授权公开访问或签发Attempt。页面安全显示保存notice与字段，proof只在内存，迟到响应在权限失效后不得恢复。

85项唯一相关本地检查通过、0跳过：20生命周期PG/HTTP、11真实浏览器、28相关会话/Owner、24Node、2实际Main/CRM互通。 真实五分钟过期覆盖进入与提交边界，故障回滚没有部分proof/审计/outbox；相邻精确边界、实际DB时间前后和禁用后继验证。实际Main登录/OIDC/签发→Node BFF→CRM Owner完成新两个purpose及Main撤权拒绝；Main生产者代码未变，复用已核对构建并实际重跑互通。浏览器页面交付与签名来源为隔离夹具，组件/BFF/HTTP/PG为实际实现；没有Next完整SSR/build或生产TLS声明。证据 `D:/CP6-local-test/wave105/foundation/final-validation.json`。

自有PG55446与Main55435、HTTP/Node/Chromium停止，原中断库和最终隔离库/失败日志保留，缓存原位复用，未新增junction或额外源码树。新schema仅显式安装于隔离环境，既有schema不自动修复/迁移；运行身份不需要生命周期UPDATE/DELETE/DDL。一项集中审查完成，无Actions、保护变更或生产部署。

WP03-01只完成配置预览/激活/生效选择限定窗口。公开表单/Attempt/提交、源登记发布、正式outbox发送、常驻C2及跨仓即时撤权仍为后继；企业政策和实际审批不虚构。24/375、0/125、247NOT_RUN不提升，七天ACTIVE。
Handoff [PR81](https://github.com/GTX537/CP6.AI-Handoff/pull/81) 已采用 `53cddda6f0bbc6dd0db3d5481613b9f937772f41`，原准确分支删除、固定DEV-2 finish释放。 CRM分支删除/席位释放已核对；Main本次正常PR及finish继续，当前START后墙钟39.64min（原估180–270），最终按Main finish计，不叠加各仓。

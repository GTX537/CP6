# W104 冻结配置页面与可信上下文执行计划

> 本聊天COORD按writing-plans / executing-plans单人连续执行；用户既定精简节奏、固定席位与正常PR收尾优先，不新开代理或确认流程。

目标：在已采用W103真实Main权限/BFF/PG配置Owner上交付可用的冻结配置读取、显式政策编辑和草稿保存页面。
架构：受保护原会话、实际精确Site/Form、持久签名信任和当前身份共同授权后才返回页面上下文；页面仅提供输入和操作恢复，所有保存仍走原Owner。现有冻结合同不增字段，新增可信上下文是版本化内部BFF入口。原业务feature开关选择原生页面，默认旧路径不变。
技术：现有.NET/Npgsql/PostgreSQL、Next16.3.3/React19、严格无损JSON解码/编码，Node22.22.0逐进程运行。

## 最新前置和已完成范围

Main `3ac47c4191e839751f2383dfa08cff24c08e0d65`、CRM `0045bff214909b5282c975b583f79e8247beee2d`、Handoff `c1978b0a92fd81164751c6c5791e20a4c946ca06` 已远端核实。W103 Main515/CRM130/Handoff79完整合并finish，三准确分支删除、固定席位释放，181相关本地检查通过，实际65.42min（原估90–150）；不重复。目录连接由用户本机移除，目标依赖缓存完整；官方bind/resume已完成，旧审批阻塞已解决。

WP03-01完整前置仍按整卡保持NOT_CHECKED/PLANNED；本限定窗口只消费WP02-09已实际采用的配置读/保存Owner、原会话和当前授权，不以其他未完成Owner关闭前置。W82显式notice修订、W102/103服务端冻结请求/响应已具备。当前原生宿主不暴露旧 `/workspace` 与 `/leads/options`；旧Configuration.tsx使用扁平合同、未经登记的初始默认和不同ETag语义，不能直接改URL称为采用。

## 任务与预计实际时间

|任务|范围|实际执行估计|
|---|---|---|
|W104-01 / Main|登记WP03-01限定窗口、W103最终证据、台账及四状态收尾|10–20min|
|W104-02 / CRM|可信上下文与BFF、冻结配置页面、相关PG/HTTP/浏览器验证、单份交付|90–130min|
|W104-HANDOFF|一次START、原分支恢复交付与PR/席位收尾|20–30min|

本轮总估120–180min；Main START `2026-10-24T08:36:53.961777+00:00`。按Main最终finish计算墙钟并单列等待，不重复累加仓库耗时；持续目标仍为CP6-7D-20261022 ACTIVE。

## 实施步骤和文件边界

- [x] 可信上下文：`src/CP6.CRM.Api/Inquiry/CrmInquiryConfigurationOwnerV2.Context.cs`扩展原Owner，`CrmCommandIdentityV1.cs`只增加内部受保护会话的显示/CSRF材料。先Capture和真实read-purpose签发，后Serializable当前身份/精确pair/信任/登记检查，最终重核并提交后返回；无网络持锁、无续期、无任意浏览器actor/tenant/pair。
- [x] `CrmInquiryNativeHostV2.cs`与`apps/web/src/server/crm-proxy.ts`新增GET-only上下文适配，沿用inquiry-v2、no-store/no-referrer和严格响应边界，查询/写方法/外来凭据拒绝。上下文版本、原CSRF、实际源ETag/精确版本token、当前可用部门及显示信息均白名单返回，不返回JWT、权限证明或持久指纹。configure标记只作页面可用性提示，保存重新取得真实write-purpose证明。
- [x] `apps/web/src/inquiry/configuration-api.ts`处理上下文、严格ConfigurationView和ConfigurationSaveRequest、原body/ETag/幂等键；`configuration-editor.ts`负责无损输入，`ConfigurationPage.tsx`维护可重试操作状态并按现有CRM外观显示站点/表单、名称/notice版本和正文、部门、日历/效期和必要政策参数。初次政策与时段由用户填写；只自动提供冻结合同明确常量，不猜企业值。
- [x] 三个`app/(crm)/crm/site`页面按服务端`CRM_INQUIRY_NATIVE_ENABLED`选择新版组件，所有相关数字/时间按冻结精度传输；显示既有草稿不伪装公开生效。网络/503/提交后回执丢失保持同一body/key并禁止悄然换键；401/403清空可披露内容和待提交材料，迟到响应不恢复旧状态；412提示刷新源上下文。页面不增加尚未实现的预览/激活按钮。
- [x] `tests/CP6.CRM.IdentityPostgreSqlTests/InquiryConfigurationContextTests.cs`验证实际原生HTTP上下文、可信部门/源版本、profile/query/header负例、当前撤权和原会话不续期；同源和会话过期由相关BFF/Command_host回归覆盖。`apps/web/tests/inquiry-configuration.test.ts`和相关浏览器用例验证真实用户编辑/保存、精度、原键重试、拒绝披露及首次未填政策不能保存。
- [ ] 本地类型检查、受影响API构建、相关行为测试及集中审查已通过；CRM/Handoff正常PR、远端采用、定向分支清理和finish已完成。Main本提交合并及finish待最终核对。

开发只复用DEV-1和DEV-2；CRM PostgreSQL55446，必要实际Main互通55435；均为新自有隔离目录。外部metadata-only依赖缓存复用，不新增待删除junction、源码副本或工作树。测试输出留D:/CP6-local-test/wave104，不进入源码目录。Next当前本地指南已核对服务器/客户端边界与环境变量。

## 保留边界

首份法定政策内容和实际环境数据不编造；显式手工输入的本地测试数据只证明行为。预览/激活、数据库时间生效选择、公开Attempt/提交、正式outbox发送和常驻C2/即时跨仓撤权仍属后继。此轮不迁移既有库、不改变合同合法精度、不执行Actions/生产部署/保护变更。24/375、0/125、247NOT_RUN保持真实。

## 实际交付与生命周期

CRM [PR131](https://github.com/GTX537/CP6.CRM/pull/131) 已采用 `77dd02db308c5a746fe0ae672e0a8fc13e108185`，来源 `8a6b6368173db5fd06798d3f44ee9fdc47c96a60`；[单份交付](https://github.com/GTX537/CP6.CRM/blob/77dd02db308c5a746fe0ae672e0a8fc13e108185/docs/development/evidence/W104-02/DELIVERY.md)。

新增当前授权的版本化内部上下文：实际primary Site/Form、源版本token/ETag、原CSRF及当前可用部门。原会话不续期，签发网络在业务持锁前，当前身份/持久信任/精确配对及最终重核通过才返回白名单。configure标记仅提示编辑，保存/重试仍由原Owner新取write-purpose证明并最终授权。

同一服务器显式开关接入站点/表单/日历冻结页面，另补 `/crm` 登录落点进入站点页，避免原生宿主调用不存在的旧workspace/options。首份企业政策和日历显式输入，不造默认；合法数字/日期/高精度时间保留。未知保存固定原body/key/CSRF/源ETag，401/403清空且迟到响应不得恢复；412要求重新读取。草稿未公开生效，预览/激活按钮不提前加入。

63项最终适用的唯一相关本地检查通过、0跳过：9上下文PG/HTTP、26原会话/Owner回归、22Node、6真实React浏览器到Node BFF及原生HTTP/PG Owner。 API必要编译、四个服务器入口及组件类型、桌面/手机显示通过；证据 `D:/CP6-local-test/wave104/foundation/final-validation.json`。浏览器页面交付和签名身份来源为隔离夹具，BFF/HTTP/PG Owner为实际实现；没有Next完整SSR/build、本轮实际Main或生产TLS验收声明。412模拟页面不匹配ETag，保留源登记不可变，不伪造已实现后续发布接口。历史W103实际Main互通只引用未变范围，不计本轮执行。

自有PG55446 smart stop，HTTP/Node/浏览器退出；本轮未启动Main55435。依赖缓存原位保留，没有新增junction、源码副本或工作树。类型/输入/fixture UTF8/不可变登记及控件可访问名称失败均保留，修正后只复查对应范围。一次COORD集中审查；无Actions、依赖版本、保护、既有库或部署变更。

WP03-01仅配置读取/编辑/保存限定窗口采用；预览/激活、源登记发布、DB时间生效选择、公开Attempt/提交、正式outbox发送、常驻C2和跨仓即时撤权仍为后继。正式24/375、0/125、247NOT_RUN不提升；七天ACTIVE。
Handoff [PR80](https://github.com/GTX537/CP6.AI-Handoff/pull/80) 已采用 `6cbbde316a20d7b5eb85f6180d59b9743e420ecc`，原准确分支删除、固定DEV-2 finish释放。 CRM分支删除/席位释放已核对；Main本次正常PR及finish继续，当前START后墙钟60.84min（原估120–180），最终按Main finish计，不叠加各仓。

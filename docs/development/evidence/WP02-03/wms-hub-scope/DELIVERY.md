# W46 / BUG404：WMS真实Hub租户作用域

COORD本聊天单人；七天一期前置。Main基线`97b56ae91da46af4dc55fec784aa9e483e696c1b`；DEV-1 `codex/bug-404-wms-hub`、DEV-2 `codex/w46-handoff`。07:47:04Z START，初估实际20–40min，最终按finish修正。W37–W45已收尾，W45实际33.04min、Issue401关闭，不重复。

## 根因、修复与一次集中审查

[Issue404](https://github.com/GTX537/CP6/issues/404)为唯一BUG记录。先以实际Program登录/协商/WebSocket复现：非默认租户合法用户连接被type7关闭；运行日志定位CurrentPermissionContext.cs:56租户不匹配。SignalR的Hub独立DI作用域未经过TenantMiddleware，原直接构造Hub测试未覆盖该宿主事实。

最小修复只在PG AuthorizeInventory先从唯一认证连接身份读取tenant，绑定Hub作用域ITenantContext并核对CP6Context；随后仍由StockOperations实际Owner重新消费原登录会话和当前动作/范围来源。仓库/产品参数不选租户或额外组，原租户空载荷通知和非PG历史路径保留，无新SQLServer实现。

提取W45实际WebSocket测试体供MES/WMS共用，不重复维护握手与消息协议。WMS新用例覆盖合法调用、查询权撤销/恢复、原用户停用后拒绝。原PG直接Hub夹具补真实身份，与产品新入口一致。一次作者完整SPEC/QUALITY审查已覆盖3项产品/测试diff、当前Owner、组隔离、旧路径及证据；无剩余合并阻断，无代理。

## 实际验证

8项相关.NET通过（6项PG/实际HTTP-WebSocket与通知边界、2项旧路由兼容）；无前端变更，不重复前端验证。`D:/CP6-local-test/wave46/foundation/final-validation.json`列逐项结果。realtime-final包括WMS新真实连接1项、MES既有4项、PG库存组/空载荷1项、历史WMS路由2项。原wms-scope-red真实失败和runtime.log保留，未改写为成功。

自有PG/API停止，2处生成data经ownership/绝对路径/无进程/无重解析点检查后回收228653928字节；日志/TRX/ownership与复用缓存保留。未跑全仓/浏览器E2E/真实DEV-UAT/生产，当前结果不能证明这些环境验收。相关API构建随测试完成；Actions未触发，无工作流改动/取消。

## 剩余和生命周期

本修复只恢复已有库存Owner的真实连接准入，未改变库存业务、旧数据库历史或正式WMS合同。已撤权旧连接仍可能收到无参数提示；不承诺即时断开，不把提示当可靠投递。设备/OEE/计划独立Owner仍下一窗口，整卡/125目标/247运行不提升；七天持续目标ACTIVE。

产品正常PR、Handoff采用及Issue关闭待收尾；DEV-1/2仍属于本窗，集成席空闲。准确分支删除/工作区与席位finish见最终PR/管理登记。无新树/既有迁移/生产，根用户资料保留。

## 远端采用与原任务收尾

产品[PR405](https://github.com/GTX537/CP6/pull/405)和[Handoff22](https://github.com/GTX537/CP6.AI-Handoff/pull/22)采用，source`c09ee7f4bf32b735f98b45e8164d7a1fd84d45f9`、远端main`883709c885d6b13ca272c211f8dde06a7cbd939f`；13任务文件一致。Handoff source`d5ced62b53dc502690df1ce75dade0e0f8ef2b3b`、目标`ad787e92495ad409e96358190bf9939753fb1ddb`，3项资料一致，DEV-2准确分支删除/席位释放。

8项相关.NET通过（6项PG/实际HTTP-WebSocket与通知边界、2项旧路由兼容）；无前端变更，不重复前端验证。产品未变复用上述实际证据；最后文档仅diff/链接/台账检查。原DEV-1最后文档正常PR后finish、关闭Issue404；未提前声明释放。实际分支/工作区/席位/耗时见最终PR、实时登记与`C:\Users\tt\CP6-planning-reports\20261017-foundation\wave46-final-verification.json`。无Actions/触发变更/既有迁移/生产；七天目标ACTIVE，继续设备/OEE/计划独立Owner，不提升整卡/125目标/247运行。

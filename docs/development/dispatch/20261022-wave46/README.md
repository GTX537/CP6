# W46-01 真实 START / 七天一期前置修复

COORD本聊天单人；2026-10-22T07:49:10.357212+00:00；Main基线`97b56ae91da46af4dc55fec784aa9e483e696c1b`。W45 PR402/403、Handoff21已采用，Issue401关闭、准确两分支删除/席位finish，实际33.04分钟；W37–W45不重复。

P1 #404 WMS真实SignalR租户作用域修复；[Issue404](https://github.com/GTX537/CP6/issues/404)为唯一BUG记录。W45真实MES宿主发现独立DI作用域租户缺失，当前WmsHub有同样静态缺口；先在真实PG/Program WebSocket复现，不把直接构造Hub视为宿主通过。若复现，最小绑定当前连接tenant并保原会话/Owner动作再校验；不得退化为客户端编码授权或全连接业务推送。旧非PG历史实现不在新增数据库目标内改造。

主任务`BUG-404-WMS-HUB`，DEV-1 `codex/bug-404-wms-hub`；Handoff DEV-2 `codex/w46-handoff`。预计本聊天实际20–40分钟（复现/修复与相关验证10–20min、审查/合并与收尾10–20min）。复用W45实际WebSocket夹具和既有库存Owner；scope限定WmsHub及相关测试/报告，不混入Machine/OEE开发。无代理/新树/Actions/既有迁移/生产。按原固定席位/准确分支finish规则闭环，再继续D3设备/OEE独立Owner。

roadmap唯一任务状态，WP02-03局部修复不提升整卡/125目标/247运行，七天持续目标ACTIVE。

## 候选验证

WMS实际连接拒绝已复现并修复；8项相关.NET通过（6项PG/实际HTTP-WebSocket与通知边界、2项旧路由兼容）；无前端变更，不重复前端验证。正常PR、Handoff、Issue404关闭和准确分支/席位finish待执行。

[DELIVERY](../../evidence/WP02-03/wms-hub-scope/DELIVERY.md)。

## 远端采用与原任务收尾

产品[PR405](https://github.com/GTX537/CP6/pull/405)和[Handoff22](https://github.com/GTX537/CP6.AI-Handoff/pull/22)采用，source`c09ee7f4bf32b735f98b45e8164d7a1fd84d45f9`、远端main`883709c885d6b13ca272c211f8dde06a7cbd939f`；13任务文件一致。Handoff source`d5ced62b53dc502690df1ce75dade0e0f8ef2b3b`、目标`ad787e92495ad409e96358190bf9939753fb1ddb`，3项资料一致，DEV-2准确分支删除/席位释放。

8项相关.NET通过（6项PG/实际HTTP-WebSocket与通知边界、2项旧路由兼容）；无前端变更，不重复前端验证。产品未变复用上述实际证据；最后文档仅diff/链接/台账检查。原DEV-1最后文档正常PR后finish、关闭Issue404；未提前声明释放。实际分支/工作区/席位/耗时见最终PR、实时登记与`C:\Users\tt\CP6-planning-reports\20261017-foundation\wave46-final-verification.json`。无Actions/触发变更/既有迁移/生产；七天目标ACTIVE，继续设备/OEE/计划独立Owner，不提升整卡/125目标/247运行。

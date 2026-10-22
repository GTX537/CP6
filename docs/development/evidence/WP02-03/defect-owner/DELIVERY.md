# W40-01 交付：MES不良记录独立 Owner

COORD本聊天单人；基线`4ace44a582a4fcd605de38e4716031019603b4b8`；任务分支codex/w40-01，原DEV-1；Handoff codex/w40-handoff，原DEV-2。关联[Issue #389](https://github.com/GTX537/CP6/issues/389)。本地Ready to merge Yes；远端采用、分支与席位处置待实际核对。

## 实现和审查

DefectRecordService分页/单票/分类、手工创建/修改/删除接入当前原会话Owner，完整主资源/来源动作和图范围先于读取，序号/写入/最终权限原子提交。真实Actor、合法精度、旧Creator与默认状态行为保留；通知确认提交后发送，UNKNOWN不重放。三个实际GET补query，HTTP使用Owner安全错误和no-store。页面保护重复、旧身份/目标/响应、已确认结果及未知结果。合同见[Owner v1](../../../../design-handbook/remediation/main-defect-owner-v1.md)。无新授权/依赖/模型/迁移。

一次作者SPEC/QUALITY集中审查完整产品diff、独立/父入口范围、原生关系、整图/可信部门、旧新关联、锁顺序、事务/序号/通知、实际HTTP及页面时序；定向修正HTTP异常/缓存接线、已确认新建编号保留、迟到关闭/详情、旧CSV身份及看板不良来源同图过滤。最后修改部分和相关验证已复查，无本窗剩余合并阻断。普通状态文档只做diff/链接/台账检查；无代理和重复全仓审查。

## 实际验证

83项去重相关.NET成功：12项本轮原生PG/实际HTTP、55项既有Owner PG/HTTP、16项MES兼容/审计/权限；7项Vitest及完整vue-tsc --build通过。最终适用集合0失败/跳过。逐项来源`D:/CP6-local-test/wave40/foundation/final-validation.json`，按精确方法身份保留最新适用通过结果，非83项一次全跑。

|记录|实际结果和适用范围|
|---|---|
|red-20261022T024236Z|2项真实失败：缺Owner读取/写入均未拒绝；后续原步骤通过|
|owner-20261022T024727Z|6通过/3失败；缺HTTP no-store/Owner接线，序号全局累计及服务夹具未模拟真实会话的测试假设不成立；全部原记录保留|
|owner-fix-20261022T025600Z|10/10通过；修正上述HTTP行为/夹具假设，实际Program验证旧会话失效|
|final-boundaries-20261022T025929Z|11通过/1失败：新增检验并发用例的固定QI号碰撞现有全局唯一键；改用唯一夹具号，不改业务存储/合同|
|shared-owner-20261022T030144Z|57/57通过：2项本轮最终整图/并发，加55项既有Owner；覆盖新共享表围栏和看板来源影响|
|compatibility-20261022T030525Z|16/16通过：MES权限目录/属性、原品质检验自动QC链和相关审计|
|web-final / typecheck-final|7/7页面行为及完整vue-tsc --build退出0；更早6项页面及类型成功记录保留|

12项本轮原生PG/HTTP包括缺Owner、租户/自身含软删除图/原生键、可信部门和来源scope、当前动作/完整字段源、脏上下文/外来事务、冻结可变请求、真实最终action撤权回滚、丢失真实commit确认/通知禁发、序号增量回滚、raw检验子图写入在围栏等待和下一意图拒绝、实际Program登录/GET授权/CRUD/CSRF/无缓存/旧会话401。API夹具跳过全量初始化，目录由已有独立种子用例覆盖；Vitest不是浏览器E2E。无全仓/真实DEV/UAT或生产验收。

## 工作区与后续

5处自有PG/API已停止；按ownership、绝对目录边界、无进程/重解析点核对后回收生成data 577860693字节（0.538GiB）；原失败/日志/TRX/ownership保留。固定DEV-1/DEV-2待采用后finish，INTEGRATION-1空闲；零新树/clone/副本/代理，根用户未跟踪资料保留。

预计代理实际60–100分钟，故障余量3小时，24小时窗口内完成；实际耗时最终以管理finish核对。WMS库存QC/入库检验、MES品质检验自身Owner及补充/退货/设备/OEE/计划等独立入口、无订单正式部门绑定、采购/财务自身Owner、持久operation/outbox、可靠投递、workflow/CRM和真实DEV/UAT仍独立；完整WP02-03、125目标/247运行验收不提升。无Actions取消/触发/重跑、workflow变更、既有数据库迁移、真实权限授予或生产部署。W37–W39不重复。

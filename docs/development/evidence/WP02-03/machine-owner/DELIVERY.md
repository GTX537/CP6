# W47-01：设备/停机独立Owner与可信设备部门绑定

COORD本聊天单人；Main基线`6bada0cf13eeabcb8b25dab485e5c20435941258`，DEV-1 `codex/w47-01`，DEV-2 `codex/w47-handoff`；2026-10-22 08:08:39Z START，初估实际90–150min，实际以本任务finish为准。W37–W46均已收尾，不重复；七天目标ACTIVE。

## 行为与当前权限

[Issue407](https://github.com/GTX537/CP6/issues/407)记录真实红测：无Owner仍能读设备/修改状态，未获查询权的实际登录账号GET返回200。PostgreSQL MachineService全部独立入口现由OperationsOwnerV1消费原会话、当前Actor、动作/范围/组织版本；缺少同Context Owner或真实序号服务拒绝。Controller三个读取入口要求mes-machine:query，Owner错误保留401/403/503与no-store；新增catalog不自动授予任何角色，既有权限不作为默认新增授权。

范围1按真实Creator；2/3/4仅采用machine真实UUID到同租户健康部门的显式绑定，5保留全租户范围。向前迁移扩展W42原表原触发器，不改写旧迁移、不回填真实对象。IAM目录只查设备标识/名称，变更继续当前IAM动作、CAS、引用版本、原操作结果与审计。设备代码、WgCd/BaseCd/ProcessCd及当前用户部门都不替代归属。部门范围的新设备无可信绑定即整体回滚；预先可信创建流程另属后续任务。

停机在分页/计数前同时过滤设备、本人范围和可选关联工单的当前独立query/完整范围。设备列表的当前工单附加字段也要求工单Owner；无工单查询动作时只返回设备、隐藏关联字段和关联停机，不放大授权。旧TodayOee附加值在PG暂不披露，等待下一OEE独立Owner窗口；不是OEE已采用。

设备/停机写入的序号、状态、记录、已有字段审计共用原Context/真实PG事务，最终再次验证现权和对象；客户端userName不成为实际Actor。已知提交后才发空载荷MES失效提示，失败/UNKNOWN不通知、不伪称回滚，也未声称已有设备持久原操作查询。设备/停机纳入已有共同图写锁。保留既有合法输入/精度与历史非PG路径，不增加SQLServer实现。

设备页使用当前身份令牌与读取序号，切换身份/撤权清除保护数据，编辑重新读取单票；冻结写入并防重，旧确认不能发起删除，旧响应不能报成功。UNKNOWN阻止重发，随后403刷新也不能清除未知状态；新建/停机确认按钮和命令均检查动作。该UI保护不等于持久操作幂等，不能跨重新登录/重开页声称确定结果。

## 本地证据与一次集中审查

38项相关.NET用例通过（按TRX testId合并定向复查，含PG/实际HTTP/并发/迁移/种子），21项Vitest通过、其中5项修改后定向复查通过，完整vue-tsc -b通过。详细来源`D:/CP6-local-test/wave47/foundation/final-validation.json`。实际HTTP覆盖拒绝/恢复现权、no-store、成功状态修改及真实用户审计；PG覆盖三类部门绑定、跨租户原生拒绝、分页、自有及关联来源、最终失败整体回滚和提交回执丢失；向前迁移在已有租户上验证补catalog且不授权。旧MachineStatusMonitor的真实并发测试确认其乐观版本拒绝覆盖刚改变的设备状态，因此本窗不修改该后台；它仍是10分钟无报工启发式，不能当作正式运行事实/RES Owner。

红测3项真实权限缺口、5项原组件行为失败保留；最初ElTable测试替身错误及两个旧种子数量断言失败均保留，不冒充产品失败或成功。种子测试改为独立权限集合、不授新权和幂等核对。一次作者完整SPEC/QUALITY审查涵盖授权、事务/审计、绑定迁移、输入冻结、通知时点、前端状态与范围；无剩余合并阻断，不启动代理/全仓检查。

5处自有PG/API全部停止，经ownership、绝对路径、进程和重解析点检查后回收生成data 574069080字节；日志/TRX/runtime/ownership和复用缓存保留。未跑全仓、浏览器E2E、真实环境迁移或生产门禁；未触发Actions、未改变触发或取消其他运行。相关代码未变时复用上述证据，合并/SHA改变不重跑。

## 尚未采用与收尾

正式MES-RES-01资源/能力版本、Claim/StageA-B，MES-PERF-01 Owner快照/实际运行/OEE发布与SCHED适配仍未采用。旧默认480、状态启发式、字符串关联和单表OeeDaily不被认作正式合同。全任务24/375、125目标0交付、247运行NOT_RUN不因本窗口提升。

下一步正常产品PR与Handoff采用、原任务最后状态PR、远端blob核对，再定向删除两个准确分支并manage finish。DEV-1/2仅本窗占用，集成席空闲，根用户未跟踪资料保留。Issue407在远端采用及生命周期完成后关闭；完成后连续推进OEE/计划Owner，不终止七天目标。

## 远端采用与原任务收尾

产品[PR](https://github.com/GTX537/CP6/pull/408)和[Handoff](https://github.com/GTX537/CP6.AI-Handoff/pull/23)已采用，source`9295ebac050bb43c3330638a59e27e71dc2ef8b2`、远端main`276e05aeeed41c560777e1d73ced49d0108387b1`，26任务文件blob一致；Handoff source`0536f372b2507fe7dceb85aa6a12e98fe881f8a5`、目标`722cc9d6a9034d181cd26d0ec07e186d24327b6c`，DEV-2准确分支删除/席位释放。

38项相关.NET用例通过（按TRX testId合并定向复查，含PG/实际HTTP/并发/迁移/种子），21项Vitest通过、其中5项修改后定向复查通过，完整vue-tsc -b通过，仅最后文档diff/链接/台账检查；原DEV-1最后文档PR后finish、关闭Issue407。分支/工作区/席位/实际耗时以最终PR、实时登记及`C:\Users\tt\CP6-planning-reports\20261017-foundation\wave47-final-verification.json`为准，未提前声明释放。七天目标ACTIVE，下一OEE/计划Owner，正式范围与整体计数不提升。

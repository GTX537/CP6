# W83 / 七天一期D2-D3：材料缺料管理独立Owner、并发与无载荷通知

COORD本聊天单人，开工2026-10-23T22:15:20.853823+00:00。Main `9f9192152584b0afab5dd7afe3611a5cd4f3e0ce`，CRM `3e69fa188a43d261660fe871103bc2b7b400a674`，Handoff `fc0c7fa5169574ab083c72522bcc343411fe8656`。W82 Main467/CRM121/Handoff58全部采用、Issue109关闭、21相关用例及原/父/新revision只读检查通过；实际16.83min＋独立前置3min。三准确分支删除、固定树干净detached、全部席位释放，Actions0，无PG启动。W37–W82不重复。

## 核查与限定范围

最新AGENTS/POLICY哈希未变，官方status/capacity无告警；根目录原用户未跟踪文件保持，两非allowlist历史目录不动，无重复开放shortage Issue，登记[Issue468](https://github.com/GTX537/CP6/issues/468)。复核MaterialShortage/Outbound/Backflush/Pur生成服务、当前OperationsOwner/原生来源范围、现有SignalR空提示、页面与实际PG模型。

现列表仅Authorize，服务无独立Owner；Resolve/Dismiss按Id/OPEN写入而没有当前来源/字段/CAS。缺料通知仍以Clients.All广播WorkOrderNo/ProductCd/RequiredQty，真实PG出库/反冲调用也可到达。页面数值使用Number减法且无授权epoch。旧状态标记只是跟进记录，不能表示实际补货、采购创建或库存处理完成。

1. PG具名MATERIAL-SHORTAGE-OWNER/1读取与处理入口接OperationsOwner当前原会话、wms-material-shortage query/resolve/dismiss、原生业务锁与提交前最终重核。独立scope1核实际Creator及来源整对象；2–4沿真实WorkOrder订单/无订单UUID绑定；scope5仍租户/来源/仓库约束。必须与mes-work-order:query范围取交集；有RelatedOutboundNo时核真实关联的出库及其独立query范围/仓库区位，无源/错源不回退。按授权后集合计算总数/未处理数与稳定分页，不用页内行数冒KPI。
2. Remark采用当前字段MIN读取/写入决策；新命令显式SetRemark，避免隐藏字段被空值覆盖。真实RowVersion必需且提交CAS，服务器盖当前actor，输入在首个await前冻结；旧无版本写入PG明确拒绝，陈旧/冲突不重试。旧无精确表示读要求具名新版。新DTO不回传整实体/额外actor或租户字段，数量与差额保原21,8精度字符串，不经JS Number。
3. 保留真实出库Owner产生缺料的同数据库/事务原子性：通过内部不可伪造OperationsAccess能力验证当前allocate父路径，公开Create不能在PG绕过父Owner写入；不能因新增独立入口而要求已授权父操作另持独立管理动作。原有父最终授权/已提交后通知规则不变。只扩展实际受影响的一处父调用与定向回归。
4. PG缺料通知复用可信租户库存空失效提示，不广播单号/物料/数量，不在未提交/未知结果时发布；保留明确非PG历史行为。实际SignalR适配器及生产DI路径定向核对。采购GenerateFromShortage旧直读/估价/建申请未有自身最终Owner与原子作用者，PG在读取/编号/价格/写入前明确拒绝；正式采购采用留独立任务，本窗不实现其新业务合同。
5. query目录新增只登记、不自动授角色；field/scope与单个PG目录迁移串行修改。HTTP no-store和统一Owner错误；页面采用版本数据、精确字符串、实际CAS与字段/动作约束，身份epoch/乱序/失败清空行/KPI/编辑框。成功只表示缺料跟进状态改变，不显示库存已补足。

## 验证、耗时、生命周期

预计实际60–90min：实现30–45、相关PG/页面验证20–30、一次集中SPEC/QUALITY与合并收尾10–15。独立前置约2min；W82收尾内只读不重复计时，实际以本任务start/finish为准。保持七天ACTIVE目标。

真实PG定向验证缺Owner、原会话/撤权、tenant/self/部门/source/warehouse交集、隐藏字段、精确极值与分页、实际RowVersion竞争、最终授权失败/未知提交与父原子创建、HTTP/生产DI无全体载荷；UI核权限/epoch/乱序/失败清除和版本提交。已有成功未变证据直接引用，历史非PG相关测试保持真实范围；不做全仓回归。自有PG夹具55435串行启动停止，保存真实红灯/数据。无Actions、既有环境迁移或生产部署。

DEV-1 `codex/bug-468-material-shortage-owner`承担完整产品与协调，DEV-2 `codex/w83-handoff`保存START/DELIVERY，固定路径复用、不增加工作树/代理。按正常PR核远端、定向删除本任务分支和finish释放。24/375整卡、125目标0完整交付、247正式运行NOT_RUN不提升；本窗不关闭完整WP02-03、采购Owner、CRM公开受理或正式库存/财务验收。W51停止data删除的自动审批拒绝blocked by policy继续保留不重试。

## W83验证完成

37独立.NET用例（21原生PG、15既有非PG行为、1两租户启动种子）及9Vue用例通过，无跳过；相关构建和vue-tsc通过。集中审查补新租户WmsPermissionSeed.OwnerActions登记，不授query角色；范围同任务串行扩展至该直接目录消费者及现有种子行为用例。真实双连接一个版本提交，源权限前过滤/计数和隐藏字段保值通过；5个PG停止保留。进入两仓正常采用及同任务最终收尾，七天ACTIVE。

## 实际采用与最后收尾

Main [PR469](https://github.com/GTX537/CP6/pull/469) source `778338a1048ca9bc41752ac691a27c68b6604013`采用远端main `3b3855ebd2b8125a90e7e1674ada216c8ae95a73`，29任务路径逐blob核对；Handoff [PR59](https://github.com/GTX537/CP6.AI-Handoff/pull/59)采用`6ba2532ea511b004e025170c0c3fee3ec398250d`，准确分支删除、固定树干净detached、finish释放。19个产品路径与已测提交逐blob一致，复用有效结果，最后文档diff/台账生成单独检查。

Main同任务最终状态PR/finish及Issue468最后关闭待完成；当前约16.56min，原估60–90min＋独立前置2min，实际按finish不重复计Handoff。最终证明`C:\Users\tt\CP6-planning-reports\20261017-foundation\wave83-final-verification.json`。无Actions、既有库迁移或部署。七天目标ACTIVE，继续剩余独立Owner及D5/D6真实前置，正式procurement/Finance仍未采用。

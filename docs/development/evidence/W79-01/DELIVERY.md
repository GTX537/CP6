# W79-01 / Issue460 工作中心与工序费率独立入口当前Owner

COORD本聊天单人，2026-10-23T21:05:17.156508+00:00。Main基线`3ab6662229c1ccb70cb2b377cb96fea686d7b3ec`；DEV-1 BUG-460-WORKCENTER-OWNER / codex/bug-460-workcenter-owner；DEV-2 W79-HANDOFF。唯一产品报告，[Issue460](https://github.com/GTX537/CP6/issues/460)。

## 行为与边界

旧服务仅持DbContext，独立读写不核当前Owner；两条原生PG用例复现无Owner仍读取及伪造调用者写入。现两服务全部PG公开入口均要求同上下文OperationsOwner：原会话、tenant、当前动作/角色/组织/字段、原生事务和最终重核。原始非PG私有Core只保历史范围；请求身份、tenant、Creator、审计/删除状态不复制到新对象。

本人依据实际Creator；部门/下级/自定义依据明确同tenant work-center UUID绑定；全域仍需当前动作。费率部门以实际存活同tenant工作中心关联为源，在费率自己的授权范围计算，不把WgCd字符串或当前操作者部门推定为归属，也不继承工作中心其他动作。新建部门范围未有可信绑定即原子回滚；scope1/5可创建，再由IAM显式绑定。旧部门映射七kind、原CAS/原操作结果/审计守卫保持。

新增两资源才锁WorkCenter/ProcessCostRate表并扩预跟踪保护，继续使用既有权限原生围栏；外层事务、脏上下文或已跟踪业务对象拒绝且保留调用者工作。新建对象分配实际ID，既有对象要求原ID/WgCd身份和PG持久RowVersion。缺版本/旧版本409；旧无版本DELETE明确拒绝，页面走版本删除。删除工作中心为原有软删除，费率因父对象不存活不可见，源行保留；已用含删除业务键不得重建而静默重挂历史费率。

费率区间沿用闭区间和实际PG时间语义，重叠在同一Owner表锁内检查；更新不得借另一WgCd移动原行。字段按已接受的真实存行数值MIN再默认，1可读写/2只读/3隐藏，明确1不能被误解为拒绝优先。新DTO隐藏费率返回null；更新省略/null表示保留，创建两费率必填。单字段写入核当前CanWrite，删除费率需两个费率均可写。旧实体List/Resolve不能表达隐藏值，字段不可读时明确403，绝不以0代替。

明确本地接口版本`MES-MASTER-OWNER/1`，HTTP `/api/mes/work-center/owner-v1`、`/api/mes/process-cost-rate/owner-v1`（列表/详情或resolve、upsert、delete）；不是归档合同的无版本改写。原有decimal(21,8)所有非负值可用13位整数＋8位小数字符串无损表示，服务和Web不经JS Number，越界或需要小数舍入明确400。旧.NET decimal写入格式化保留全部有效小数后验证，不能先四舍五入伪装为有效输入。版本以opaque base64传输。原日期合同仍遵循既有PG行为，包括Npgsql实际支持的最小/最大DateTime；页面仅显示时截日期，未编辑时间原字符串不截断。

两MES HTTP旧入口及新入口均核当前query/edit/del并接Owner错误/no-store；新增query只登记目录，不自动给既有角色授权。IAM对象选择器只返回实际ID及工作中心名称；单个后继PG迁移扩kind、保实际对象/租户/部门/不可变身份约束，全部只在新隔离库验证，无既有环境迁移。

CostCollect旧流程没有Finance Owner且有缺费率自动估算路径；PG Collect在任何查询/编号/成本/审计效果前503 FINANCE_OWNER_REQUIRED。集中审查补该Collect HTTP动作的Owner错误映射与no-store，避免通用500；这是同一消费者安全拒绝接线，不实现Finance Owner。其他Finance读/结转/完整成本核算仍未在本轮采用，不能把本轮单据主数据维护当完整财务交付。

两个页面冻结提交、携原ID/版本；创建清旧ID/版本，隐藏值不造0，只读/隐藏费用不回写。身份变化、读取失败和旧响应清受保护数据；失败/409不报成功，UNKNOWN保留不确定状态，即使刷新列表也不自动解锁重发。正常已知成功可重新读取；动作函数本身亦核当前界面权限，服务器Owner始终权威。实际页面组件配API替身测试，不宣称现场浏览器E2E。

## 验证与集中审查

37独立.NET（25真实PG、12既有InMemory）＋25Vue通过，无跳过；相关构建及最终Vue类型检查通过，合并去重共62条；不按报告数累加重复用例。一次作者完整SPEC/QUALITY已检查Owner原会话/范围/字段、原生事务及审计回滚、未知提交、真实父身份、精确数值与版本、迁移旧kind、HTTP接线及页面身份。修正仅对受影响行为复验，其余有效结果复用；无剩余阻断。

- `owner-red-20261023T202842Z.json`保留2条原生缺陷真实红灯。
- `owner-first-20261023T203226Z.json`25/25：13新PG＋12既有InMemory（WorkCenter3/Rate5/CostCollect4）。
- `owner-boundaries-20261023T205137Z.json`18项，17通过/1失败；失败源于新增测试把MIN误写为拒绝优先，核对Sys_RoleFieldPerm及既有聚合契约后修正测试，未改权限算法。`owner-field-contract-20261023T205323Z.json`1/1，验证单角色隐藏/只读、双角色显式1及移除后当前结果。
- 并发为真实第二连接：重叠区间第二Owner实际等待权限锁，第一提交后第二409；原生撤权先于围栏则零写入，围栏内则等待提交后生效；父表原生删除遇55P03。最终来源失败回滚主数据/费用/期间/审计，丢失commit确认报告UNKNOWN而不伪称零效果。
- `owner-reviewed-20261023T210040Z.json`6/6：真实Finance Collect HTTP503及5条旧部门绑定定向回归。`owner-temporal-20261023T210232Z.json`1/1：既有原生时间极值、有效期闭边界和反向区间。
- `ui-third.json`15/15真实SFC行为；`ui-first.json`中独立IAM套件10/10复用。最初UI红灯`ui-red.json`/`ui-second.json`是组件桩缺失，不能当旧业务问题已被测试复现；`ui-first`另一套件因箭头对象语法失败，已修正。真实失败均保留。
- `type-check-fixed.log`空且exit0；初次语法失败日志保留。最后界面仅空白行清理，成功结果复用。普通构建保原有依赖警告，未做漏洞清理或全仓回归，未宣称生产候选检查。

六个本轮自有PG簇均ownership/stopped存在且无postmaster.pid，实际复用现有夹具55435端口串行运行；START预留55447未使用，台账已纠正。全部数据、日志、TRX保留。无Actions、生产部署、自动角色授予或既有库迁移。

## 未完成范围与收尾

完整工艺/成本/Finance Owner、生产业务验收、CRM Site/Form最终权限/配置保存真实Owner/投递和公开生命周期仍待落实。24/375整卡、125目标0完整交付、247运行NOT_RUN保持，D3仍部分采用，七天目标ACTIVE。原估120–180分钟＋独立前置6分钟，实际以Main start/finish计，Handoff不重复累计。

候选通过后依次Main产品采用、Handoff交付与finish、同任务Main最终状态/PR/finish，远端核对后准确删除两任务分支并释放固定席位；现在尚不提前声明已释放。W51指定停止data删除曾被自动审批以blocked by policy拒绝，保留不重试或绕过。

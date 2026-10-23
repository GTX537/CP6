# W79 / 七天一期 D3：工作中心与工序费率独立入口当前Owner

COORD本聊天单人，开工2026-10-23T20:22:59.694495+00:00；Main `3ab6662229c1ccb70cb2b377cb96fea686d7b3ec`，Handoff `995822bcff997ff6d7d5e8a2fbf347b0b1ba7609`。W78 Main459/CRM119/Handoff54全部采用，37独立PG通过，实际27.07min＋前置3min；三准确分支删除、固定树干净detached、全部席位释放，Actions0、自有PG停止保留。W37–W78不重复。

## 前置、范围与估时

AGENTS/POLICY哈希未变，status/capacity无容量告警，原非allowlist两目录保持。查无同类开放Issue后登记[Issue460](https://github.com/GTX537/CP6/issues/460)。本窗回到七天计划明确包含的D3工作中心/费率入口，采用现有OperationsOwner、当前原会话、IAM真实对象部门绑定、PG持久token及本地验证夹具。CRM配置保存基础已到原生Outbox；当前Site/Form最终授权、真实投递和公开生命周期仍保留未交付，不把DeptIds改称Site/Form范围。

WorkCenter/ProcessCostRate旧服务只有CP6Context，直接读写；HTTP GET只有登录，写入外层权限不能代替服务Owner；费率字段裸返回。两实体继承BaseBizEntity，保已有tenant过滤/审计/RowVersion，无直接部门字段。已有WgCd关联不能推断历史部门；本人依据真实Creator，部门/子部门/自定义依据明确UUID映射，工序费率从真实同tenant工作中心关系确定部门来源。代码/当前调用人不能替代绑定。

预计本聊天实际120–180min：Owner/范围与原生绑定40–60，版本/字段/精确HTTP页面35–55，PG/HTTP/页面定向验证及合并收尾45–65。独立前置约6min；W78结束前只读和已记时内容不重复累计。沿用同一七天ACTIVE目标，不另起目标。

1. 工作中心List/Get/Upsert/Delete和费率List/Resolve/Upsert/Delete均走原会话+当前动作/权限源的原生事务，作用域先于结果返回；写入重核实际对象/父关系及当前权限。扩展OperationsOwner资源、按实际影响锁表和业务脏上下文识别；保租户、明确操作者，拒绝请求伪造身份/审计/删除状态。scope1实际Creator；scope2–4显式work-center UUID绑定；scope5当前完整授权。新建部门范围不自动推定操作者部门。
2. 费率LaborRate/OverheadRate按当前字段MIN进行只读投影及写入门；未授权字段不以0冒充合法值。原.NET Resolve的财务计算消费者不能获得屏蔽为0的实体或自动回退估算。CostCollect当前尚无Finance Owner，相关PG旧消费在任何编号/成本写入前明确拒绝，不把本窗当财务归集采用；既有非PG历史测试保留。
3. 维护保实际PG RowVersion，更新/删除要求原版本，拒绝同业务键覆盖不同对象；原工作中心和费率WgCd身份不通过编辑静默迁移。费率期间冲突在Owner锁内串行校验，失败与审计共同回滚。产能/费率按既有decimal(21,8)存储语义无损处理，越界/需舍入不静默存储；Web不能经Number或固定两位输入破坏八位精度。必要时增明确版本Owner DTO/端点，字符串精确表示并注明无损依据，保旧入口真实历史范围和安全拒绝，不隐式改变已有归档合同。
4. 实际HTTP采用当前Owner错误码/no-store，query动作只登记目录不自动授角色。UI接精确Owner响应/版本，身份变化与乱序/失败清理数据；隐藏字段不伪造0，拒绝/冲突/未知结果不显示成功、不自动重发。正常成功可以主动重新读取当前值。IAM绑定UI补真实work-center对象选择；新PG迁移只在隔离测试库验证，不执行既有业务环境迁移。

允许：两服务/接口与显式DTO、OperationsOwner/Access、IAM显式映射、单个PG后继迁移、两HTTP、Program/目录seed、CostCollect相关PG入口阻断、相关Web页面/API/types/IAM选项、定向Tests及本任务docs。公共合同/目录/模型编辑本聊天串行，不为未涉及领域做重构。

验证覆盖实际无Owner失败、tenant/self/部门树/自定义/当前撤权/字段、真实父身份、精度/版本/并发期间冲突、异常回滚/UNKNOWN、绑定原生守卫、实际Program/login/HTTP及页面身份/失败/精确数值。只做相关构建/测试和一次集中作者SPEC/QUALITY，已有输入未变证据复用。24/375整卡、125目标0交付、247运行NOT_RUN不提升；D3当前入口不冒充完整工艺/成本/财务Owner。

DEV-1 `codex/bug-460-workcenter-owner`，DEV-2 `codex/w79-handoff`固定席位，不新增代理/工作树。正常PR、远端采用、准确分支清理和finish；停止自有PG保数据/日志/真实红灯。无Actions、角色自动授予、既有环境迁移或生产部署。W51指定停止data删除曾被自动审批以blocked by policy拒绝，保留不重试。

## W79验证完成

37独立.NET（25真实PG、12既有InMemory）＋25Vue通过，无跳过；相关构建及最终Vue类型检查通过。实际既有夹具端口55435，55447预留未用。补Finance Collect这一动作的503/no-store错误接线，保其余财务剩余范围；不改变字段MIN、原生时间语义或完整验收。六PG停止保留，进入两仓采用及同任务收尾，七天目标ACTIVE。

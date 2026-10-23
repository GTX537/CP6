# W70 / 七天一期 D2 优先安全修复：RMA独立查询Owner与未采用命令阻断

COORD本聊天单人，真实开工 2026-10-23T16:01:09.069548+00:00。Main `56ccc3c7e3e7087ed921426d656108ce1fbf5b24`；Handoff `8e9652dcaa45e0cb0ea6326f37f645d31c502ee8`。W69 Main444/CRM112/Handoff45全部采用，最终21项相关PG通过；实际登记47.72min、前置约17min，三分支删除、固定树干净detached、全部席位释放、Actions0。W37–W69不重复，七天目标ACTIVE。

## 前置与任务

AGENTS/POLICY哈希未变，status/capacity无告警。查询开放Issue为空后登记[Issue445](https://github.com/GTX537/CP6/issues/445) P0。当前RmaService无独立Owner，GET仅登录，搜索/明细未按当前对象/仓库/字段范围过滤；Create直接置Authorized，Receive拼接RMA-HOLD并直调库存，Judge直接MOVE/ADJ，Close先保存后触发贷项。此前D2已登记正式RMA适配器未采用，本轮补堵现有独立入口，不重复W42绑定或W44库存原结果。

已核对原RMA R3第一分册的Owner、安全、字段和SPEC01–04相关段落，以及七天D2前置调整、当前服务/接口/控制器/实体、OperationsOwner及机器/盘点采用模式、源字段MIN与当前目录。原合同申请与授权分离；收货必须WMS-IN-RMA-ADAPTER-1，同库MainUow采用未证即拒绝；五类终端处置Effect Owner仍REQUIRED_FENCED，不能用旧本地状态或库存调用冒业务事实。完整RMA新模型/来源cut/授权SOD/质量/商业与财务仍未交付。

## 实现范围

- PostgreSQL所有RmaService公共入口接独立当前原会话Owner，控制器增加query权限。查询在分页前应用当前租户、本人/部门/子部门/自定义/全域范围和仓库范围，最终事务复核身份/角色/字段/组织版本。部门来源仅沿实际存储的OriginalShippingNo到存活Outbound/Order，不由WarehouseCd、用户部门或文本猜归属；没有可信父链的部门对象不可见。本人范围核头与明细的既存Creator，仓区无法证明时拒绝，RoleId=1不跳过动作/对象/字段。
- 列表只返回查询必要信息，不下发客户名称、操作者及自由文本；详情CustomerName、OperatorCd、ReturnReason、Remarks按完整注册字段域及当前MIN规则投影，明细Remarks沿同字段约束。缺目录/身份/Owner/结构或字段来源异常拒绝，不能把不可用当空列表。不存在新PII用途或正式R3字段映射完成声明。
- PostgreSQL旧Create/Receive/StartInspection/Judge/Close/Cancel均先核当前动作和已有对象（创建无既有对象），随后RMA_OWNER_ADOPTION_UNPROVEN拒绝，业务表/库存/编号/审计/ERP不写不发。创建即授权、猜位置、直接质量处置和取消覆盖均缺正式模型，不留旧路径后门。SQL Server/旧测试历史行为保留，不新增其实现或验收。
- 提供当前只读能力结果；页面仅查询，命令按钮默认不可用，明确业务功能尚未开通；查询/能力失败清理过期数据并显示错误，不展示成功或假空数据。权限directive继续生效，页面不是最终权限门。
- 注册wms-rma scope/字段及query动作目录；前向PG迁移只补缺失动作目录，不授予角色，不运行既有环境迁移。RMA原表加入本资源原生读锁与脏跟踪隔离，不给其他Owner增加RMA表锁。无新依赖或新生产数据库。

## 时间、验证及收尾

前置只读约6min。预计实际90–140min：Owner/范围/字段30–45，命令阻断及页面20–30，实际PG/HTTP/页面、一次作者SPEC/QUALITY及合并收尾40–65；最终以start/finish记录，子任务不重复累计。

DEV-1 `codex/bug-445-rma-owner`；DEV-2 `codex/w70-handoff`；复用固定目录。相关隔离PG夹具55435只建自有库，真实失败保留，结束正常停止保留数据。先复现无Owner独立读/写，再验证有权范围过滤先于分页、跨租户/撤权/字段隐藏、实际Program登录HTTP、所有未采用命令零副作用和页面只读/错误状态；复用未变成功证据，不跑全仓/Actions。

一份交付、Main台账/四状态/生成页、Handoff START/DELIVERY、正常PR和远端精确采用后准确分支清理、finish释放。WP02-03整卡及完整RMA不提升；24/375整卡、125目标0交付、247运行NOT_RUN保持。CRM完整Bundle/Owner前置仍留D6，Issue109保持OPEN。W51指定停止data删除自动审批曾拒绝，原因为blocked by policy；保留且不重试。

## W70验证完成

20项独立.NET用例通过（19项最终组合运行成功复用、1项异常包装断言修正后复验），7项页面用例、Vue类型检查及相关Main构建通过；无跳过。四个PG停止保留，原始红灯、编译与夹具异常断言失败保留。进入两仓采用及同任务最后收尾；完整RMA不提升，七天目标ACTIVE。

## 实际采用与最后收尾

Main [PR446](https://github.com/GTX537/CP6/pull/446) source `748477dfa7a67d8b5cb8f4b6ba688b07585b2d8e`采用远端main `8080bd7dda999bc2f8593866458a534f469b9631`，24任务路径核对通过。Handoff [PR46](https://github.com/GTX537/CP6.AI-Handoff/pull/46)采用 `8a83015c13b7abc3305928f9ed3200ddc17551a1`，准确任务分支删除、固定树干净detached、finish释放。十四个产品路径与已测提交逐blob相同，复用有效成功结果；最后文档diff和台账生成单独检查。

Main同一任务分支最后状态PR及finish待完成；Issue445在最终远端和生命周期核对后关闭。当前约58.00min，原估90–140min，最终以finish为准，前置只读约6min、子任务不重复累计。最终证明`C:\Users\tt\CP6-planning-reports\20261017-foundation\wave70-final-verification.json`。无Actions、既有库迁移或部署。七天目标ACTIVE，继续REP独立Owner与正式Transfer等前置；D5/D6其余范围保留。

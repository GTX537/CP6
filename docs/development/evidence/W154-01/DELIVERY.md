# W154-01 实际资源来源目录与普通资料原操作

本聊天COORD单人，Main基线5ab8b45be51aca261a0012c1b640f3f3bc15a904，固定DEV-1/DEV-2。[计划及合同依据](../../dispatch/20261026-wave154/README.md)。预计60–90分钟，Main START至本登记25.73分钟；最终以finish/proof计，不叠加Handoff。原七天目标ACTIVE。

## 实际交付

现有Machine和WorkCenter仍为唯一可写主资料。Main.Mes.ResourceSource.v1读取同一原UUID/租户的来源原文、SHA256和原生单调版本。PostgreSQL前向迁移在原表同事务捕获INSERT/UPDATE/DELETE，历史不可更新/删除/截断，头与原件延迟外键且单调；硬删除产生不可复用身份，历史读取仍要求当前原对象权限。已有行只从实际迁移观察开始，不伪造此前历史、不改业务行、不自动授角色权限。

来源分别记录资料、原能力描述、旧归属代码和控制轴；只在相应内容变化时升轴版本，机器瞬时Status及修改人/时间不升技术来源版本。原默认480、null和原数值真实保留；十进制通过字符串保留PostgreSQL精度，不把旧BaseCd推导为Site UUID、不把能力字段推导成批准单位/资格。当前源必须存在、捕获触发器有效且原表体与当前原件一致。

两类Owner使用原mes-machine/mes-work-center当前query/edit和原UUID对象范围，含租户、创建人/部门及最终身份检查。普通编辑只允许name及机器制造商/型号/日期/备注；明确patch区分省略、null、空值，拒绝重复字段、非法日期及能力/归属/启停/代码修改。准确Owner/Kind/UUID/version/digest检查防止过期覆盖；同一原表、来源版本、审计和Main原操作处于实际PG事务。无变化不增加来源版本；同键异体拒绝，原键可查询COMMITTED/UNKNOWN或关闭，关闭键禁止晚写。

独立路径/mes/machine-sources与/mes/work-center-sources从原页面进入，使用原query门和edit能力；支持稳定分页、当前资料、明确历史版本、原值/版本展示和普通编辑。身份/路由切换及当前权限拒绝清理私有内容；恢复只保存租户/用户命名空间下operationId/kind/objectId，Web Locks协调多页，UNKNOWN不重发，恢复提交后清除旧编辑器。用户所需来源版本信息可见，不声称生产资格。

## 实际验证与集中审查

46个不同本地PG18/实际HTTP用例、29个组件/恢复/IAM用例全部通过，无跳过；vue-tsc及Vite通过。原Owner相关行为按实际影响定向验证。证据D:/CP6-local-test/wave154/foundation/final-validation.json绑定最终源码/测试blob；生成脚本和原始失败记录保留在同目录及规划报告目录。

验证包括准确数值/空值、分轴更新和Status无变化、patch省略/null/日期/受控字段、旧版本/current权限/跨租户拒绝、原键重放/异体/UNKNOWN关闭、真实并发CAS只产生一后继、提交中断整体回滚、捕获失效及原体不匹配拒绝、不可变历史/禁止身份复用、真实前向迁移不改旧数据/角色授予、HTTP no-store及实际编辑。页面覆盖精确值/Ref、历史不覆盖草稿、最小恢复/不重发、身份切换迟到响应丢弃、403清理和工作中心独立编辑。

失败如实保留：先验证未实现HTTP路由返回404；测试attribute和前端路由/patch泛型推断的编译错误修正。组合PG检查43/45时，新回滚测试误计其他租户历史，已改按tenant/UUID计数；旧Machine迁移用例在共享数据库重放早期约束，撞到后续工作中心夹具，原用例未改，在独立新数据库重新验证。未把该失败抹去或跳过必需门禁。

审查发现命令应先冻结再校验，已改为校验冻结副本并复测12项受影响来源用例；34项未变原Owner检查保留真实来源，不重复计数。一次作者SPEC/QUALITY审查完整diff，核对实际原源、单写/精度/独立版本、数据库锁/当前Owner/同事务原操作、隐私清理与恢复、全部失败处理、台账范围与链接。无未解决实质阻断。无Actions、Docker、部署或既有环境迁移；本地测试不冒充真实企业业务验收。

## 剩余范围与收尾状态

本来源适配器不声明采用完整RES-WRITE-1、正式Capability/UOM批准、Site/Assignment时间计划、资格/Claim或生产Start准入。完整WP08-16、工艺/BOM、原WO/SCHED五域同事务和原七天D2/D5/D6/D7出口仍须继续。Continue real capability/UOM/assignment controls and original resource/material/tool prerequisites for routing/BOM and original WO/SCHED; preserve every original D2/D5/D6/D7 exit and real external prerequisites.

Handoff正常PR https://github.com/GTX537/CP6.AI-Handoff/pull/130，source e3bd1f17be66b8946bdec4476c7144789307a2dd，目标 53091ddccd5abd2d3a5792044491694dc16d4f96，精确分支已删除、DEV-2官方finish释放。Main此提交保留真实收尾前快照；正常PR、远端精确采用、分支清理和官方finish由最终proof确认。固定目录/缓存保留。

W153仅归档一次，WP02-13.wave153_nested_source_fence按最终proof关闭；本轮只新增WP08-16.wave154_resource_sources限定记录。375整卡仍24DONE/2IN_PROGRESS/349PLANNED，125完整目标0、247运行NOT_RUN、41历史静态不变。仅D1/D4整阶段完成，2/7=28.6%是阶段比例，不能当作工时/功能完成百分比。

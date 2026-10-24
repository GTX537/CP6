# W96-01：库存调整实际页面与原操作恢复

COORD本聊天单人，WP02-10/D4。[开工与范围](../../dispatch/20261024-wave96/README.md)。Main基线`cf6fbd214c638d917f4cf617ee3a6d876ee77ea5`；`W96-01` / `codex/w96-01`，固定DEV-1 Main、DEV-2 Handoff复用。实际start2026-10-24T03:14:39.959147Z，原估60–90min＋独立前置约6min，finish计时且不重复Handoff。W95已完整收尾证明回填，未重做。

## 实际交付和审查范围调整

StockQueryView真实库存行接入ADJ调整对话框；读取当前服务端tenant/actor scope，当前query和adjust决定UI，服务端Owner继续核实际动作、字段、对象和仓区。冻结原业务键、单位、所有者与备注，保留尾空格；不把客户端Operator作为身份、不回填舍入后的库存价格。输入是带符号差分，按现有decimal(21,8)范围精确文本传送，整数13位／小数8位，不经JS Number、不舍入。既有库存单价不被此表单修改；API既有数字调用仍兼容，v1后端DTO、规范输入、数据库和Owner未改变。

公共恢复引用只有operationId/kind，按服务端tenant/actor分区，发送前持久保存并回读核实；浏览器Web Locks采用ifAvailable独占，第二标签页不会排队等前次成功后另造新键。缺安全锁、存储失败／损坏、scope变化时不发命令。原引用不随刷新、注销、403、超时、UNKNOWN、错误响应或写权限撤销而删除；所有响应要求当前身份／页面代次、原编号、种类和完整流水号匹配。当前query可以查询或显式close，只有COMMITTED/CLOSED终态才释放；close已提交操作只返回原结果，不能撤销库存。无自动重发／换键。跨标签storage变化同步恢复区、清除旧草稿，身份／撤权／卸载拒绝旧响应。该持久性是同源浏览器存储保留期间的恢复引用，不替代服务端原日志或站点数据删除后的查找入口。

集中审查发现旧MoveCoreAsync新库位默认属性问题，隔离原生PG重现CUSTOMER变SELF，登记[P1 Issue503](https://github.com/GTX537/CP6/issues/503)。原计划新增MOVE提交因此未采用：本轮移除了新MOVE按钮和对话框提交路径；原MOVE API、业务合同不改写，已有同格式MOVE引用仍可按原编号恢复。此范围变更是明确前置缺陷，必须独立修复后继续移动页面，不能以隐藏按钮或本轮交付关闭Issue，也不把本轮称为完整移动功能。诊断仅执行至第一个所有者失败，其余单位/QC/召回属性由源码定位，尚未宣称各自运行断言通过。

## 验证与集中审查

1项原生PostgreSQL实际HTTP端到端＋28项去重Vue/client检查通过，vue-tsc通过；另1项MOVE属性诊断红灯留为Issue503，未计通过。一次集中SPEC/QUALITY覆盖完整产品diff、v1规范输入不变、精度、原键生命周期／跨标签竞态、当前身份和权限、页面真实接线；发现的MOVE阻断已从本次新增写入口排除并单独登记。ADJ与恢复的适用门禁无未解决失败。

- `ui-http-20261024T031759Z.json`：实际Program登录/CSRF HTTP把`9007199254740.12345678`、`-0.00000001`精确落库；MOVE既有接口双腿数量与原结果、撤销写权限后query/close、UNKNOWN→CLOSED、迟到原键409，以及3次真实命令→4流水／3同事务Outbox，重放不新增。MOVE这部分只证明数量／恢复，不证明保管属性或新页面采用。
- `stock-ui-final.json`18项：8项实际新组件/库存页接线与10项既有盘点/库存/看板回归；`stock-ui-fixtures.json`中7项恢复助手证据复用，`stock-ui.json`中3项原API证据复用，去重28Vue/client。覆盖精度边界、双击／跨标签成功竞争、UNKNOWN/403/错编号/错类型/缺双腿/存储故障、刷新恢复、当前身份／撤权／卸载、另一标签页状态与真实按钮。最终类型检查`stock-types-final.log`，实际exit0 2026-10-24T03:22:38.618058Z。
- 初始UI一次断言失败因ElFormItem桩未呈现label，另旧套件导入新子组件后实际permission依赖链撞到原router mock；补齐桩和该旧套件的只读permission mock。初次类型检查指出测试WmsApi缺message，补齐。最终适用结果通过，所有失败文件保留，不修改为成功。
- `move-metadata-red-20261024T032129Z.json`独立诊断1执行/1失败保留；诊断源`C:\Users\tt\CP6-planning-reports\20261017-foundation\wave96-move-metadata-diagnostic.cs`保留，不将已知失败放进本轮采用测试或宣称修复。源码暂存探针已从工作树移出，无其他产品修改。2个自有PG均停止、无PID、数据和日志保留。
- 复用W44原操作／W50原生Outbox后端既有语义证据；本轮新增实际HTTP补覆盖字符串数值接收，没有重跑整套后端／全仓、浏览器现场或真实租户验收。无Actions、触发修改、既有库迁移、镜像或部署。

## 采用、剩余与用户授权

待正常Main产品PR采用、Handoff交付/finish、同Main任务分支最终状态PR/finish，核对远端后定向删除本轮准确分支、释放席位。24/375整卡、125目标0完整、247正式运行NOT_RUN不变；正式Stock Root/Claim/TRANSFER、REP/RMA、Finance和下游业务ACK未提升。

用户在本轮明确回复“授权重绑并继续 W89”。原自动审批拒绝保留历史，但不再作为当前待确认阻塞。W96收尾释放后优先官方bind固定DEV-2 Handoff→CRM，resume原W89任务和保存分支，落实真实业务Owner事务；Issue503移动属性修复及新页面继续保留为随后任务。七天全目标ACTIVE。

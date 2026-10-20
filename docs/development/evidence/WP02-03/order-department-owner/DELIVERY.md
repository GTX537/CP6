# W33-01 部门可信映射与PA090 Owner：单份交付

2026-10-20，COORD本聊天独立，原DEV-1 / codex/w33-01，基线`22866fd2e40eb70c55f28967f7380fe78e61291c`。[本轮计划](../../../dispatch/20261020-wave33/README.md)、[Owner v2](../../../../design-handbook/remediation/main-order-price-correction-owner-v2.md)、[P0 #368](https://github.com/GTX537/CP6/issues/368)。限定范围本地可合并Yes；本段为候选记录，远端采用与席位释放随后核对。

## 已完成范围

新增租户内ERP订单部门码到IAM DeptId的显式绑定、启停/重绑、独立revision及普通SQL触发器。原生bpchar/case-insensitive、尾空格和10个UTF-16单位合同保持，不截断、不猜同名、不自动回填。目标必须同租户且启用链健康；部门外键阻止误删。新迁移只在自有PG55435夹具安装。

真实部门管理页新增绑定入口，后端GET/POST接入真实原登录/CSRF、当前pub-dept动作及实际定义、IAM共同根、operation/CAS/原因/前后审计。父链、引用或绑定ABA均改变CAS；原回执可查/封闭但不替代当前授权。前端复用跨标签页恢复锁，412保留草稿但必须封闭、重读、明确保存；回执不明不重放。IAM共同入口显式使用新鲜READ COMMITTED，拒绝环境或已有事务，相关原IAM验证一起覆盖。

PA090写Owner在可信绑定上支持本部门、下级和自定义范围，读取当前健康树/用户部门/角色集合。未绑定行不进入列表计数和分页；写未知绑定503，已知超范围403；自定义空集合为空，不提升权限。重绑、移动、停用及普通SQL撤销立即影响下一次读写。W32根/订单头/所有明细锁序、最终原会话/当前权限检查、全部传播效果授权、decimal(21,8)、业务键、RowVersion与业务/审批意图原子性保持。

#368原步骤已修复：实际列表、金额计算内部服务也必须有Owner，独立query/对象/字段Read在返回前重查。隐藏价格及其依赖金额不输出；隐藏金额拒绝金额范围筛选，隐藏价格拒绝暂定价筛选，计算拒绝无读权限或越界对象，GET禁止缓存。v2将改价说明同时归入UnitPrice读写许可，防止价格未变时绕过说明字段限制。页面遵守隐藏/只读提示，拒收旧身份/旧搜索响应，失权清除旧资料；提示不能代替后端授权。

没有改写其他Order CRUD、导出/下载或CRM Owner。内部workflow_intents仍为AwaitingAdapter；没有正式POWER EGG报文/发送器或跨重载业务operation恢复。没有修改SQL Server、既有环境或真实租户映射。

## 实际验证与证据

证据根`D:/CP6-local-test/wave33/foundation`。最终101个不同相关.NET/原生PG/实际HTTP测试、27个Vitest4.1.11测试、vue-tsc通过，0最终失败、0跳过；重叠执行不累加。

|执行|实际结果|
|---|---|
|owner-read-red-20261020T133216Z|2项原步骤失败：隐藏价格/派生金额仍返回、隐藏金额可筛选/计算；保留原失败|
|owner-read-initial-20261020T134116Z|上述2项修复后通过|
|owner-mapping-native-20261020T134450Z / 135128Z|先暴露测试HttpContextAccessor的AsyncLocal生命周期问题，再暴露测试jsonb Contains翻译问题；修正夹具，未弱化产品权限，失败记录保留|
|owner-iam-native-http-20261020T135749Z|99项通过：新增绑定/范围/撤销及实际Program接口，连同受共同IAM入口影响的组织/用户分配原回归|
|owner-final-boundaries-20261020T140243Z|2项通过：部门移动/重绑/空范围；旧事务/缺Owner/最终来源失败拒绝|
|owner-web-red-tests|原页面5通过5失败；另保留首次错误runner调用日志|
|owner-web-delivery|最终11项通过，包含真实渲染隐藏列、只读禁选、失权清空、旧响应拒收与原UNKNOWN/确认保护|
|mapping-web|16项通过：新绑定页7、原部门页6、原跨Tab恢复3|
|type-check|vue-tsc --build退出0；之后仅测试/文档变化，不重复整仓前端构建|

PG使用正式迁移/触发器：显式绑定、合法原生代码、跨tenant目标及raw SQL拒绝、记录审计、原operation回执、重复编码、超长拒绝、ABA CAS、健康父链、本部门/下级/custom、空范围、重绑/移动/停用。并发测试在pg_stat_activity确认普通撤销实际等待业务COMMIT，随后下一读取/写入拒绝。原W32传播原子性、来源/认证故障、到期、原生精度、提交回执UNKNOWN均纳入相关测试；未变字段算法和认证底座复用W31/W32范围内证据。

实际API子进程使用当前Program与真实登录Cookie/CSRF，验证未登录401、缺CSRF403、绑定CAS412、当前edit撤销403、隐藏字段/筛选/计算及query撤销、绑定后的scope2改价成功和停用后拒绝。服务级actor回调是受控夹具，HTTP为真实认证，不混作真实DEV业务验收。未新增浏览器E2E；页面以组件、类型检查及实际HTTP合同验证。

## 一次集中作者SPEC/QUALITY

完整差异对照SYS-IAM、W32 PA090和Owner v2范围审查：显式身份边界、native代码/容量、树与引用版本、共同根及锁序、撤销/最终检查、筛选与派生字段防泄漏、operation未知恢复、前端身份失效和权限提示。说明字段写入检查及最终失败边界已定向补齐；无本任务剩余阻断。没有新增代理或以静态断言数替代业务测试。

## 资源与收尾

自有PG与实际API进程已停止。6处自有生成data核对ownership、停止/PID、进程引用、绝对路径和reparse后回收；编译失败未创建的实例只记录不删除。逻辑回收798421171字节（0.744GiB）；清理时段D盘实际空闲增加801910784字节（0.747GiB）。日志、TRX、原失败、ownership与必要资料保留，未清理其他数据库/历史目录。

四仓合计2开发+1集成，复用原DEV-1/DEV-2，INTEGRATION-1空闲；零新树/clone/完整副本。产品正常PR后，同一分支完成最终状态文档；Handoff同一原分支交付并合并。最后manage finish定向处置本地/远端任务分支、保留固定目录并释放席位，不能在候选阶段提前声称释放。

完整WP02-03、其余Order业务Owner、CRM持久消费、正式外部工作流及真实验收未完成。125目标/247真实运行保持NOT_RUN范围，无Actions、既有环境迁移、真实tenant审批或生产部署。

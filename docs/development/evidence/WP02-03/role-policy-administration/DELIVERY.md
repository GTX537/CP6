# W29-02 封闭授权编辑：单份交付

2026-10-20，COORD当前聊天独立；codex/w29-02，基线b4c60e5c54ec9db2913bdbe670bcd27485971587。本窗本地可合并：Yes。[BUG355](https://github.com/GTX537/CP6/issues/355)待正常PR集成、远端采用核对后关闭。完整IAM及真实业务验收不提升。

## 实现

新增/api/iam/role-policies：实际可信租户/actor与当前模式权限，在既有认证共同事务门内重新核角色、引用、注册目录版本。菜单与动作必须来自当前目录，动作必须依附已授菜单；菜单动作定义维护也采用版本合同，不能删除仍被授予的定义。允许既有空动作显示名并由页面回退到code，没有缩小其历史合法输入。

数据范围仅接受完整注册资源矩阵与各资源支持的类型；自定义范围必须非空、同租户、部门及全部祖先可用。字段必须是显式指定资源的完整、无重复字段矩阵，级别只允许1/2/3；全默认1明确清除该资源的显式行，其他资源保留。资源比较保持Ordinal，避免PG文本比较误及其他资源。沿用动作并集、Scope数值MAX、字段显式行MIN后完整域默认1，不把每角色默认值预先加入有效权限聚合。

新前向迁移20261020061000_IamPermissionCatalogV1为既有租户填充目录revision；Sys_MenuAction实际SQL writer纳入共同门与tenant目录版本，Sys_Menus变化更新目录版本，支持ABA检测。静态注册域的完整内容也纳入目录版本。函数为invoker、固定search_path、撤销PUBLIC执行权并纳入现有普通身份基线白名单；没有扩充SECURITY DEFINER权限。版本化角色授权、身份快照/Outbox、强审计与不可变原操作回执同事务；回执失败全回滚，丢提交ack可准确恢复，关闭屏障拒绝迟到重放。路由RoleId与Mode计入请求指纹。

PG旧RolePerm四个无版本写入口和RoleController菜单写入口明确拒绝；旧非PG历史范围不变。三个授权页及旧/permission页面统一采用版本化编辑组件。菜单父子选择保留导航祖先及取消子动作的行为。切换角色/资源、迟到响应、失权/身份变化、412草稿、UNKNOWN查询/封闭、保存后单独刷新均有处理；跨Tab只保存公开operation定位，Web Locks串行，不保存草稿、不重放写入。动作定义维护提供版本化HTTP入口，本轮未新增独立定义管理页面。

## 实际验证与复用

证据根D:/CP6-local-test/wave29/foundation；原失败保留，不复制仓库/依赖/构建目录。

- iam-policy-integration-20261020T062858Z：**75/75通过，0跳过**。真实PG/实际Program Cookie+CSRF，注册资源/字段/菜单动作约束、当前权限、路由指纹、目录ABA与实际writer锁、回滚/丢ack、聚合语义；用户/角色/组织与现有CRM身份writer的相关组合回归。旧writer测试采用新合同，保留原投影/跨tenant/并发断言；菜单并发变化先412，再明确重读确认。
- iam-policy-writer-baseline-20261020T062612Z：**2/2通过，0跳过**。普通PG身份的实际服务到公共身份收集器，以及外部事务raw writer与菜单扇出；OwnerQueries仍为0，另tenant投影不受影响。
- iam-policy-migration-20261020T063207Z：**1/1通过，0跳过**。独立空测试库安装至前一版本，先填tenant/角色/菜单/动作/授权，再执行真实前向迁移；原角色revision及授权保留，目录revision回填后实际SQL更新会改变它。未安装既有环境。
- 上述**78项不同.NET用例**全部通过。早期30项native及13项core与75集合重叠，不叠加计数。
- iam-policy-components-final：Vitest4.1.11 **19/19通过**（新编辑器、共用恢复、角色及用户相关组件）；iam-policy-typecheck-route：vue-tsc --build通过。
- iam-policy-browser-final：实际Chrome/Vue/ElementPlus受控HTTP **4/4通过**，旧菜单路由与新共用编辑器、另标签刷新后准确恢复且只一次写入、CAS保留自定义部门并明确重确认、字段默认清理/切资源、迟到角色响应/共享身份变化；浏览器夹具不是既有DEV验收。
- 原失败：iam-policy-red-v2的3个真实旧入口问题已修正；首次red编译的params目标类型修正后才执行有效红测；首轮browser的option disabled matcher与实际HTML属性不一致，改为核对原生属性并用真实键盘End验证不会选中禁用部门，未降低产品约束。
- W27认证跨Tab、SSO首次批准/映射及W28未受影响证据按原范围复用，不重新执行全仓/SQL Server/全部CRM/真实业务。既有NuGet告警未做无关升级。

## 集中作者SPEC/QUALITY与收尾

已集中检查完整任务diff：新旧入口、当前授权/租户过滤、共享事务门、ABA与静态目录、完整替换边界、有效权限聚合、Outbox/审计/回执原子性、原操作owner/路由指纹、普通PG基线、升级保留、实际页面。发现旧菜单页遗留调用已接入共用组件并真实浏览器复查。当前范围无剩余合并阻断；这是作者审查，不冒称独立评审。

自有PG55435、HTTP fixture及Vite58427 PID40892已停止并核对端口。DEV-1在原固定目录，DEV-2仅Handoff；零新工作树/代理/完整副本。正常PR及远端采用后调用manage finish定向清理本地/远端任务分支并释放固定席位；最终结果由W29总收尾/管理登记补记。

#353仍Open：固定Main/CRM身份v1仅接受正整数role subject，完整int合同需要版本化跨仓采用；当前不兼容写入503且全回滚。本轮未改v1身份含义或绕过捕获，不宣称完整IAM/角色整数域完成。没有Actions、现有数据库迁移、真实租户审批或生产部署；CRM/Owner、125目标及247真实运行验收不提升。

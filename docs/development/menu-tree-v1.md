# 共享菜单树排序与层级 V1

GET `/api/menu/tree-v1`返回当前revision与完整菜单树；POST `/preview`、`/commands`接受operationRef、expectedRevision、positions[{menuId,parentId,orderNo}]、reason；POST `/commands/read`按operationRef恢复历史结果。revision及引用计数以十进制字符串传输，避免浏览器丢失bigint精度。严格JSON、不接受自报actor/tenant，NoStore/CSRF沿实际Main会话策略。

这是共享全局定义管理：当前有效平台管理员且具有menu:edit，拒绝模拟/非完整会话；普通tenant动作管理员不能使用V1。复用IAM完成门、PlatformAdminAuthorization及CurrentPermissionContext，门内重新读取当前授权；不新增重复资格层。独立PG事务/干净context，提交前复核门外时钟/注销和原授权租户期限。后续查询/重试也需当前权限且原actor/actorTenant匹配。

Sys_Menus仍唯一树。新增cp6_menu_tree_v1.root单调版本，由原生菜单INSERT/UPDATE/DELETE/TRUNCATE语句推进，旧写/删除重建/ABA可见；包括无效果语句，版本可跳多步，不保证一次命令只加一。迁移先锁原表，等待已开始写者，原行不改写；安装/再次迁移保留原记录。root禁止倒退/删除/清空，commands禁止修改/删除/清空。Down明确拒绝，恢复需前向任务；本轮只迁移自有测试数据库。

首次命令核对expectedRevision，再验positions非空、正ID/非负顺序/无重复、节点存在、父存在、不自引用及完整合并树无环。支持partial positions，未提交节点不变。只修改ParentId/OrderNo，不修改菜单键、路由、Enable或角色关系；菜单可见不赋予动作权限。冲突412，非法结构400，不存在404。

预览返回实际直接变化节点；存在变化时，引用数保守覆盖当前完整共享树的所有tenant RoleMenus/RoleActions，角色以(tenant,RoleId)去重，不误将同号跨租户角色合并。它是引用范围，不是声称每条引用的有效权限都变化；无变化返回0。预览不是授权票据；提交在同一共同门内重新核版本、权限与当前引用计数，角色映射变化可使提交计数不同于预览。

operationRef为非空合法Unicode，允许NUL；bytea完整保存原键/请求/结果，SHA256仅索引并比较原字节。reason必填≤256字符，合法Unicode且无NUL。JSONB审计只放关联operationHash，原请求仍在bytea完整保存，避免PG JSONB拒绝NUL。位置写入、原件、现有字段审计/安全审计在同一事务；审计/最终会话失败全部回滚。开始提交后不确定503 MENU_TREE_OUTCOME_UNKNOWN，原键查询或原样重试；同义返回历史，异义409，他人403。恢复优先当前CAS/目标存在性，目标变化/删除后不重建。历史回执不是当前树。

MenuView实际拖拽采用V1，预览全局范围后确认；412保留草稿并可只读当前差异，显式刷新放弃草稿后重新调整。发送前按浏览器身份epoch在sessionStorage保留准确命令，存储失败不发送；刷新后可查询/原样重试。后续404/403不清除未知请求。确认后独立刷新当前树与当前浏览器导航，单项读取失败可恢复、不重发写命令；期间产生的新草稿保留。身份变化/卸载丢弃晚答，应用整体可按epoch重新挂载页面。

旧节点编辑/添加/删除及PUT tree保持历史兼容路径，不因V1强制它们CAS。本期未实现全相关用户缓存广播/确认，页面只报告当前浏览器导航；其他菜单字段、路由/权限命名空间治理及KPI范围见[交付](evidence/W266-01/DELIVERY.md)。没有生产部署或既有环境迁移。

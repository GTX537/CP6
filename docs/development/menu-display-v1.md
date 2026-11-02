# 菜单展示配置 V1

POST `/api/menu/tree-v1/display/preview`和`/display/commands`接受operationRef、expectedRevision、menuId、menuName、icon、enable、reason；读取当前树和原结果复用W266 GET `/api/menu/tree-v1`与POST `/commands/read`。严格JSON拒绝未声明字段，尤其RoutePath/MenuKey/ActorId。共享全局配置要求当前完整平台管理员会话及menu:edit，拒绝模拟/普通tenant；NoStore/CSRF沿实际Main策略。

MenuDisplayCommandV1独立DTO，不修改旧MenuTreeCommandV1序列化。共享IAM完成门、树版本和bytea原件表，不新建表/迁移/重复资格。拖拽/展示/旧原生菜单写互相推进同一版本；同键字节一致恢复历史，跨命令同键409，他人403；恢复优先当前版本/目标存在性。新展示命令只写MenuName/Icon/Enable，原ID/创建时间/父级/顺序/路由/权限键及角色映射保留。

名称必填非空白≤100、图标null或≤100（空字符串与null保留区别）、reason必填≤256。文本合法Unicode且无NUL，在JSON编码前拒绝孤立代理字符；不trim、不截断。原operationRef沿W266合法Unicode/允许NUL/bytea协议。预览和写入分别核当前条件，不将预览当票据；存在变化时引用范围保守覆盖全树所有tenant角色，预览不承诺每条引用的权限都变化。

复用原子CompleteCommand，展示字段、强审计、字段审计和原件同事务；审计记录kind=shared-menu-display-v1及operationHash。审计或最终会话失败回滚；提交丢答503 MENU_TREE_OUTCOME_UNKNOWN，原样重试不重写。无字段变化仍保存审计和原件，树版本不推进。Enable只影响原导航过滤，隐藏不删除角色关联、不等于动作权限撤销；恢复显示沿当前身份重新查询导航。

MenuView集成MenuDisplayPanel，独立名称/图标/显示状态编辑；先读当前值/版本，预览全局范围再确认。412保留草稿，显式重读当前值后核对重提。sessionStorage按身份epoch保留准确原请求，刷新后无选中节点也可恢复；后续404/403不消除未知。存储失败不发送；当前读失败不伪空成功；身份与选中节点变化丢弃晚答。展示处理中/结果未知时互斥树和旧节点写入，成功后当前列表与当前浏览器导航独立刷新，历史回执不充当当前配置。

旧节点编辑/PUT/添加/删除仍按原兼容合同，尚未全面强制CAS/原命令/新全局政策；新面板不宣称消除这些旧入口。全用户缓存传播、路由/权限键、个性配置与KPI见[交付](evidence/W267-01/DELIVERY.md)。生产与既有数据库不变。

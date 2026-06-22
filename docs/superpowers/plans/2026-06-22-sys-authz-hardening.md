# Sys 控制器授权加固（Authz Hardening）Plan

**Goal:** 关闭 9 个 Sys 控制器的细粒度授权缺口——它们当前仅类级 `[Authorize]`（任意登录用户可调），存在提权（自授 admin 角色/权限、重置他人密码、建高权账户）与越权写入风险。给写端点 + 敏感列表读加 `[RequirePermission(资源键, 动作)]`，并 seed 对应权限点授 admin(RoleId=1)。

**铁律（防锁死）：**
1. **`RolePermController.my-actions`（GET）永不 gate** —— 它是前端权限自举入口（`loadMyActions()` 拉 actionKeys），gate 它 = 全站 403 死锁。
2. **跨页下拉/树/元数据读保留 `[Authorize]`** —— `role/all`、`dict/types|data|options`、`dept/tree`、`rolePerm/*-resources`、`menu GetAll`，gate 会让"编辑用户取角色下拉"等其它页崩。
3. **Dashboard `GetSummary`（首页只读）保留 `[Authorize]`** —— 无写、无提权价值，gate 有锁死落地页风险。
4. seed 后 **admin(RoleId=1) 必拿到所有新 action**，否则 admin 自己也 403。`PermissionService.HasActionAsync` 无 admin 旁路。

**资源键 = 目标菜单 MenuKey（由 RoutePath 派生）。** 权限检查只看"该角色的 RoleAction 是否含 资源键:动作"，与前端从哪个菜单进无关——故一个控制器可借任一已 seed 的 MenuKey 做键。

## Gate 矩阵（端点 → 资源键:动作 / 或保留 [Authorize]）

| 控制器 | 端点 | 决策 |
|---|---|---|
| **UserController** (/user→`user`, menu104) | GetList | `user:query` |
| | Add / Update / Delete | `user:add` / `user:edit` / `user:delete` |
| **RoleController** (/role→`role`, menu101) | GetList | `role:query` |
| | GetAll (/role/all) | **[Authorize]**（角色下拉，跨页用） |
| | Add / Update / Delete | `role:add` / `role:edit` / `role:delete` |
| | GetRoleMenus | `role:query` |
| | SaveRoleMenus | `role:edit` |
| **MenuController** (/menu→`menu`, menu102) | GetAll | **[Authorize]**（菜单列表被 rolePerm 页等用） |
| | Add / Update / Delete | `menu:add` / `menu:edit` / `menu:delete` |
| **DictController** (/dict→`dict`, menu106) | GetTypes/GetData/GetOptions | **[Authorize]**（字典选项全站下拉用） |
| | AddType/AddData | `dict:add` |
| | UpdateType/UpdateData | `dict:edit` |
| | DeleteTypes/DeleteData | `dict:delete` |
| **DeptController** (/pub/dept→`pub-dept`, menu108) | Tree | **[Authorize]**（组织树被 user/data-scope 用） |
| | Create / Update/Move/Leader / Delete | `pub-dept:add` / `pub-dept:edit` / `pub-dept:delete` |
| **UserRoleController** (/pub/user-role, 无独立菜单→借 `user`) | Get | **[Authorize]**（读自身角色，编辑用户页用） |
| | Save (PUT, 分配角色=自提权向量) | `user:edit` |
| | Migrate (POST) | `user:edit` |
| **RolePermController** (/pub/role-perm 等 3 菜单) | **my-actions** | **[Authorize]**（🔒自举，永不 gate） |
| | data-scope/resources, field-perm/resources | **[Authorize]**（元数据） |
| | GetRolePerm/GetMenuActions/GetAllMenuActions | `pub-role-perm:query` |
| | SaveMenuActions/SaveRolePerm (PUT, 授权=自提权向量) | `pub-role-perm:edit` |
| | GetRoleDataScopes | `pub-data-scope:query` |
| | SaveRoleDataScopes (PUT) | `pub-data-scope:edit` |
| | GetRoleFieldPerms | `pub-field-perm:query` |
| | SaveRoleFieldPerms (PUT) | `pub-field-perm:edit` |
| **OperLogController** (/operlog→`operlog`, menu107) | GetList | `operlog:query` |
| | Clear (DELETE) | `operlog:delete` |
| **DashboardController** (/dashboard→`dashboard`, menu2) | GetSummary | **[Authorize]**（首页只读，无写，铁律3） |

## Program.cs seed 权限点（仿 fin/T8 块，授 admin RoleId=1，幂等）

```
(101 role)  query/add/edit/delete
(102 menu)  add/edit/delete
(104 user)  query/add/edit/delete
(106 dict)  add/edit/delete
(107 operlog) query/delete
(108 pub-dept) add/edit/delete
(109 pub-role-perm) query/edit
(110 pub-data-scope) query/edit
(111 pub-field-perm) query/edit
```
（菜单 MenuKey 全局回填 §684 已覆盖 menu101~113，无需本地补；114/sys-security-log T8 已 seed。）

## 实施步骤
1. 9 控制器加 `using CP6.Core.Auth;` + 按矩阵贴 `[RequirePermission]`（保留铁律端点 [Authorize]）。
2. Program.cs 加权限点 seed 块（接 T8 安全日志 seed 之后）。
3. build + dotnet test（995 应保持——[RequirePermission] 是授权过滤器，单测直调控制器不触发，无破坏）。
4. **gstack QA（关键，防锁死）**：起后端 → admin 登录 → ①admin 能正常 GET/写 user/role/dict 等（200）；②`my-actions` 正常返回（自举不死）；③角色下拉/字典选项/部门树正常（200）；④Dashboard 正常；⑤建无权测试用户 → 对写端点 403、对 my-actions/下拉 200。
5. commit + push。
6. 质量审（对抗：能否绕过/是否锁死自举/admin 是否仍全可用）。

## 风险
- **锁死自举**：已用铁律1隔离 my-actions。
- **下拉破**：已用铁律2隔离跨页读。
- **admin 自锁**：seed 授全 action 给 RoleId=1。
- 非 admin 角色若原先在管这些 → 被挡（=正确的安全修复，非回归；可由 admin 在权限分配页再授）。

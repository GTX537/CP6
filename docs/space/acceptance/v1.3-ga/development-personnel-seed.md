# Space Studio 单人开发人员种子

当前开发由一名真实开发者完成。为了不中断角色切换、权限矩阵、任务归属和界面流程验证，开发环境固定使用 `00001`～`00005` 五个虚拟人员。

这些人员的证据类别是 `DevelopmentSeed`，不是员工主数据或生产账号，也不代表五名真实投入人员。其角色只是测试视角：

| 人员 | 开发测试视角 |
|---|---|
| `00001` | Product、Backend、DevOps |
| `00002` | QA、Backend |
| `00003` | WMS、Frontend3D |
| `00004` | Architecture、Frontend3D |
| `00005` | Security、QA |

使用边界：

- 可以用于本地流程演练、UI 角色切换、权限自动化和单人开发任务归属。
- 虚拟编号不可以写入正式 GA 的 Owner、接受人或 `DeliveryOwner` 签字字段；真实开发者本人可以实名承担这些职责。
- 不可以参加双仓 Pilot 或生产访问。
- 正式 GA 不再要求团队人数或多角色独立签字；72% 是否提高只取决于真实结果证据是否完成。

机器校验入口：

```powershell
./tools/Test-SpaceGaDevelopmentPersonnelSeed.ps1
./tools/Test-SpaceGaDevelopmentPersonnelSeed.Tests.ps1
```

如需把这些编号创建为可登录的开发账号，应另开身份与权限任务，使用开发租户、独立凭据和最小权限；本人员册不会创建密码、Token 或生产身份。

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
- 不可以写入正式 GA 证据索引的 Owner、接受人或签字人字段。
- 不可以证明 `2 Backend + 2 Frontend3D + 1 QA` 的真实团队投入，不可以参加双仓 Pilot 或生产访问。
- 正式 GA 仍需要产品、QA、WMS、架构、安全五个具有审批权的实名人员。单人开发模式不会提高当前 72% 的正式完成度。

机器校验入口：

```powershell
./tools/Test-SpaceGaDevelopmentPersonnelSeed.ps1
./tools/Test-SpaceGaDevelopmentPersonnelSeed.Tests.ps1
```

如需把这些编号创建为可登录的开发账号，应另开身份与权限任务，使用开发租户、独立凭据和最小权限；本人员册不会创建密码、Token 或生产身份。

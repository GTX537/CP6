# 主体导出来源清单 v1

W311采用WP02-15/SYS-PRIV-01的稳定主体、Owner目录、字段分类与遗漏语义。原HTTP入口 `GET /api/platform/gdpr/export/subject/{userId}` 仍要求当前平台身份；它返回同步JSON附件，不代表持久导出操作、批准或全域完成。

`scope.version` 从 `cp6.subject-export-scope.v1` 升为 `cp6.subject-export-scope.v2`，新增 `manifest`（`cp6.subject-export-manifest.v1`）和 `roleAssignments`。保留 `user/securityLogs/operLogs/exportedAt/scope` 顶层形状；字段值仅在Owner明确纳入时返回。此版本有意停止将未知字段、嵌套JSON和日志自由内容自动披露；它们在逐字段说明中排除，原数据库内容不修改、不销毁。密码、令牌及认证材料不导出。

当前实际来源为用户原行、其目标TenantId+稳定UserId匹配的安全事件与角色关联。用户主角色引用仍在用户资料中；关联不等价于完整当前权限快照。不因操作者所在租户改变目标，也不以当前/历史用户名、Creator、Modifier或ImpersonatorId推断主体归属。仅导出关系自身的引用，不读取关联第三人的资料。

每个来源给出Owner、实体/来源名、筛选依据、状态、数量、各自读取起止UTC、版本依据和字段排除。`INCLUDED` 的零数量才表示该次实际查询无记录；`UNKNOWN_SCOPE` 的数量和读取时间必须为null，不得伪装成零。认证材料可标记 `EXCLUDED_WITH_REASON` 且不执行查询。必要已接入来源读取失败则整个请求失败，没有catch-and-empty、重试或部分成功替身。

剩余EF实体从当前映射元数据列入目录，未认领的Owner/主体关系如实标记未采用，不读取其业务行。Main原生OA/PLM/业务操作、CRM、文件/外副本、备份及目录外余项分别声明未知。元数据列表不是所有部署/跨应用来源完整证明，目录外余项始终保留。添加新EF实体不会静默从缺项清单消失，也不会自动获得披露资格。

当前来源是独立读取的可变行，不宣称跨表或跨库同一快照；`UNVERSIONED_LIVE_ROWS_READ_WINDOW` 明确没有不可变来源版本或精确水位。Manifest状态为 `PARTIAL`、`complete=false`。页面显示具体目标、真正零记录、缺项及独立读取时间；文件带完整来源与字段说明。主体响应 `Cache-Control: no-store`，页面只保留Manifest用于显示，不保存业务内容或创建永久链接。

W312继续持久导出操作、准确结果恢复、用途/接收方及当前下载门；W313–315继续批准、hold、逐Owner处置、UNKNOWN和墓碑恢复。W311不关闭完整隐私卡，不执行既有数据擦除或生产部署。

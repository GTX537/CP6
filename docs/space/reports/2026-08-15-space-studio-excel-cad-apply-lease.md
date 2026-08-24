# Space Studio Excel–CAD 确认租约与 Revision Fence

日期：2026-08-15
范围：LM-FR-024 / WP4 的 Excel–CAD 权威确认安全前置条件

## 结论

Excel–CAD Match Artifact 的确认写入不再只依赖 Content Revision。确认请求现在必须同时携带当前页面实例、活动编辑租约、Floor Revision 与 Content Revision；服务端在确认入队和后台 Worker 实际写入前都重新验证这些 Fence。任何会话漂移、租约释放/过期或 Revision 漂移都会在 Draft 写入前失败关闭。

本任务没有把 Excel–CAD 确认宣称为 LM-FR-024 完成：后台 Apply 尚未生成可供 Space Studio 统一历史栈消费的完整补偿命令；底图挂接/标定的可逆合同也仍待单独冻结。因此 WP4 保持 `Partial/Pending`，核心 GA 保持 72% / `NoGo`。

## 实现

- `ConfirmSpaceExcelCadMatchRequest` 新增必填 `clientInstanceId`、`leaseId` 和 `expectedFloorRevision`；OpenAPI、C# SDK 与 TypeScript SDK 同步。
- 确认服务在 Serializable 事务内取得与 Design V1 普通编辑相同的 Floor application lock，按身份链定位 Floor 后先验证租约，再验证 Floor/Content Revision，最后执行幂等复用或排队。
- Worker payload 升为 schema v2、处理器升为 `space-excel-cad-apply-v3`，冻结确认时的页面实例、租约和双 Revision。
- Worker 在真正创建 CommandBatch、Rack、RackLevel、Location、Binding 或 Attribute 前，再以 SQL Server `SYSUTCDATETIME()` 验证该租约仍由原请求人和同一页面实例持有；失败返回稳定 `SPACE_EDIT_LEASE_LOST` 且零写入。
- 历史 schema v1 的已成功 Apply 仍可读取或幂等复用；未完成的旧 Job 不会被当前确认请求复用，v3 Worker 只接受带租约 Fence 的 payload v2。
- Space Studio 只有在当前页面拥有租约、Floor Revision 可用且 Match Artifact 未因 Content Revision 漂移时允许确认；请求使用页面实际 `clientInstanceId` 和当前 Lease/Floor 身份。

## 已验证门禁

- `SpaceExcelCadMatchServiceTests`：14/14。
  - 不同页面会话与 Floor 漂移同时存在时，租约错误优先且零排队/零写入。
  - 确认排队后释放租约，Worker 返回 `SPACE_EDIT_LEASE_LOST`，Rack/CommandBatch/CommandRecord 均为零，来源保持 `PreviewReady`。
- Controller + Design V1 OpenAPI：50/50。
- Space Unit：533/533。
- Space Integration 默认门禁：322 passed / 111 个既有 SQL 环境门控项 skipped / 0 failed；本任务聚焦集合 14/14、0 skipped。
- CP6.Tests：2919 passed / 19 个既有环境门控项 skipped / 0 failed。
- Web 全量：814/814；Vue type-check 与 production build 通过。
- Space Studio Playwright：21/21；其中 Excel–CAD 确认路径 1/1。
- 完整 `CP6.slnx` Release build：0 warning / 0 error。
- OpenAPI/C#/TypeScript SDK 已由仓库生成器同步，二次生成 drift 检查通过；GA 证据校验保持预期 `NoGo`（5 输入、9 Gate、5 签字 Pending）。

## AutoCAD 边界

本机 `D:\AutoCAD 2025` 的 Core Console 安装与开发转换链已由前一份报告记录。本任务没有把本机 AutoCAD 注册为 Site GA Provider，也没有生成真实客户 CAD、主备 Provider、隔离 Worker、Pilot 或正式接受证据。

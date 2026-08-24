# Design V1 Floor shell 与项目入口报告

日期：2026-08-15
任务分支：`codex/space-design-floor-shell`

## 结论

Blank Draft 现在可以从 Site 的 `Space Studio` 入口继续完成设计楼层初始化和选择。新入口读取活动生产 Draft；没有 Draft 时允许显式创建 `Blank`，没有楼层时要求用户填写楼层编码、名称、层级、标高和层高，创建成功后直接以服务端返回的 Floor LogicalId 进入既有统一工作台。

系统不复制旧运行态楼层、不从 CAD 静默推断楼层，也不填充业务默认值。平台/租户整仓模板、Published/template 四模式统一向导以及 LM-FR-002 项目元数据仍未完成，因此 LM-FR-001 和 WP1 保持 `Partial/Pending`，核心 GA 保持 72% / `NoGo`。

## 合同与并发

- `GET /api/space/design/v1/versions/{versionId}/floors` 只向内部 Design V1 读者返回活动设计楼层，并按 Level、FloorCode、LogicalId 稳定排序。
- `POST /api/space/design/v1/versions/{versionId}/floors` 要求 `space:model:edit`、`Idempotency-Key` 和全部六个请求字段。
- Floor 尚不存在时不能伪造 Floor Lease；初始化改用 Version 级 SQL application lock、Draft/Purpose 检查和 `ExpectedContentRevision`。Floor 创建成功后，后续编辑继续使用既有 Floor Lease、Floor Revision 和 Command Batch。
- Floor、Version Content Revision 与 Idempotency 结果在同一 Serializable 事务提交；同键同参返回相同 LogicalId，异参失败关闭，过期 Revision 和重复 FloorCode 均零额外写入。
- 新接口已同步 Design V1 OpenAPI、C# SDK、TypeScript SDK、权限白名单和 Problem Details。

## 前端行为

- Space 落地页每个 Site 增加 `Space Studio` 入口，不再要求用户掌握 VersionId/FloorLogicalId。
- 入口显示三步状态：建立草稿、初始化楼层、进入工作台；已有楼层可以直接选择，也可显式新增楼层。
- 所有字段具有关联 Label、键盘焦点和至少 44px 热区；低于 1280px 禁止创建，只允许查看并进入现有只读工作台。
- 创建失败保留用户输入并显示服务端可恢复错误；创建成功使用同一 Design Revision 权威进入 `DesignUnderlayView`。

## 验证

- SQL Server LocalDB 聚焦 4/4：显式字段、内容 Revision、相同请求重放、异参幂等冲突、过期 Revision、重复 FloorCode、并发唯一胜者、楼层列表、空场景和外部主体拒绝。
- OpenAPI 与权限聚焦 88/88：GET/POST 路径、required body/schema、唯一 operationId 和读写权限。
- 前端 API/入口/Space 首页聚焦 8/8；Vue TypeScript 检查通过。
- Space Unit 全量 534/534；Space Integration 在真实 LocalDB 全量 441/441、0 跳过；CP6.Tests 2,923 通过、19 项既有环境跳过、0 失败。
- Web 全量 166 个测试文件 / 848 项测试、生产构建通过；Design V1 OpenAPI 与 C#/TypeScript SDK 漂移检查、EF pending-model 检查和 GA 证据校验通过。
- 完整 `CP6.slnx` Release 在关闭共享构建节点后以非增量、单线程、禁用节点复用/共享编译方式通过：0 warning / 0 error；未降低 Android 构建强度。
- 本任务不修改数据库结构；既有 `Space_FloorRevision` 与 Idempotency 表足以承载该合同。

## 后续

1. 建立 System/Tenant 不可变整仓模板目录与模板版本。
2. 把 Blank、Published、平台模板、租户模板收敛为统一创建向导，并补来源、创建者、更新时间与 Blocking 摘要。
3. 以真实空白仓 E2E 覆盖 Floor 创建、租约获取、Zone/Aisle/Rack/Location、编码、校验和发布。

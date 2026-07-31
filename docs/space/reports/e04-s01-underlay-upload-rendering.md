# E04 S01 底图上传与渲染完成报告

- 状态：Complete
- 日期：2026-07-30
- 功能提交：`1d57a3b5`
- no-ff 集成提交：`e8e84853`
- 集成分支：`integration/space-v1-20260730`
- 范围：PDF / PNG / JPG 底图上传、安全扫描、楼层挂接与前端渲染

## 1. 交付结果

E04 S01 已把底图从前端占位状态推进为可用的 Design V1 产品链路：

1. 用户在独立底图编辑视图上传 PDF、PNG 或 JPG。
2. 服务端把文件写入租户隔离的受控对象目录，并创建 E01 文件安全扫描 Job。
3. 上传响应返回 `FileId`、`SourceId`、`ScanJobId` 与轮询地址；扫描终态在同一数据库事务中同步 Source 状态。
4. 只有 `Ready + Clean` 的来源可以挂接到指定楼层；挂接采用 `Idempotency-Key`，并推进 Version/Floor revision。
5. 前端通过受权内容端点读取 Blob；PDF 使用本地固定版本 PDF.js worker 解码，PNG/JPG 使用浏览器安全解码。
6. Konva 底图层支持显示/隐藏、透明度和锁定，并在替换或卸载时释放对象 URL、ImageBitmap、PDF document 与 worker 资源。

本卡没有提前实现 E04 S02 两点标定，也没有混入 S03/S04 的元素选择、属性编辑、多选、对齐、分布或阵列命令。

## 2. API 与权限

| 操作 | 路径 | 权限/约束 |
|---|---|---|
| 上传底图 | `POST /api/space/design/v1/versions/{versionId}/underlay-sources` | `space:source:upload` + `space:model:edit`；multipart；100 MiB；返回 202 |
| 查询文件状态 | `GET /api/space/design/v1/versions/{versionId}/files/{fileId}` | `space:model:read` |
| 读取底图内容 | `GET /api/space/design/v1/versions/{versionId}/sources/{sourceId}/content` | `space:model:read`；仅 Clean/Ready；`private, no-store` + `nosniff` |
| 挂接楼层底图 | `PUT /api/space/design/v1/versions/{versionId}/floors/{floorLogicalId}/underlay` | `space:source:upload` + `space:model:edit`；必需 `Idempotency-Key` |

所有读写都会重新验证 Tenant、Site、Version、Floor、File 与 Source 关系；没有公开静态文件目录，也不接受任意外部 URL。OpenAPI、C# SDK 与 TypeScript SDK 已同步生成并通过 drift/编译门禁。

## 3. 安全与一致性边界

- MIME、扩展名和 SourceType 必须一致，且仅允许 PDF、PNG、JPG/JPEG。
- 文件写入先进入临时文件，再原子移动至随机 storage key；路径经过规范化并限制在配置根目录内。
- E04 使用显式 Pending Source，不放宽既有通用 Source API 的 Clean-only 合同。
- 扫描成功把 File 与 Source 同步推进为 Clean/Ready；恶意、策略拒绝或终止失败会同步拒绝 Source。
- 内容端点只提供经过授权且扫描干净的 Blob，不返回磁盘路径，不开启 range，不缓存私有内容。
- 挂接在数据库事务中验证 source/file/floor/version，幂等重放返回稳定结果；并发唯一键冲突会回读既有结果或返回 409。
- PDF.js 固定为 `5.4.624`，worker 随应用打包；禁用 eval 与 XFA，并限制页数、尺寸和总像素，未引入 CDN。
- 本卡没有数据库模型变化或 Migration；EF `has-pending-model-changes` 门禁通过。

## 4. 部署约束

- 默认文件根目录为应用下 `App_Data/space-files`。生产多副本部署必须把 `Space:Files:RootPath` 指向所有 API/Worker 可访问的共享耐久卷，并保持租户目录隔离。
- 默认安全扫描器仍按 E01 S06 失败关闭：没有部署真实扫描引擎时，文件会停留/返回隔离态，不会错误进入 Ready。生产启用前必须配置受支持的扫描器和 worker。
- 100 MiB 是 HTTP 与表单层的双重上限；反向代理和平台入口需要设置不低于该值但不能绕过应用上限。

## 5. 验证证据

| 门禁 | 结果 |
|---|---|
| E04 API / OpenAPI / 权限 / 文件存储聚焦 | 22/22 passed |
| Space UnitTests | 205/205 passed |
| 默认 Space IntegrationTests | 48 passed / 42 SQL-gated skipped |
| `KOUSQLSERVER` 文件安全与底图事务聚焦 | 6/6 passed，无跳过 |
| CP6.Tests 全量 | 2685 passed / 17 environment-gated skipped |
| 前端 E04 聚焦 | 2 files / 11 tests passed |
| 前端全量 | 90 files / 557 tests passed |
| 前端 type-check | passed |
| 前端 production build | passed；仅保留既有大 chunk 提示 |
| 合并态 `dotnet build CP6.slnx -c Release --no-restore` | 0 warning / 0 error |
| 合并态 Space UnitTests | 205/205 passed |
| 合并态 API / 权限聚焦 | 18/18 passed |
| SDK | drift、C# build、TypeScript strict compile passed |
| EF Migration 一致性 | 无待迁移模型变化 |
| 差异与安全扫描 | `git diff --check`、外部 URL/eval/动态脚本/raw SQL 等聚焦扫描通过 |

SQL-gated 默认跳过项仍表示环境门禁，不记作“已通过”；6 个底图相关数据库测试已在本机 `KOUSQLSERVER` 上实际启动并通过。

## 6. 下一步

下一张独立卡固定为 E04 S02 两点标定：定义像素点到世界坐标的可审计变换、精度/退化条件、持久化与 revision 语义，并复用本卡的 Ready/Clean 底图来源。S02 不应夹带 S03 选择/属性面板或 S04 多选/对齐/分布/阵列。

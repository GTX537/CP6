# E04 S02 两点标定与坐标确认完成报告

- 状态：Complete
- 日期：2026-07-30
- 功能提交：`20ee0af0`
- no-ff 集成提交：`c1043d15`
- 集成分支：`integration/space-v1-20260730`
- Migration：`20260731032506_SpaceE04S02UnderlayCalibration`
- 范围：PDF/PNG/JPG 底图两点标定、第三控制点验证、可审计持久化、revision 与 Clone 语义

## 1. 交付结果

E04 S02 已把 S01 的 Ready/Clean 底图推进为可确认坐标的设计版本内容：

1. 画布按顺序采集 P1、P2 和独立验证点 V，像素坐标固定为左上原点、Y 向下。
2. 用户为三个点填写楼层世界坐标；世界坐标使用整数毫米、Y 向上。
3. P1/P2 唯一确定二维等比变换的比例、RotationZ 和原点偏移；前端即时预览比例、旋转和第三点误差。
4. 服务端独立重算结果，不信任前端派生值。第三点误差必须满足 `max(50mm, P1/P2 实际距离 × 0.2%)`；绝对下限和相对容差均由服务端配置控制。
5. 标定保存后推进 Floor revision 和 Version content revision，并把当前标定 ID、比例、偏移和旋转返回统一 Scene Floor DTO。
6. 标定记录 append-only；重新标定创建新记录，Floor 只指向当前记录。替换底图会清除旧标定指针与派生变换。
7. Published→Draft Clone 会重映射 Source/Calibration RowId，保留 LogicalId、控制点、有效阈值、误差和当前标定引用。

本卡没有引入 E04 S03 的通用元素选择/属性编辑，也没有混入 S04 的多选、对齐、分布或阵列命令。

## 2. 变换与退化条件

设 PDF/图像像素点为 `(px, py)`、图像高度为 `H`，先转换到局部 Y-up 坐标 `(px, H-py)`，再应用统一比例、RotationZ 和世界偏移。

服务端拒绝以下输入：

- 页码、栅格尺寸或像素点越界；
- P1/P2 像素距离小于 10 px；
- P1/P2 世界点不相同或实际距离小于 1 mm；
- 验证点距离 P1/P2 控制线小于 `max(5px, 控制线长度 × 1%)`；
- 非有限、越界或不能安全量化为持久化精度的派生值；
- 第三点误差超过服务端有效阈值；
- 非 PDF 来源使用非第 1 页；
- Source 不是楼层当前底图，或不处于 Ready + Clean。

## 3. API、权限与幂等

| 操作 | 路径 | 权限/约束 |
|---|---|---|
| 读取当前标定 | `GET /api/space/design/v1/versions/{versionId}/sources/{sourceId}/underlay-calibration?floorLogicalId=...` | `space:model:read`；重新验证 Tenant/Site/Version/Floor/Source |
| 保存标定 | `POST /api/space/design/v1/versions/{versionId}/sources/{sourceId}/underlay-calibration` | `space:source:upload` + `space:model:edit`；必需 `Idempotency-Key` |

保存操作在 Serializable 事务内完成 Source/File/Floor/Version 复验、标定追加、Floor/Version revision 推进和幂等账本写入。同一键同一完整请求稳定回放；路由中的 `sourceId` 也进入请求哈希，同一键切换来源会返回 `SPACE_IDEMPOTENCY_KEY_REUSED`，不会误回放旧底图标定。

OpenAPI、C# SDK 与 TypeScript SDK 已同步生成并通过 drift 检查。

## 4. 数据一致性与审计

- `Space_UnderlayCalibration` 保存来源、页码、栅格尺寸、P1/P2/V 像素点和世界点、比例、偏移、旋转、误差、有效阈值、创建时间与操作者。
- Floor 当前引用使用 Tenant + Version + Floor + Source + Calibration 复合外键，数据库层阻止来源与楼层标定错配。
- 标定行禁止修改或删除；Published/Superseded 快照继续受不可变护栏保护。
- Floor Scene DTO 公开当前 `UnderlayCalibrationId`；派生比例/偏移/旋转继续供渲染链使用。
- 独立幂等 SQL 脚本已生成；EF `has-pending-model-changes` 证明模型与 Migration 一致。

## 5. 验证证据

| 门禁 | 结果 |
|---|---|
| Space UnitTests | 210/210 passed |
| 默认 Space IntegrationTests | 48 passed / 43 SQL-gated skipped |
| `KOUSQLSERVER` 文件安全、标定 Migration 与 Clone 聚焦 | 9/9 passed，无跳过 |
| CP6.Tests 全量 | 2687 passed / 17 environment-gated skipped |
| API / OpenAPI / 权限聚焦 | 20/20 passed |
| 前端 E04 聚焦 | 3 files / 15 tests passed |
| 前端全量 | 91 files / 561 tests passed |
| 前端 type-check | passed |
| 前端 production build | passed；仅保留既有大 chunk 提示 |
| 合并态 `dotnet build CP6.slnx --no-restore` | 0 warning / 0 error；包含 Desktop 与 Android |
| 合并态 Space UnitTests | 210/210 passed |
| SDK | OpenAPI、C#、TypeScript drift check passed |
| EF Migration 一致性 | 无待迁移模型变化 |
| 差异门禁 | `git diff --check` passed |

默认 SQL 跳过项仍是环境门禁，不记作已通过。上表 9 个相关数据库测试已使用本机 `KOUSQLSERVER`、Windows 集成认证和每测试唯一临时数据库真实执行，测试结束自动清理。

## 6. 下一步

下一张独立卡固定为 E04 S03：通用元素选择与属性面板。它应复用统一 Design Scene 与既有通用元素领域模型，提供墙、柱、门等草稿元素的选择、编辑和删除；仍不得提前混入 S04 的多选、对齐、分布或阵列命令。

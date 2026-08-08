# E04 S06 2D/3D 同源预览完成报告

- 状态：Complete
- 日期：2026-08-02
- 功能提交：`20f248bd`
- no-ff 集成提交：`2b6ef127`
- 起始基线：`99e367f1`
- 集成分支：`integration/space-v1-20260730`
- Migration：无
- API / OpenAPI / SDK：无表面变化
- 范围：同一 Design Revision 的 2D 编辑投影、只读 3D 预览与机器可核验一致性证明

## 1. 交付结果

E04 S06 已完成并进入受控集成基线：

1. Design V1 编辑器默认提供 2D+3D 分屏，并可切换为纯 2D 或纯 3D；窄屏自动改为上下布局。
2. 2D 画布与 3D 预览消费同一个响应式 `ISpaceDesignSceneDto`，不存在第二套建模数据。保存成功后沿用服务端回读场景，同时重建两种投影。
3. 3D 预览复用 `SceneBuilder.buildDesign`、参数化资产和 InstancedMesh 渲染链，提供俯视、等轴和正视预设以及自动适配视野。
4. 预览明确标识 Draft/Published 等版本状态，并固定显示“只读预览 · 不含生产库存/任务”；3D 区域不提供编辑入口，也不叠加 WMS 运行态。
5. 页面显示 2D/3D 对象数量、一致性结果和两端 SHA-256 摘要。任何对象缺失、身份、父级、编码、位置、尺寸、旋转、层规格或图元差异都会失败关闭并展示技术错误。

## 2. 一致性证据模型

一致性验证不依赖截图，也不把输入 DTO 自我比较：

- 2D 清单来自编辑器实际 Konva 投影覆盖和规范化参数化图元计划。
- 3D 清单从实际 Three.js 对象树、InstancedMesh 实例矩阵、pick map 与多边形几何反向导出。
- 两端统一使用右手 Z-up、整数毫米和 `RotationZ`，并比较对象数量、LogicalId、对象类型、ParentLogicalId、业务编码、位置、尺寸、旋转、货架逐层规格及规范化 primitive。
- 规范 JSON 使用 Web Crypto SHA-256 生成摘要；结构逐项相等且摘要相同才标记通过。
- Removed / Disabled 对象不会进入预览。已移除货架即使仍带 Active 子层也整体跳过；真正的孤立 Active RackLevel 继续失败关闭。
- 2D 路径的多个 Konva 线段在套索选择时按 LogicalId 去重，保持“一个语义对象就是一个选择对象”。

自动化还会直接篡改构建后的 InstancedMesh 缩放矩阵，证明检查器能够从实际 3D 结果识别尺寸漂移，而不是复述源场景。

## 3. 验收映射

| 验收要求 | 机器证据 |
|---|---|
| 保存后不进行二次建模 | 同一个 scene prop 替换后 2D/3D 同步重建；保存/回读场景改变时两端摘要一起变化 |
| 2D/3D 对象数量一致 | 页面显示两端计数；清单测试逐项比较对象集合 |
| 标识、父级和编码一致 | Rack、RackLevel、Box、Path、Polygon 的 LogicalId、ParentLogicalId 和业务编码逐项相等 |
| 位置、尺寸和旋转一致 | 2D 规范图元与实际 3D 实例矩阵/对象几何统一归一到毫米后逐项相等 |
| 逐层货架与通用元素一致 | 自动化覆盖逐层规格、货架、箱体、路径和多边形 |
| 不以截图冒充验收 | 清单来自实际渲染结构；人为修改 InstancedMesh 尺寸后比较必定失败 |

## 4. 验证证据

| 门禁 | 结果 |
|---|---|
| 新增/相关前端聚焦 | 4 files / 13 tests passed |
| 前端全量（功能分支及合并态） | 108 files / 612 tests passed |
| 前端 type-check | passed |
| 前端 production build | passed；仅保留既有大 chunk 提示 |
| Space UnitTests | 231/231 passed |
| 默认 Space IntegrationTests | 140 passed / 55 SQL 环境门禁 skipped |
| `KOUSQLSERVER` Design Scene 真实 SQL | 3/3 passed，无跳过 |
| `KOUSQLSERVER` Space Integration 全量 | 195/195 passed，无跳过 |
| CP6.Tests 全量 | 2720 passed / 17 既有环境门禁 skipped / 0 failed |
| 完整 solution Release 非增量构建 | 0 error / 10 条既有 warning |
| SDK drift 与 TypeScript SDK strict no-emit | passed；无生成差异 |
| 差异门禁 | `git diff --check` passed |

默认 SQL 跳过项只按环境门禁记录，未伪装为通过。相关 Design Scene 与完整 Space Integration 已另用本机 `KOUSQLSERVER`、Windows 集成认证和临时数据库执行，均为零跳过。

## 5. 边界与下一步

本卡没有新增端点、DTO、数据库模型、Migration、OpenAPI 或 SDK 表面；没有把库存、任务或其他生产运行态写入 Draft，也没有在 3D 预览中开放写操作。

E04 S06 已完成。E04 S05 仍等待 E02 S07；E06 S01 仍依赖尚未完成的 E02～E05 与 E13 链路。下一张建议独立卡为 E03 S01“标准建模 Excel 模板”，其 E05 S02 前置依赖已满足，可先固化货架、逐层规格、库位与映射说明，为后续 Excel 字段映射和导入预检建立稳定输入合同。

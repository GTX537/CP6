# E05-S05 参数化 3D 生成器扩展完成报告

状态：**Complete**

证据日期：2026-07-30

功能提交：`856f138c`

受控集成提交：`a3864d9c`

## 交付结论

前端新增确定性参数化渲染链路，直接消费 Design API v1 生成客户端的场景类型，并通过
`SceneBuilder.buildDesign(scene)` 输出 Three.js 场景对象、参数化渲染计划和稳定拾取映射。

渲染器版本固定为：

```text
space-parametric-v1
```

相同场景输入、相同渲染器版本和相同 LogicalId 会生成顺序、键值与几何结果一致的渲染计划。

## 逐层货架

货架渲染严格使用 `RackRevision` 总包络与 `RackLevelRevision` 逐层参数：

- 每层独立使用 `BottomZ`、`ClearHeight`、`BinCount`、`DepthCount`、
  `CellWidth`、`CellDepth` 与 `BeamHeight`；
- 每个层梁、列位和深位生成稳定 primitive key；
- 层号重复、层间重叠、越过货架包络或缺少逐层数据时 fail closed；
- 明确禁止回退到旧的均匀 `levels × cols × depthCount` 推导；
- 货架以 `RackRevision` 原点角为旋转锚点，`RotationZ` 按角度解释；
- 派生库位中心坐标归一为整数毫米，以保持场景与缓存位置的确定性。

箱体实例的缩放顺序为数据坐标轴 `(Width, Depth, Height)`，随后由既有
`SceneRoot` 统一完成 Z-up 到 Three.js Y-up 的世界坐标转换。新增的旧货架框架回归测试还固定了
90 度原点角旋转后的中心、缩放和旋转值，防止新渲染链路破坏旧入口。

## 通用元素与资产

参数化生成器支持 schema v1 的常用元素几何：

- `box`；
- `path`，包括多段与斜向线段；
- `polygon`，包括洞和固定高度挤出；
- `point`；
- `asset`。

`point` 的 Z 坐标按领域契约为必填，缺失时失败关闭；`path` 与 `polygon` 的可选 Z 则保持
0 毫米默认值。元素平移、元素旋转、资产局部平移、局部旋转和缩放按复合变换顺序计算。
资产引用必须与场景中固定的具体 `AssetVersionId` 及 `System`/`Tenant` 范围一致。

浏览器端不会根据场景数据加载任意外部 URL、脚本或扩展。资产 v1 使用安全线框占位体；
transform 采用精确字段白名单，出现 `externalUrl` 等未知字段时立即拒绝。

## Three.js 构建与拾取

- 同材质箱体按角色合并为共享 `UNIT_BOX` 的 `InstancedMesh`；
- 货架包络、层梁、库位单元、通用元素和资产占位体使用独立材质角色；
- polygon 使用独立 `ExtrudeGeometry`，保留外环、洞、位置与旋转；
- 提供 instance → primitive、object → primitive、LogicalId → instances 的稳定映射；
- `dispose()` 释放本次构建的实例对象和 polygon geometry，同时保留共享 geometry/material。

既有 `makeInstanceMatrix` 和旧货架框架同步固定了两个坐标契约：

- `RotationZ` 按 API 契约使用角度；
- 实例缩放使用数据轴 `(W, D, H)`。

## 集成边界

E05-S05 不新增后端端点、数据库表、OpenAPI、SDK 或 Migration。它消费 E05-S02 的逐层货架
数据、E05-S03 的统一 Design scene DTO，以及 E05-S04 的固定资产版本与范围字段。

当前入口完成渲染器与统一 DTO 的集成；具体编辑器页面的数据获取和交互接线仍由对应的
前端编辑器/查看器任务负责。候选中的实例批次着色、库位计数、库存过滤、任务路由和性能场景
未被带入本卡。

## 验证

- 针对性渲染测试：2 个文件，7 passed；
- 完整前端单元测试：88 个文件，546 passed，0 failed；
- `vue-tsc --build`：通过；
- Vite production build：通过；
- 安全与范围扫描：未发现外部 URL 加载、动态 import、脚本执行或运行态业务能力混入；
- `git diff --check`：通过。

覆盖场景包括非均匀两层货架、原点角旋转、旧货架框架坐标、箱体/path/polygon/point/固定
资产元素、数据轴实例缩放、稳定拾取映射、资产复合变换，以及缺层数据、point 缺 Z、运行态
混入和不安全资产字段的 fail-closed 行为。

Vite 仍报告项目级大 chunk 提示，但不影响构建成功；本任务未扩大范围处理现有拆包策略。

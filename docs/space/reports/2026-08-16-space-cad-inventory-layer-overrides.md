# Space CAD 图层/块审核与逐层 Override 报告

日期：2026-08-16

任务分支：`codex/space-cad-inventory-overrides`

## 结论

详细 Spec LM-FR-012 的仓库纵切已闭环：CAD 起始向导不再只显示汇总数字，而是直接消费 Design V1 Preparation Preview 返回的完整图层与块审核清单。用户可以在启动解析前查看名称、颜色、线型、可见性、对象计数、支持状态以及块定义/引用统计。

LM-FR-013 本次只关闭逐图层 Override 和 Profile Scope 展示：用户可以沿用服务器 Profile、忽略图层，或覆盖语义目标、几何规则和置信度；系统公共与租户私有 Scope 在选择器中明确区分。当前默认 `ISpaceCadMappingProfileCatalog` 仍只注册内置 System Profile，因此可保存、版本化、跨租户隔离的 Tenant 私有 Profile 目录仍是后续任务，不能把 LM-FR-013 整体标记为完成。

## 数据与信任边界

- 来源字节继续只进入已批准的隔离 Preparation Provider；浏览器不会获得原始 DWG/DXF 内容。
- 服务端复用既有 `SpaceCadInventory` 和 `SpaceCadMapping` 权威，不建立第二套解析或映射模型。
- Preview 返回完整 Layer/Block 审核清单，但不返回逐 Block Reference 明细或可能携带来源路径的 External Reference Token，避免把无关属性值、客户路径和大体量引用列表传入浏览器。
- Mapping Override 仍由服务端验证已知 Layer、唯一性、目标/几何兼容性、尺寸与置信度范围，并写入既有 Mapping Replay Snapshot。
- 单位、坐标、Profile 或 Override 发生变化时，前端立即撤销两项确认并禁止使用旧 Start Request；只有重新生成且服务端重新密封的 Preview 才能启动 Parse。
- 本纵切没有数据库 Schema 或 Migration 变化。

## 用户可见行为

- Profile 选项显示“系统公共”或“租户私有”、版本和规则数。
- 图层清单支持按图层 ID、名称、颜色或线型搜索；显示可见/隐藏、实体、支持和未支持计数。
- 块清单支持按块 ID 或名称搜索；显示本地/外部、定义、引用和属性引用计数，但不显示外部引用路径令牌。
- 每个图层可以选择使用 Profile、忽略或映射为既有语义目标；目标覆盖时可调整几何规则与置信度。
- Override 或其他 Preparation 输入变更后持续显示“必须重新生成预览”，启动按钮保持禁用。

## 自动化证据

- CAD Preparation 服务聚焦：4/4 passed，验证 Inventory 返回、颜色/线型/可见性、块清单和密封 Override Snapshot。
- Design V1 CAD OpenAPI 聚焦：1/1 passed；新增 Inventory DTO、Layer/Block/Summary/Bounds 必填字段进入合同。
- Space Unit：540/540 passed。
- Space Integration：在 SQL Server 17 LocalDB 设置进程级 `CP6_TEST_SQLSERVER` 后 447/447 passed、0 skipped，最终复跑耗时 6 分 10 秒。
- CP6.Tests：2,932 passed；19 个既有环境门禁 skipped。
- Web：170 个文件、863 个测试 passed；CAD 向导聚焦 4/4 passed。
- Vue TypeScript 检查和 production build passed；OpenAPI、C#/TypeScript SDK 已重新生成；完整 solution Release build 0 warning / 0 error。

## 未关闭范围

- Tenant 私有 Mapping Profile 的持久化、版本管理、权限、跨租户隔离和管理 UI 未实现，因此 LM-FR-013 仍为 Partial。
- LM-FR-010～011、014～016、019/019A 仍须按当前实现与详细 Spec 逐项审计，不能因已有领域类或 fixture 自动视为完成。
- 本机 AutoCAD Core Console 仍只是一条开发转换链，不是 Site 已认证主 Provider；真实主备 Provider、20 份授权黄金 CAD、CP6 WMS、双仓 14 天 Pilot 和五方签字均未关闭。

因此 LM-FR-012 的仓库实现完成，LM-FR-013 部分完成；WP4 继续为 `Partial/Pending`，核心 GA 继续为 72% / `NoGo`。

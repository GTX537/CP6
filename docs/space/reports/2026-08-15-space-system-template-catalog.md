# Space System 整仓模板目录与实例化预览报告

日期：2026-08-15
任务分支：`codex/space-design-template-catalog`

## 结论

Design V1 现在提供第一份版本化、不可变的平台整仓模板目录。内置 `SPACE-STANDARD-01` 只复用仓库既有的确定性标准仓布局事实，不包含库存、任务或运行态数据；模板固定为 2 个楼层、7 个库区、20 条巷道、500 个货架和 10,000 个派生库位。

项目入口可以列出模板并请求实例化预览。预览返回模板/版本身份、布局内容 SHA-256、提案 SHA-256、计数和完整 Floor/Zone/Aisle/Rack 创建计划，同时固定 `writesDraft=false`。本纵切不创建 Draft、不申请租约、不提交命令，也不将平台模板伪装成租户模板。

租户私有不可变模板、Template → Draft 原子 Apply 及 Blank/Published/System/Tenant 四模式统一创建向导仍未完成，因此 LM-FR-001/WP1 保持 `Partial/Pending`，核心 GA 保持 72% / `NoGo`。

> 后续状态（2026-08-15）：System Template → 既有 Draft Floor 的分楼层原子 Apply 已由独立纵切交付，见 `2026-08-15-space-system-template-floor-apply.md`。Tenant 私有模板与四模式统一向导仍未完成，整体状态不变。

## 合同与安全边界

- `GET /api/space/design/v1/templates` 返回内部用户可见的整仓模板目录，可显式按 `System` 或 `Tenant` 过滤；当前 `Tenant` 结果诚实为空。
- `POST /api/space/design/v1/templates/{templateId}/instantiate` 只生成预览。请求必须绑定当前模板版本；过期版本返回稳定 409，未知模板返回稳定 404。
- 两个接口都拒绝外部主体。GET 要求 `space:model:read`；预览属于后续写入准备动作，要求 `space:model:edit`。
- 平台模板身份、版本身份、布局内容哈希和对象父级引用均由服务端确定；客户端不能提交任意模板内容或伪造 System scope。
- OpenAPI、C#/TypeScript SDK、required 字段和 Problem Details 已同步；本纵切没有数据库或 Migration 变化。

## 工作台行为

- 没有活动 Draft 时，Site 项目入口同时显示空白创建与平台整仓模板目录。
- 模板卡展示作用域、版本和楼层/货架/库位计数。
- 用户可查看密封预览的摘要和 Proposal Hash；页面持续提示“未写入 Draft”及下一步 Apply 尚未接通。
- 低于 1280px 时仍可查看模板预览，但不能通过既有入口创建 Draft/Floor。

## 验证

- 平台模板领域聚焦 2/2：身份/内容哈希稳定、标准计数一致、Floor→Zone→Aisle→Rack 引用完整、旧版本失败关闭。
- 服务边界聚焦 1/1：System/Tenant 过滤、预览零写入、旧版本 409、非法 scope 422、外部主体拒绝。
- Space Unit 全量 536/536；CP6.Tests 2,924 通过、19 项既有环境跳过、0 失败。
- OpenAPI 与权限聚焦 89/89；Web 全量 166 个测试文件 / 851 项测试，前端 API/入口/Space 首页聚焦 11/11；Vue TypeScript 与生产构建通过。
- OpenAPI 与 C#/TypeScript SDK 漂移检查、EF pending-model 检查和 GA 证据校验通过；完整 `CP6.slnx` Release 在 restore 后以非增量、单线程、禁用节点复用/共享编译方式通过：0 warning / 0 error，未降低 Desktop/Android 构建强度。

## 后续

1. 新增租户私有整仓模板的不可变目录、版本、同名跨租户隔离和公共模板只读保护。
2. 将模板预览绑定目标 Site、活动 Draft、Expected Content Revision 与 Proposal Hash，分楼层原子生成 Floor/Layout 命令并支持幂等重放。
3. 把 Blank、Published、System Template、Tenant Template 收敛成同一创建向导，并补 LM-FR-002 草稿来源、创建者、更新时间和 Blocking 摘要。

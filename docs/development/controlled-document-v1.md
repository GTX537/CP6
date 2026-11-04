# 受控技术文档原件与工作修订 v1

独立 Owner `Main.Plm.ControlledDocument.v1`，菜单 `plm-controlled-document`，页面 `/plm/controlled-document`，API `/api/controlled-document/owner-v1`。新增存储只支持 PostgreSQL。依据 WP05-16、SUP-REQ-002/003、AC-SUP-003/004 和 PG04-A177–182，实现准确来源登记和不可变工作内容。专业送审、原生 FLOW 签署与文档 Owner 应用见[文档专业评审 v1](document-review-v1.md)；REL 用途许可、正式工程冻结、分发/现场应用仍为后续输出。

## 内容与版本

一个文档具有稳定 UUID；每次 CREATE/REVISE 生成连续工作版本、独立 ETag、原始 JSON 字节与 SHA256。`revisionLabel` 是文控填写的修订标记，不能作为已批准的证据。原始 Owner、单位、来源版本、日期、取得依据、语言权威依据分别保存；未知资料允许留空，查询返回明确待补充项。登记者声明的来源与语言权威仍待专业认定。

每个内容位置有稳定 occurrence UUID，角色为 SOURCE、CONTROLLED、PREVIEW 或 TRANSLATION。预览/翻译明确指向本版的一份原件/正文，以及派生依据。它们引用 FILE Owner 的准确 ID、内容版本、SHA256、长度和媒体类型，不以文件名或 URL 代替身份。同名不同字节分别上传为不同文件；同一文件不能在一个文档版本内冒充多个内容角色。同一派生文件修订时不能静默改指不同原件；必须取得对应的新派生内容。

文档版本插入触发器核对准确 FILE 引用、租户和工程父对象，并在同一事务建立永久 bindings。文档版本、绑定、原命令结果和审计共同提交或回滚。数据库拒绝更改/删除原版本和绑定，保护不可用时服务拒绝继续。当前绑定表只是本 Owner 的实际引用，不代表全平台物理处置图已完整。

## 当前权限与历史

文档 action、Metadata/Content 字段权限与对象范围独立检查；部门范围来自真实工程修订绑定，Scope 1 按文档创建人。文件的 Content/FileName 字段、原父工程与产品当前权限继续独立检查。写入要求当前工程修订与产品版本；历史查询读取准确原版本，保留后来不可用的文件引用，不替换为最新内容。新增绑定验证原文件当前可用并读取准确存储字节检查长度与摘要。

`GET /{id}/content/{occurrence}?version=...` 在一个 Owner 事务内重新检查文档与文件当前权限、检查/保留状态和实际字节。返回 attachment、octet-stream、no-store、nosniff；文件完整性失败沿用 W189 持久隔离，随后恢复字节也不能跳过重新检查。历史状态不授予当前下载权。存储可达性最终以实际读取为准，目录中的安全状态不保证后续磁盘/网络持续可用。

工程 DOCUMENT 组成只能引用准确原生文档版本。原工程快照不因文档新版而移动；缺项和待审批状态继续显式返回。可加入工作组成不等于已获专业批准或允许制造。

查询的 `state`/`readiness` 是当前观察：只有对应准确版本的真实应用结果可以返回 APPROVED；后续工作修订仍为 DRAFT。未配置可信审批环境标识时，现有文档读取、修订和下载保留原权限要求，状态返回 UNKNOWN / REVIEW_STATUS_UNAVAILABLE，不读取其他环境的批准记录，不声称已批准或确定未批准。已配置环境的来源或数据库保护失效仍拒绝披露批准结论。

## 恢复与调用

- `GET /scope`、`GET /?revisionId=&search=&page=`、`GET /{id}?version=`、`GET /{id}/diff?before=&after=`。
- `POST /commands`：operationId、documentId、CREATE/REVISE、expectedVersion/expectedEtag、完整 body 和 reason。已处理原键按当前读取权限披露原结果；不同输入冲突，未知结果不得换键重试。
- `GET /operations/{id}`、`POST /operations/{id}/close`。确认未提交后可持久关闭；关闭键不可重新执行。

页面使用浏览器 Web Locks 和 tenant/actor 隔离的恢复记录，仅持久化操作与对象 UUID。身份变化、当前拒绝或卸载清除私有内容；迟到结果不能恢复旧身份数据。下载先核对文档中的准确长度与摘要。

## 准确修订的独立可用性（W307）

每个不可变文档修订另有 `availability`，含状态版本、独立 ETag、动作、原因、实际操作者、记录时间和原操作键。初始 AVAILABLE 只表示文档未被暂停或撤回；文件检查、当前权限、专业批准及制造用途许可仍分别裁决。它不改变文档内容版本、摘要、审批记录或已经固定的工程引用。

`POST /availability-commands` 接收 `operationId`、准确 `document` 引用、`action`、`expectedStateVersion`、`expectedEtag` 和 `reason`。独立权限 `plm-controlled-document:availability` 不自动授予已有编辑者；同时要求当前文档字段可写、父工程可编辑及原内容可读。HOLD 将 AVAILABLE 变为 HELD；RELEASE 将 HELD 变为 AVAILABLE，并重新核对 FILE 当前检查/hold/准确字节；WITHDRAW 将 AVAILABLE 或 HELD 变为不可恢复的 WITHDRAWN。后继工作修订是另一个准确身份，不继承旧批准。

可操作旧修订，无需把旧文档或旧工程改为最新版。普通修订编辑不更改旧修订可用性。FILE 的独立暂停/隔离不会被 DOC 的 RELEASE 解除；同一文件在另一个文档修订中的合法引用也不会因本修订暂停被全局改写。DOC 下载和新的工程引用、送审、评审决定、批准应用均遵守当前可用性；历史资料与原结果仍在当前读取权限下可查。已下载现场副本的处置、全局隐私保留/擦除及 LCM 义务不能由此状态替代。

状态事实、不可变原结果和审计同事务提交。数据库拒绝非法状态转换、历史修改、缺少准确回执的状态，以及缺少状态效果的可用性回执；读写锁沿原工程/文档顺序持有到提交。未知结果继续使用既有 `/operations/{id}` 查询/关闭协议，撤销写权限后仍可在当前读权限下取回原结果，不能换键重发。

`GET /{id}/availability-history?version=...&before=...` 按状态版本倒序，每页最多 20 条；`before` 使用上一页最小状态版本，历史不会因新状态插入而重复或移位。页面对当前与历史修订分别提供明确确认、取消、记录查询及未知结果恢复。

前向迁移为现有准确版本建立初始控制记录，原版本、FILE 安全状态和批准事实保持；GET 不补建缺失状态。此次仅在新建隔离 PostgreSQL 数据库及模拟前代布局的私有克隆验证，没有迁移既有环境。实际结果和未完范围见 [W307](evidence/W307-01/DELIVERY.md)。

## 验证范围

实际本地 PostgreSQL/HTTP 证明原件/派生关系、不可变绑定、历史不回退、当前字段/部门/父对象权限、并发 CAS、最终授权失败回滚、原键恢复和工程采用。文档事务测试的存储/扫描使用明确标识的测试替身；实际 HTTP 下载使用隔离本地磁盘，复用扫描事实不等于新扫描。W189 实际 Defender/HTTP 路径作为受影响回归执行，详细来源以[本轮报告](evidence/W190-01/DELIVERY.md)为准。不代表真实企业内容审批、生产验收或环境推广。

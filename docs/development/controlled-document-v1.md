# 受控技术文档原件与工作修订 v1

独立 Owner `Main.Plm.ControlledDocument.v1`，菜单 `plm-controlled-document`，页面 `/plm/controlled-document`，API `/api/controlled-document/owner-v1`。新增存储只支持 PostgreSQL。依据 WP05-16、SUP-REQ-002/003、AC-SUP-003/004 和 PG04-A177–182，实现准确来源登记和不可变工作内容；实际专业审批、REL 用途许可、正式工程冻结、分发/现场应用仍为后续输出。

## 内容与版本

一个文档具有稳定 UUID；每次 CREATE/REVISE 生成连续工作版本、独立 ETag、原始 JSON 字节与 SHA256。`revisionLabel` 是文控填写的修订标记，不能作为已批准的证据。原始 Owner、单位、来源版本、日期、取得依据、语言权威依据分别保存；未知资料允许留空，查询返回明确待补充项。登记者声明的来源与语言权威仍待专业认定。

每个内容位置有稳定 occurrence UUID，角色为 SOURCE、CONTROLLED、PREVIEW 或 TRANSLATION。预览/翻译明确指向本版的一份原件/正文，以及派生依据。它们引用 FILE Owner 的准确 ID、内容版本、SHA256、长度和媒体类型，不以文件名或 URL 代替身份。同名不同字节分别上传为不同文件；同一文件不能在一个文档版本内冒充多个内容角色。同一派生文件修订时不能静默改指不同原件；必须取得对应的新派生内容。

文档版本插入触发器核对准确 FILE 引用、租户和工程父对象，并在同一事务建立永久 bindings。文档版本、绑定、原命令结果和审计共同提交或回滚。数据库拒绝更改/删除原版本和绑定，保护不可用时服务拒绝继续。当前绑定表只是本 Owner 的实际引用，不代表全平台物理处置图已完整。

## 当前权限与历史

文档 action、Metadata/Content 字段权限与对象范围独立检查；部门范围来自真实工程修订绑定，Scope 1 按文档创建人。文件的 Content/FileName 字段、原父工程与产品当前权限继续独立检查。写入要求当前工程修订与产品版本；历史查询读取准确原版本，保留后来不可用的文件引用，不替换为最新内容。新增绑定验证原文件当前可用并读取准确存储字节检查长度与摘要。

`GET /{id}/content/{occurrence}?version=...` 在一个 Owner 事务内重新检查文档与文件当前权限、检查/保留状态和实际字节。返回 attachment、octet-stream、no-store、nosniff；文件完整性失败沿用 W189 持久隔离，随后恢复字节也不能跳过重新检查。历史状态不授予当前下载权。存储可达性最终以实际读取为准，目录中的安全状态不保证后续磁盘/网络持续可用。

工程 DOCUMENT 组成只能引用准确原生文档版本。原工程快照不因文档新版而移动；缺项和待审批状态继续显式返回。可加入工作组成不等于已获专业批准或允许制造。

## 恢复与调用

- `GET /scope`、`GET /?revisionId=&search=&page=`、`GET /{id}?version=`、`GET /{id}/diff?before=&after=`。
- `POST /commands`：operationId、documentId、CREATE/REVISE、expectedVersion/expectedEtag、完整 body 和 reason。已处理原键按当前读取权限披露原结果；不同输入冲突，未知结果不得换键重试。
- `GET /operations/{id}`、`POST /operations/{id}/close`。确认未提交后可持久关闭；关闭键不可重新执行。

页面使用浏览器 Web Locks 和 tenant/actor 隔离的恢复记录，仅持久化操作与对象 UUID。身份变化、当前拒绝或卸载清除私有内容；迟到结果不能恢复旧身份数据。下载先核对文档中的准确长度与摘要。

## 验证范围

实际本地 PostgreSQL/HTTP 证明原件/派生关系、不可变绑定、历史不回退、当前字段/部门/父对象权限、并发 CAS、最终授权失败回滚、原键恢复和工程采用。文档事务测试的存储/扫描使用明确标识的测试替身；实际 HTTP 下载使用隔离本地磁盘，复用扫描事实不等于新扫描。W189 实际 Defender/HTTP 路径作为受影响回归执行，详细来源以[本轮报告](evidence/W190-01/DELIVERY.md)为准。不代表真实企业内容审批、生产验收或环境推广。

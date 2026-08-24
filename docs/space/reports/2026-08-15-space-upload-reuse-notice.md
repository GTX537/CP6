# Space 上传重复内容复用提示报告

日期：2026-08-15
任务分支：`codex/space-upload-reuse-notice`

## 结论

Space Studio 的 CAD 与 PDF/图片底图上传现会消费服务端已有的 `Reused` 事实，并在检测到重复内容时明确告诉用户：系统已按 SHA-256 复用受控文件或当前来源，不会重复保存原文件。

本纵切没有把文件哈希或复用判断移到浏览器。扩展名、声明 MIME、文件签名、大小、恶意内容和压缩包检查仍由既有隔离上传与安全扫描链负责；只有服务端确认 `Reused=true` 时页面才显示复用提示。

## 行为边界

- CAD 上传响应的前端合同补齐 `file` 与必填 `reused`，不再静默丢弃服务端复用事实。
- 新 CAD 显示常规上传成功提示；重复 CAD 改为明确的 SHA-256 复用提示，随后仍进入同一安全扫描/起始向导。
- 重复底图显示同一复用提示，随后仍按文件当前状态执行 Clean 直接挂接、隔离轮询或 Rejected 失败关闭。
- 提示措辞同时覆盖“复用同一 Tenant 的受控文件”和“复用当前 Version 已有来源”，不虚构物理存储细节。
- Excel 上传后端与生成 SDK 已返回 `Reused`，但当前 Space Studio 尚无统一 Excel 起始上传界面；该 UI 仍随三路径统一向导处理。

## 自动化证据

- 新增纯行为测试，验证新文件不提示、重复 CAD/底图返回明确复用说明。
- CAD 与底图 API 聚焦测试连同新行为测试 10/10 通过。
- Vue TypeScript 检查、Web 全量 168 个测试文件/858 个测试与 production build 通过；构建只有既有大 chunk 提示。
- 本纵切无后端、OpenAPI、SDK、数据库 Schema 或 Migration 变化。

## 未关闭范围

- LM-FR-003 的真实恶意文件扫描引擎和隔离 Worker 仍需生产等价部署证据；仓库代码失败关闭并不能替代外部验收。
- Excel 的统一上传/映射向导尚未在 Space Studio 直接消费 `Reused` 提示。
- LM-FR-005 的来源删除引用预检尚未实现。
- 真实 DWG/DXF/Excel/PDF、多 Provider、WMS、双仓 Pilot 和五方签字仍是 GA 硬门槛。

因此 LM-FR-004 的后端复用合同和当前两条直接上传 UI 已闭环，但 WP4 继续为 `Partial/Pending`，核心 GA 继续为 72% / `NoGo`。

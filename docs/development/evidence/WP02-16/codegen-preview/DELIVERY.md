# WP02-16 · 安全即时预览局部交付

本次对应第九轮 D 已确认的 CodeGen 局部窗口。源码基线为 `37d84b4b6eaeefe7e8b35c21df26a8b1f8c119d5`；Handoff 分配为 `b2c72583d8bb1147e2e853abf0e7e63a111c9830`，START 为 `c3da88a0cf1d273e29c0137e6528673294ed753b`。候选提交、草稿 PR 与交付消息的固定链接由 Handoff DELIVERY 记录，避免报告自引用。

## 改动及兼容性

固定内嵌 CRUD 模板在渲染前检查标识符、支持的标量类型、重复字段、模板成员冲突和安全建议文件名；文本按 C# 字符串、XML 文档注释、TypeScript 字符串分别转义。沿用实体字段长度边界，并限制请求体和列数。输入不能选择或下载模板，也不能执行生成文本。

新增 `POST /api/pub/codegen/preview/v1`，成功仍为 `{code:0,data:...}` 包装；data 包含 `schemaVersion`、固定 `templateRef` / `templateVersion`、`inputDigest`、`applied:false` 和 `outputs[{fileName,language,content}]`。摘要绑定渲染输入与内嵌模板内容；不纳入自动生成的 ID、审计时间。原 POST `/preview` 与持久 GET `/{id}/preview` 成功时保持文件名字典形状；校验失败返回安全的 400 错误，不泄露服务器路径。预览端点保留认证并要求 `pub-codegen:view`；持久 GET 补齐同一 view 检查。Tables / Save 流程和保存权限未改。

现有页面显式调用 v1，展示模板、摘要、建议文件和“未应用”；内容使用 Vue 文本插值。编辑输入会清除已有预览，编辑期间返回的旧请求也不会重新显示。预览不调用保存或采番，原保存按钮仍单独受 `pub-codegen:save` 控制。

## 验证

2026-10-16 完成相关本地验证和一次集中 SPEC/QUALITY 审查。集中审查指出 `ColumnConfig` / `VolTable` 被无实际冲突地限制为非法实体/模块名；已移除这两项限制并加入合法名称回归，审查者定向复查通过。没有另启全量审查。

- SDK 8：`dotnet restore CP6.Tests/CP6.Tests.csproj --locked-mode --packages D:/CP6-local-test/WP02-02/adopt-a1/n -p:NuGetAudit=false --ignore-failed-sources` 成功，项目和锁文件未改。
- `dotnet test CP6.Tests/CP6.Tests.csproj --no-restore --filter FullyQualifiedName~CodeGen --logger "trx;LogFileName=codegen.trx" --results-directory D:/CP6-local-test/WP02-16/backend-01 -p:UseSharedCompilation=false -m:1`：**25 通过，0 失败，0 跳过，退出码 0**。涵盖现有生成接口回归、安全与边界、真实 Kestrel/MVC 的未认证 401、仅 save 无 view 的 403、view 允许预览而不允许保存、旧字典/v1、持久错误元数据、无效 JSON/null、超限请求与保存拦截。测试用内存数据库和测试身份/权限源，不启动正式宿主或数据库服务。原始结果：[codegen.trx](codegen.trx)。
- 审查修正后，同一命令改用 `--filter FullyQualifiedName~CodeGenPreviewTests`，结果目录 `backend-fix`、日志名 `codegen-fix.trx`：**20 通过，0 失败，0 跳过，退出码 0**，包含两个新增合法名称回归。仅放宽这两个无冲突名称，原通过的 HTTP/旧接口测试结果从上一轮复用，没有将其改称再次执行。原始结果：[codegen-fix.trx](codegen-fix.trx)。相关构建只有既有未改文件的异步/分析器警告，没有本任务构建错误。
- 前端 `npm ci --ignore-scripts --no-audit --no-fund` 按既有锁文件安装；`npm run type-check` 成功。后续只改变未应用标记的 HTML/CSS，不改变已检查的 TypeScript 输入，复用该结果。
- `node docs/development/evidence/WP02-16/codegen-preview/check-page.mjs` 在真实 Vue 页面、浏览器和 API 夹具下通过：版本接口请求、只有 view 无 save、只读恶意文本、多文件切换、输入变更清空、忽略过期响应、安全错误、无 view 隐藏、无保存请求。不是生产认证或真实业务端到端验收。
- 页面检查最终使用操作系统分配的自有 `127.0.0.1:58176`，浏览器与 Vite 在 finally 中关闭。首次检查虽通过，但 Vite 将配置的 0 解释为默认 5173；已关闭当次自有实例并修正检查脚本，保留原记录。未停止他人服务。
- 本地日志保留在 `D:/CP6-local-test/WP02-16/`：`restore.log`、`type-check.log`、`page-check-01.log`、`page-check-02.log`；页面截图在 `page-final/preview-page.png`。首条 START 文档曾因 CRLF 被 diff-check 拒绝，提交前已修正为 LF，没有改写已发布消息。
- 后端完整输出为同目录 `backend-01.log`、`backend-fix.log`；两次测试均首跑通过，没有隐藏失败或跳过项。未因提交或 SHA 改变重复测试。

发布前核对候选与远端 main 的 `.github/workflows` tree 均为 `3355235636fd45116f5445fca32fc65e49f13003`：12 个仅手动工作流，1 个 R2 工作流仅匹配 `v*.*.*` Tag；无 PR 或任务分支自动编译/测试触发。当前任务分支无 Actions 运行，工作流及其依赖文件未改。最终 diff 仅限派工路径，包含的前端 Git 路径原本为小写 `cp6.web`（Windows 工作树同路径）。

## 适用范围与未执行项

仅覆盖 NU04 的即时纯预览、版本追溯、局部能力检查和只读展示。测试中的身份、权限来源和内存元数据是测试夹具，不证明真实租户存储隔离、真实 Owner 采用或生产权限配置。原 16 条业务 AC 不整体提升；元数据 CAS 保存/unknown 恢复、原子采番、编号事务、实际文件应用、数据库迁移、发布与部署均未实施或验收。

完整 WP02-16 的 `PLANNED / NOT_CHECKED` 与 WP02-10 前置保持；共享台账、生成页、四份状态文件和 main 合并由 COORD 串行处理。没有运行或重跑 GitHub Actions，没有取消任务运行或改变触发、保护配置，没有使用 PostgreSQL 实例、发包、Tag 或部署。

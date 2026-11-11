# W483-01 · 询盘导出刷新及重新登录恢复

状态：本任务已正常远端采用并完成生命周期；完整原卡不提前关闭。用户接受的 [12h 执行](../../planning/20261111-codex-12h-business-chain.md)继续；单对话 COORD，无子代理。Main/CRM 各使用已有 DEV-1/DEV-2，一个跨仓任务、两个 `codex/w483-01` 分支；基线 Main `7a8cd529f255a762f33fca11488e1e93dd6762c6`、CRM `8c78760ef7ae746a55bdf34d81a17439f7307715`。两仓官方 status/start 已完成，未新增物理工作树。

## 前项及 4h 检查点

W482 Main [#950](https://github.com/GTX537/CP6/pull/950) 与 CRM [#168](https://github.com/GTX537/CP6.CRM/pull/168) 已正常采用并官方 finish，两仓准确任务分支清理、工作区干净、席位释放；Main/CRM 主目录已安全快进。实际范围及失败保留见 [W482 唯一报告](../W482-01/DELIVERY.md)。首个检查点原生读数为 14,623 秒（4h03m43s），包含正常收尾；12h 总目标仍 ACTIVE。W481 v11 原件组合采用已完成，原 WP02-02 的剩余与正式依赖不改称完成。

W482 已完成真实 PG/HTTP/TLS/HTTPS/React 导出链、一次性文件、原 202 恢复、五分钟 TTL 与原 60 分钟保留/hold/故障原子回滚及联合清除。正式原卡、60 AC/R1、PG16 PASS/11 NOT_RUN 保持真实边界。后续 4–8h 优先本窗口刷新/重新登录恢复、R1 终态页面及准确原验收采用；构建历史 OOM 若确实影响后继，按独立构建任务处理，不混入业务修复。

首次组合 start 命令在执行前被自动审批以 `blocked by policy` 拒绝，没有创建分支/占用；只读复核三席位空闲及无重复 PR 后，逐仓通过同一官方入口正常登记成功。没有绕过登记、提高配额或新建目录；此前确认问题已不再需要，沿用用户的 12h 授权继续。

## 准入、真实问题及设计

原任务 WP03-04 / CRM-INQ-01 的未知导出与恢复行为，直接采用 W482 已交付原生 Owner、Main v2 当前服务上下文、R3/R1 严格 DTO、加密文件和默认关闭开关。只读源码显示申请 key/job 仅在组件内存，v1 呈现上下文不返回原 job 定位；先用实际 HTTPS 浏览器构造“实际提交后失去 202、刷新页面”的场景确认，不以静态判断冒业务失败。

原生产基线的两个真实 HTTPS 浏览器场景均已 RED：丢失原 202 后刷新、同一主体实际登录/回调换新会话，两者均因 65 秒内没有下载入口失败，非 CSS/夹具错误。已登记 CRM [Issue #169](https://github.com/GTX537/CP6.CRM/issues/169)，沿已登记的同一 `codex/w483-01` 修复。

只补明确版本的只读恢复上下文和页面采用：`GET .../{submissionId}/privacy-export-context-v2` 返回 `schemaVersion=crm-inquiry-privacy-export-context-v2`，在原七字段上增加可空 `latestJobId`。在同一现有当前人工权限/Submission 锁边界内，仅查询同租户、同主体、同 Submission 的最近原任务，不返回凭证；后续沿原 job 读取/等待/下载重新核对当前权限，不自动创建第二 Job。新会话不复用旧授权；凭证/PII 不进 URL、localStorage/sessionStorage。原 v1 七字段、R3/R1、默认下载元数据及数据库安装/权限均保持。只有终态后用户明确选择再次申请，才允许读取新版本并创建新申请。

互斥资源由 COORD 串行独占：CRM 原生 privacy Owner/呈现 context、Web API/BFF/React、相关测试与必要文档；Main 仅协调报告/roadmap/生成页及四状态。没有授权改 R3/R1 业务合同、缩小合法输入、变更当前授权/行截止/Owner、迁移既有环境、触发 Actions 或部署。依赖代码与成功证据按未变输入复用，不为 SHA 或文档变化重跑。

## 可核验出口

- [x] 真实浏览器 RED 确认刷新恢复缺口；CRM Issue #169 为本问题唯一活动 BUG。
- [x] 修复后原实际 202 丢失/刷新及实际重新登录恢复同一 Job，数据库只有原申请效果；不自动另发申请。
- [x] 当前主体/租户/资源与权限变化保持必要边界，旧 v1 严格形状保留；原结果/一次性凭证/实际文件不变。
- [x] R1 终态页面按原合法五分钟 TTL 实际执行，内部/数据库时钟不改变；复用 W482 未受影响的 60 分钟保留证据。
- [x] 受影响行为/必要共享页面回归，一次完整任务 SPEC/QUALITY 审查；仅相关修复回归。
- [x] 正常分仓 PR/远端 main 核对、官方 finish；停止自有进程，准确分支处置、工作区状态和席位释放分别报告。

实际日志/制品保存在 `D:/CP6-local-test/CP6-12H-20261111/W483-01`。业务/身份/政策及外部 IAM 输入明确模拟；真实 Node/HTTPS/BFF、原生应用、PostgreSQL 18、worker、React/Playwright、登录回调与文件下载实际执行。

| 证据 | 实际结果与范围 |
|---|---|
| `browser-restore-red-01/results.trx` | 0 PASS / 2 FAIL / 0 skipped，未修改生产代码前的刷新/实际新登录失败；原始失败和截图保留。 |
| `recovery-green-01/results.trx` | 9 PASS / 0 FAIL / 0 skipped：6 项真实 HTTPS 浏览器（含原正常/丢202/拒绝/晚到及新增刷新/实际新登录），3 项原生 PG/HTTP 的最近自有 Job、v1、同租户其他已授权主体、跨租户/资源及当前撤权。恢复场景数据库各仅一个 Job/一次凭证消费。 |
| Web TypeScript 检查、`inquiry-privacy-export.test` | `next typegen` / `tsc --noEmit --incremental false` 及测试 TS 编译退出 0；6 项导出/BFF/client 行为通过，含显式 v2、v1 闭合形状和错绑拒绝。 |
| `web-shared.log` | 14 PASS / 0 FAIL / 0 skipped：同一 BFF/client 的既有 intake proxy、intake client 与 consent 行为。 |
| `recovery-terminal-01/results.trx` | 1 PASS / 1 FAIL / 0 skipped：已消费后页面恢复终态，用户明确选择后才创建第二个申请并下载，实际两个 Job/两次消费。五分钟项在过原期限后的夹具维护调用碰到不存在的 `crm_inquiry_retention_v1.Coverage`，42P01；本场景未启用 retention schema，属夹具错误，原失败保留，不计通过。 |
| `recovery-expiry-02/results.trx` | 1 PASS / 0 FAIL / 0 skipped。去掉上述无关 Coverage 调用，仅按原入口刷新当前权限并运行 privacy worker；实际 18:43:23Z 至 18:48:39Z（5m15s）。原五分钟到期后，真实 API 返回 R1 99、expiry/artifact/proof 均 null；刷新显示失效/再申请入口、无下载、无自动 POST。生产实现无变化，前述有效成功直接复用。 |

复现命令为 CRM `dotnet test tests/CP6.CRM.IdentityPostgreSqlTests/CP6.CRM.IdentityPostgreSqlTests.csproj`，恢复过滤 `FullyQualifiedName~Native_privacy_recovery|FullyQualifiedName~Native_privacy_browser`，终态仅 `export-consumed|export-expired`；设置自有 `CP6_CRM_PG_SETTINGS`、Node 22、`CP6_CRM_CONFIGURATION_UI_OUTPUT` 与真实 `apps/web/app/styles.css`。完整私有调用保存在 `run-tests.ps1`。Web 按既有 `tsconfig.test.json` 输出到私有目录，Node `--test` 指定上述受影响文件。Main 无产品代码变化，文档只查差异、引用和台账生成，不做全仓编译。

未变化的 Main v2 服务来源/TLS、原 202 事务、精确文件字节和一次性凭证、原 60 分钟保留/hold/故障共同回滚/联合清除直接引用 [W482 有效证据](../W482-01/DELIVERY.md)，不改称本轮新执行。完整原卡、原 60 AC、两项修订及 PG16/11 尚未据本窗口提升。

集中审查已覆盖当前两仓完整修改范围：原 v1/R3/R1 和新 v2 的闭合边界、当前人工权限/锁内最后核对、参数化同租户/主体/询盘查询、读取失败不自动新申请、原 Job/证明/文件与 once 消费、页面中止/清除和晚到、显式终态新意图，以及范围内报告/台账/四状态。未改存储、DDL、依赖或 workflow，不扩展为全仓测试。唯一后续调整为上述夹具的无关 Coverage 调用，仅复查该处并实际五分钟回归通过；无未解决的本任务必需门禁失败。桌面、手机和实际 99 终态截图已查看，无横向溢出；纯样式未扩大修改。

## 正常采用与生命周期

CRM [PR #170](https://github.com/GTX537/CP6.CRM/pull/170) 已将源 `da9ed652e946e5ecec03dc9a1b7577453ee25376` 正常合入远端 main `8edb41fee2a300a5177eb142ced397253aeeeb07`；已 fetch 核对祖先关系与完整源码树均为 `3d9aff6c1f0ae942a5535dfbcb12a805b1121298`。按未变输入复用成功验证，不因 merge SHA 重跑；[Issue #169](https://github.com/GTX537/CP6.CRM/issues/169) 在此远端核对后回写结果并关闭。官方 finish 已成功：准确本地/远端 `codex/w483-01` 已删除，DEV-2 干净、detached 供复用，席位已释放；证明位于本任务私有根 `crm-finish.json`。保留 node_modules；本轮 typegen 生成的 `.next` 四文件完整可逆移至私有 `retained-generated/next`，未删文件。

本轮自有 PostgreSQL 经系统身份、目录、marker 与无其他客户端核验后 smart 停止，数据和失败日志保留；本轮 HTTP/.NET/Node/Chromium 进程已退出。Main/CRM workflow 树仍分别为 `3355235636fd45116f5445fca32fc65e49f13003` / `0c2d74f9fdc63f5178f97baf33950fb3d4dad751`；普通入口手动、未建版本 tag，CRM 本任务 Actions 列表为空。无运行取消、触发/保护修改、既有数据库安装迁移或部署。

Main [PR #951](https://github.com/GTX537/CP6/pull/951) 已将协调源 `f92a220b5a40046a8a500cffa0c607942278db10` 正常合入远端 main `0a30d55436a5169174da95e905a8163abaf51b86`，已 fetch 核对祖先与完整源码树一致。官方 finish 成功，准确本地/远端任务分支删除，DEV-1 干净、detached 供复用，席位已释放；本轮一份 Python 再生缓存完整可逆保存后释放，未删除。最终证明为同任务私有 `main-finish.json` / `crm-finish.json`。两仓主目录随后安全快进，Main 既有14,932个未跟踪资料路径保留。12h 总目标继续 ACTIVE；本恢复出口不关闭原 WP03-04、原60AC/R1、PG16/11或生产资格。

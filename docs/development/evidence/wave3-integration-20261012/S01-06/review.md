# S01-06 协调接受核对

结论：**可接受进入协调串行集成，无新增实质阻塞。** 本报告不代表任务 DONE、远端 main 已包含或生产验收。既有集中审查覆盖实现；最终增量仅为证据，适用的成功验证可复用。

- 核对日期：2026-10-12（America/New_York）。只读工作区：`D:/CP6-worktrees/s01-06-20261012`。
- 固定基线：`8569b669fed56ddc7b53b7bb24666c7fb93d544a`。
- 最终候选：`f406e4e515a7ce82d77db5307e467e7bcdbc25e4`；核对时 HEAD 相等，工作区干净。
- 既有集中审查对象：`f13da00e8470200877c715658891f09833280592`，见任务 `REVIEW.md`；Critical/Important 均为 0。

## 范围与审查复用

已阅读派工 S01-06.md、DELIVERY/REVIEW/IMPLEMENTATION、source-provenance、green-state、validation-summary、原始 TRX/TS JSON、case-matrix、证据生成脚本及本次源码/测试/Schema。

基线至最终候选共新增 32 个文件，全部位于派工五类独占路径：C# PlanAtp 目录、Web plan-atp 目录、指定 StableIdTests.cs、指定 atp-stable-id.schema.json、S01-06 独有证据目录。没有修改共享项目/依赖配置、数据库、其他任务文件或项目状态页。完整差异格式检查通过。

既有审查提交至最终候选仅改变 DELIVERY.md、IMPLEMENTATION.md、REVIEW.md、publication-precheck.json、start-transport-verification.json 五个证据文件。IMPLEMENTATION 只修正创建基线与实际执行基线措辞；源码、测试、Schema、依赖和测试输入没有后续变化。复用原任务级集中审查，补核上述最终证据增量，没有重启整套测试。

## 验证证据核对

下列测试数字来自原执行结果；本次只独立解析报告、检查哈希与来源，没有重新执行业务测试。

| 项目 | 原结果及本次核对 |
|---|---|
| C# | TRX 实际 60 条结果，60 执行/通过，失败、跳过及其他非通过计数为 0；实际 assembly 路径指向本工作区产物 |
| TS + Schema | JSON 实际 81 条断言全部 passed，失败/待执行为 0，suite 路径指向本工作区 stableId.test.ts |
| 固定向量 | 22 个唯一 ID 与派工 profile 完全相等；原输入/预期相等；C#、TS、Schema 各有 22 条对应的实际通过结果 |
| GREEN 输入/产物 | green-state 的 21 项文件（含源码、测试、Schema、输入、配置、DLL/deps/assets）及 5 项原始输出，当前原字节长度和 SHA-256 全部匹配 |
| Web 类型检查 | 复用原执行者记录的 npm type-check 退出 0，以及既有独立审查；原日志仅含命令启动信息，不能单靠空白输出推出退出 0 |
| 固定来源 | 四份原始/composed 来源及五项 patch bundle 引用/pin 均与固定字节匹配；完整 PG 九项操作内存重组的最终 SHA 与记录相等 |
| 原失败 | RED C# 3 通过/57 失败，TS 40 通过/41 失败，无跳过；保留原记录，未以 GREEN 覆盖 |

新源码、测试、Schema 和冻结 profile/向量的 Git blob 字节与执行快照完全一致。另有八个未修改配置/派工输入采用工作区 CRLF、Git blob LF：dispatch.json、CP6.Entity.csproj、其 packages.lock.json、Directory.Build.props、Web package.json/package-lock.json/tsconfig.app.json/vitest.config.ts。它们的原字节均匹配 GREEN 快照，LF 规范化后匹配最终 Git blob，且 Git blob 与基线相等；这是换行表示差异，不是输入内容变化。本次最初诊断曾使用“所有工作区原字节必须等于 Git blob”的过严断言，定位上述原因后按工作区实际执行身份及固定 Git 内容分别核对通过；未修改任何快照。

时间与产物交叉核对：TRX 运行于 2026-10-12 20:37:19–20:37:21 -04:00；TS 报告使用同一工作区的绝对路径；GREEN 快照时间为 2026-10-13T00:38:34.503409+00:00，晚于两者。环境记录为 Windows、.NET SDK 8.0.424/net8.0、Node 20.19.0、npm 10.8.2；这些版本来自原执行声明，capture-state 中为固定字符串，本次未重新测量环境版本。

## 重点代码核对

- C# StableId 是 sealed record class，不是 struct，不存在 default(struct) 产生合法外观空值的问题。默认引用为 null；私有构造与只读 Value 限制正常绕过解析；JSON converter 的 HandleNull 路径拒绝 null，且仅接受 string token。
- C# 使用绝对起止锚点，TS 使用绝对结束断言；首字符限 ASCII 字母数字。保留原大小写、标点和字符串值，不 trim、不截断、不自动转换。边界 1/16/17/128 接受、129 拒绝；尾 LF、CRLF、CR 及 Unicode 换行有覆盖。生产 StrictJson 的原 UTF-8 保留与重新序列化后的字符串值分开验证。
- Schema 使用独立标量片段身份 `urn:cp6:plan-atp:stable-id:20261011-r1`，保留完整 ATP wire 来源说明。其 type/minLength/maxLength/pattern 四个断言与冻结 profile、实际 composed 来源相等。81 项结果中的 Schema 断言是局部关键字执行，不是通用 Schema 引擎或完整 ATP wire 验收。
- PostgreSQL DDL 仅作为固定只读来源，在内存完成九项 patch 操作重组：四项 lexical 替换覆盖五个 UOM 列，加五项 PLAN-IF-005 恢复/重放绑定与锁规则。五列为 evaluation.result_uom（可空）、segment.uom、basis.native_uom、basis.result_uom、reservation_ledger.uom（后四项不可空），均保持 varchar(128) 和既定 lexical 检查。没有 DDL 编辑、数据库执行或迁移，存储采用留待 S01-10。
- 现有 ISupplyService 仍为旧 MRP decimal 标量供应入口；本候选未冒认为 ATP R11 Owner 接入。

## 操作与未覆盖范围

本次执行的是 `git -c core.longpaths=true status --short`、固定 SHA 的 `git diff --stat/--name-status/--check`、`git show` 读取 blob，以及本地 PowerShell/Python 文件读取、JSON/TRX 解析、SHA-256 和内存重组诊断。核对结束时任务工作区仍干净。唯一写入为本报告所在 scratch 目录；没有源码编辑、提交、网络、Actions、数据库或全量业务验证。

未覆盖完整 ATP wire、业务/E2E、Owner 采用、数据库迁移、包发布、生产候选/签名或部署。类型检查退出状态与环境版本按上述原执行来源复用，不宣称本次独立重新测得。publication-precheck 为原时点的证据；协调者在后续 push/PR 前仍须按策略核对当时远端工作流/保护及依赖，串行集成后同步状态并核对远端 main 包含性。

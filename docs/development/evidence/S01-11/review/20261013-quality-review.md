# S01-11 独立质量审查

结论：**PASS**。阻断项 0；保留 1 项真实接线前的非阻断容量风险。此为 SPEC PASS 后、同一次任务集中审查的 QUALITY 阶段，不是重复全仓审查。

审查对象为 `e20740e37e20bc0210e0b32c5670c2bce962002a` 基线上的任务实现、测试与已暂存证据。已核对 88 个 staged 文件全部在任务白名单内；8 个生产/测试文件与 SPEC、最终运行及 index 的原始字节完全一致。`git -c core.longpaths=true diff --cached --check` 返回 0。当前阶段索引清单及源码摘要见 [质量审计数据](20261013-quality-review.json)。后续协调代理的交付、源码绑定和发布证据由其继续核对。

## 实现与测试结论

- C# 金额/数量语义使用 BigInteger，TS 使用 bigint；原有金额字符串、身份、原始 UTF-8 和摘要分开保留。受限整数在精确 token 校验后才转换，没有用浮点比较大整数或金额。
- 构造器及异步入口在 hash/await 前复制输入；C# 返回独立 JsonElement，TS 递归冻结完整值。原始字节访问始终返回副本。源码与现有回归都覆盖调用方修改输入、返回字节、schema buffer 的情况。
- 严格解析、decoded 重复键、own-property、封闭字段、required/null-only、schema pin 和具名 root 检查连贯。精确结构相等处理顺序差异、数值词法及带 NUL 的引用键，不依赖拼接分隔符或 JSON 浮点再序列化。
- current/frozen 的 resultRef 判定与 registry 明确隔离；Frozen replay 只保存和返回完整旧 body，没有重建/重新脱敏/再次业务写入入口。适配结果明确否认 FOUND、释放和派发权限，无法由 wire 成功替代可信 Owner/Store 证明。
- 测试直接调用生产 codec，无 mock 或跳过替代。固定向量、完整对象、union/required/null-only、非法金额、错身份、严格 JSON、字节保留与证明不足断言均有实际执行结果。362 个构造用例和 38 个选定设计投影保持不同来源；RED 与首次 type-check 失败未被覆盖。
- 固定三文件闭包解释器与源/API 映射一致，未宣称通用 schema 服务。共享合同、依赖、业务写入口与数据库未扩展。局部敏感 token/私钥模式扫描没有命中；这不等于全面安全审计。

复用已核验的最终 C# 417/417、TS 416/416、type-check exit 0、设计审计 5/5 结果。未重跑这些门禁。

## 非阻断项 Q-NB-01：大量合法区间的容量

`CP6.Entity/Contracts/ErpCredit/NativeCreditSemantics.cs:89` 与 `cp6.web/src/contracts/erp-credit/creditCodec.ts:245` 每加入一个区间都扫描已见区间，复杂度为 O(n²)。准确 schema 允许 20 个组件、每组件 100 个 allocation、每 allocation 100 个 range，即最多 200,000 个区间；逐对比较的理论数量为 19,999,900,000。

为确认影响，只执行了一次小范围 TS 内存探针：读取未变化的生产文件、使用已安装 TypeScript 内存转译，并构造合法不重叠区间。1,000、2,000、4,000 个区间分别约 76、178、579 毫秒，全部接受；运行环境为 Node 20.19.0。记录见 [容量探针](20261013-quality-capacity-probe.json)。没有执行最大规模，也没有测量 C# 容量，不能把理论比较量或延伸估计当作最大规模实测。

该算法沿用原归档思路；当前只交付合同库，没有 HTTP/Owner 接线或容量 SLA，因此不阻止本次交付。协调代理已同意将限制写入交付说明。未来接入真实消费者前，应使用精确排序后的区间扫描并完成真实容量验收；保留半开区间、金额/数量精度与合法输入域，不能以降低 schema 上限代替处理。

## 结论范围

本次可以继续协调代理的正常交付流程。真实 Owner、Store/issuer、权限/租户、PostgreSQL、事务、HTTP 业务与生产部署均继续 NOT_RUN；C06/F09 未开放。共享状态、保护合并和远端 main 包含性仍由协调代理完成，未由本报告代称完成。

审查未修改生产/测试/共享文件；仅新增本报告、质量审计 JSON 与定向探针报告。未提交、push、运行 Actions、操作数据库或执行归档程序。

收尾说明：协调代理已完成 `provenance.json -text whitespace=cr-at-eol` 属性及该文件的暂存字节修正，保留真实受测 CRLF，未重写原数据。审查侧再次读取确认该文件 index 与工作区原字节一致（398 个 CRLF）；协调代理的 88 文件 raw/index 全量回读及 `diff --cached --check` 均通过。该修正未改变受测源码、fixture、依赖或环境，没有重跑功能测试。

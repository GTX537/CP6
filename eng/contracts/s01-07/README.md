# S01-07 NCR/IPQC 合同输入与复现

版本 `CP6-S01-07-NATIVE-CONTRACTS/1`。本目录固定 S01 输入包 `CP6-S01-CONTRACT-INPUTS/1.0.0` 的 NCR/IPQC 子集；原始 expected、历史接受和 NOT_RUN 未修改。`sources.json` 列出每份原件、组合件、补丁、Git blob 和容器成员 hash。`inputs/` 按原字节保存，运行不需要 D 盘归档。

```powershell
python eng/contracts/s01-07/import_sources.py --check
node eng/contracts/s01-07/ts/generate.mjs --check
dotnet test eng/contracts/exact-values-tests/CP6.ExactValues.Tests.csproj --filter FullyQualifiedName~QualityContractTests
npm --prefix cp6.web test -- src/contracts/quality/ipqc.test.ts
npm --prefix cp6.web run type-check
```

Python 使用 3.10+，Node/npm 采用仓库锁定依赖及 package engines；本次实际 Node 22.22.0 / npm 10.9.4。首次导入通过 `import_sources.py --archive-root <归档原件目录>` 明确选择只读来源。正常校验和生成不访问归档。C# 生成入口及来源映射见 `csharp/`；TS 生成入口为 `ts/generate.mjs`，生成记录为 `ts/generation.json`。生成只消费 hash 验证过的文件，未知 Schema 关键词会使生成失败。

TS 保留 composed-19 的 203 个类型别名；运行时只开放 `ipqcSchemas.g.ts` 列出的 23 个完整对象入口。21 个 native roots 使用 registry 的原谓词，`R4FrozenPayload` 与 `R4QapFullCountPlanContent` 从组合 TS 声明适配，复用其引用的原生 scalar/Ref 约束。这两个 R4 类型没有伪造 `/types/...` 原生 registry 身份。未列入运行时路由的类型别名只提供静态声明，不声称通过完整解码验收。

五种事件的映射来自固定 `CANONICAL_CONTRACT_RECOVERY_R4_SYSTEMIC.json#/payloadTypeRegistry`，测试直接读取独立的原始 `PAYLOAD_PUBLICATIONS_RECOVERY_R4_SYSTEMIC.json` 中完整 body。`ts/manual-fixtures.json` 是按原字段独立编写的 18 个构造合同对象，不能作为真实 Owner/业务证据。六个 `IPQC-TYPE-*` 向量保持原 expected，在完整 R4 对象中验证对应字段。日期字符串保留原文本和小数精度，校验 Gregorian 日历与 [RFC 3339 的格式/闰秒位置](https://www.rfc-editor.org/rfc/rfc3339#section-5.7)；不查询 IERS 公告日历。

QAP Evidence 是 `owner/id/version/digest`，QAP Uom 是 `id/version`；Exec/Out Evidence 是 `ref/digest`；Exec Scope 的 range 必须有 `portionId`，Out/QAP range 没有该字段。这些是各模块独立合同，结构相似不产生 Owner 转换或写权限。C# 继续复用既有 StrictJson/NullOnly，TS 在 typed decode 前检查解码后的重复属性名，两端不做 Unicode 归一化。

不同来源的字符串边界保留差异：TS 接受 IPQC Schema 未禁止的 JSON 转义孤立 UTF-16 代理值，C# 原生 materializer 拒绝。CP4 LANGUAGE-CONTRACTS L02 限定原 CP4 合同范围，不能据此新增 IPQC 拒绝条件；当前结果不声明不同模块的全部字符串 profile 可互换。

原 NCR/IPQC pattern 文本保持不变。实测 Node 的非 multiline ECMAScript `$` 拒绝末尾 LF/CR/CRLF/U+2028/U+2029；.NET 的 ECMAScript 选项仍允许末尾 LF。C# 的结束锚翻译用于匹配原 ECMAScript 方言，不是采用 StableId 的其他 profile 修订。针对末尾行终止符和中间非法字符分别保留回归；一次错误预期的失败记录完整保留。

消费者接线和真实 QA Owner 范围见任务证据目录。没有新增数据库实现、业务服务接线、HTTP 签名/摘要重算、包发布或生产推广；`PROPOSED` 与 RequiredFenced 保留原义。

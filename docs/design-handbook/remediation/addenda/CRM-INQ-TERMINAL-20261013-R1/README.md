# CRM INQ 终态公开投影补充 R1

本目录补齐已接受设计决定与组合 OpenAPI 之间漏同步的两处节点。新采用身份为 **CRM-INQ-TERMINAL-PROJECTION/20261013-R1**；状态为 ADOPTION_PENDING，业务执行 NOT_RUN。原归档、组合资产、输入包、旧设计正文及历史接受记录均不改写。

权威依据为 [Wire 修订第 57 行](../../06-wire-types-and-fixtures.md) 的既定决定：PrivacyExportView 的 90/99 公开 expiresAtUtc、artifactId、downloadProof 均为 null，历史到期时刻保留在内部原记录，页面内嵌对象同样适用。[版本与效力](../../01-version-and-authority.md) 第 7/11/21 行要求按明确范围采用修订，不赋予 Schema 天然优先级，也不在封闭原体暗加版本字段。这两份文件在 Main 8569b669fed56ddc7b53b7bb24666c7fb93d544a 的完整 Git 字节摘要及准确行文保存在 [overlay.json](overlay.json)。

## 有界变化

原生来源为 composed-20，OpenAPI 3.1.0 / CRM-INQ-01 RECOVERY-R3 V15 / 1.0-recovery-r3，根 /components/schemas，原组合 SHA-256 为 740b840c697b60ebf51ceb0bd9a1898238814a49e36c2afe1d819c473b7df349。来源定位仍通过原输入索引；CRM 已保存的对应输入文件名为 8538042210844249__crm-inq.openapi.json。

| 精确 JSON pointer | 原值 | 后值 |
| --- | --- | --- |
| /components/schemas/PrivacyExportView/allOf/3/then/properties/expiresAtUtc | 原 date-time 字符串节点，完整 type/format/pattern 见补丁 | {"type":"null"} |
| /components/schemas/PrivacyExportView/allOf/3/then/x-semantic | requestedAtUtc <= completedAtUtc < expiresAtUtc；终态保留公开时钟 | requestedAtUtc <= completedAtUtc；公开 expiry/artifact/proof 为 null，历史 expiry 仅留内部原记录且不构成当前下载授权 |

每项必须恰好应用一次，并先匹配完整原值。99 的 completedAtUtc 类型、必填字段、其他 nullable 域不变；requestedAtUtc <= completedAtUtc 必须继续由实际 decoder 的语义检查执行，不能因 x-semantic 仅为注释而丢失。30 已完成下载分支和 requested <= completed < expires 规则完全保留；90 已有的三个 null 规则不变，不在本补充增加其他时钟规则。公开终态 null 不授权删除、覆盖或伪造内部历史 expiry。

38 个定义、257 处引用全部保持并解析成功。PrivacyExportView 的七处引用（两处 HTTP 响应和 PrivacyExportUiState 五个 export 分支）共同采用同一有效 Schema；消费者必须在嵌套 decoder 中执行 99 时序校验，不能只修根类型。UI 正例资产 /46 的 99/null 投影已经符合决定，原组合 UI 文件 SHA-256 b5cd3840d288f191ed213e348ae2fed4d01f26c2c28812ab92d3e4ed323b989b 保持不变。

## 明确采用身份

[profile.json](profile.json) 固定三个独立身份分量：原生 Schema locator、overlay.json 原字节 SHA-256、确定序列化后的 effective Schema SHA-256。实际入口必须显式选择新 profile 并校验三个分量；未知或不匹配时拒绝，不隐式升级旧入口或失败后 fallback。有效 Schema 保留原 info/title/version 作为来源身份，因此这些字段单独不代表新修订已采用。封闭业务 payload 不加入 profile/version 字段。

- 补丁 SHA-256：bce20c5a3bc93826993be6c2ba7c9a440fffd467f746245e5665707d8eedf549。
- 有效 Schema SHA-256：fd9a9444eac23225ecbf8ab91856dc7df2c0e4da53d9b765cbfccdeb2223ba2d。
- 有效 Schema 输出名：crm-inq.terminal.openapi.json，238954 字节；本目录不另存原 Schema 全量副本。

CP6-S01-CONTRACT-INPUTS/1.0.0 的全部 30 条冻结向量与预期保持不变。其中四条 INQ-VERSION 向量只证明原生来源 locator 子断言，不能单独证明新 profile。CRM 采用时需独立增加新 profile/三摘要错配、99/null 接受、99/string 与缺字段拒绝、嵌套 99 时序、90 和 30 未回退的真实两端用例；当前合同库静态证明不代表 HTTP 接线、授权、Owner 或生产验收。

## 可复现检查与生成

[verify_overlay.py](verify_overlay.py) 仅使用 Python 3.8+ 标准库和本地 Git，不访问网络，不执行归档代码，不写输入或仓库文件。调用者显式提供准确原组合 Schema 路径和具有固定权威提交的 Main checkout：

```text
python verify_overlay.py --base-schema <native-composed-openapi.json> --authority-root <Main-checkout> --check
python verify_overlay.py --base-schema <native-composed-openapi.json> --authority-root <Main-checkout> --emit-effective
```

--check 输出静态报告；--emit-effective 在同样校验后只向标准输出写准确 Schema 字节。使用 Python subprocess.run(..., stdout=二进制文件, check=True) 等二进制捕获方式保存输出；不要使用会改编码或换行的文本重定向再声称摘要相同。输出采用 UTF-8 无 BOM、保持原键插入顺序、json.dumps(ensure_ascii=False, indent=2)、LF 和单个末尾 LF。目录的 .gitattributes 固定 LF，以保证原字节 patch 身份可移植。

[static-check.json](static-check.json) 是本次实际静态执行结果，含两次精确应用、38/257/7 计数、零额外节点变化、八个身份/前值/操作数量拒绝用例、脚本与所有输入身份；未执行 CRM C#/TS decoder 或业务测试。[static-check-history.json](static-check-history.json) 保留首次 Python 3.8 不支持 removeprefix 的真实失败及兼容修正后的成功，不把该工具错误冒充合同业务 RED。

COORD 负责源提交、串行合并与远端核对；消费者只在取得固定发布提交后绑定采用。Main 资产交付不自动表示 CRM 已实现或验收通过。

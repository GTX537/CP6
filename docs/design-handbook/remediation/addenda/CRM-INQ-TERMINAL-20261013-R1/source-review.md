# Issue 189 / CRM-INQ-TERMINAL-20261013-R1 定向来源审查

**ACCEPT：七份 Main 来源修补文件可进入正常串行交付。** 没有阻塞项。该结论仅覆盖静态 overlay/身份/生成工具，不解除 CRM 真实采用与受支持 Node/npm 验证门禁，不宣称 S01-08 或业务验收完成。

审查目录：`D:/CP6-worktrees/fw-s02-1012/docs/design-handbook/remediation/addenda/CRM-INQ-TERMINAL-20261013-R1/`。实际HEAD `08bf7c923fa0b21af39d5e1f1fd550ed635246ed`，分支 `codex/bug-189-crm-inq-terminal-contract`。审查时仅该addenda目录未跟踪；未运行整个既有检查、未编译/测试业务、未网络或修改仓库。独立的小范围静态核验只在内存重建effective字节并复算摘要、节点差异和引用集合。

## 接受依据

- 精确原composed Schema SHA `740b840c697b60ebf51ceb0bd9a1898238814a49e36c2afe1d819c473b7df349`；不是读取“最新同名”资产。两项操作完整匹配old值、各应用一次，只变化 `/components/schemas/PrivacyExportView/allOf/3/then/properties/expiresAtUtc` 与相邻 `x-semantic`。
- 99 public expiresAtUtc整节点由string/date-time/pattern改为 `{type:null}`；99 requested<=completed、内部历史expiry不用于当前下载授权清楚。completedAtUtc规则、root required/closed、status20/30/90原分支不变；30仍保留三时钟规则。
- 独立对比原/effective递归结构，变化节点恰好两个；重新计数38 definitions、257处引用全部解析、7处PrivacyExportView引用。README/profile明确5个UI嵌套export的语义检查义务，没有用x-semantic注释替代消费者执行。
- profile把native source locator、overlay原bytes hash、effective schema hash分开；具名 `CRM-INQ-TERMINAL-PROJECTION/20261013-R1` 必须明确选择。旧source locator不足以选择revision，禁止隐式升级/fallback；effective保留native title/version只作来源信息；不向原payload添加字段。
- 冻结package1.0.0、30向量expected、原schema/原件/组合输入/UI `/46`未被修改；独立核了profile-inputs、vectors、input-index、原core-types bounded-patches与当前Git字节无差异。
- 两份authority文件的固定commit `8569b669fed56ddc7b53b7bb24666c7fb93d544a`、全文件Git blob SHA及被引用行均匹配，且与当前HEAD字节一致。既定null-only决定得到补齐，没有新造业务决策。

## 工具和证据适用性

`verify_overlay.py` 的入口先验证base hash、两限定路径/顺序、完整old值、严格new值、application_count、三个身份分量，然后生成effective并校验摘要；恢复两个old节点后必须与原JSON结构相等，并核38/257/7、三个公开终态字段required null、其他状态分支保持。使用标准库，读取本地固定Git authority；仅stdout输出，不写输入。`--emit-effective`与`--check`执行相同验证后分别输出准确schema bytes或静态报告。

八项负例确实执行具体突变与拒绝判断：base bytes、patch bytes、未知profile、native locator、patch hash、effective hash、old value、extra operation。此次复用该已记录静态执行结果，阅读负例实现并复核脚本/profile/overlay/报告/历史链哈希，未将本次静态重算声称为重新执行八项。首次Python3.8 removeprefix错误与修正保留，兼容修复不改schema或patch。

README示例要求显式base schema和authority checkout；二进制stdout捕获及UTF-8/LF/键序序列化约定可执行，`.gitattributes`固定LF。消费者仍须在自身便携导入链固定本次发布commit及profile/patch/output摘要，不将本地authority checkout当运行时依赖。

| 身份 | 本次独立匹配值 |
| --- | --- |
| overlay.json SHA-256 | `bce20c5a3bc93826993be6c2ba7c9a440fffd467f746245e5665707d8eedf549` |
| profile.json SHA-256 | `c354974c918dbc39d1ecf9352db79ab5b2b8a61e901f2db6ea328d6112993645` |
| verify_overlay.py SHA-256 | `48d84de422b3a46d9c596e8b7c440bef9e7f0ded8d27209fac23cf1ed36c2d12` |
| effective Schema SHA-256 / bytes | `fd9a9444eac23225ecbf8ab91856dc7df2c0e4da53d9b765cbfccdeb2223ba2d` / 238954 |
| static-check.json SHA-256 | `659bac22385095f74d328137bdc345895be80710bfb4ecb3a8a34a8f482af704` |
| README审查时SHA-256 | `ea493cfbdd57d3ced1bf8655f8e0644c86d9b1f76fdd0a6203956fc4456b7d7d` |

## 非阻塞文档精确性备注

README第一段把版本规则“第7/11/19行”连同“不在封闭原体暗加字段”一起描述。逐字核对第19行实际是“新规范算例”表行，禁止暗加字段在第21行。全文件authority hash已经覆盖第21行，profile也明确禁止payload新增字段，所以不影响合同/身份或接受结论。可仅修README引用文字，无须为此改overlay/profile身份或重跑静态检查。

后续只在CRM修补完成后复核显式revision采用、相关decoder/嵌套语义/测试预期与最终环境证据。Main四状态、Issue189记录、合并/远端包含核对由COORD补齐；本静态ACCEPT不替代这些交付步骤。
COORD 收尾：上述 README 引用已改为第 7/11/21 行；仅文字定位修正，overlay/profile/verifier/static 证据字节不变。Issue [CP6#189](https://github.com/GTX537/CP6/issues/189) 保持 Open，等待 CRM 采用、回归与两仓远端交付。

# S01-11 执行计划

本任务依据 `e20740e37e20bc0210e0b32c5670c2bce962002a` 的第四轮派工执行。准确设计输入仍固定在 `e6ccc89b3e62c7ed2c3d4739757b6ae9f4490c47`；两者之间只新增协调文档。`entry.json` 记录实际来源重验，`start-receipt.json` 记录远端固定 START 回读。

1. 核对独立工作树、CONFIRMED 窗口、S01-02 前置、原任务与三个修订 finding。重验原始/组合 codec、两个 native schema、两个 registry、OpenAPI、正文与 Store 依赖链的准确版本。已完成来源重验；未运行归档程序。
2. 在任务独占目录实现 current-v1 / frozen-R8 C# 与 TypeScript 合同入口。采用完整 native schema 和已发布的 codec 根；记录原件、补丁及本地适配之间的映射。复用 net8 的 StrictJson / JsonSemanticEquality；保留原生金额词法、封闭字段和必填/null-only 约束。
3. 冻结命令/回执按真实 UTF-8 字节保留及计算摘要。适配不得仅由 wire 成功推导 Owner 成功、FOUND 或释放占用；没有准确可信来源时保持 READ_ONLY_UNPROVEN / MANUAL_RECONCILIATION。C01/C02 HTTP200 返回保存的首次 HTTP201 COMMITTED 完整正文；其根分别为 `EC_C01_COMMITTED_CommercialDraftView`、`EC_C02_COMMITTED_CommercialDraftView`，不与 TerminalCommandReceipt 混用。两个重放根在两版本 registry 中都有完整三文件依赖闭包，可在具名重放入口单独校验。真实业务写入口与数据库不接线。
4. 对十条冻结向量逐 ID 执行 C# / TS 测试，补完整 native 对象的正反例、版本/Schema 错配、金额边界、结构相等、严格 JSON、重放与证据不足场景。保存有效失败、最终通过及命令/环境/输入/源码/产物身份；执行指定前端 type-check。
5. 完整任务集中审查一次，按合同符合性、代码质量顺序检查。实质修复后只复查受影响部分并做对应回归。审查与实际测试结果分开记录。
6. 仅暂存白名单文件，形成可审计提交。每次 push / PR 前重新检查候选及远端 main 的工作流与依赖，使用本地验证，避免 Actions 自动触发。发布候选与 DELIVERY，由 COORD 统一更新共享状态、保护合并及核验远端 main。

当前准确三文件包没有 F09-LOCAL-TYPES；不扩展 EC_C06_200_StoredTerminal、EC_C06_202_StoredAdmission 或 WholeC06PublicRequest 根。如新增入口需要这些根，先向 COORD 申请准确闭集与路由窗口。

测试采用构造数据，仅证明覆盖到的合同行为。247 完整运行场景、真实 Owner / 存储 / 权限 / HTTP 业务验收保持 NOT_RUN；41 历史静态 REFERENCE_ONLY、ITER/REL 原件缺失及 WP05 PROPOSED 不变。新数据库唯一目标 PostgreSQL；本任务不操作任何数据库。

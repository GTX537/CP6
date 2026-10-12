# S01-05 独立集中审查

审查日期：2026-10-12。结论：**可以合并；未发现阻塞问题。** 此结论只覆盖本次合同库任务，不代表实际业务 Owner、页面或 PostgreSQL 已采用。

## 审查身份与范围

- 候选工作树：`D:/CP6-worktrees/s01-05-20261012`。
- 基线：`4ab979e2f391c5a5645da0d1586781e824a9f8cd`。
- 候选：`b1f5206d3a0ca1af6513c4984700823c81dea210`，单个提交 `feat(contracts): add exact TypeScript credit and inventory values (S01-05)`。
- 已确认基线为候选祖先，候选工作树干净。完整差异为 12 个新增文件，全部位于 `cp6.web/src/contracts/exact/` 与 `docs/development/evidence/S01-05/`。
- 按仓库 AGENTS.md 和 requesting-code-review 技能作一次任务级独立审查；读取本轮 S01-05 执行包、dispatch.json、原任务卡、固定输入包中本任务五个 profile 和全部 34 条向量，以及全部实现、测试、说明和证据。
- 审查仅作只读核查；没有修改仓库、Git 引用或远端，没有执行 push、PR、Actions、部署或数据库操作。唯一写入是本报告。

## 功能与合同核对

| 核对项 | 审查结果 |
| --- | --- |
| FIN-CREDIT 符号及位数 | `creditAmount.ts` 先验证 JSON 字符串和完整词法，再计算总位数/小数位数；signed、nonnegative、positive 分域，38 总位/8 小数保持；负零、尾零、前导零、指数、空白和正号拒绝。位数逻辑与固定原生 pattern 的整数/小数组合一致。 |
| FIN-INV 原 wire | `inventoryDecimal.ts` 在归一化前检查 Decimal18/Qty12 的原始小数位数；整数位未新增有限上限。`originalWire` 独立保存 `-0.000`、尾零等合法形式，`toWire`/`toJSON` 保留该字段字符串。原始 JSON 文档字节另由消费者保存，说明未混淆二者。 |
| 精确运算 | `exactDecimal.ts` 全程以 bigint 系数运算；number 仅承载 scale、长度和比较结果。加减/比较按较大 scale 对齐，去零仅作等值归一化；无 Number/parseFloat 金额转换、舍入或截断。对象冻结，隐式数值转换拒绝。 |
| 最终字段边界 | `fromExact` 通过目标 profile 再验证；允许任意精度中间结果，但超出目标位数或符号域时拒绝。FIN-INV 的 `toWire(targetProfile)` 检查原始小数位，显式 `fromExact` 创建新值，两种行为分别测试。 |
| 固定向量 | 测试同时读取每条 input/expected，断言 34 个准确 ID、唯一性和 profile 集。所有操作为 lexical，未知 operation/profile 明确失败。没有使用另一实现动态生成预期。 |
| 补充覆盖 | 已读测试覆盖 JS 安全整数两侧、38 位授信上限及进位溢出、90 位库存极值与计算、200 位无整数上限示例、负数/负零/尾零、错误 JSON token、最终小数越界、跨 profile 校验、加减/比较及源对象不变。 |

未发现功能、精度、合法域缩减、合同破坏或共享范围冲突。没有需要提交行号和复现步骤的缺陷。

## 证据完整性与复用

本次独立执行了只读 SHA-256/包结构核验，结果均通过：

1. 8 个输入及配置 pin 的候选 Git blob 哈希与 `verification.json` 一致；工作树字节哈希也与其记录一致。
2. 4 个被验证 TypeScript 文件的候选 Git blob/工作树字节哈希均匹配既有成功运行所记身份。
3. 4 份原始日志的候选 Git blob 哈希和工作树哈希均匹配清单；清单正确区分 CRLF 工作树与存储 blob 表示。
4. dispatch、profile-inputs、vectors、verification 中包版本均为 `CP6-S01-CONTRACT-INPUTS/1.0.0`；34 个 case ID 和结果记录逐项一致。分组为 signed 14、positive 5、nonnegative 6、decimal18 4、qty12 5。
5. 公共输入包与 dispatch 在基线到候选之间未改变。原包的 NOT_RUN 范围没有被修改成业务 PASS。
6. 13 份工作流的候选 blob 哈希匹配所留证据，且与给定基线相同；本审查未重新查询远端或运行队列，合并前实时远端/工作流核对由协调方负责。
7. 完整差异检查在仅关闭 `blank-at-eof` 后通过；四份原始 CLI 日志的尾部空行已经披露，不构成功能或证据阻塞。

**复用已有运行，未重跑业务测试或类型检查：**

- `green.log`：202 项测试通过，1 个测试文件，0 跳过；其中 34 条共用向量和 168 项其他测试。Vitest 4.1.11，记录开始时间 06:51:53。
- `type-check.log` 与 `verification.json`：`npm --prefix cp6.web run type-check` / `vue-tsc --build`，记录退出 0。
- 原验证环境：Windows、Node v20.19.0、npm 10.8.2；清单记录时间 `2026-10-12T10:55:04.033491+00:00`。
- `collection-failure.log` 保留 0 项执行的 BigInt 标题格式收集失败；`red.log` 单独保留实际执行 202 项时 100 失败/102 通过。没有把收集失败冒充有效 RED，也没有把失败抹去。

复用依据是上述固定来源、代码和配置身份相符；本次新执行的是静态审查和哈希核验，不把历史测试结果称作本次重新执行。

## 合并建议与未验证范围

建议协调方按正常保护规则合并该候选，并在串行交付窗口同步共享状态。若合并仅增加其他独立任务/状态文档且本任务源码、固定输入、依赖和配置未变，可继续复用这些成功结果，不因提交 SHA 改变重跑同一验证。

本审查不证明远端 main 已包含候选；该核对属于协调方后续交付步骤。没有验证实际页面/WMS/MES 输入迁移、C#/TS/PostgreSQL 跨实现采用、业务 Owner、授权/租户隔离/事务、数据库存储或迁移，以及部署和生产验收。报告与原证据保持这些边界。

# 精确金额与数量合同

本目录采用 `CP6-S01-CONTRACT-INPUTS/1.0.0` 的五个 profile。调用者必须从实际字段合同选择 profile；本库不推断 Owner、货币、单位或业务权限，也不用于 ERP 原生 Money。

| 类型 | profile | 输入约束 |
| --- | --- | --- |
| `CreditAmount` | `fin-credit-signed` | 38 总位、最多 8 位小数，可负 |
| `CreditAmount` | `fin-credit-nonnegative` | 同上，不可负 |
| `CreditAmount` | `fin-credit-positive` | 同上，必须大于零 |
| `InventoryDecimal` | `fin-inv-decimal18` | 整数位不增加上限，最多 18 位小数 |
| `InventoryDecimal` | `fin-inv-qty12` | 整数位不增加上限，最多 12 位小数 |

`parse(unknown, profile)` 只接受字符串，不把 JSON number 转回字符串补救精度。两域均拒绝前导零、正号、指数和空白。FIN-CREDIT 还拒绝尾零和负零；FIN-INV 在检查原始小数位数后才归一化运算值，并独立保存 `originalWire`。

```ts
import { CreditAmount } from './creditAmount'
import { InventoryDecimal } from './inventoryDecimal'

const left = CreditAmount.parse('9007199254740992', 'fin-credit-signed')
const right = CreditAmount.parse('1', 'fin-credit-signed')
const total = left.exact.add(right.exact)
// 明确目标字段；越界会抛出 RangeError，不舍入、不截断。
const amount = CreditAmount.fromExact(total, 'fin-credit-nonnegative')
amount.toWire() // '9007199254740993'
JSON.stringify({ amount }) // {"amount":"9007199254740993"}

const quantity = InventoryDecimal.parse('-0.000', 'fin-inv-qty12')
quantity.exact.toString() // '0'：规范运算值/展示值
quantity.toWire() // '-0.000'：原字段字符串
JSON.stringify({ quantity }) // {"quantity":"-0.000"}
```

`ExactDecimal` 以不可变 `bigint coefficient + number scale` 表示数值。`scale` 仅为非负安全整数元数据，金额和数量从不经浮点数。提供精确 `add`、`subtract`、`compare`；中间值可以超出字段精度，写回时必须经过 `fromExact` 和目标 profile 校验。没有默认四舍五入、除法或单位换算策略。

`toWire(targetProfile)` 会重新检查目标字段。FIN-INV 此操作保留原 wire，例如 `1.0000000000000` 从 Decimal18 写入 Qty12 会拒绝；显式 `fromExact(value.exact, 'fin-inv-qty12')` 创建的新值可为 `1`，原对象仍保持不变。两种操作不能混同。

`toJSON()` 按对象创建时的 profile 输出字符串。`toString()` 与模板字符串提供文本展示；隐式数值运算/转换会抛出 `TypeError`。不要再将输出字符串传入 `Number`、`parseFloat` 或原有浮点输入组件。

`originalWire` 只是解析后的单个字段字符串，不是原始 JSON 文档 UTF-8 字节。签名、摘要、命令重放必须由消费者另存原始字节；重编码整个 JSON 不能作为原字节证据。

测试直接读取仓库固定包的 input/expected，并与派工的 34 个 ID 核对。未知 operation 或 profile 会失败。没有接入页面、WMS/MES 表单、C# 或数据库；跨语言/数据库一致性和实际 Owner 采用由后续任务完成。

```powershell
npm --prefix cp6.web test -- src/contracts/exact/profiles.test.ts
npm --prefix cp6.web run type-check
```

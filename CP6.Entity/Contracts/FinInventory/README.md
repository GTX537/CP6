# FIN-INV 精确数值合同

来源为 `CP6-S01-CONTRACT-INPUTS/1.0.0` 中的 `fin-inv-decimal18` / `fin-inv-qty12`，对应 `urn:cp6:fin-inv:r10:wire`。原生数值定义和数据字典的无损存储修订见 `CP6-EXACT-NUMERIC-STORAGE/1`；本目录提供内存合同类型，不改变数据库或既有过账入口。

| `InventoryDecimalProfile` | 原生定义 | 约束 |
| --- | --- | --- |
| `Decimal18` | `/$defs/Decimal18` | 有符号，最多 18 位小数 |
| `Qty12` | `/$defs/Qty12` | 有符号，最多 12 位小数 |
| `NonNegativeQty12` | `/$defs/NonNegativeQty12` | 无负号，最多 12 位小数；`-0` 也拒绝 |

整数位不增加有限上限；90 位是固定向量样例。必须按字段选择 profile，不能根据属性名字推断。完整字段绑定保持在固定 `eng/contracts/s01/profile-inputs.json`：Money/原始金额等用 Decimal18，Quantity/变动数量等用 Qty12；NonNegativeQty12 和 ACTIVE/EMPTY 的更严格字段条件分别保留，标量校验不认证完整对象。

```csharp
using CP6.Entity.Contracts.FinInventory;

var quantity = InventoryDecimal.Parse("-0.000", InventoryDecimalProfile.Qty12);
_ = quantity.OriginalWire; // "-0.000"
_ = quantity.Value.Coefficient; // BigInteger.Zero
_ = quantity.Value.Scale; // 0
_ = quantity.ToWire(); // "-0.000"

var left = InventoryDecimal.Parse("9007199254740992", InventoryDecimalProfile.Decimal18);
var right = InventoryDecimal.Parse("1", InventoryDecimalProfile.Decimal18);
var sum = left.Value.Add(right.Value);
var amount = InventoryDecimal.FromValue(sum, InventoryDecimalProfile.Decimal18);
_ = amount.ToWire(); // "9007199254740993"
```

`InventoryDecimal` 保存原字段字符串和不可变 `InventoryDecimalValue`；先验证原始小数位数，再归一化系数/scale。两域都保留负零和尾零，不套用 FIN-CREDIT 的规范金额词法。指数、正号、前导零、空白及非 ASCII 数字拒绝。

`InventoryDecimalValue` 用 `BigInteger` 系数和非负 `int` scale 做精确加减、乘法和比较。中间值不套最终字段的小数精度；`FromValue(value, destinationProfile)` 在输出时重新检查目标字段，不舍入或截断。scale 元数据与 .NET 字符串/内存有宿主表示和资源边界，不另设业务整数长度限额。没有浮点或 `System.Decimal` 中介。

`ToWire(destinationProfile)` 保留原词法并检查目标字段。因此 Decimal18 的 `1.0000000000000` 直接输出为 Qty12 会失败；显式 `FromValue(source.Value, Qty12)` 创建的新 wire 可为 `1`，源对象仍保留原字符串。`InventoryDecimal.ToString()` 返回原 wire；`Value.ToString()` 返回规范运算文本，二者用途不同。

JSON 必须显式指定字段转换器：`Decimal18JsonConverter`、`Qty12JsonConverter`、`NonNegativeQty12JsonConverter`，或用 `new InventoryDecimalJsonConverter(profile)` 配置单域序列化器。转换器要求 JSON string，拒绝 required scalar 的 null 和其他 token，写入时仍检查目标字段。字段是否存在需要 enclosing type 的 `required` 或完整 schema 验证；可空/可选字段需要独立规则，不能用此 required scalar 转换器放宽。

在入口使用已交付的 `CP6.Entity.Contracts.Wire.StrictJson`，先拒绝任意层重复键，再进行 typed decode。`OriginalWire` 不等于原 JSON 文档字节；摘要、签名和重放应取 `StrictJsonDocument.GetOriginalUtf8()`，不能重序列化后冒称原字节相同。

## 比例运算与原生对象边界

`InventoryRatio.Create(BigInteger numerator, BigInteger denominator)` 仅为内部算术值：任意精度有符号分子、严格正分母，按 gcd 约分，零规范为 `0/1`。提供加减、乘法和比较。

`DivRem()` 对约分后的分子和分母向零截断，余数与分子同号或为零：`Numerator = Quotient * Denominator + Remainder`，且 `abs(Remainder) < Denominator`。例如 `-7/3` 得商 `-2`、余数 `-1`；`8/6` 约分为 `4/3` 后得商 `1`、余数 `1`。余数的单位是约分后的分母，不是原 wire 的分母。

| 原生对象 | 仍须由后续消费者采用的约束 |
| --- | --- |
| `Rational` | 正规范整数字符串分子和分母、必填 `basisRef`、Ref schema 及封闭对象规则 |
| MAVG `MovingAveragePoolRevision` | 分子 `^-?[0-9]+$`，允许 `00` / `-0`；分母为正规范整数；基础可 null，ACTIVE 必须有值且 afterQty 为正，EMPTY 必须为 null 并满足零数量/金额字段约束 |

本任务不提供这些对象或 MAVG 字符串 codec。`InventoryRatio` 没有保存原分子/分母或 `basisRef`；不得直接把约分值当成原生对象、原始字符串或重放证据。它没有引入显示舍入、MAVG 成本过账、单位换算或库存计价政策。

全部测试（含比例）位于 `InventoryDecimalTests`，直接读取固定包的 input/expected，核对恰好 9 个分配 ID，并拒绝未知 operation；补充用例不改共用预期。

```powershell
dotnet test eng/contracts/exact-values-tests/CP6.ExactValues.Tests.csproj --filter FullyQualifiedName~InventoryDecimalTests
```

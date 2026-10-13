# S01-10 scale8→18 正式采纳审查

**建议：可采纳为 CP6-POSTGRESQL-EXACT-MAPPINGS/1.0.0 的有界 PG 存储补充。当前候选仍是 PROPOSED；正式文件与COORD决定必须写入仓库后才能计交付。**

候选 `d90be911bd54dc14bf9f11cdc73bf237c778d2f9`；提案原字节 SHA256 `15a542e7031ff1c4b8a2e86543cde6bb2c899156819c6219b412a913b5f1cf59`。重新核对 FIN-INV R10 Schema `3a61c4b68b8f5c01f65025a9fae5647188446569762e4315237f5f1d4fa0ba65` 与原字典全部20个pointer；原 CP6-EXACT-NUMERIC-STORAGE/1 及固定输入均未改变。

原 Money.amount 直接引用 Decimal18；Money.scale=0..8 是独立业务政策字段。RoundingTrace 的四个金额字段也直接引用 Decimal18，trace.scale仍为0..8，calculationScale仍18。规范值对允许数值scale0..18，保留独立原wire；18位非零小数不舍入、不丢失，原scale≤8值完全包含。完整对象/运算/授权关系依旧约束Owner，存储容量不是业务舍入规则。

ValueLine的3项是字典派生存储量：依据原字典 FinalAmount=RoundedBeforeResidue+ResidueApplied 与 LayerConsumption 外键/同名Money金额。原 ValueEffectResult.lines 只含 roundedAmount 等原属性；本补充不创造同名wire字段，不把字典派生绑定称完整原生codec。其余17项有直接原生金额引用链。

| 字典字段 | 原pointer | old → new 数值scale | 原生/派生语义来源 |
|---|---|---|---|
| `ValueLine.RoundedBeforeResidue` | `/Tables/11/Columns/11` | 0..8 → 0..18 | 字典派生；LayerConsumption.roundedBeforeResidue → Money.amount → Decimal18 |
| `ValueLine.ResidueApplied` | `/Tables/11/Columns/12` | 0..8 → 0..18 | 字典派生；LayerConsumption.residueApplied → Money.amount → Decimal18 |
| `ValueLine.FinalAmount` | `/Tables/11/Columns/13` | 0..8 → 0..18 | 字典派生；LayerConsumption.finalAmount → Money.amount → Decimal18 |
| `LayerConsumption.RoundedBeforeResidue` | `/Tables/12/Columns/10` | 0..8 → 0..18 | LayerConsumption.roundedBeforeResidue → Money.amount → Decimal18 |
| `LayerConsumption.ResidueApplied` | `/Tables/12/Columns/11` | 0..8 → 0..18 | LayerConsumption.residueApplied → Money.amount → Decimal18 |
| `LayerConsumption.FinalAmount` | `/Tables/12/Columns/12` | 0..8 → 0..18 | LayerConsumption.finalAmount → Money.amount → Decimal18 |
| `RoundingTrace.TargetTotal` | `/Tables/13/Columns/6` | 0..8 → 0..18 | RoundingTrace.targetTotal → Decimal18 |
| `RoundingTrace.LineRoundedTotalBeforeResidue` | `/Tables/13/Columns/7` | 0..8 → 0..18 | RoundingTrace.lineRoundedTotalBeforeResidue → Decimal18 |
| `RoundingTrace.Residue` | `/Tables/13/Columns/8` | 0..8 → 0..18 | RoundingTrace.residue → Decimal18 |
| `RoundingTrace.FinalTotal` | `/Tables/13/Columns/11` | 0..8 → 0..18 | RoundingTrace.finalTotal → Decimal18 |
| `ReconciliationLine.InventoryValue` | `/Tables/20/Columns/11` | 0..8 → 0..18 | ReconciliationRow.inventoryValue (Money / null) → Money.amount → Decimal18 |
| `ReconciliationLine.InTransitValue` | `/Tables/20/Columns/12` | 0..8 → 0..18 | ReconciliationRow.inTransitValue (Money / null) → Money.amount → Decimal18 |
| `ReconciliationLine.WipValue` | `/Tables/20/Columns/13` | 0..8 → 0..18 | ReconciliationRow.wipValue (Money / null) → Money.amount → Decimal18 |
| `ReconciliationLine.FgValue` | `/Tables/20/Columns/14` | 0..8 → 0..18 | ReconciliationRow.fgValue (Money / null) → Money.amount → Decimal18 |
| `ReconciliationLine.GrniValue` | `/Tables/20/Columns/15` | 0..8 → 0..18 | ReconciliationRow.grniValue (Money / null) → Money.amount → Decimal18 |
| `ReconciliationLine.ApValue` | `/Tables/20/Columns/16` | 0..8 → 0..18 | ReconciliationRow.apValue (Money / null) → Money.amount → Decimal18 |
| `ReconciliationLine.CogsValue` | `/Tables/20/Columns/17` | 0..8 → 0..18 | ReconciliationRow.cogsValue (Money / null) → Money.amount → Decimal18 |
| `ReconciliationLine.GlValue` | `/Tables/20/Columns/18` | 0..8 → 0..18 | ReconciliationRow.glValue (Money / null) → Money.amount → Decimal18 |
| `ReconciliationLine.Difference` | `/Tables/20/Columns/19` | 0..8 → 0..18 | ReconciliationRow.difference (Money / null) → Money.amount → Decimal18 |
| `ProvisionalValueFact.MaxAmount` | `/Tables/41/Columns/5` | 0..8 → 0..18 | ProvisionalEnabled.maxAmount → Money.amount → Decimal18 |

全部20行有 full-storage 实际PG极值/原串/null耦合测试，输入90整数位加18个非零小数位；这只证明存储容量，不称Money政策/完整Owner对象已验收。总映射仍66项/13表，51非空/15可空；另外46项不变。每行精确native pointer链、旧类型、新表示、nullability、policy和实际测试名见 scale-adoption-review.json。

正式文件静态同义性核对：按JSON中的 `semantic_keys` 排序键、UTF8、紧凑分隔符序列化，预期 SHA256 `7bb24e1e36610996a46d13cace0a58e8032a3eb369e4dda235af5ec3c58e8752`。采用状态和决定元数据不在这个语义hash中；不修改历史运行的PROPOSED身份。

1. COORD 在本任务公共合同窗口新增 eng/contracts/s01/postgresql-mappings.json；初次正式版本沿用 CP6-POSTGRESQL-EXACT-MAPPINGS/1.0.0，记录采纳决定、候选 SHA、提案 SHA256、原件/旧存储版本和这20项有界覆盖。原固定输入和提案保留不变。
2. 正式文件保留本提案所有66行、credit/atp/assigned_vectors及语义字段；只修改 adoption、scale_change.approval 并增加决定/证据元数据。按本报告 semantic_keys 比较 semantic_sha256，应与已测试提案相同。不是把 Money.scale 改18，也不是扩大原 wire 词法。
3. TestInputs.MappingPath 已优先选择正式文件；COORD 静态核对正式路径、mapping_version 与 ExactValueStorage.MappingVersion 相同且采用状态已确认，生成 fixture 所消费字段与提案相同。保持历史运行 mapping_adoption=PROPOSED，不回写为正式运行。
4. 仅移动相同语义并补决定元数据，可复用现有210+57结果，注明原工作文件hash与CRLF/LF差异；任何profile/null/sign/scale/SQL/参数/runner语义变化才扩大到受影响回归。
5. 同步本任务交付文档和协调状态，正常集成并核远端main包含后才能DONE；Owner真实表、全写者切换、迁移、租户/权限/池、完整对象与业务算法继续各自UNPROVEN/NOT_RUN。

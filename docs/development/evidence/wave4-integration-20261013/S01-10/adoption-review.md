# S01-10 正式映射采纳定向审查

**ACCEPT。未发现需修正的问题。** 本轮只审查正式映射、COORD说明和新增采纳证据，未重做全任务审查、未执行PG测试或数据库操作、未修改仓库。实际工作树分支为 `codex/s01-10-coord-adoption-20261013`，检查时HEAD为 `165104f58ca446594e37c07854a544a1f681c2e8`；正式采纳提交SHA由COORD后补，结论绑定下列工作文件摘要。

正式 `eng/contracts/s01/postgresql-mappings.json` 与已审提案作完整JSON深比较：只改 `adoption=ADOPTED_BY_COORD`、`scale_change.approval=APPROVED_STORAGE_CAPACITY_ONLY`，只新增 `coordinator_decision`。66项/13表、51非空/15可空、42个CREDIT字段绑定、ATP来源和56固定向量均保持。20项扩展集合与原审查逐行一致，其余46项尺度不变。

决定 `CP6-S01-10-SCALE-20261013-R1` 保持 Money.scale/trace.scale为0..8、calculationScale为18；17个直接原生金额链与3个ValueLine字典派生量明确区分，不新增原wire属性，不改变null/sign/Owner规则。历史测试仍是PROPOSED身份的210项、重叠57项与生命周期1项，没有改称正式文件新执行。真实生产Owner、全写者切换、租户/权限/连接池、业务算法和迁移均未据此验收。

现有 `TestInputs.MappingPath` 优先正式文件，适配器 `ExactValueStorage.MappingVersion` 与映射均为 `CP6-POSTGRESQL-EXACT-MAPPINGS/1.0.0`。定向阅读新增 `verify_formal_mapping.py` 及其结果，并另以只读内联断言核验文件hash、完整深比较、行数/nullability/扩展集合及语义hash；未执行会写回仓库证据的脚本。8个COORD说明链接及决定内2条证据路径均存在，4份原审查副本与scratch原件逐字节相同。

语义SHA256为 `7bb24e1e36610996a46d13cace0a58e8032a3eb369e4dda235af5ec3c58e8752`。准确序列化参数为：取下列**11个**顶层字段组成对象，保持数组顺序；`json.dumps(obj, ensure_ascii=False, sort_keys=True, separators=(',', ':')).encode('utf-8')`，不含BOM、不追加换行，再计算SHA256。`mapping_version`另严格核验；adoption、scale_change及coordinator_decision不在此hash中，其允许差异由完整深比较核验。

```text
database
source_revision
source_sha256
package_version
native_inventory_schema_sha256
credit
inventory_mappings
atp
assigned_vectors
arithmetic
owner_relations
```

新增文件摘要如下，路径相对 `D:/CP6-worktrees/s01-10-20261012`，均是审查时工作文件原始字节。提交若发生纯CRLF/LF归一化，应另记录Git blob摘要，不能把二者冒称同一原字节。

| 文件 | SHA256 |
|---|---|
| `eng/contracts/s01/postgresql-mappings.json` | `4d8bf3f537b6caed04d12fd6a9794677c35dbf3c8ec26ac0653ac7a2cad42106` |
| `docs/development/evidence/S01-10/COORD-ADOPTION.md` | `9524cbb284af7437d93888298899aa1f5b2130e79edb065145787409d0fc5468` |
| `docs/development/evidence/S01-10/coord-adoption/S01-10-original-validation.json` | `8d22c516298b836761f4ccf91864a806b0623bc1246b79f0315edccdb22db098` |
| `docs/development/evidence/S01-10/coord-adoption/S01-10-review.md` | `ecc4d10aa10dec25ddd3dc7941da2fef2dced06739264741f88d181c2ae79134` |
| `docs/development/evidence/S01-10/coord-adoption/scale-adoption-review.json` | `c6ddc99baa5bddb5427a060e3f7d6bd95f5c9a4b97bc7f2751b6060acaf64bff` |
| `docs/development/evidence/S01-10/coord-adoption/scale-adoption-review.md` | `b6901514d6e1715a2a251478bd720b054e0583e4252107504fb783c3f3009772` |
| `docs/development/evidence/S01-10/coord-adoption/verification.json` | `4d19ed4dafdaf53ffc058289c6b2048206de5cd563f3569ae3cb7a4886995b22` |
| `docs/development/evidence/S01-10/coord-adoption/verify_formal_mapping.py` | `eb8bcdac2a5f896ef3ee14d0463221014bc6735d82253d0f26597e5222bef8b4` |

本结论解除原审查的“正式映射尚未采纳”条件；提交、正常集成、共享状态与远端main包含核对仍由COORD完成，未在本报告提前宣称任务DONE。

COORD subsequent byte-normalization check: formal mapping normalized CRLF to LF to match existing eng/contracts/s01/.gitattributes. Parsed JSON is identical; no semantic/source change. Static check regenerated with the same semantic SHA. Original raw review hashes above retain their historical identity. Evidence JSON copied from reviewer remains exact CRLF bytes under existing -text; diff check uses core.whitespace=cr-at-eol only to avoid classifying preserved CR as trailing spaces.

Final normalized/updated evidence eng/contracts/s01/postgresql-mappings.json: `96ef3c9f22919e790f750773c2a4bdc675b7df5f145f7b9bfffeda6d19c1ec16`.

Final normalized/updated evidence docs/development/evidence/S01-10/coord-adoption/verification.json: `f9194ead7c35beeabee33b7f80329d3a7951dc164a9b9d88da66e9d0fa5c7d6b`.

Final source binding after unchanged reviewed bytes were committed: `a304f9e728775109cfada83312dbeb23b2da4a98`.

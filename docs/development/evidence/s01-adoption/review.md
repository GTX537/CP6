# S01-12 任务级审查

审查单位是本任务完整范围：`eng/contracts/s01-12/` 与 `docs/development/evidence/s01-adoption/`，基线 Main `47c9b12c796620effe41c62d16992ccd629ee949`。采用先 SPEC、后 QUALITY 的集中审查；修复后只复核相关修改及对应比较器回归，不重跑既有业务套件。

## 第一轮 SPEC：发现1项实质问题

独立审查者 `s01_12_spec` 检查实际来源、代码与输出，未执行产品测试或数据库。被审比较器 SHA-256：`ddd4c399b0657f26f26a1b0355e4629a0f45c0c233c78b21f6b875d9618a98d4`。

P2：`DR-NCR-FIELD-01::NCR-FIELD-01-POS` 错接到 `NCR-NULL-001/005/009/013/017`，这些向量验证的是 `originalQuery/guardRef/code/value` 的 const-null，而原验收的五字段是 `priorHeadRef/sourceKind/keyword/cursor/nextCursor` 的 nullable/required 规则。初稿还把子断言改述为 required/null-only，未引用现有五字段测试。原定位为比较器535–538行、修订矩阵856–876行；不把该轮审查称为通过。

要求移除无关固定向量，按五字段原规则拆解，并定位原 S01-07 C# TRX 中：

- `Five_field_metadata_rows_retain_exact_required_nullable_and_editable_flags`，testId `9769af35-af53-08f7-9c17-c76f48c68c70`。
- `Required_nullable_values_are_not_omitted_by_relaxed_caller_options`，testId `04dbbf18-9182-683a-8813-d9a7c858892a`。

动态 shape 证据不足的部分仍写 PARTIAL/NOT_COVERED，整场景仍 NOT_RUN。修复与复核结果记录在后续段落；旧30项比较器成功只对原源码成立，不伪称发现了这项规格错接。

其他已审重点未发现阻断：122向量与56词法范围、历史断言和标量区别、S08/S10/S11最终身份、源码复用解释、六组消费者责任、业务状态边界及 COORD 同步建议。

## SPEC 定向复核：PASS

同一审查者独立核对修正后的五字段 Schema/清单/元数据连接，以及两个原 TRX 的 testId、完整名称和索引。五字段元数据为 FULL，三 query 字段动态 null 保留为 PARTIAL，其他动态 shape 为 NOT_COVERED；整 case 仍 PARTIAL/NOT_RUN，原错接阻断关闭。

修正后的比较器 SHA-256 为 `0d53315b8f8f6e938570fa40beebc047cd4c9b01006379d74b69d9941239fede`。独立核对122向量字节、其他18个case内容未变，复用 [2项定向回归](implementation/spec-field-targeted-tests.json) 通过证据；[真实RED](implementation/spec-field-red.json) 保留。完整比较见 [修复后记录](implementation/spec-field-verification.json)。没有重跑旧套件、数据库、构建或此前30项检查。

## QUALITY：PASS

新的独立审查者 `s01_12_quality` 完整阅读974行比较器、239行测试和发布前检查脚本，核对修订与消费者声明及代表性固定源码。重算根验证记录所列2个源码、3个产物、consumers及12个引用文件的哈希均一致；完整暂存diff的空白检查通过。没有发现阻断性正确性、数据一致性、证据范围或维护性问题。

复用原30项检查、2项定向回归与最终828个引用比较记录，没有再次运行比较器全套、旧产品测试、数据库、构建或Actions。审查时仅父代理更新中的本review文件尚未暂存；交付前一起审查并保存，不当作产品缺口。

## 父代理收尾核对

只写两个授权目录，原 expected/profile/Schema/映射、产品、依赖和共享状态未改。旧验证与新比较保持独立；最初因性能停止的读器记录、SPEC真实RED与原30项结果全部保留。任务文档的18个本地链接在修复阶段检查无缺失；后续新增链接均指本目录已存在的证据。三仓再次fetch的主线仍与固定输入一致。暂存内容与工作字节一致性、窄范围凭据模式检查和完整diff检查另留收尾记录；它们不是产品或生产验收。

最终审查结论：**SPEC PASS / QUALITY PASS**。后续仍须发布前触发检查、可审计提交、候选交付、COORD共享状态同步、正常保护合并和远端Main核对；审查通过本身不登记DONE。

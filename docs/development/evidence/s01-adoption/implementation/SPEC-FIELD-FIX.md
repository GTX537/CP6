# SPEC定向修正：NCR五字段修订证据错接

审查确认 `DR-NCR-FIELD-01::NCR-FIELD-01-POS` 原expected为“准确可空值/必需非空按shape表现”。此前接到五个NCR-NULL const-null向量，实际覆盖originalQuery/guardRef/code/value，不能证明priorHeadRef/sourceKind/keyword/cursor/nextCursor。原错误case与修正前hash保留在 `spec-field-pre-fix.json`，不改历史记录。

本次仅修正该case：移除全部fixed_vector_ids错引，采用S07原C#657运行中的两个精确TRX testId和完整方法名，不用泛字符串匹配。

| 原测试 | 精确testId | 证明范围 |
|---|---|---|
| Five_field_metadata_rows_retain_exact_required_nullable_and_editable_flags | `9769af35-af53-08f7-9c17-c76f48c68c70` | 五字段required=true、editable=false；priorHeadRef nullable=false，其余四字段nullable=true |
| Required_nullable_values_are_not_omitted_by_relaxed_caller_options | `04dbbf18-9182-683a-8813-d9a7c858892a` | 仅sourceKind/keyword/cursor显式null可解码，且调用方设置WhenWritingNull时仍保留字段 |

每个字段分别绑定原生Schema指针、原field-inventory指针、metadata声明和原测试源码行。metadata子断言标FULL，仅指这五行精确flags；query三字段动态shape标PARTIAL（只证明选定null情形）；priorHeadRef和nextCursor动态shape标NOT_COVERED。没有推导全部非null值域、缺失字段拒绝或真实运行期权限。修订整体继续PARTIAL/NOT_RUN，业务写集未执行。

新增映射回归先在旧映射下真实失败：`spec-field-red.json`，发现1/执行1/失败1；随后 `spec-field-green.json` 是初次单项修正通过。最终 `spec-field-targeted-tests.json` 实际发现2/执行2/通过2/失败0/错误0/跳过0，包含用无关NCR-NULL通过结果替代metadata结果时必须拒绝的源侧负例。原30项结果未覆盖、未重称为本次运行；本次变更只影响这一映射和其准确定位，复用原30项对应未变化的解析/篡改检查范围。

执行使用同一Python3.13、`-B -X utf8`和三个仓库参数，在原比较器测试命令上增加：

```text
--test test_ncr_field_revision_uses_exact_five_field_metadata
--test test_ncr_field_mapping_rejects_substituted_passing_result
--output docs/development/evidence/s01-adoption/implementation/spec-field-targeted-tests.json
```

机器产物使用原verify_adoption入口的 `--write-manifests` 再生并校验，记录为 `spec-field-verification.json`，exit0。解析828来源引用，122ID/352适用/502不适用、19case计数未变。`spec-field-diff-check.json` 独立核对：vectors文件字节完全不变；其他18修订case的规范JSON字节不变；manifest仅增加原native-field-inventory来源引用，其余字段不变。`git diff --check`通过。

没有运行旧库套件、数据库、HTTP、Owner或页面；没有修改产品、父代理consumer/README/validation，也没有提交或暂存。

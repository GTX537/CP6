# 目标启用前的来源冻结证明

`prove-target-enable-actual` 只读取已冻结的实际来源，供 CRM 在持有整组目标锁期间验证。它复用 `ActualSourceFreezer.StatusAsync` 对原请求、实际 SQL/容器身份、Frozen Scope、20 张表保护以及 `Frozen`/代数 `1` 的验证；不创建冻结，也不改变来源状态。

入口只接受以下私有环境输入：

| 环境变量 | 含义 |
| --- | --- |
| `C04A_SQL_CONNECTION` | 本份来源的连接，不放入命令参数或公共报告 |
| `C04A_REQUEST_PATH` | 既有 `ActualSourceFreezeRequest` 的绝对路径 |
| `C04A_REQUEST_FILE_SHA256` | 原文件准确字节的小写 SHA-256 |
| `C04A_TARGET_SET_ANCHOR_SHA256` | 本次目标完整集合 Anchor 摘要，须与原冻结请求一致 |
| `C04A_PROOF_CHALLENGE` | 协调器为本次观察新生成的非空 GUID |
| `C04A_LOCK_TIMEOUT_MS` | 可选，默认 `5000` |
| `C04A_COMMAND_TIMEOUT_SECONDS` | 可选，默认 `30` |

数据库、服务器、BrokerGuid 和可选 `LocalContainer` 完整绑定均从已验证原文件的 `SourceIdentity` 取得。本入口不读取旧的 `C04A_EXPECTED_*` 或额外容器绑定文件；API 选项中 `ExpectedDatabaseGuid` 对应 SQL 的 BrokerGuid，证明中的 `DatabaseGuid` 对应实际数据库 GUID。

```powershell
dotnet CP6.Crm.SourceFence.dll prove-target-enable-actual
```

成功返回一行默认 PascalCase 的规范 `CP6.C04A.SourceFrozenProof.v1` JSON。传输层去除行结束符后，`SourceFrozenProof.Parse(string)` 只接受准确规范表示（最多 16 KiB），拒绝未知、重复、缺失、大小写变化或被篡改的 getter 字段。`SourceIdentitySha256` 绑定完整 `SourceIdentity` 的默认规范 JSON；`FrozenScopeSha256` 来自本次真实状态验证。

库入口为 `VerifyFrozenForTargetAsync(request, expectedRequestSha256, targetSetAnchorSha256, challenge, token)`。错误保持固定代码，CLI 退出码 `2`，不输出连接、原请求或异常堆栈：

| 代码 | 含义 |
| --- | --- |
| `C04A_REQUEST_MISMATCH` | 请求内容或预期摘要不匹配 |
| `C04A_SOURCE_TARGET_SET_MISMATCH` | 完整目标集合 Anchor 无效或不匹配 |
| `C04A_SOURCE_PROOF_CHALLENGE_INVALID` | 挑战 GUID 为空 |
| `C04A_SOURCE_FROZEN_PROOF_INVALID` | 证明字段或规范 JSON 无效 |
| 原有 `C04A_*` 状态/文件错误 | 原文件、SQL 身份、保护、状态、锁或取消检查未通过 |

证明仅表示来源在本次观察时冻结。协调器必须匹配新挑战、请求摘要、集合 Anchor 和来源唯一身份，并持续持有全部目标锁直至启用提交，才能排除原合法恢复协议穿插。`CompleteWriteFenceVerified` 和 `ApprovalIndependentlyVerified` 均为 `false`；管理员隔离、所有旧写入入口、路由、审批以及实际切换验收仍需另行完成。

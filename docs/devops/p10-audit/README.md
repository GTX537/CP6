# P10 非部署型候选审计

本目录记录 P10 的 append-only 项目决定，不是候选发现入口，也不是部署批准。首条 [审计决定](entries/5adde0e29c8cf755d3ad8dfe6e050779d787faeb7040e584428d75dfa0f84558.json) 将 `v0.10.1-p10.1` 记为 **Frozen / Consumable**；仅在本条目与四份台账正常合入远端 main、必需 exact-main 检查成功后生效。未预填收尾 PR、合并 SHA 或未来检查结果。

## 已核验的实际链路

| 阶段 | 精确 run / attempt / job | 结果 |
| --- | --- | --- |
| 正式 validation | [34310410625](https://github.com/GTX537/CP6/actions/runs/34310410625) / 1 / 102335730781 | success；1651 项全量，零失败/跳过；19 项 gate Success |
| 条件 publication | [34312917625](https://github.com/GTX537/CP6/actions/runs/34312917625/attempts/3) / 3 / 102355271120 | success；签名、intent、只读 pre-commit、Locator 条件创建和 postcheck |
| 独立只读 audit | [34317792175](https://github.com/GTX537/CP6/actions/runs/34317792175) / 1 / 102357568213 | success；VerifiedNonDeployable、candidateAccepted=true、deployable=false |

三个 workflow 源码均为 `f992adc3295722a0edae151c454665a4be4b689b`，分别绑定实际 workflow blob；entry 包含完整时间、artifact ID、归档摘要与到期时间。人工审批保留，助手没有自批。

- 候选 SHA-256：`bc3cae8ede16e26a909519ec250fbdf5989b4aa4edfcd6b39397e0958a59ea4f`。
- OCI：`ghcr.io/gtx537/cp6-p10-verifier@sha256:e743e911380a4ed6732685ea33694b6ffb910051c20621388b6903386947296b`。仅 validation 构建一次，publication/audit 没有重建。
- 正式云端原生 SARIF 规则计数：CRITICAL 0、HIGH 0、MEDIUM 3、LOW 4、UNKNOWN 3；这不是逐位置漏洞结果数，也不是先前本地扫描计数。
- Platform 源码 `3ff27e26962dcfd722887afb80a4306010dd9ee1`，PR #51 / run 34123886765 五项成功，exact-main run 34125176309 成功；正式发布 34126521193 / 1，证据 main `808a201f0cf6f877f8ca9c804e304585a29c446a` / run 34132548893 成功。
- CRM PR #46：源码 `a31ca0e323418f7e4108cc6220c0f5fa132e7fc2`，PR/main runs 34133944140 / 34134695003；前向归档 PR #47：`bb1fd8b4f250fabde4476b6a450435de2d07c03f`，PR/main runs 34137355910 / 34138142163，均成功。净化证据含两平台各 16/16 正式消费验证及全部五项 main 作业，不公开私有日志。
- CP6 PR #93 七项成功，exact-main 五条 GitHub workflow / 六作业成功。Azure Artifact 桥不是 P10 候选权威，本文不据 GitHub 成功推断 Azure 状态。
- 七包均为 `0.10.1`、同一 build invocation、签名前后/feed 回读身份有明确映射。NuGet 作者为已批准 PinnedSelfSigned，`publicCaTrusted=false`、`internallyTrusted=true`；RFC3161 链独立验证。信任/证据策略版本为 1，Locator 与 OCI 使用不同用途公钥。

## 留存与复核

`evidence/v0.10.1-p10.1/` 保留从 GitHub 精确 Artifact ID 下载并验证摘要的三份原始 ZIP，以及未经重排或换行转换的 27 个公开内容文件：validation 索引和 23 对象、两文件签名意图、一个普通只读结果。总计 30 文件，逐文件路径、长度和 SHA-256 写入 entry。`.gitattributes` 禁止证据和条目的文本转换。未复制 NuGet 二进制、私有 CRM 原始归档、Secret 或本机诊断日志。

原生 SBOM/SARIF 原样保留；净化 CRM 结果、签名证明与全部 evidence record/payload 的 hash 链可以离线对账。候选 payload 仍在其签名引用的 R2 内容地址，未声称这里保存了它的原始副本。R2 Locator 是唯一权威发现入口；此目录不替代 GitHub 实际运行核对、远端可读性或消费者的当前信任/吊销检查。GitHub Artifact 将于 2026-12-08 到期，Git 留存使这些公开原始记录不会仅剩过期链接，但不能绕过实时 verifier 的过期门禁。

在仓库根目录执行下列只读检查，可复核条目及全部留存字节：

```powershell
$taskEntry = Get-Item 'docs/devops/p10-audit/entries/5adde0e29c8cf755d3ad8dfe6e050779d787faeb7040e584428d75dfa0f84558.json'
if ((Get-FileHash $taskEntry.FullName -Algorithm SHA256).Hash.ToLowerInvariant() -cne $taskEntry.BaseName) { throw 'entry hash mismatch' }
$taskDecision = Get-Content $taskEntry.FullName -Raw | ConvertFrom-Json
foreach ($taskFile in $taskDecision.retainedFiles) {
    if ((Get-FileHash -LiteralPath $taskFile.path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $taskFile.sha256 -or
        (Get-Item -LiteralPath $taskFile.path).Length -ne $taskFile.byteLength) { throw 'retained evidence mismatch' }
}
```

首次 entry 的 `previousEntrySha256=null`。将来改变状态只能增加新条目并引用前一条目原始字节摘要，不编辑旧 entry、候选、Locator 或证据。JSON entry 是项目审计记录，不伪装为 Platform candidate schema；其文件摘要包括文件实际结尾换行。普通 CLI 结果的摘要同样包括原始末尾换行。

## 历史失败与边界

publication attempt 1 因 publisher 凭据格式失败，未写 R2；attempt 2 的 prepare/sign 成功，但 consumer 凭据格式令 store-bundle 失败，留下未被 Locator 引用的内容对象。Owner 分别更新两组凭据并独立批准后，attempt 3 完成。失败仍是失败；孤立对象未删除，未覆盖 Locator，也未降低任何扫描/签名/审批门禁。

本决定只关闭 P10 Platform reference 阶段，所有候选仍 `deployable=false`。不构成 WMS/CRM System release、四仓兼容性验收、CRM runtime 激活、DEV/UAT/PROD 或数据库迁移授权。后续工作需按既有路线单独立项。

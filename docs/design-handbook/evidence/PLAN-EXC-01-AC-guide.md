# PLAN-EXC-01 原103项验收的开发阅读版

来源为CP12组合指定的 `EXC_NEW_AC_CATALOGUE.json`，SHA `6619955d7e11d661981f8c5fab61c235b9978302750c562eb6d1bf4c677f954273`。本页将已逐项实读的标题/API、specificConditions、步骤、specificAssertions和完整Problem转排；未复制完整原生事实、私有图和每一before/writes/after，精确file/JSON Pointer依当前351成员组合解析。这里的引用仍属于用例规范的一部分，不能用摘要替其正文，也不能认为本页证明所有引用正文已读。

[完整AC原件](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-exc-cp12/EXC_NEW_AC_CATALOGUE.json>)｜[组合成员账](root-plan-exc-current-members.json)｜[模块说明](../modules/PLAN-EXC-01.md)

全部为NOT_RUN，actualRunId/environment/result为null；本页不是自动化测试实现或执行证据。各SPEC数量按后继勘误为25/18/15/19/26。源文件旧newCriteriaPerSpec的80项以及CP04 pending标签保留历史，不能选作当前统计/接受状态。

共同断言：不写source Owner业务、不产生真实grant/生产专业采用、不执行部署；同原槽保持原请求及不可变结果原字节。可变行BodyCanonical是原生rowversion同statement读回后的非持久投影，不是全列SQL输入。独立数字用例按自己的明确差异判断，不声称引用正常对象保持不变却满足另一数值。

已发现一处待源文勘误：AC087步骤1仍写readcut22，当前同项specificConditions.fixedReadCut=113，CP12主文及独审亦明确固定cut113；开发采用当前精确输入/packet的113，保留旧步骤文字可追溯。此处是文档局部陈旧措辞，不是运行失败。

## EXC-AC-001｜授权固定cut列表

原SPEC：SPEC-PLAN-EXC-01-01；API：EX01, EX02；原JSON：`/criteria/0`。

前提：

```json
{
  "E": "DESIGN-E1",
  "T": "DESIGN-T1",
  "sites": [
    "SITE-A"
  ],
  "cases": [
    "A",
    "B"
  ],
  "readCut": 17,
  "pageSize": 1,
  "sort": "DETECTED_DESC",
  "detectedAtEqual": true
}
```

步骤：

1. POST CaseFilter初页，随后同cursor续页

必须得到：

```json
{
  "page1": [
    "A"
  ],
  "page2": [
    "B"
  ],
  "duplicateRows": 0,
  "readCutBoth": 17,
  "tieBreak": "caseId ordinal",
  "newCaseAtSeq18": "not included",
  "mutationRows": 0
}
```

## EXC-AC-002｜越权Site筛选

原SPEC：SPEC-PLAN-EXC-01-01；API：EX02；原JSON：`/criteria/1`。

前提：

```json
{
  "authorizedSites": [
    "SITE-A"
  ],
  "requestedSites": [
    "SITE-B"
  ]
}
```

步骤：

1. 提交含SITE-B CaseFilter

必须得到：

```json
{
  "rowsDisclosed": 0,
  "siteBNameDisclosed": false,
  "domainWrites": 0
}
```

完整错误：

```json
{
  "type": "plan-exc.problem/1-proposed",
  "status": 403,
  "code": "PURPOSE_NOT_AUTHORIZED",
  "messageKey": "PURPOSE_NOT_AUTHORIZED",
  "correlationId": "533c46d7-578d-5b01-990e-035d5433239b",
  "operationId": null,
  "retryMode": "NONE",
  "fields": [],
  "reasonCodes": [
    "PURPOSE_NOT_AUTHORIZED"
  ],
  "requiredOwner": null,
  "currentVisibleRevision": null
}
```

## EXC-AC-003｜完整已知短缺20

原SPEC：SPEC-PLAN-EXC-01-01；API：EX03, EX09；原JSON：`/criteria/2`。

前提：

```json
{
  "demand": 100,
  "uom": "EA",
  "onTimeCoverage": 80,
  "requiredSources": "COMPLETE",
  "allConditions": "KNOWN",
  "view": "ACTUAL_QUALIFIED"
}
```

步骤：

1. 读取准确assessment

必须得到：

```json
{
  "knowledge": "COMPLETE",
  "businessOutcome": "KNOWN_SHORTAGE",
  "requiredOutstanding": "100.00000000",
  "onTime": "80.00000000",
  "knownShortage": "20.00000000",
  "MRPInputAllowed": true,
  "physicalStockWrites": 0
}
```

## EXC-AC-004｜必要源未知不能净20

原SPEC：SPEC-PLAN-EXC-01-01；API：EX03, EX09；原JSON：`/criteria/3`。

前提：

```json
{
  "demand": 100,
  "knownCoverage": 80,
  "unreadNecessaryOwner": "MES-WO"
}
```

步骤：

1. 完成合法partial capture并读解释

必须得到：

```json
{
  "knowledge": "PARTIAL",
  "businessOutcome": "UNDETERMINED",
  "knownCoverage": "80.00000000",
  "knownShortage": null,
  "missingOwner": "MES-WO",
  "oldCompleteAssessment": "retained"
}
```

## EXC-AC-005｜UOM不同不汇总

原SPEC：SPEC-PLAN-EXC-01-01；API：EX02；原JSON：`/criteria/4`。

前提：

```json
{
  "caseA": {
    "required": 10,
    "uom": "EA"
  },
  "caseB": {
    "required": 10,
    "uom": "KG"
  },
  "conversionFact": null
}
```

步骤：

1. 请求数量KPI/缺口排序

必须得到：

```json
{
  "groups": [
    "EA",
    "KG"
  ],
  "crossUomTotal": null,
  "SHORTAGE_DESC_SAME_UOM": "requires one exact UOM group"
}
```

## EXC-AC-006｜预留分配一次扣减

原SPEC：SPEC-PLAN-EXC-01-01；API：EX03；原JSON：`/criteria/5`。

前提：

```json
{
  "eligibleStock": 60,
  "reservation": 25,
  "allocationSubset": 15,
  "allOverlapProofs": "COMPLETE"
}
```

步骤：

1. 读自由与coverage

必须得到：

```json
{
  "free": "35.00000000",
  "notFree": 20,
  "allocationAdditionalDebit": 0,
  "reservationValue": 25,
  "allocationDisplayedAsSubset": 15
}
```

## EXC-AC-007｜FLOOR跨桶只一次

原SPEC：SPEC-PLAN-EXC-01-01；API：EX03, EX04；原JSON：`/criteria/6`。

前提：

```json
{
  "D1": {
    "gross": 100,
    "supply": 100,
    "floor": 20,
    "planned": 20
  },
  "D2": {
    "gross": 0,
    "newSupply": 0,
    "opening": 20,
    "floor": 20
  }
}
```

步骤：

1. 读两个MRP ledger节点

必须得到：

```json
{
  "D1Net": 20,
  "D2Net": 0,
  "EXCAdditionalSafetyDebit": 0,
  "carryCount": 1,
  "floorIsCustomerDemand": false
}
```

## EXC-AC-008｜试制QA通过而普通用途原Owner未知

原SPEC：SPEC-PLAN-EXC-01-01；API：EX03, EX09；原JSON：`/criteria/7`。

前提：

```json
{
  "stockQty": 50,
  "originPurpose": "TRIAL",
  "quality": "ACCEPTED",
  "requiredPurpose": "PRODUCTION",
  "ordinaryUseAuthorization": null,
  "purposeOwnerFact": null,
  "purposeKnowledge": "UNKNOWN",
  "allOtherRequiredSources": "COMPLETE",
  "trialBaselineAllows": [
    "TRIAL"
  ]
}
```

步骤：

1. 评价按时合格coverage

必须得到：

```json
{
  "qualityFact": "ACCEPTED retained",
  "ordinaryCoverageFromTrial": 0,
  "purposeCondition": "UNKNOWN",
  "knownShortage": null,
  "RELGrantCreated": 0,
  "knowledge": "PARTIAL",
  "businessOutcome": "UNDETERMINED"
}
```

## EXC-AC-009｜已知Hold遇查询失败不变PASS

原SPEC：SPEC-PLAN-EXC-01-01；API：EX03；原JSON：`/criteria/8`。

前提：

```json
{
  "qualityFact": "HOLD-15",
  "qualityCurrentService": "UNAVAILABLE"
}
```

步骤：

1. 读取current观察

必须得到：

```json
{
  "knownCondition": "NOT_SATISFIED",
  "conditionQty": 15,
  "currentEffectivity": "UNKNOWN",
  "historyHoldFact": "retained",
  "PASSGenerated": false
}
```

## EXC-AC-010｜cursor过期

原SPEC：SPEC-PLAN-EXC-01-01；API：EX02；原JSON：`/criteria/9`。

前提：

```json
{
  "cursorIssuedAt": "2026-10-04T18:00:00Z",
  "cursorExpiresAt": "2026-10-04T18:15:00Z",
  "now": "2026-10-04T18:15:01Z",
  "permissionDigestChanged": false
}
```

步骤：

1. 同cursor读下一页

必须得到：

```json
{
  "rows": null,
  "domainWrites": 0,
  "clientNext": "new first read, no mixed cuts"
}
```

完整错误：

```json
{
  "type": "plan-exc.problem/1-proposed",
  "status": 410,
  "code": "CURSOR_EXPIRED",
  "messageKey": "CURSOR_EXPIRED",
  "correlationId": "94146149-bcdb-57a3-8aef-34b3fbe1ce3d",
  "operationId": null,
  "retryMode": "READ_NEW_PREDECESSOR",
  "fields": [],
  "reasonCodes": [
    "CURSOR_EXPIRED"
  ],
  "requiredOwner": null,
  "currentVisibleRevision": null
}
```

## EXC-AC-011｜旧单行0保真

原SPEC：SPEC-PLAN-EXC-01-01；API：EX21, EX22；原JSON：`/criteria/10`。

前提：

```json
{
  "legacyRequired": 25,
  "legacyAvailable": 0,
  "legacyBranch": "no single stock row >=25",
  "twoPhysicalLots": [
    15,
    15
  ],
  "sourceMapping": null
}
```

步骤：

1. legacy看板和新解释同屏

必须得到：

```json
{
  "reportedDifference": 25,
  "legacyAvailable": 0,
  "globalOnHandInferred": null,
  "knownShortage": null,
  "knowledge": "LEGACY_UNVERIFIED",
  "sourceStockModified": false,
  "httpStatus": 200,
  "mappingState": "UNMAPPED",
  "mappingRevision": 0,
  "newCaseRows": 0,
  "newAssessmentRows": 0,
  "normalKpiIncluded": false
}
```

## EXC-AC-012｜旧RESOLVED不证明补足

原SPEC：SPEC-PLAN-EXC-01-01；API：EX21, EX22；原JSON：`/criteria/11`。

前提：

```json
{
  "legacyStatus": "RESOLVED",
  "remark": "filled",
  "ResolvedAt": "2026-06-01T08:00:00Z",
  "newEvidence": null
}
```

步骤：

1. 读取legacy anchor

必须得到：

```json
{
  "reportedStatus": "RESOLVED",
  "reportedResolvedAt": "2026-06-01T08:00:00Z",
  "newCaseClosure": null,
  "businessOutcome": "UNDETERMINED",
  "httpStatus": 200,
  "mappingState": "UNMAPPED",
  "mappingRevision": 0,
  "newCaseRows": 0,
  "newAssessmentRows": 0,
  "normalKpiIncluded": false
}
```

## EXC-AC-013｜旧RowVersion/Tenant正确保留

原SPEC：SPEC-PLAN-EXC-01-01；API：EX21, EX22；原JSON：`/criteria/12`。

前提：

```json
{
  "oldClass": "MaterialShortage:BaseBizEntity:BaseTenantEntity",
  "RowVersion": "AAAAAAAAAAE="
}
```

步骤：

1. 展示legacy并创建只读mapping候选

必须得到：

```json
{
  "TenantIdPreserved": "2eb01f10-0c5b-56f3-90a7-9b31756b259d",
  "RowVersionPreserved": "AAAAAAAAAAE=",
  "claimOldNoConcurrency": false,
  "newBusinessCurrentGateProven": false,
  "httpStatus": 200,
  "mappingState": "UNMAPPED",
  "mappingRevision": 0,
  "newCaseRows": 0,
  "newAssessmentRows": 0,
  "normalKpiIncluded": false
}
```

## EXC-AC-014｜导出超限拒绝不截断

原SPEC：SPEC-PLAN-EXC-01-01；API：EX18, EX19；原JSON：`/criteria/13`。

前提：

```json
{
  "visibleRows": 50001,
  "maxRows": 50000,
  "purpose": "EXCEPTION_EXPLANATION"
}
```

步骤：

1. 提交固定cut导出

必须得到：

```json
{
  "fileArtifact": null,
  "truncatedFile": null,
  "MRPRunCreated": 0
}
```

完整错误：

```json
{
  "type": "plan-exc.problem/1-proposed",
  "status": 422,
  "code": "EXPORT_LIMIT_EXCEEDED",
  "messageKey": "EXPORT_LIMIT_EXCEEDED",
  "correlationId": "754572c4-2f2f-5644-8934-9e1f314a4e0c",
  "operationId": null,
  "retryMode": "NONE",
  "fields": [],
  "reasonCodes": [
    "EXPORT_LIMIT_EXCEEDED"
  ],
  "requiredOwner": null,
  "currentVisibleRevision": null
}
```

## EXC-AC-015｜精确来源document-line-schedule

原SPEC：SPEC-PLAN-EXC-01-02；API：EX04；原JSON：`/criteria/14`。

前提：

```json
{
  "demands": [
    {
      "document": "SO-A",
      "line": "L1",
      "schedule": "S1",
      "version": "A",
      "qty": 60
    },
    {
      "document": "SO-A",
      "line": "L1",
      "schedule": "S2",
      "version": "A",
      "qty": 40
    }
  ]
}
```

步骤：

1. 固定assessment读来源

必须得到：

```json
{
  "distinctDemandSlices": 2,
  "demandAQty": 60,
  "demandBQty": 40,
  "opaqueVersion": "A",
  "mergeBySameDocumentLine": false
}
```

## EXC-AC-016｜同产品不是责任去重

原SPEC：SPEC-PLAN-EXC-01-02；API：EX04；原JSON：`/criteria/15`。

前提：

```json
{
  "demandA": {
    "item": "X",
    "qty": 20,
    "date": "D1"
  },
  "demandB": {
    "item": "X",
    "qty": 20,
    "date": "D1"
  }
}
```

步骤：

1. 读取完整source graph

必须得到：

```json
{
  "contributions": 2,
  "gross": 40,
  "sourceKeysBoth": "retained",
  "sourceWrites": 0
}
```

## EXC-AC-017｜需求有效/覆盖/实际履约分层

原SPEC：SPEC-PLAN-EXC-01-02；API：EX04；原JSON：`/criteria/16`。

前提：

```json
{
  "demandOutstanding": 100,
  "plannedCoverage": 100,
  "actualShipment": 0
}
```

步骤：

1. 读需求和coverage

必须得到：

```json
{
  "outstanding": 100,
  "planCoverage": 100,
  "fulfilled": 0,
  "DemandAutoFulfilled": false
}
```

## EXC-AC-018｜完整MRP原谱系

原SPEC：SPEC-PLAN-EXC-01-02；API：EX04；原JSON：`/criteria/17`。

前提：

```json
{
  "keys": [
    "REQ1",
    "RUN1",
    "I1",
    "AT1",
    "SEAL1",
    "RS1",
    "VAL1",
    "SEL1",
    "PUB1",
    "ES1"
  ],
  "singleAttempt": "AT1"
}
```

步骤：

1. 展开来源链

必须得到：

```json
{
  "allRefsExactly": [
    "REQ1",
    "RUN1",
    "I1",
    "AT1",
    "SEAL1",
    "RS1",
    "VAL1",
    "SEL1",
    "PUB1",
    "ES1"
  ],
  "resultSetAttempt": "AT1",
  "publicationResultSet": "RS1",
  "missingLayer": false
}
```

## EXC-AC-019｜非MRP原Owner来源

原SPEC：SPEC-PLAN-EXC-01-02；API：EX04；原JSON：`/criteria/18`。

前提：

```json
{
  "originKind": "OWNER_OCCURRENCE",
  "owner": "WMS-STOCK",
  "movement": "MOVE-1",
  "line": "L1",
  "version": "A"
}
```

步骤：

1. 读source detail

必须得到：

```json
{
  "mrpLineage": null,
  "ownerOccurrence": "MOVE-1/L1/A",
  "originTimeVsObservedTime": "separate"
}
```

## EXC-AC-020｜100→PO60→Receipt30阶段唯一

原SPEC：SPEC-PLAN-EXC-01-02；API：EX04, EX09；原JSON：`/criteria/19`。

前提：

```json
{
  "firm": 100,
  "actualPO": 60,
  "accepted": 20,
  "rejected": 10,
  "exactTransferProof": "present"
}
```

步骤：

1. 组装代表集

必须得到：

```json
{
  "currentSpans": {
    "unconverted": 40,
    "POUnreceived": 30,
    "acceptedStock": 20,
    "rejectedStock": 10
  },
  "sum": 100,
  "ordinaryPlanCandidateUpper": 90,
  "historicalSum190Used": false,
  "rejectedAsUnreceived": 0
}
```

## EXC-AC-021｜材料NET40已抵20不再抵

原SPEC：SPEC-PLAN-EXC-01-02；API：EX04；原JSON：`/criteria/20`。

前提：

```json
{
  "oldResidual": 40,
  "alreadyOffset": 20,
  "newDerived": 180,
  "extraFree": 50,
  "oldAndNewDisjoint": true
}
```

步骤：

1. 查看MRP材料责任和抵扣

必须得到：

```json
{
  "grossForNewNetting": 220,
  "net": 170,
  "offset20Reused": false,
  "offset20StillProtected": true,
  "EXCRebuildMESLedger": false
}
```

## EXC-AC-022｜自有预留只覆盖自身

原SPEC：SPEC-PLAN-EXC-01-02；API：EX04；原JSON：`/criteria/21`。

前提：

```json
{
  "AOwnReserved": 25,
  "BOtherReserved": 20,
  "free": 15
}
```

步骤：

1. 读A覆盖明细

必须得到：

```json
{
  "ACandidateCoverage": 40,
  "B20UsableByA": 0,
  "globalFree": 15,
  "physicalReserveCreated": 0
}
```

## EXC-AC-023｜EXACT有理转换

原SPEC：SPEC-PLAN-EXC-01-02；API：EX04；原JSON：`/criteria/22`。

前提：

```json
{
  "sourceQty": "3.00000000",
  "ratioNumerator": 2,
  "ratioDenominator": 1,
  "sourceUom": "M",
  "targetUom": "KG",
  "approvedConversion": "CV1"
}
```

步骤：

1. 读映射

必须得到：

```json
{
  "targetQty": "6.00000000",
  "sourceQtyPreserved": "3.00000000",
  "conversionRef": "CV1",
  "implicitRounding": 0
}
```

## EXC-AC-024｜换算无法精确表示

原SPEC：SPEC-PLAN-EXC-01-02；API：EX09；原JSON：`/criteria/23`。

前提：

```json
{
  "sourceQty": "1.00000000",
  "ratio": {
    "n": 1,
    "d": 3
  },
  "targetScale": 8,
  "approvedRounding": null
}
```

步骤：

1. 构造完整assessment候选

必须得到：

```json
{
  "newPreciseTarget": null,
  "oldAssessmentHead": "unchanged",
  "candidateIssue": "UOM_NOT_EXACT"
}
```

完整错误：

```json
{
  "type": "plan-exc.problem/1-proposed",
  "status": 422,
  "code": "UOM_NOT_EXACT",
  "messageKey": "UOM_NOT_EXACT",
  "correlationId": "637ebdb6-e017-5e8f-a177-4a856f076953",
  "operationId": null,
  "retryMode": "NONE",
  "fields": [],
  "reasonCodes": [
    "UOM_NOT_EXACT"
  ],
  "requiredOwner": null,
  "currentVisibleRevision": null
}
```

## EXC-AC-025｜PR草稿不是承诺

原SPEC：SPEC-PLAN-EXC-01-02；API：EX04；原JSON：`/criteria/24`。

前提：

```json
{
  "PR": {
    "source": "Shortage",
    "SourceRefNo": "SH1",
    "qty": 30,
    "status": "Draft"
  },
  "POCommitment": null
}
```

步骤：

1. 读来源与confirmed coverage

必须得到：

```json
{
  "PRSourceAnchor": "SH1",
  "PRProcessingQty": 30,
  "confirmedFutureFromPR": 0,
  "newPRCreated": 0,
  "resolvedByPR": false
}
```

## EXC-AC-026｜两页混watermark不完整

原SPEC：SPEC-PLAN-EXC-01-02；API：EX09；原JSON：`/criteria/25`。

前提：

```json
{
  "page1": {
    "capture": "C1",
    "watermark": "v1"
  },
  "page2": {
    "capture": "C1",
    "watermark": "v2"
  },
  "replacementProof": null
}
```

步骤：

1. 装配source candidate

必须得到：

```json
{
  "knowledge": "PARTIAL",
  "issue": "PAGE_GAP",
  "completeInput": false,
  "fakeMissingQtyZero": false
}
```

## EXC-AC-027｜有权更正保旧发生事实

原SPEC：SPEC-PLAN-EXC-01-02；API：M01, EX04；原JSON：`/criteria/26`。

前提：

```json
{
  "oldReceipt": 30,
  "newCorrectedReceipt": 20,
  "supplierObligationSuccessor": null,
  "oldReceivedAt": "2026-10-04T08:00:00Z"
}
```

步骤：

1. 接收CORRECTION并刷新

必须得到：

```json
{
  "old30Artifact": "immutable",
  "currentReceipt20": "new fact",
  "POObligationAutomaticallyRestored10": false,
  "oldOccurredAtUnchanged": true,
  "assessmentCurrent": "needs reevaluation"
}
```

## EXC-AC-028｜来源unknown/N_A分开

原SPEC：SPEC-PLAN-EXC-01-02；API：EX04；原JSON：`/criteria/27`。

前提：

```json
{
  "technicalRef": null,
  "technicalKnowledge": "UNKNOWN",
  "OwnerNotApplicableProof": null
}
```

步骤：

1. 显示资格及source coverage

必须得到：

```json
{
  "technicalKnowledge": "UNKNOWN",
  "notApplicable": false,
  "latestRevisionAssumed": null,
  "purposeGrant": null
}
```

## EXC-AC-029｜准确impact seed

原SPEC：SPEC-PLAN-EXC-01-03；API：EX10；原JSON：`/criteria/28`。

前提：

```json
{
  "changedSupply": "ROOT1/[20,30)",
  "unrelatedSameSKU": "ROOT2",
  "shares": {
    "ROOT1": [
      "A",
      "B"
    ],
    "ROOT2": [
      "C"
    ]
  }
}
```

步骤：

1. 建立ImpactManifest

必须得到：

```json
{
  "affectedBeneficiaries": [
    "A",
    "B"
  ],
  "unrelatedCIncluded": false,
  "seedRange": "ROOT1/[20,30)",
  "sourceStockWrites": 0
}
```

## EXC-AC-030｜A取消不删B共享PO

原SPEC：SPEC-PLAN-EXC-01-03；API：EX10；原JSON：`/criteria/29`。

前提：

```json
{
  "A": 60,
  "B": 40,
  "sharedPO": 100,
  "ADecrease": 30
}
```

步骤：

1. 构建A取消影响

必须得到：

```json
{
  "AOutstanding": 30,
  "BOutstanding": 40,
  "POActualQty": 100,
  "affected": "A/[30,60)",
  "retained": [
    "A/[0,30)",
    "B/[60,100)"
  ],
  "PODeleteRows": 0
}
```

## EXC-AC-031｜A取消不返B真实预留

原SPEC：SPEC-PLAN-EXC-01-03；API：EX05；原JSON：`/criteria/30`。

前提：

```json
{
  "AReserved": 20,
  "BReserved": 25,
  "demandACancelled": true,
  "WmsReleaseReceipt": null
}
```

步骤：

1. 读impact边界

必须得到：

```json
{
  "BReserved": 25,
  "AReservedRemains": 20,
  "AReleaseObligation": "OPEN",
  "freeIncrement": 0
}
```

## EXC-AC-032｜缺外部保护不得局部重算

原SPEC：SPEC-PLAN-EXC-01-03；API：EX10, EX15；原JSON：`/criteria/31`。

前提：

```json
{
  "requested": "A",
  "sharedPool": 80,
  "BOwnerProtection": "UNKNOWN"
}
```

步骤：

1. 判局部闭包并请求重算

必须得到：

```json
{
  "localRecalcAllowed": false,
  "unknownBoundaryOwner": "B protection Owner",
  "newMRPRequest": 0
}
```

完整错误：

```json
{
  "type": "plan-exc.problem/1-proposed",
  "status": 422,
  "code": "SHARED_PROTECTION_UNKNOWN",
  "messageKey": "SHARED_PROTECTION_UNKNOWN",
  "correlationId": "583db4ee-6bb0-558d-8d89-6a96f99a4a5f",
  "operationId": null,
  "retryMode": "NONE",
  "fields": [],
  "reasonCodes": [
    "SHARED_PROTECTION_UNKNOWN"
  ],
  "requiredOwner": null,
  "currentVisibleRevision": null
}
```

## EXC-AC-033｜有证独立closure合法

原SPEC：SPEC-PLAN-EXC-01-03；API：EX10；原JSON：`/criteria/32`。

前提：

```json
{
  "closureA": [
    "DA",
    "SA"
  ],
  "allBoundaryEdges": "proven retained",
  "allRequiredOwners": "complete",
  "sharedUnknown": false
}
```

步骤：

1. 保存独立Impact

必须得到：

```json
{
  "graphCompleteness": "COMPLETE",
  "executionCertainty": "KNOWN",
  "localRecalcAllowed": true,
  "exactExit": [
    "OLD-A-SUG"
  ],
  "retained": [
    "FIRM-B"
  ]
}
```

## EXC-AC-034｜Firm范围外保护

原SPEC：SPEC-PLAN-EXC-01-03；API：EX05, EX15；原JSON：`/criteria/33`。

前提：

```json
{
  "pool": 80,
  "retainedFirmB": 50,
  "requestedA": 60
}
```

步骤：

1. 查看局部重算输入保护

必须得到：

```json
{
  "AAvailableAtMost": 30,
  "FirmBProtection": 50,
  "deleteFirmB": false,
  "using80ForA": false
}
```

## EXC-AC-035｜同材料不同Occurrence不合并

原SPEC：SPEC-PLAN-EXC-01-03；API：EX05；原JSON：`/criteria/34`。

前提：

```json
{
  "WO": "WO1",
  "material": "X",
  "occurrences": [
    "OP1-L1",
    "OP2-L1"
  ],
  "qty": [
    10,
    15
  ]
}
```

步骤：

1. 构建材料影响

必须得到：

```json
{
  "materialObligationNodes": 2,
  "totalOnlyIfCompatible": 25,
  "occurrenceKeys": "both preserved",
  "usingMaterialCdAsIdentity": false
}
```

## EXC-AC-036｜收到停止/禁止/在途分层

原SPEC：SPEC-PLAN-EXC-01-03；API：EX05；原JSON：`/criteria/35`。

前提：

```json
{
  "stopReceipt": "RECEIVED",
  "newAdmissionForbidden": true,
  "originalPending": [
    "K1"
  ],
  "originalK1Outcome": "UNKNOWN"
}
```

步骤：

1. 评估关闭所需execution coverage

必须得到：

```json
{
  "stopReceived": true,
  "newForbidden": true,
  "inflightCleared": false,
  "obligationK1": "OUTCOME_UNKNOWN",
  "closureReady": false
}
```

## EXC-AC-037｜完整在途列表仍不能清未知

原SPEC：SPEC-PLAN-EXC-01-03；API：EX05；原JSON：`/criteria/36`。

前提：

```json
{
  "pathCoverage": "COMPLETE",
  "operations": [
    {
      "state": "UNKNOWN",
      "potentialQty": 20
    }
  ]
}
```

步骤：

1. 读取impact

必须得到：

```json
{
  "graphCompleteness": "COMPLETE",
  "executionCertainty": "UNKNOWN",
  "potentialQty": 20,
  "closureReady": false
}
```

## EXC-AC-038｜不可拆共享group保护

原SPEC：SPEC-PLAN-EXC-01-03；API：EX10；原JSON：`/criteria/37`。

前提：

```json
{
  "parentPortion": 40,
  "pegs": [
    20,
    20
  ],
  "indivisibleGroup": "G1",
  "requestedOnlyFirstPeg": true
}
```

步骤：

1. 建立删半范围的closure

必须得到：

```json
{
  "wholeGroupRequired": 40,
  "acceptedHalfClosure": false,
  "domainHeadWrites": 0
}
```

完整错误：

```json
{
  "type": "plan-exc.problem/1-proposed",
  "status": 409,
  "code": "INDIVISIBLE_GROUP_CONFLICT",
  "messageKey": "INDIVISIBLE_GROUP_CONFLICT",
  "correlationId": "fa26da83-cf67-5dac-8622-800af8dfba09",
  "operationId": null,
  "retryMode": "NONE",
  "fields": [],
  "reasonCodes": [
    "INDIVISIBLE_GROUP_CONFLICT"
  ],
  "requiredOwner": null,
  "currentVisibleRevision": null
}
```

## EXC-AC-039｜责任分派不等接受

原SPEC：SPEC-PLAN-EXC-01-03；API：EX11, EX05；原JSON：`/criteria/38`。

前提：

```json
{
  "OwnerWork": "registered",
  "outbox": "DELIVERED",
  "emailSent": true,
  "OwnerReceipt": null
}
```

步骤：

1. 读受理情况

必须得到：

```json
{
  "Work": "WAITING_OWNER",
  "Obligation": "OPEN",
  "AcceptedIndependent": false,
  "CaseClosed": false
}
```

## EXC-AC-040｜Owner部分受理明确份额

原SPEC：SPEC-PLAN-EXC-01-03；API：M02, EX05；原JSON：`/criteria/39`。

前提：

```json
{
  "requestedObligations": [
    "O1-70",
    "O2-50"
  ],
  "OwnerAccepted": [
    "O1-70"
  ],
  "OwnerUnknown": [
    "O2-50"
  ],
  "total": 120
}
```

步骤：

1. 接收PARTIAL_ACCEPTED receipt

必须得到：

```json
{
  "acceptedQty": 70,
  "unknownQty": 50,
  "unknownReturnedAsFree": 0,
  "acceptedAndUnknownDisjoint": true,
  "closureReady": false
}
```

## EXC-AC-041｜技术变更不代Quality解除

原SPEC：SPEC-PLAN-EXC-01-03；API：M01, EX05；原JSON：`/criteria/40`。

前提：

```json
{
  "ECO": "new baseline applicable",
  "QualityHold": "QH1 ACTIVE"
}
```

步骤：

1. 构建source correction影响

必须得到：

```json
{
  "baselineCurrent": "new",
  "QualityHold": "QH1 ACTIVE",
  "HoldReleasedByEXC": false,
  "OwnerActions": "PLM and QUALITY separately"
}
```

## EXC-AC-042｜未决数量无上界不填0

原SPEC：SPEC-PLAN-EXC-01-03；API：EX05；原JSON：`/criteria/41`。

前提：

```json
{
  "ownerPendingOperation": "K-U",
  "potentialRange": null,
  "potentialQuantityState": "UNKNOWN"
}
```

步骤：

1. 呈现inflight风险

必须得到：

```json
{
  "potentialValue": null,
  "state": "OUTCOME_UNKNOWN",
  "closureReady": false,
  "tooltip": "range not proven, not zero"
}
```

## EXC-AC-043｜有据补足解决

原SPEC：SPEC-PLAN-EXC-01-04；API：EX12；原JSON：`/criteria/42`。

前提：

```json
{
  "caseRevision": 4,
  "assessment": "A5 COMPLETE CURRENT",
  "required": 100,
  "onTime": 100,
  "shortage": 0,
  "late": 0,
  "impact": "I5 COMPLETE",
  "requiredObligations": "all SETTLED",
  "exactRequest": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/RESOLVE_AFTER_M04_AND_EPOCH9/request"
  },
  "exactBeforeHead": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/RESOLVE_AFTER_M04_AND_EPOCH9/fullBeforeCaseHead"
  }
}
```

步骤：

1. Resolve GAP_ELIMINATED with exact expected+evidence

必须得到：

```json
{
  "httpStatus": 201,
  "Resolution": "R1",
  "Closure": "C1",
  "newRevision": 5,
  "lifecycle": "CLOSED",
  "requiredOutstanding": 100,
  "DemandFulfilledByResolve": false,
  "StockWrites": 0,
  "exactImmutableRecordedResult": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/RESOLVE_AFTER_M04_AND_EPOCH9/recordedResult"
  },
  "fullBeforeAndAfter": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/RESOLVE_AFTER_M04_AND_EPOCH9"
  }
}
```

## EXC-AC-044｜人工待办不能绕必要未知

原SPEC：SPEC-PLAN-EXC-01-04；API：EX11, EX12；原JSON：`/criteria/43`。

前提：

```json
{
  "inflight": "K1 UNKNOWN may execute20",
  "humanTask": "assigned",
  "noOwnerNoEffectProof": true
}
```

步骤：

1. Resolve INDEPENDENT_RESIDUAL_ACCEPTED

必须得到：

```json
{
  "Resolution": null,
  "Closure": null,
  "K1Remains": "OUTCOME_UNKNOWN",
  "caseHead": "unchanged"
}
```

完整错误：

```json
{
  "type": "plan-exc.problem/1-proposed",
  "status": 422,
  "code": "UNRESOLVED_EXECUTION",
  "messageKey": "UNRESOLVED_EXECUTION",
  "correlationId": "c6248694-7816-59ba-a755-349e501ad502",
  "operationId": null,
  "retryMode": "NONE",
  "fields": [],
  "reasonCodes": [
    "UNRESOLVED_EXECUTION"
  ],
  "requiredOwner": null,
  "currentVisibleRevision": null
}
```

## EXC-AC-045｜未来供给不足当天仍缺

原SPEC：SPEC-PLAN-EXC-01-04；API：EX12；原JSON：`/criteria/44`。

前提：

```json
{
  "requiredAtD2": 60,
  "onTime": 0,
  "futureAtD3": 60,
  "plannedCoverage": 60
}
```

步骤：

1. 请求GAP_ELIMINATED

必须得到：

```json
{
  "Resolution": null,
  "shortageAtD2": 60,
  "future": 60,
  "customerDateChanged": false
}
```

完整错误：

```json
{
  "type": "plan-exc.problem/1-proposed",
  "status": 422,
  "code": "GAP_NOT_ELIMINATED",
  "messageKey": "GAP_NOT_ELIMINATED",
  "correlationId": "bc458e7a-05e1-5a91-9362-9a31e2fb9080",
  "operationId": null,
  "retryMode": "NONE",
  "fields": [],
  "reasonCodes": [
    "GAP_NOT_ELIMINATED"
  ],
  "requiredOwner": null,
  "currentVisibleRevision": null
}
```

## EXC-AC-046｜Quality解除单原因不全链通

原SPEC：SPEC-PLAN-EXC-01-04；API：EX12；原JSON：`/criteria/45`。

前提：

```json
{
  "qualityHoldRemoved": true,
  "purposePermission": "UNKNOWN",
  "assessmentCurrent": "UNKNOWN"
}
```

步骤：

1. Resolve GAP_ELIMINATED

必须得到：

```json
{
  "Resolution": null,
  "PurposeAutoGranted": false,
  "technicalOrPurposeBypass": false
}
```

完整错误：

```json
{
  "type": "plan-exc.problem/1-proposed",
  "status": 422,
  "code": "GAP_NOT_ELIMINATED",
  "messageKey": "GAP_NOT_ELIMINATED",
  "correlationId": "db748d0c-846d-524f-97bc-e67c3ee3f546",
  "operationId": null,
  "retryMode": "NONE",
  "fields": [],
  "reasonCodes": [
    "GAP_NOT_ELIMINATED"
  ],
  "requiredOwner": null,
  "currentVisibleRevision": null
}
```

## EXC-AC-047｜忽略只告警轴

原SPEC：SPEC-PLAN-EXC-01-04；API：EX13；原JSON：`/criteria/46`。

前提：

```json
{
  "caseRevision": 1,
  "lifecycle": "OPEN",
  "shortage": "20.00000000",
  "alertPolicy": {
    "file": "CP03_NATIVE_REGISTRY.json",
    "jsonPointer": "/documentsBySemanticId/F097"
  },
  "now": "2026-10-04T18:10:00.500Z",
  "until": "2026-10-04T18:10:00.900Z",
  "exactRequest": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/ALERT_DISMISS/request"
  },
  "exactBefore": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/ALERT_DISMISS/before"
  }
}
```

步骤：

1. Dismiss with policy and exact current

必须得到：

```json
{
  "httpStatus": 201,
  "newRevision": 2,
  "alert": "DISMISSED",
  "lifecycle": "OPEN",
  "shortage": "20.00000000",
  "ResolvedAt": null,
  "OwnerObligations": "unchanged",
  "exactOriginalResult": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/ALERT_DISMISS/recordedResult"
  },
  "fullBeforeOrderedWritesAfter": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/ALERT_DISMISS"
  }
}
```

## EXC-AC-048｜责任终止全路径证据

原SPEC：SPEC-PLAN-EXC-01-04；API：EX12；原JSON：`/criteria/47`。

前提：

```json
{
  "DemandCurrent": "RETIRED range[30,60)",
  "allEntryPaths": "forbidden and no pending mayexecute",
  "BStillActive": 40,
  "OwnerIndependentResidualReceipt": "PUR-PO exact SETTLED target[30,60), remaining0; actual sharedPO100 and A30/B40 retained",
  "caseRevision": 2,
  "exactRequest": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/SHARED_RESOLVE/request"
  },
  "exactBeforeHead": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/SHARED_RESOLVE/fullBeforeCaseHead"
  }
}
```

步骤：

1. Resolve RESPONSIBILITY_RETIRED

必须得到：

```json
{
  "AClosure": "present",
  "BActive": 40,
  "PODeleted": false,
  "residualOwnerReceipt": "retained",
  "externalBusinessWrites": 0,
  "newRevision": 3,
  "exactImmutableRecordedResult": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/SHARED_RESOLVE/recordedResult"
  },
  "fullBeforeAndAfter": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/SHARED_RESOLVE"
  },
  "httpStatus": 201
}
```

## EXC-AC-049｜无policy不能忽略

原SPEC：SPEC-PLAN-EXC-01-04；API：EX13；原JSON：`/criteria/48`。

前提：

```json
{
  "AlertPolicyFact": null,
  "userReason": "just ignore"
}
```

步骤：

1. Dismiss

必须得到：

```json
{
  "Dismissal": null,
  "alert": "VISIBLE",
  "sourceHead": "unchanged"
}
```

完整错误：

```json
{
  "type": "plan-exc.problem/1-proposed",
  "status": 503,
  "code": "ALERT_POLICY_REQUIRED_FENCED",
  "messageKey": "ALERT_POLICY_REQUIRED_FENCED",
  "correlationId": "108ec9b4-9096-5466-879b-288d9e136c48",
  "operationId": null,
  "retryMode": "NONE",
  "fields": [],
  "reasonCodes": [
    "ALERT_POLICY_REQUIRED_FENCED"
  ],
  "requiredOwner": null,
  "currentVisibleRevision": null
}
```

## EXC-AC-050｜到期无需worker才可见

原SPEC：SPEC-PLAN-EXC-01-04；API：EX03, EX14；原JSON：`/criteria/49`。

前提：

```json
{
  "DismissalUntil": "2026-10-04T18:10:00.900Z",
  "now": "2026-10-04T18:10:00.901Z",
  "expiryWorkerHasRun": false
}
```

步骤：

1. 读取并随后恢复

必须得到：

```json
{
  "effectiveVisible": true,
  "displayAlert": "EXPIRED_DISMISSAL",
  "oldDismissal": "immutable",
  "ExpiryOrRestoreWinner": "one CAS",
  "resolved": false
}
```

## EXC-AC-051｜Resolve与source变化竞争

原SPEC：SPEC-PLAN-EXC-01-04；API：EX12, M01；原JSON：`/criteria/50`。

前提：

```json
{
  "expectedRevision": 5,
  "sourceChangeCommittedRevision": 6,
  "oldShortage": 0,
  "newShortage": 10
}
```

步骤：

1. 提交旧Resolve expected5

必须得到：

```json
{
  "Resolution": null,
  "headRevision": 6,
  "shortage": 10,
  "oldSlotOutcome": "REJECTED"
}
```

完整错误：

```json
{
  "type": "plan-exc.problem/1-proposed",
  "status": 412,
  "code": "STALE_HEAD",
  "messageKey": "STALE_HEAD",
  "correlationId": "83c3f459-a3d0-55d7-9343-151bd7f6f80b",
  "operationId": null,
  "retryMode": "READ_NEW_PREDECESSOR",
  "fields": [],
  "reasonCodes": [
    "STALE_HEAD"
  ],
  "requiredOwner": null,
  "currentVisibleRevision": null
}
```

## EXC-AC-052｜同slot同body不可变重放

原SPEC：SPEC-PLAN-EXC-01-04；API：EX12, EX07；原JSON：`/criteria/51`。

前提：

```json
{
  "currentHeadLater": 7
}
```

步骤：

1. 同K-RES/H1重放

必须得到：

```json
{
  "returnedReceipt": "exact original immutable body bytes; original recordedAt/appliedRevision retained",
  "resultRecordedAt": "original",
  "newResolutionRows": 0,
  "currentHead": 7,
  "replayHeader": true,
  "httpReplayStatus": 200,
  "originalRecordedStatus": 201,
  "newEffects": 0,
  "returnedOriginalBody": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/RESOLVE_AFTER_M04_AND_EPOCH9/recordedResult"
  }
}
```

## EXC-AC-053｜同slot异body冲突

原SPEC：SPEC-PLAN-EXC-01-04；API：EX12；原JSON：`/criteria/52`。

前提：

```json
{
  "oldBody": "evidenceA",
  "newBody": "evidenceB"
}
```

步骤：

1. 同key提交改变证据body

必须得到：

```json
{
  "originalRequest": "unchanged",
  "newDomainEffectRows": 0,
  "conflictAudit": "added"
}
```

完整错误：

```json
{
  "type": "plan-exc.problem/1-proposed",
  "status": 409,
  "code": "IDEMPOTENCY_BODY_CONFLICT",
  "messageKey": "IDEMPOTENCY_BODY_CONFLICT",
  "correlationId": "7c720e33-ff92-5fcd-94db-bd9d896b701b",
  "operationId": null,
  "retryMode": "NONE",
  "fields": [],
  "reasonCodes": [
    "IDEMPOTENCY_BODY_CONFLICT"
  ],
  "requiredOwner": null,
  "currentVisibleRevision": null
}
```

## EXC-AC-054｜撤权后replay不重执行业务

原SPEC：SPEC-PLAN-EXC-01-04；API：EX12, EX07；原JSON：`/criteria/53`。

前提：

```json
{
  "priorResolution": "R1 applied",
  "todayResolveOrReadGrant": "DENIED"
}
```

步骤：

1. 重放/读取原结果

必须得到：

```json
{
  "R1History": "retained",
  "newResolution": 0,
  "protectedFieldsDisclosed": 0
}
```

完整错误：

```json
{
  "type": "plan-exc.problem/1-proposed",
  "status": 403,
  "code": "PURPOSE_NOT_AUTHORIZED",
  "messageKey": "PURPOSE_NOT_AUTHORIZED",
  "correlationId": "f377a4a4-a56b-5c9d-aee8-c9c62ee96301",
  "operationId": null,
  "retryMode": "NONE",
  "fields": [],
  "reasonCodes": [
    "PURPOSE_NOT_AUTHORIZED"
  ],
  "requiredOwner": null,
  "currentVisibleRevision": null
}
```

## EXC-AC-055｜事务中途失败全本域回滚

原SPEC：SPEC-PLAN-EXC-01-04；API：EX12；原JSON：`/criteria/54`。

前提：

```json
{
  "allResolveGates": "passed",
  "failurePoint": "after Resolution insert before CaseHead",
  "outerCommit": "confirmed rollback"
}
```

步骤：

1. 未来实现故障注入目标（本任务NOT_RUN）

必须得到：

```json
{
  "ResolutionPersisted": 0,
  "ClosurePersisted": 0,
  "CaseRevisionPersisted": 0,
  "Head": "old exact tuple",
  "AuditSuccess": 0,
  "OutboxSuccess": 0
}
```

## EXC-AC-056｜提交确认未知先查原

原SPEC：SPEC-PLAN-EXC-01-04；API：EX12, EX07；原JSON：`/criteria/55`。

前提：

```json
{
  "outerCommit": "UNKNOWN",
  "requestKey": "K1"
}
```

步骤：

1. 收到LOCAL_COMMIT_UNKNOWN再GET原slot

必须得到：

```json
{
  "firstResponse": 503,
  "retryMode": "QUERY_ORIGINAL",
  "automaticNewKey": false,
  "resolutionCountAfterConfirmedQuery": "at most1",
  "unknownNotAssumedRollback": true
}
```

## EXC-AC-057｜Owner成功响应丢失恢复

原SPEC：SPEC-PLAN-EXC-01-04；API：EX11, M02, EX16；原JSON：`/criteria/56`。

前提：

```json
{
  "OwnerActuallyAccepted": "OR1",
  "localReceipt": "not captured",
  "originalSlot": "OWNER/K1",
  "dispatch": "MAY_HAVE_BEEN_SENT"
}
```

步骤：

1. EX16 QUERY_ORIGINAL然后接收真实OR1

必须得到：

```json
{
  "sameOriginalKey": "OWNER/K1",
  "newOwnerCreateOrHandoff": 0,
  "localOwnerReceiptCount": 1,
  "acceptedRange": "original only",
  "originalOwnerTruthRetained": true
}
```

## EXC-AC-058｜冷恢复保原body/actor/tenant

原SPEC：SPEC-PLAN-EXC-01-04；API：EX16；原JSON：`/criteria/57`。

前提：

```json
{
  "epochBefore": 8,
  "epochAfter": 9,
  "exactOriginalBody": {
    "file": "CP03_NATIVE_REGISTRY.json",
    "jsonPointer": "/documentsBySemanticId/COORD_REQUEST_NEW"
  },
  "exactOriginalClientSlot": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/RECALC_REGISTER/businessSlot"
  },
  "observationOrdinalBeforeFound": 0,
  "nextActionOrdinalBeforeFound": 1
}
```

步骤：

1. 准确takeover后原query

必须得到：

```json
{
  "newCheckpointEpoch": 9,
  "originalUserActor": {
    "file": "CP03_NATIVE_REGISTRY.json",
    "jsonPointer": "/documentsBySemanticId/F017"
  },
  "newHolderServicePrincipal": {
    "file": "CP03_NATIVE_REGISTRY.json",
    "jsonPointer": "/documentsBySemanticId/CORE_HOLDER9"
  },
  "originalOwnerRequestUnchanged": {
    "file": "CP03_NATIVE_REGISTRY.json",
    "jsonPointer": "/documentsBySemanticId/COORD_REQUEST_NEW"
  },
  "lastObservationOrdinalAfterFound": 1,
  "nextActionOrdinalAfterFound": 2,
  "oldWorkerDomainWrites": 0,
  "newOwnerRequest": 0
}
```

## EXC-AC-059｜解除一个告警不改业务

原SPEC：SPEC-PLAN-EXC-01-04；API：EX14；原JSON：`/criteria/58`。

前提：

```json
{
  "alert": "DISMISSED",
  "shortage": 20,
  "lifecycle": "OPEN",
  "requiredObligation": "O1 OPEN",
  "caseRevision": 2,
  "exactRequest": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/ALERT_RESTORE/request"
  },
  "exactBeforeHead": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/ALERT_RESTORE/before"
  }
}
```

步骤：

1. RestoreAlert

必须得到：

```json
{
  "alert": "VISIBLE",
  "shortage": 20,
  "lifecycle": "OPEN",
  "O1": "OPEN",
  "oldDismissalHistory": "retained",
  "newRevision": 3,
  "exactImmutableRecordedResult": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/ALERT_RESTORE/recordedResult"
  },
  "fullBeforeAndAfter": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/ALERT_RESTORE"
  },
  "httpStatus": 201
}
```

## EXC-AC-060｜已关闭后correction新episode

原SPEC：SPEC-PLAN-EXC-01-04；API：M01, EX03；原JSON：`/criteria/59`。

前提：

```json
{
  "oldEpisode": "E1 CLOSED ClosureC1",
  "newOwnerCorrection": "shortage10 actual"
}
```

步骤：

1. 接收准确correction

必须得到：

```json
{
  "oldClosure": "C1 immutable",
  "newEpisode": "E2",
  "priorClosureRef": "C1",
  "lifecycle": "REVIEW_REQUIRED",
  "alert": "VISIBLE",
  "shortage": 10,
  "oldResolvedAt": "unchanged"
}
```

## EXC-AC-061｜NEW_FACTS首请求无未来ID

原SPEC：SPEC-PLAN-EXC-01-05；API：EX15；原JSON：`/criteria/60`。

前提：

```json
{
  "currentImpact": "I1 complete independent",
  "changeFact": "WMS receive20",
  "oldRun": "RUN1",
  "expectedEffectiveSet": "ES1"
}
```

步骤：

1. 登记NewFacts RecalcCommand

必须得到：

```json
{
  "Work": "RW1 REGISTERED",
  "requestSlot": "K-MRP1",
  "RunRef": null,
  "InputSetRef": null,
  "MRPRequestOutboxCount": 1,
  "oldES1": "unchanged"
}
```

## EXC-AC-062｜准备/封装/Run逐真产物

原SPEC：SPEC-PLAN-EXC-01-05；API：M03；原JSON：`/criteria/61`。

前提：

```json
{
  "receipts": [
    "ACCEPTED prepP2",
    "INPUT_SEALED I2",
    "RUN_REQUESTED RUN2 requestK2"
  ]
}
```

步骤：

1. 顺序摄取MRP coordination receipts

必须得到：

```json
{
  "prepRef": "P2",
  "inputSetRef": "I2",
  "runRef": "RUN2",
  "I1Members": "unchanged",
  "futureAttempt": null,
  "fakeInitialRUN2": false
}
```

## EXC-AC-063｜单Attempt完整ResultSet

原SPEC：SPEC-PLAN-EXC-01-05；API：M03；原JSON：`/criteria/62`。

前提：

```json
{
  "RUN2": "AT2 completed sealed",
  "ResultSet": "RS2",
  "memberSets": [
    "net",
    "suggestions",
    "materials",
    "pegging",
    "exceptions",
    "exit",
    "retained"
  ]
}
```

步骤：

1. 接收RESULT_SEALED

必须得到：

```json
{
  "ResultSet": "RS2",
  "Attempt": "AT2",
  "stage": "RESULT_SEALED",
  "Selection": null,
  "Publication": null,
  "MRPEffectiveSetChanged": false
}
```

## EXC-AC-064｜不可拼两Attempt片段

原SPEC：SPEC-PLAN-EXC-01-05；API：M03；原JSON：`/criteria/63`。

前提：

```json
{
  "netFrom": "AT2",
  "peggingFrom": "AT3",
  "resultSetClaim": "RS2 complete"
}
```

步骤：

1. 摄取声明完整result

必须得到：

```json
{
  "ReceiptApplied": false,
  "WorkPriorStage": "unchanged",
  "oldEffectiveSet": "ES1",
  "mixedResultSelected": false
}
```

完整错误：

```json
{
  "type": "plan-exc.problem/1-proposed",
  "status": 422,
  "code": "RESULTSET_INCOMPLETE",
  "messageKey": "RESULTSET_INCOMPLETE",
  "correlationId": "3a669490-721d-50ec-8399-120619fc19bc",
  "operationId": null,
  "retryMode": "NONE",
  "fields": [],
  "reasonCodes": [
    "RESULTSET_INCOMPLETE"
  ],
  "requiredOwner": null,
  "currentVisibleRevision": null
}
```

## EXC-AC-065｜选择不等正式发布

原SPEC：SPEC-PLAN-EXC-01-05；API：M03, EX08；原JSON：`/criteria/64`。

前提：

```json
{
  "receiptStage": "SELECTED",
  "selection": "SEL2",
  "result": "RS2",
  "Publication": null
}
```

步骤：

1. 读取recalc progress

必须得到：

```json
{
  "stage": "SELECTED",
  "publicationRef": null,
  "effectiveSet": "ES1",
  "CaseClosed": false
}
```

## EXC-AC-066｜发布到EXC消费完整链

原SPEC：SPEC-PLAN-EXC-01-05；API：M03, EX03；原JSON：`/criteria/65`。

前提：

```json
{
  "PUB2": "RS2/AT2/I2",
  "previousEffective": "ES1",
  "actualRetained": [
    "FIRM-B"
  ],
  "actualExit": [
    "OLD-A-SUG"
  ]
}
```

步骤：

1. 摄取PUBLISHED并同本地消费

必须得到：

```json
{
  "MrpReceipt": "PUB2 retained",
  "ResultConsumptionCount": 1,
  "newAssessment": "A2",
  "oldAssessment": "A1 retained",
  "retainedFirmB": "unchanged",
  "CaseAutoClosed": false
}
```

## EXC-AC-067｜新Run失败零旧有效变化

原SPEC：SPEC-PLAN-EXC-01-05；API：M03；原JSON：`/criteria/66`。

前提：

```json
{
  "oldEffective": "ES1",
  "oldFirm": "F1",
  "oldPegging": [
    "PG1",
    "PG2"
  ],
  "RUN2": "FAILED",
  "failedInput": "I2"
}
```

步骤：

1. 摄取ATTEMPT_FAILED

必须得到：

```json
{
  "oldEffective": "ES1",
  "F1": "unchanged",
  "PG1/PG2": "unchanged",
  "newPublication": null,
  "failureHistory": "recorded",
  "EXCWorkStage": "FAILED"
}
```

## EXC-AC-068｜空建议仍保责任60

原SPEC：SPEC-PLAN-EXC-01-05；API：M03；原JSON：`/criteria/67`。

前提：

```json
{
  "demand": 60,
  "existingQualifiedSupply": 60,
  "suggestions": [],
  "net": 0,
  "pegging": 60
}
```

步骤：

1. 接收完整零新增ResultSet

必须得到：

```json
{
  "demandMember": 60,
  "netMember": 0,
  "peggingMember": 60,
  "suggestionCount": 0,
  "resultNotEmpty": true,
  "clearAllDemandOrCoverage": false
}
```

## EXC-AC-069｜真空结果准确退出保Firm

原SPEC：SPEC-PLAN-EXC-01-05；API：M03；原JSON：`/criteria/68`。

前提：

```json
{
  "formalDemandSet": [],
  "oldOrdinaryExit": [
    "S1"
  ],
  "FirmMaterialObligation": "settled/protected accordingOwner",
  "retainedFirm": [
    "F1"
  ],
  "unknownInFlight": []
}
```

步骤：

1. 接收LEGITIMATE_EMPTY_WITH_EXACT_EXIT

必须得到：

```json
{
  "metadataInputResult": "present",
  "exit": [
    "S1"
  ],
  "retained": [
    "F1"
  ],
  "globalDelete": false,
  "unrelatedDemand": "unchanged"
}
```

## EXC-AC-070｜publish未知只原query

原SPEC：SPEC-PLAN-EXC-01-05；API：M03, EX16；原JSON：`/criteria/69`。

前提：

```json
{
  "selected": "SEL2",
  "publishSlot": "PK2",
  "response": "timeout",
  "actualPublication": "not known"
}
```

步骤：

1. 保存PUBLICATION_UNKNOWN再Resume QUERY_ORIGINAL

必须得到：

```json
{
  "publicationRef": null,
  "oldEffective": "ES1",
  "samePublishSlot": "PK2",
  "newPublishKey": null,
  "CaseClosed": false
}
```

## EXC-AC-071｜同InputSet技术恢复需隔离

原SPEC：SPEC-PLAN-EXC-01-05；API：EX15；原JSON：`/criteria/70`。

前提：

```json
{
  "Run": {
    "file": "CP03_NATIVE_REGISTRY.json",
    "jsonPointer": "/documentsBySemanticId/F130"
  },
  "InputSet": {
    "file": "CP03_NATIVE_REGISTRY.json",
    "jsonPointer": "/documentsBySemanticId/F022"
  },
  "algorithm": {
    "file": "CP03_NATIVE_REGISTRY.json",
    "jsonPointer": "/documentsBySemanticId/F025"
  },
  "Attempt": {
    "file": "CP03_NATIVE_REGISTRY.json",
    "jsonPointer": "/documentsBySemanticId/F131"
  },
  "MRPIsolationProof": "IF1 generation2"
}
```

步骤：

1. SAME_INPUT_RECOVERY登记后摄取AT2

必须得到：

```json
{
  "Run": {
    "file": "CP03_NATIVE_REGISTRY.json",
    "jsonPointer": "/documentsBySemanticId/F130"
  },
  "InputSet": {
    "file": "CP03_NATIVE_REGISTRY.json",
    "jsonPointer": "/documentsBySemanticId/F022"
  },
  "algorithm": "ALG1",
  "Attempt": {
    "file": "CP03_NATIVE_REGISTRY.json",
    "jsonPointer": "/documentsBySemanticId/F134"
  },
  "generation": 2,
  "AT1History": "failed retained",
  "newRunCreated": 0,
  "newInputSet": null,
  "newRun": null,
  "publication": null
}
```

## EXC-AC-072｜新供给版不是同输入恢复

原SPEC：SPEC-PLAN-EXC-01-05；API：EX15；原JSON：`/criteria/71`。

前提：

```json
{
  "originalInput": "I1 supplyV1",
  "todaySupply": "V2",
  "mode": "SAME_INPUT_RECOVERY"
}
```

步骤：

1. 提交恢复

必须得到：

```json
{
  "originalI1": "immutable",
  "sameInputRecoveryDispatched": 0,
  "permittedPath": "NEW_FACTS new I2/newRun"
}
```

完整错误：

```json
{
  "type": "plan-exc.problem/1-proposed",
  "status": 422,
  "code": "RECOVERY_INPUT_CHANGED",
  "messageKey": "RECOVERY_INPUT_CHANGED",
  "correlationId": "c39b1085-7a8a-5864-801a-94d28f7888cf",
  "operationId": null,
  "retryMode": "NONE",
  "fields": [],
  "reasonCodes": [
    "RECOVERY_INPUT_CHANGED"
  ],
  "requiredOwner": null,
  "currentVisibleRevision": null
}
```

## EXC-AC-073｜未决部分不重新转源业务

原SPEC：SPEC-PLAN-EXC-01-05；API：EX15, EX16；原JSON：`/criteria/72`。

前提：

```json
{
  "POConversionPortions": {
    "acceptedPR": 70,
    "unknownWO": 50
  },
  "total": 120,
  "MRPRecalcRequested": true
}
```

步骤：

1. 重算/投影修复读取原责任

必须得到：

```json
{
  "acceptedPR70": "retained",
  "WOUnknown50": "protected",
  "newPRRows": 0,
  "newWORows": 0,
  "newTransferRequestRows": 0,
  "unknownReturned50": false
}
```

## EXC-AC-074｜取消/发布真实winner

原SPEC：SPEC-PLAN-EXC-01-05；API：M03；原JSON：`/criteria/73`。

前提：

```json
{
  "orderA": "Cancel wins before publish",
  "orderB": "Publish wins before cancel"
}
```

步骤：

1. 分别摄取两合法Owner控制receipt

必须得到：

```json
{
  "A": {
    "cancelled": true,
    "selectPublishAllowed": false,
    "lateAttemptDiagnosticRetained": true
  },
  "B": {
    "Publication": "PUB1",
    "cancelOutcome": "ALREADY_PUBLISHED",
    "deletePub": false
  }
}
```

## EXC-AC-075｜旧Attempt晚到不得影响选择

原SPEC：SPEC-PLAN-EXC-01-05；API：M03；原JSON：`/criteria/74`。

前提：

```json
{
  "currentGeneration": 2,
  "oldAttemptGeneration": 1,
  "oldAttemptLateOutput": "complete"
}
```

步骤：

1. 摄取晚到diagnostic

必须得到：

```json
{
  "oldOutputHistory": "retained",
  "selectedResultOverwritten": false,
  "effectiveSetOverwritten": false,
  "domainSelectionEffect": 0
}
```

## EXC-AC-076｜MRP成功EXC投影失败可修

原SPEC：SPEC-PLAN-EXC-01-05；API：M03, EX17；原JSON：`/criteria/75`。

前提：

```json
{
  "MRPPublication": "PUB2 committed",
  "localProjection": "P1 old",
  "localFailureAfterReceipt": "projection not committed"
}
```

步骤：

1. 保WAITING_LOCAL_APPLY，固定RepairPlan再EX17

必须得到：

```json
{
  "PUB2": "unchanged",
  "ResultConsumptionCount": 1,
  "newMRPPublish": 0,
  "sourceBusinessRequest": 0,
  "newResultConsumption": 0
}
```

## EXC-AC-077｜撤回后晚到真实winner保留

原SPEC：SPEC-PLAN-EXC-01-05；API：M01, M02, M03；原JSON：`/criteria/76`。

前提：

```json
{
  "sourceV2": "withdrawn",
  "originalV1OwnerResult": "real accepted OR1",
  "OccurredBeforeStop": true
}
```

步骤：

1. 后收到真实OR1及MRP当前影响

必须得到：

```json
{
  "OR1History": "accepted immutable",
  "sourceV2": "withdrawn remains",
  "ownerActualObject": "retained",
  "newEpisode": "REVIEW_REQUIRED",
  "restoreCancelledDemand": false
}
```

## EXC-AC-078｜epoch后完整Head tuple

原SPEC：SPEC-PLAN-EXC-01-05；API：EX16, M03；原JSON：`/criteria/77`。

前提：

```json
{
  "newLeaseEpoch": 9,
  "newNonterminalReceipt": "R4"
}
```

步骤：

1. consume pending observation新Revision4

必须得到：

```json
{
  "oldRevision3Epoch8": "immutable",
  "fullCompositeFK": [
    "EnvironmentId",
    "TenantId",
    "CaseId",
    "CaseDigest",
    "RevisionId",
    "RevisionDigest",
    "BusinessRevision",
    "WriterName",
    "WriterEpoch",
    "LocalCommitSeq"
  ]
}
```

## EXC-AC-079｜冷恢复原slot/body不换键

原SPEC：SPEC-PLAN-EXC-01-05；API：EX16；原JSON：`/criteria/78`。

前提：

```json
{
  "originalOwnerRequest": {
    "file": "CP03_NATIVE_REGISTRY.json",
    "jsonPointer": "/documentsBySemanticId/COORD_REQUEST_NEW"
  },
  "dispatch": "MAY_HAVE_BEEN_SENT",
  "ownerPureLookup": "NOT_OBSERVED"
}
```

步骤：

1. 重启扫描，先原query

必须得到：

```json
{
  "originalRequestUnchanged": {
    "file": "CP03_NATIVE_REGISTRY.json",
    "jsonPointer": "/documentsBySemanticId/COORD_REQUEST_NEW"
  },
  "status": "OWNER_UNKNOWN",
  "newRunKey": null,
  "newOwnerCreate": 0,
  "noEffectInferred": false
}
```

## EXC-AC-080｜新EXC合同不借PO采用

原SPEC：SPEC-PLAN-EXC-01-05；API：EX09, EX11, EX15；原JSON：`/criteria/79`。

前提：

```json
{
  "POCP22DesignAccepted": true,
  "PO17DesignAdopted": true,
  "EXC_PC04_RuntimePin": null,
  "CoreEXCProviderGrant": null
}
```

步骤：

1. 尝试EXC新协调动作

必须得到：

```json
{
  "requiredFenced": true,
  "MRPDispatch": 0,
  "grantCreated": 0,
  "registryScoreIncrement": 0,
  "BusinessACStatus": "NOT_RUN"
}
```

完整错误：

```json
{
  "type": "plan-exc.problem/1-proposed",
  "status": 503,
  "code": "CORE_AUTHORIZATION_REQUIRED_FENCED",
  "messageKey": "CORE_AUTHORIZATION_REQUIRED_FENCED",
  "correlationId": "780cba21-22d4-5a41-a73e-db3202379775",
  "operationId": null,
  "retryMode": "NONE",
  "fields": [],
  "reasonCodes": [
    "CORE_AUTHORIZATION_REQUIRED_FENCED"
  ],
  "requiredOwner": null,
  "currentVisibleRevision": null
}
```

## EXC-AC-081｜父Case可读而source/impact/history目的拒绝

原SPEC：SPEC-PLAN-EXC-01-01；API：EX03, EX04, EX05, EX06；原JSON：`/criteria/80`。

前提：

```json
{
  "grants": {
    "plan-exc.read": "GRANTED",
    "source/impact/history": "DENIED"
  }
}
```

步骤：

1. Read exact declared original/typed input and full predecessor; apply only this AC’s specified alternate in a future authorized implementation; assert every explicit expected field, relation, error and prohibited effect below

必须得到：

```json
{
  "CaseHttp": 200,
  "sourceHttp": 403,
  "impactHttp": 403,
  "historyHttp": 403,
  "privateBodiesChanged": 0,
  "originalKeyLeak": 0
}
```

## EXC-AC-082｜数量可见而原basis禁读

原SPEC：SPEC-PLAN-EXC-01-01；API：EX03；原JSON：`/criteria/81`。

前提：

```json
{
  "privateRequiredCell": {
    "state": "KNOWN",
    "value": "100.00000000",
    "uomRef": {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/F000"
    },
    "basisRef": {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/F012"
    },
    "reasonCodes": []
  },
  "quantityField": "GRANTED",
  "sourceBasisField": "DENIED"
}
```

步骤：

1. Read exact declared original/typed input and full predecessor; apply only this AC’s specified alternate in a future authorized implementation; assert every explicit expected field, relation, error and prohibited effect below

必须得到：

```json
{
  "publicRequired": {
    "visibility": "VISIBLE",
    "cell": {
      "state": "KNOWN",
      "value": "100.00000000",
      "uomCode": "EA",
      "basis": {
        "visibility": "REDACTED",
        "reasonCode": "FIELD_NOT_AUTHORIZED"
      },
      "reasonCodes": []
    }
  },
  "originalDemandKeyDisclosed": false,
  "privateBasisRetained": {
    "file": "CP03_NATIVE_REGISTRY.json",
    "jsonPointer": "/documentsBySemanticId/F012"
  }
}
```

## EXC-AC-083｜KPI完整分组身份及隐藏维度整组集遮蔽

原SPEC：SPEC-PLAN-EXC-01-01；API：EX02；原JSON：`/criteria/82`。

前提：

```json
{
  "groups": [
    {
      "item": "FG-A",
      "technical": "PRODUCTION-A",
      "site": "SITE-A",
      "purpose": "PRODUCTION",
      "owner": "SELF-A",
      "uom": "EA",
      "node": "2026-10-05T09:00:00Z",
      "view": "ACTUAL_QUALIFIED"
    },
    {
      "item": "FG-A",
      "technical": "PRODUCTION-A",
      "site": "SITE-A",
      "purpose": "PRODUCTION",
      "owner": "SELF-A",
      "uom": "KG",
      "node": "2026-10-05T09:00:00Z",
      "view": "ACTUAL_QUALIFIED"
    }
  ],
  "conversion": null
}
```

步骤：

1. Read exact declared original/typed input and full predecessor; apply only this AC’s specified alternate in a future authorized implementation; assert every explicit expected field, relation, error and prohibited effect below

必须得到：

```json
{
  "knownGroupCount": 2,
  "uniqueOpaqueGroupIdCount": 2,
  "crossUomTotal": null,
  "quantityTotalsIfDimensionForbidden": {
    "visibility": "REDACTED",
    "reasonCode": "FIELD_NOT_AUTHORIZED"
  },
  "hiddenGroupIds": null
}
```

## EXC-AC-084｜续页权限变化和目的降权拒绝

原SPEC：SPEC-PLAN-EXC-01-01；API：EX02；原JSON：`/criteria/83`。

前提：

```json
{
  "expiry": "2026-10-04T18:25:50Z",
  "now": "2026-10-04T18:10:51Z",
  "freshReadPurpose": "GRANTED",
  "freshSourcePurpose": "DENIED",
  "oldAuthoritySequence": 40,
  "newAuthoritySequence": 41,
  "signatureProviderQualification": "CONDITIONAL_DESIGN_WORLD_ONLY_ACTUAL_UNPROVEN"
}
```

步骤：

1. Read exact declared original/typed input and full predecessor; apply only this AC’s specified alternate in a future authorized implementation; assert every explicit expected field, relation, error and prohibited effect below

必须得到：

```json
{
  "rows": null,
  "totals": null,
  "cursorAdvanced": false,
  "newWrites": []
}
```

完整错误：

```json
{
  "type": "plan-exc.problem/1-proposed",
  "status": 412,
  "code": "CURSOR_PERMISSION_CHANGED",
  "messageKey": "CURSOR_PERMISSION_CHANGED",
  "correlationId": "57a08b18-0910-5006-885b-b4c1c18d6f47",
  "operationId": null,
  "retryMode": "READ_NEW_PREDECESSOR",
  "fields": [],
  "reasonCodes": [
    "CURSOR_PERMISSION_CHANGED"
  ],
  "requiredOwner": null,
  "currentVisibleRevision": null
}
```

## EXC-AC-085｜无Case旧行完整成功保真读

原SPEC：SPEC-PLAN-EXC-01-01；API：EX21, EX22；原JSON：`/criteria/84`。

前提：

```json
{
  "completeLegacySuccess": {
    "file": "CP03_AC_DIFFERENCE_SPECIMENS.json",
    "jsonPointer": "/specimens/LEGACY_EXACT_SUCCESS"
  },
  "originalScope": "OldMain157630594e3371fe181955d2f6227ff3b6962c84; originalTenantId and old8-byte rowVersion retained; no Case mapping",
  "reportedRequired": "25.00000000",
  "reportedAvailable": "0.00000000",
  "reportedStatus": "RESOLVED",
  "reportedResolvedAt": "2026-06-01T08:00:00Z"
}
```

步骤：

1. Read exact declared original/typed input and full predecessor; apply only this AC’s specified alternate in a future authorized implementation; assert every explicit expected field, relation, error and prohibited effect below

必须得到：

```json
{
  "httpStatus": 200,
  "exactFullBody": {
    "file": "CP03_AC_DIFFERENCE_SPECIMENS.json",
    "jsonPointer": "/specimens/LEGACY_EXACT_SUCCESS/body"
  },
  "visibilityEqualsActualNativeCoreFieldMask": true,
  "mappingState": "UNMAPPED",
  "mappingRevision": 0,
  "caseId": null,
  "episodeId": null,
  "newCaseRows": 0,
  "newEpisodeRows": 0,
  "newAssessmentRows": 0,
  "normalKpiIncluded": false,
  "globalStockZeroInferred": false
}
```

## EXC-AC-086｜旧映射竞争和Case无权分层

原SPEC：SPEC-PLAN-EXC-01-01；API：EX21；原JSON：`/criteria/85`。

前提：

```json
{
  "readCut17": "UNMAPPED",
  "laterIndependentMappingCut18": {
    "state": "MAPPED",
    "mappingRevision": 1
  },
  "currentRead17StillUnmapped": true,
  "unauthorizedCaseRef": {
    "visibility": "REDACTED",
    "reasonCode": "FIELD_NOT_AUTHORIZED"
  },
  "automaticMapping": false
}
```

步骤：

1. Read exact declared original/typed input and full predecessor; apply only this AC’s specified alternate in a future authorized implementation; assert every explicit expected field, relation, error and prohibited effect below

必须得到：

```json
{
  "readCut17": "UNMAPPED",
  "readCut18": "MAPPED",
  "unauthorizedCaseRef": {
    "visibility": "REDACTED",
    "reasonCode": "FIELD_NOT_AUTHORIZED"
  },
  "oldTenantRowversionRetained": true,
  "automaticMapping": false
}
```

## EXC-AC-087｜多Case导出完整槽/结果/文件关系

原SPEC：SPEC-PLAN-EXC-01-01；API：EX18_PREPARE, EX18, EX19, EX07_EXPORT_SLOT, EX07_EXPORT_SLOT_RESULT；原JSON：`/criteria/86`。

前提：

```json
{
  "completePreparation": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/MULTICASE_EXPORT_PREPARE"
  },
  "exactPrepareRequest": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/MULTICASE_EXPORT_PREPARE/request"
  },
  "completeOriginalCommand": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/MULTICASE_EXPORT_REGISTER/request"
  },
  "exactFixedMemberHeads": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/MULTICASE_EXPORT_PREPARE/exactPreparedFullMemberHeads"
  },
  "fixedReadCut": 113
}
```

步骤：

1. EX18_PREPARE with full two Case readcut22 immutable revisions and fresh export purpose/field grant
2. EX18 registers original stable bundle/export key in one exact full local UoW, original202 receipt bytes remain immutable
3. Asynchronous same original effect captures exact file, joins all full local rows and storegate CAS before READY
4. EX19 metadata/download reevaluates fresh grant; original result query separate EX07_EXPORT_SLOT, no re-generation on original query

必须得到：

```json
{
  "httpStatus": 202,
  "originalOutcome": "REGISTERED",
  "fullPrepareAfter": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/MULTICASE_EXPORT_PREPARE/after"
  },
  "fullRegistrationOrderedWrites": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/MULTICASE_EXPORT_REGISTER/conditionalWrites"
  },
  "fullRegistrationAfter": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/MULTICASE_EXPORT_REGISTER/after"
  },
  "exactOriginalRecordedResult": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/MULTICASE_EXPORT_REGISTER/firstResponse/body"
  },
  "exactOriginalResultUtf8": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/MULTICASE_EXPORT_REGISTER/firstResponse/rawUtf8"
  },
  "exactOriginalCsvUtf8": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/MULTICASE_EXPORT_CAPTURE_ORIGINAL_FILE/fullOriginalCsvUtf8"
  },
  "exactOriginalFileArtifact": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/MULTICASE_EXPORT_CAPTURE_ORIGINAL_FILE/originalFileArtifact"
  },
  "wholeFileCaptureBeforeWritesAfter": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/MULTICASE_EXPORT_CAPTURE_ORIGINAL_FILE"
  },
  "CaseOperationRows": 0,
  "CaseMutationRows": 0,
  "sourceOwnerWrites": 0,
  "dynamicReadyDoesNotRewriteOriginal202": true
}
```

## EXC-AC-088｜重排Case集合不换bundle原body重放

原SPEC：SPEC-PLAN-EXC-01-01；API：EX18_PREPARE, EX18, EX07_EXPORT_SLOT_RESULT；原JSON：`/criteria/87`。

前提：

```json
{
  "exactReorderedPrepareAndCanonicalCommand": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/MULTICASE_EXPORT_REPLAY_AND_DENIALS/sameSetReorderedPrepare"
  },
  "completeOriginalCommand": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/MULTICASE_EXPORT_REGISTER/request"
  },
  "originalFullSlot": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/MULTICASE_EXPORT_REGISTER/businessSlot"
  }
}
```

步骤：

1. Read exact declared original/typed input and full predecessor; apply only this AC’s specified alternate in a future authorized implementation; assert every explicit expected field, relation, error and prohibited effect below

必须得到：

```json
{
  "httpStatus": 200,
  "replayHeader": true,
  "sameBundle": true,
  "exactOriginalImmutableBody": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/MULTICASE_EXPORT_REPLAY_AND_DENIALS/originalResult"
  },
  "exactOriginalResultUtf8": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/MULTICASE_EXPORT_REPLAY_AND_DENIALS/rawOriginalResultUtf8"
  },
  "newFile": 0,
  "regeneration": 0,
  "caseMutation": 0
}
```

## EXC-AC-089｜同export槽改列/新Episode不换槽绕冲突

原SPEC：SPEC-PLAN-EXC-01-01；API：EX18；原JSON：`/criteria/88`。

前提：

```json
{
  "completeOriginalSlot": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/MULTICASE_EXPORT_REGISTER/businessSlot"
  },
  "sameSlotColumnsDifference": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/MULTICASE_EXPORT_REPLAY_AND_DENIALS/sameSlotChangedColumns/request"
  },
  "separateSameSlotEpisodeDifference": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/MULTICASE_EXPORT_REPLAY_AND_DENIALS/newEpisodeSameSlot"
  }
}
```

步骤：

1. Read exact declared original/typed input and full predecessor; apply only this AC’s specified alternate in a future authorized implementation; assert every explicit expected field, relation, error and prohibited effect below

必须得到：

```json
{
  "httpStatus": 409,
  "code": "IDEMPOTENCY_BODY_CONFLICT",
  "originalSlotAndResultUnchanged": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/MULTICASE_EXPORT_REPLAY_AND_DENIALS/sameSlotChangedColumns"
  },
  "sameStableBundleWhenOnlyEpisodeChanges": true,
  "episodeDoesNotCreateNewSlot": true,
  "newFile": 0,
  "newGeneration": 0
}
```

完整错误：

```json
{
  "type": "plan-exc.problem/1-proposed",
  "status": 409,
  "code": "IDEMPOTENCY_BODY_CONFLICT",
  "messageKey": "IDEMPOTENCY_BODY_CONFLICT",
  "correlationId": "4d338d62-3038-54e8-9704-4072d23eac31",
  "operationId": null,
  "retryMode": "NONE",
  "fields": [],
  "reasonCodes": [
    "IDEMPOTENCY_BODY_CONFLICT"
  ],
  "requiredOwner": null,
  "currentVisibleRevision": null
}
```

## EXC-AC-090｜导出撤权/expiry冷恢复原文件不重做

原SPEC：SPEC-PLAN-EXC-01-01；API：EX19_CONTENT, EX07_EXPORT_SLOT, EX07_EXPORT_SLOT_RESULT；原JSON：`/criteria/89`。

前提：

```json
{
  "completeOriginalFile": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/MULTICASE_EXPORT_CAPTURE_ORIGINAL_FILE/originalFileArtifact"
  },
  "completeOriginalSlot": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/MULTICASE_EXPORT_REGISTER/businessSlot"
  },
  "freshGrant": "DENIED",
  "separateExpiry": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/MULTICASE_EXPORT_REPLAY_AND_DENIALS/expiredAt"
  },
  "coldUnknownFileCapture": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/MULTICASE_EXPORT_REPLAY_AND_DENIALS/coldFileCaptureUnknown"
  }
}
```

步骤：

1. Read exact declared original/typed input and full predecessor; apply only this AC’s specified alternate in a future authorized implementation; assert every explicit expected field, relation, error and prohibited effect below

必须得到：

```json
{
  "revokedHttp": 403,
  "revokedCode": "PURPOSE_NOT_AUTHORIZED",
  "revokedFullOriginalRetention": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/MULTICASE_EXPORT_REPLAY_AND_DENIALS/readRevoked"
  },
  "expiryHttp": 410,
  "expiryCode": "EXPORT_EXPIRED",
  "expiryFullOriginalRetention": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/MULTICASE_EXPORT_REPLAY_AND_DENIALS/expiredRead"
  },
  "download": null,
  "newFile": 0,
  "regeneration": 0,
  "newCaptureKey": false,
  "coldNextAction": "QUERY_FILE_CAPTURE",
  "originalResultUnchanged": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/MULTICASE_EXPORT_REGISTER/originalFirstResult"
  }
}
```

完整错误：

```json
{
  "type": "plan-exc.problem/1-proposed",
  "status": 403,
  "code": "PURPOSE_NOT_AUTHORIZED",
  "messageKey": "PURPOSE_NOT_AUTHORIZED",
  "correlationId": "e4e869a5-9f11-5b67-bf2b-a51a81e55f43",
  "operationId": null,
  "retryMode": "NONE",
  "fields": [],
  "reasonCodes": [
    "PURPOSE_NOT_AUTHORIZED"
  ],
  "requiredOwner": null,
  "currentVisibleRevision": null
}
```

## EXC-AC-091｜M02/03真实epoch及重启序列不归零

原SPEC：SPEC-PLAN-EXC-01-02；API：M02, M03；原JSON：`/criteria/90`。

前提：

```json
{
  "completeM02OriginalEpochAndStream": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/M02_ORIGINAL_STREAM_BEGIN"
  },
  "completeM03OriginalEpochAndStream": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/M03_ORIGINAL_STREAM_BEGIN"
  },
  "M02Sequence1": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/M02_ORIGINAL_SEQUENCE1"
  },
  "M02Sequence2": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/M02_ORIGINAL_SEQUENCE2"
  },
  "M03Sequence1": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/M03_ORIGINAL_SEQUENCE1"
  },
  "M03Sequence2": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/M03_ORIGINAL_SEQUENCE2"
  }
}
```

步骤：

1. Read exact declared original/typed input and full predecessor; apply only this AC’s specified alternate in a future authorized implementation; assert every explicit expected field, relation, error and prohibited effect below

必须得到：

```json
{
  "producerIssuedEpochAnchorPreserved": true,
  "sequencesWithinOriginalEpoch": [
    1,
    2
  ],
  "restartResetsSequence": false,
  "newEpochComparedWithOldSequence": false,
  "receiverInventsEpoch": false,
  "lateCapturedSameReceiptBusinessReapply": 0
}
```

## EXC-AC-092｜M04完整stream持久等待重试

原SPEC：SPEC-PLAN-EXC-01-02；API：M04, M05；原JSON：`/criteria/91`。

前提：

```json
{
  "exactOriginalCurrentIntake": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/M04_RECEIVE_SEQUENCE2/request"
  },
  "exactOriginalAuthenticatedWrapperUtf8": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/M04_RECEIVE_SEQUENCE2/rawReceivedWrapperUtf8"
  },
  "exactOriginalNativeObservation": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/M04_RECEIVE_SEQUENCE2/completeOriginalNativeObservation"
  },
  "fullInitialBefore": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/M04_RECEIVE_SEQUENCE2/before"
  }
}
```

步骤：

1. M04 authenticates independently proposed CORE current observation original native body and all original Owner guarantee dependencies
2. Register exact stream/epoch anchor/previousSequence/previousDigest Inbox and full wrapper artifact with no fakeCase creation
3. Publish WAITING_LOCAL_APPLY or RETRYABLE_ERROR with original identity/dependency/nextAttempt and processingRevision durable; fresh claim scans/queries original
4. Full transition before/write/after states retained in packet; APPLIED/HISTORY_ONLY never second consume

必须得到：

```json
{
  "firstHttpStatus": 202,
  "firstResponse": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/M04_RECEIVE_SEQUENCE2/firstResponse"
  },
  "firstFullWritesAfter": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/M04_RECEIVE_SEQUENCE2"
  },
  "firstProcessingState": "WAITING_PREDECESSOR",
  "lateSequence1HistoryOnly": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/M04_LATE_SEQUENCE1_HISTORY_ONLY"
  },
  "retryableFailureFullBeforeWritesAfter": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/M04_LOCAL_APPLY_RETRYABLE_FAILURE"
  },
  "failedDomainUowRolledBack": true,
  "retrySameOriginalSequence2": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/M04_APPLY_ORIGINAL_SEQUENCE2"
  },
  "completeAppliedNativeReceipt": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/M04_APPLY_ORIGINAL_SEQUENCE2/fullAppliedNativeDocument"
  },
  "fullOriginalCase3ToCase4": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/M04_APPLY_ORIGINAL_SEQUENCE2"
  },
  "aliasFullBeforeWritesAfter": {
    "file": "CP03_LOCAL_UOW_LEDGER_INDEX.json",
    "jsonPointer": "/packets/M04_EXACT_NATIVE_ALIAS"
  },
  "aliasCaseWrites": 0,
  "originalFirst202Rewritten": false,
  "eventIdAsStreamKey": false,
  "samePositionDifferentSemantic": "409 SOURCE_VERSION_CONFLICT; original native/result retained"
}
```

## EXC-AC-093｜未发表真实progress未来refs明确null

原SPEC：SPEC-PLAN-EXC-01-05；API：EX07, EX08；原JSON：`/criteria/92`。

前提：

```json
{
  "progressRead": "GRANTED"
}
```

步骤：

1. Read exact declared original/typed input and full predecessor; apply only this AC’s specified alternate in a future authorized implementation; assert every explicit expected field, relation, error and prohibited effect below

必须得到：

```json
{
  "currentStage": "RUN_REQUESTED",
  "runRef": {
    "file": "CP03_NATIVE_REGISTRY.json",
    "jsonPointer": "/documentsBySemanticId/F034"
  },
  "inputSetRef": {
    "file": "CP03_NATIVE_REGISTRY.json",
    "jsonPointer": "/documentsBySemanticId/F035"
  },
  "attemptRef": null,
  "selectionRef": null,
  "publicationRef": null,
  "oldEffective": {
    "file": "CP03_NATIVE_REGISTRY.json",
    "jsonPointer": "/documentsBySemanticId/F032"
  },
  "elapsedTimeGuess": false
}
```

## EXC-AC-094｜EX10/17等待202一致

原SPEC：SPEC-PLAN-EXC-01-04；API：EX10, EX17；原JSON：`/criteria/93`。

前提：

```json
{
  "necessaryLocalDependencyWaiting": true,
  "businessApplyConfirmed": false
}
```

步骤：

1. Read exact declared original/typed input and full predecessor; apply only this AC’s specified alternate in a future authorized implementation; assert every explicit expected field, relation, error and prohibited effect below

必须得到：

```json
{
  "http": 202,
  "outcome": "REGISTERED",
  "currentStage": "WAITING_LOCAL_APPLY",
  "newClosure": null,
  "sameKeySecondSlot": false
}
```

## EXC-AC-095｜resolve current不授recalc资格

原SPEC：SPEC-PLAN-EXC-01-05；API：EX15；原JSON：`/criteria/94`。

前提：

```json
{
  "actualOtherwiseValidDifference": {
    "file": "CP03_AC_DIFFERENCE_SPECIMENS.json",
    "jsonPointer": "/specimens/EXC-AC-095"
  }
}
```

步骤：

1. Read exact declared original/typed input and full predecessor; apply only this AC’s specified alternate in a future authorized implementation; assert every explicit expected field, relation, error and prohibited effect below

必须得到：

```json
{
  "newWork": null,
  "newMRPOutbox": null,
  "CaseHeadChanged": false
}
```

完整错误：

```json
{
  "type": "plan-exc.problem/1-proposed",
  "status": 412,
  "code": "ACTION_CURRENT_MISMATCH",
  "messageKey": "ACTION_CURRENT_MISMATCH",
  "correlationId": "92b20ec6-314d-599f-b345-b31edd6ec017",
  "operationId": null,
  "retryMode": "READ_NEW_PREDECESSOR",
  "fields": [],
  "reasonCodes": [
    "ACTION_CURRENT_MISMATCH"
  ],
  "requiredOwner": null,
  "currentVisibleRevision": null
}
```

## EXC-AC-096｜新receipt20不能只保证旧stock80

原SPEC：SPEC-PLAN-EXC-01-05；API：EX15, EX12；原JSON：`/criteria/95`。

前提：

```json
{
  "actualOtherwiseValidDifference": {
    "file": "CP03_AC_DIFFERENCE_SPECIMENS.json",
    "jsonPointer": "/specimens/EXC-AC-096"
  }
}
```

步骤：

1. Read exact declared original/typed input and full predecessor; apply only this AC’s specified alternate in a future authorized implementation; assert every explicit expected field, relation, error and prohibited effect below

必须得到：

```json
{
  "missingRoot": {
    "file": "CP03_NATIVE_REGISTRY.json",
    "jsonPointer": "/documentsBySemanticId/F014"
  },
  "newMRPDispatch": 0,
  "Resolution": null,
  "precise100FromBadCutAccepted": false
}
```

完整错误：

```json
{
  "type": "plan-exc.problem/1-proposed",
  "status": 412,
  "code": "SOURCE_CURRENT_CHANGED",
  "messageKey": "SOURCE_CURRENT_CHANGED",
  "correlationId": "9b1afc6b-1112-51c1-935f-6733da5a0025",
  "operationId": null,
  "retryMode": "READ_NEW_PREDECESSOR",
  "fields": [],
  "reasonCodes": [
    "SOURCE_CURRENT_CHANGED"
  ],
  "requiredOwner": null,
  "currentVisibleRevision": null
}
```

## EXC-AC-097｜共享成员exact并拒六个不可达旧对象

原SPEC：SPEC-PLAN-EXC-01-03；API：EX04, EX05, EX10；原JSON：`/criteria/96`。

前提：

```json
{
  "completeSharedGraph": {
    "file": "CP03_PRIVATE_OBJECT_REGISTRY.json",
    "jsonPointer": "/objects/graph~1SHARED"
  },
  "completeNativeMemberDocuments": [
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/BASELINE_SCOPE_CLOSURE"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/CAPTURE_BASELINE_PLAN-MRP"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/CAPTURE_BASELINE_PLAN-POL"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/CAPTURE_BASELINE_PLAN-SUP"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/CAPTURE_SHARED_PLAN-MRP"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/CAPTURE_SHARED_PLAN-POL"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/CAPTURE_SHARED_PLAN-SUP"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/CORE_PLANNING_DELEGATION"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/CORE_PLANNING_PERMISSION"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/F000"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/F001"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/F002"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/F003"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/F004"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/F005"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/F006"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/F007"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/F008"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/F009"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/F010"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/F011"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/F012"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/F013"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/F015"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/F017"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/F018"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/F021"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/F022"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/F023"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/F024"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/F025"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/F026"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/F027"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/F028"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/F032"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/F033"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/F063"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/F064"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/F076"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/F101"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/F102"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/F103"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/F104"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/F105"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/F106"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/F107"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/F109"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/F110"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/F111"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/F112"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/F120"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/F121"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/F122"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/F125"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/F126"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/F127"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/HEAD_BASELINE_MRP_INPUT_CORE_CORE_PERMISSION"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/HEAD_BASELINE_MRP_INPUT_PLAN-MRP_MRP_EFFECTIVE_SET"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/HEAD_BASELINE_MRP_INPUT_PLAN-POL_POLICY"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/HEAD_BASELINE_MRP_INPUT_PLAN-SUP_PROTECTION"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/HEAD_BASELINE_MRP_INPUT_PLANNING-DEMAND_DEMAND"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/HEAD_BASELINE_MRP_INPUT_PLM_TECHNICAL"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/HEAD_BASELINE_MRP_INPUT_QUALITY_QUALITY"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/HEAD_BASELINE_MRP_INPUT_WMS-STOCK_SUPPLY"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/HEAD_SHARED_SOURCE_CORE_CORE_PERMISSION"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/HEAD_SHARED_SOURCE_PLAN-MRP_MRP_EFFECTIVE_SET"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/HEAD_SHARED_SOURCE_PLAN-POL_POLICY"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/HEAD_SHARED_SOURCE_PLAN-SUP_PROTECTION"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/HEAD_SHARED_SOURCE_PLANNING-DEMAND_DEMAND"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/HEAD_SHARED_SOURCE_PLM_TECHNICAL"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/HEAD_SHARED_SOURCE_PUR-PO_SUPPLY"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/MRP_EMPTY_BEFORE_RUN1"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/PAGE_BASELINE_PLAN-MRP"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/PAGE_BASELINE_PLAN-POL"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/PAGE_BASELINE_PLAN-SUP"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/PAGE_SHARED_PLAN-MRP"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/PAGE_SHARED_PLAN-POL"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/PAGE_SHARED_PLAN-SUP"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/QUERY_BASELINE_PLAN-MRP"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/QUERY_BASELINE_PLAN-POL"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/QUERY_BASELINE_PLAN-SUP"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/QUERY_SHARED_PLAN-MRP"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/QUERY_SHARED_PLAN-POL"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/QUERY_SHARED_PLAN-SUP"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/SUPPLY_PROJECTION_SHARED_0"
    }
  ],
  "completeCurrentSharedContributions": {
    "demands": {
      "file": "CP03_PRIVATE_OBJECT_REGISTRY.json",
      "jsonPointer": "/objects/graph~1SHARED/body/demands"
    },
    "supplies": {
      "file": "CP03_PRIVATE_OBJECT_REGISTRY.json",
      "jsonPointer": "/objects/graph~1SHARED/body/supplies"
    },
    "shares": {
      "file": "CP03_PRIVATE_OBJECT_REGISTRY.json",
      "jsonPointer": "/objects/graph~1SHARED/body/shares"
    }
  },
  "sixSeparatelyUnreachableExtraDocuments": [
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/F014"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/F016"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/F035"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/F037"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/F041"
    },
    {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/F045"
    }
  ],
  "extraVariantMeaning": "Append these six valid original native documents to this exact SHARED membership without a genuine dependency edge. They are not any of the reachable old historical ancestors."
}
```

步骤：

1. Read exact declared original/typed input and full predecessor; apply only this AC’s specified alternate in a future authorized implementation; assert every explicit expected field, relation, error and prohibited effect below

必须得到：

```json
{
  "exactNativeMemberCount": 85,
  "exactFullMembershipAndOriginalBytes": {
    "file": "CP03_PRIVATE_OBJECT_REGISTRY.json",
    "jsonPointer": "/objects/graph~1SHARED/body/memberRefs"
  },
  "currentAActive": "30.00000000",
  "currentARetired": "30.00000000",
  "currentB": "40.00000000",
  "actualPO": "100.00000000",
  "oldA100OrStock80UsedAsCurrentContribution": false,
  "reachableHistoricalAncestorsRetained": true,
  "sixUnreachableExtraMembersRejected": true,
  "extraVariantProblem": {
    "status": 422,
    "code": "LINEAGE_UNPROVEN"
  },
  "extraVariantCurrentHeadWrites": 0
}
```

## EXC-AC-098｜未知span量NULL与原coordinate80分列

原SPEC：SPEC-PLAN-EXC-01-02；API：EX04, EX09；原JSON：`/criteria/97`。

前提：

```json
{
  "sourceRange": {
    "from": "0.00000000",
    "to": "80.00000000",
    "quantity": "80.00000000",
    "uomRef": {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/F000"
    }
  },
  "coordinateQuantity": "80.00000000",
  "quantity": {
    "state": "UNKNOWN",
    "value": null,
    "uomRef": {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/F000"
    },
    "basisRef": {
      "file": "CP03_NATIVE_REGISTRY.json",
      "jsonPointer": "/documentsBySemanticId/F013"
    },
    "reasonCodes": [
      "CURRENT_QUANTITY_UNAVAILABLE"
    ]
  }
}
```

步骤：

1. Read exact declared original/typed input and full predecessor; apply only this AC’s specified alternate in a future authorized implementation; assert every explicit expected field, relation, error and prohibited effect below

必须得到：

```json
{
  "FromQty": "0.00000000",
  "ToQty": "80.00000000",
  "CoordinateQuantity": "80.00000000",
  "Quantity": null,
  "QuantityKnowledge": "UNKNOWN",
  "QuantityBasisFactDigest": "43707651a2889e9f532905837669db150e5eaa16b945786339c450e42b0e80ec",
  "coordinateAsKnownCoverage": null,
  "zeroSubstitute": false
}
```

## EXC-AC-099｜原Owner叶通用标签不能代完整body

原SPEC：SPEC-PLAN-EXC-01-02；API：EX09, M03；原JSON：`/criteria/98`。

前提：

```json
{
  "badRunBody": {
    "owner": "PLAN-MRP",
    "state": "CURRENT",
    "semanticRole": "RUN1"
  },
  "completeExactOriginalRun": {
    "file": "CP03_NATIVE_REGISTRY.json",
    "jsonPointer": "/documentsBySemanticId/F021"
  }
}
```

步骤：

1. Read exact declared original/typed input and full predecessor; apply only this AC’s specified alternate in a future authorized implementation; assert every explicit expected field, relation, error and prohibited effect below

必须得到：

```json
{
  "genericAccepted": false,
  "expectedProblem": {
    "status": 422,
    "code": "BODY_SCHEMA_INVALID"
  },
  "oldEffectiveRetained": {
    "file": "CP03_NATIVE_REGISTRY.json",
    "jsonPointer": "/documentsBySemanticId/F032"
  },
  "newSelection": null,
  "newPublication": null
}
```

完整错误：

```json
{
  "type": "plan-exc.problem/1-proposed",
  "status": 422,
  "code": "BODY_SCHEMA_INVALID",
  "messageKey": "BODY_SCHEMA_INVALID",
  "correlationId": "7ea4c2c4-2350-53d2-8568-7805fd0edd2e",
  "operationId": null,
  "retryMode": "NONE",
  "fields": [],
  "reasonCodes": [
    "BODY_SCHEMA_INVALID"
  ],
  "requiredOwner": null,
  "currentVisibleRevision": null
}
```

## EXC-AC-100｜确定用途Owner只准TRIAL不同于未知

原SPEC：SPEC-PLAN-EXC-01-01；API：EX03, EX09；原JSON：`/criteria/99`。

前提：

```json
{
  "fullTechnicalOwner": {
    "file": "CP03_NATIVE_REGISTRY.json",
    "jsonPointer": "/documentsBySemanticId/AC100_TRIAL_ONLY_TECH"
  },
  "trialQty": "50.00000000",
  "actualAllowedPurposes": [
    "TRIAL"
  ],
  "requestedPurpose": "PRODUCTION",
  "quality": "ACCEPTED",
  "allOtherRequiredSources": "COMPLETE"
}
```

步骤：

1. Read exact declared original/typed input and full predecessor; apply only this AC’s specified alternate in a future authorized implementation; assert every explicit expected field, relation, error and prohibited effect below

必须得到：

```json
{
  "condition": "NOT_SATISFIED",
  "coverage": "0.00000000",
  "knowledge": "COMPLETE",
  "outcome": "KNOWN_SHORTAGE",
  "shortage": "50.00000000",
  "newPurposeGrant": null
}
```

## EXC-AC-101｜原Owner audience拒不授新动作

原SPEC：SPEC-PLAN-EXC-01-05；API：EX09, M03；原JSON：`/criteria/100`。

前提：

```json
{
  "fullRunActualOwnerAudience": {
    "file": "CP03_NATIVE_REGISTRY.json",
    "jsonPointer": "/documentsBySemanticId/AC101_READ_ONLY_RUN"
  },
  "actualPlanExcAudiencePurposes": [
    "plan-exc.source.read"
  ],
  "newRequested": "plan-exc.recalc"
}
```

步骤：

1. Read exact declared original/typed input and full predecessor; apply only this AC’s specified alternate in a future authorized implementation; assert every explicit expected field, relation, error and prohibited effect below

必须得到：

```json
{
  "newDispatch": null,
  "newGrant": null,
  "originalHistoryRetained": true
}
```

完整错误：

```json
{
  "type": "plan-exc.problem/1-proposed",
  "status": 422,
  "code": "AUDIENCE_ACTION_DENIED",
  "messageKey": "AUDIENCE_ACTION_DENIED",
  "correlationId": "0bb80628-cf70-5bd9-8663-220338bd62e7",
  "operationId": null,
  "retryMode": "NONE",
  "fields": [],
  "reasonCodes": [
    "AUDIENCE_ACTION_DENIED"
  ],
  "requiredOwner": null,
  "currentVisibleRevision": null
}
```

## EXC-AC-102｜真实旧receipt与今天撤回分层

原SPEC：SPEC-PLAN-EXC-01-05；API：M03, EX16；原JSON：`/criteria/101`。

前提：

```json
{
  "todayControl": "WITHDRAWN",
  "oldIssuanceAuthorized": true,
  "freshHistoricalReceiverRead": "GRANTED"
}
```

步骤：

1. Read exact declared original/typed input and full predecessor; apply only this AC’s specified alternate in a future authorized implementation; assert every explicit expected field, relation, error and prohibited effect below

必须得到：

```json
{
  "originalImmutable": true,
  "DemandRevived": false,
  "newNativeSource": null,
  "currentLifecycle": "REVIEW_REQUIRED",
  "oldBodyUsedAsCurrent": false
}
```

## EXC-AC-103｜旧generation完整晚到仅diagnostic

原SPEC：SPEC-PLAN-EXC-01-05；API：M03；原JSON：`/criteria/102`。

前提：

```json
{
  "fullAllWriterIsolation": {
    "file": "CP03_NATIVE_REGISTRY.json",
    "jsonPointer": "/documentsBySemanticId/F098"
  },
  "fullG2Attempt": {
    "file": "CP03_NATIVE_REGISTRY.json",
    "jsonPointer": "/documentsBySemanticId/F134"
  },
  "fullLateOriginalG1": {
    "file": "CP03_NATIVE_REGISTRY.json",
    "jsonPointer": "/documentsBySemanticId/SAME_INPUT_LATE_G1_DIAGNOSTIC"
  },
  "currentGeneration": "generation-2",
  "lateGeneration": "generation-1",
  "allWriterPaths": [
    "CALCULATOR",
    "OUTPUT_SEALER",
    "SELECTOR",
    "PUBLISHER",
    "CANCEL_COORDINATOR",
    "RESTART_RECOVERY"
  ]
}
```

步骤：

1. Read exact declared original/typed input and full predecessor; apply only this AC’s specified alternate in a future authorized implementation; assert every explicit expected field, relation, error and prohibited effect below

必须得到：

```json
{
  "historyOnly": true,
  "newSealAddedMembers": [],
  "selectedOverwritten": false,
  "effectiveSetChanged": false
}
```

# Task E-T2 Report — i18n 五语 seed（serviceTask）

## 成果
- 新建 `CP6.WebApi/Seed/I18nOaServiceTaskScreenSeed.cs`（29 键，五语 ZhCN/ZhTW/En/Ja/Ko）。
- `CP6.WebApi/Program.cs` concat 链在 I18nOaApproverScreenSeed 之后加 `.Concat(I18nOaServiceTaskScreenSeed.Items)`；沿用既有 `.Where(!existingKeys)` + `.GroupBy().Select(First)` 幂等去重，未新增守卫。

## 语种集合
以 I18nOaApproverScreenSeed 实际字段为准：`ZhCN / ZhTW / En / Ja / Ko`（5 语）。

## 代码引用 vs seed 键 对账表
grep 权威源：`cp6.web/src/views/oa/designer`（ServiceTaskNode.vue / NodePropertyPanel.vue / EdgePropertyPanel.vue / designerModel.ts）。

| # | 键 | 代码引用位置 | seed | 家族 |
|---|----|----|----|----|
| 1 | oa.designer.svc.title | ServiceTaskNode:29, NodePropertyPanel:338 | ✅ | D-T2 |
| 2 | oa.designer.svc.kind.dataWriteback | ServiceTaskNode:28(动态), NodePropertyPanel:345 | ✅ | D-T2 |
| 3 | oa.designer.svc.kind.webApi | ServiceTaskNode:28(动态), NodePropertyPanel:346 | ✅ | D-T2 |
| 4 | oa.designer.svc.kind.timer | ServiceTaskNode:28(动态), NodePropertyPanel:347 | ✅ | D-T2 |
| 5 | oa.designer.svc.kind | NodePropertyPanel:343 | ✅ | D-T3 |
| 6 | oa.designer.svc.action | NodePropertyPanel:353 | ✅ | D-T3 |
| 7 | oa.designer.svc.mode | NodePropertyPanel:364,411 | ✅ | D-T3 |
| 8 | oa.designer.svc.mode.sync | NodePropertyPanel:366,413 | ✅ | D-T3 |
| 9 | oa.designer.svc.mode.async | NodePropertyPanel:367,414 | ✅ | D-T3 |
| 10 | oa.designer.svc.params | NodePropertyPanel:371,402 | ✅ | D-T3 |
| 11 | oa.designer.svc.paramsHint | NodePropertyPanel:376,407 | ✅ | D-T3 |
| 12 | oa.designer.svc.connector | NodePropertyPanel:383 | ✅ | D-T3 |
| 13 | oa.designer.svc.path | NodePropertyPanel:394 | ✅ | D-T3 |
| 14 | oa.designer.svc.pathHint | NodePropertyPanel:397 | ✅ | D-T3 |
| 15 | oa.designer.svc.delayMode | NodePropertyPanel:421 | ✅ | D-T3 |
| 16 | oa.designer.svc.delayMode.duration | NodePropertyPanel:423 | ✅ | D-T3 |
| 17 | oa.designer.svc.delayMode.untilDate | NodePropertyPanel:424 | ✅ | D-T3 |
| 18 | oa.designer.svc.delayMode.untilExpr | NodePropertyPanel:425 | ✅ | D-T3 |
| 19 | oa.designer.svc.delayValue | NodePropertyPanel:429 | ✅ | D-T3 |
| 20 | oa.designer.svc.delayValueHint | NodePropertyPanel:432 | ✅ | D-T3 |
| 21 | oa.designer.svc.timerAction | NodePropertyPanel:437 | ✅ | D-T3 |
| 22 | oa.designer.svc.maxRetries | NodePropertyPanel:450 | ✅ | D-T3 |
| 23 | oa.designer.svc.backoff | NodePropertyPanel:459 | ✅ | D-T3 |
| 24 | oa.designer.svc.errorEdge | EdgePropertyPanel:98 | ✅ | D-T3 |
| 25 | oa.designer.svc.errorEdgeHint | EdgePropertyPanel:100 | ✅ | D-T3 |
| 26 | oa.designer.errServiceConfig | designerModel.ts:159 | ✅ | D-T1 errXxx（与 errApproverConfig 同家族，随 svc seed 落地） |
| 27 | E-WF-016 | FlowSchemaValidator.cs:94（配置不完整/无成功出边） | ✅ | 后端错误码 |
| 28 | E-WF-017 | FlowSchemaValidator.cs:99,100（错误出边非法） | ✅ | 后端错误码 |
| 29 | E-WF-018 | DesignerService.cs:61,64 / WebApiExecutor / ServiceTaskNodeHandler（动作/连接器未注册） | ✅ | 后端错误码 |

对账结论：代码引用的 25 个 svc.* 键 + errServiceConfig 全部覆盖；无多余 svc 键。25 = D-T2(4) + D-T3(21)，与 brief 契约一致。三后端错误码文案沿用 E-WF-011/014/015 的 `LangKey = "E-WF-0xx"`（无前缀）格式。

## 去重 grep 结果
1) 跨 seed 冲突检查：`grep -rn 'oa\.designer\.svc|errServiceConfig|E-WF-01[678]' CP6.WebApi/Seed/` → **No matches found**（29 键在既有 Inbox/Advanced/Designer/SerialSign/Approver seed 中均无重复）。
2) 本文件内部去重：`grep -oP 'LangKey = "\K[^"]+' | sort` → 29 键，`uniq -d` → 空（无重复）。

## build 输出
`dotnet build CP6.WebApi/CP6.WebApi.csproj` → **Build succeeded, 0 Error(s), 1 Warning(s)**。
唯一 warning = CP6.Core InboundService.cs(366) CS8601（WMS 既有历史警告，与本任务无关）。

## 自查发现
- errServiceConfig 归属：designerModel.ts 用的是 `oa.designer.errServiceConfig`（不在 svc.* 下），与既有 `errApproverConfig`（在 I18nOaApproverScreenSeed）同 errXxx 家族。因该键由本任务 D-T1 校验引入且既有 seed 未含，随 svc seed 一并落地，符合 brief「看既有 errXxx 键的 seed 归属照做」。
- E-WF-018 文案对齐后端实义（DesignerService/Executor 均为「动作/连接器未注册」），非「解析失败」局部分支。
- 未新增任何「全空守卫」，遵循 01f1f6f 修复后的现行 seed 模式。
- 零 Space 污染，未改动其他文件。

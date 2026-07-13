### Task E-T1: i18n 五语 seed（39 键）

**Files:**
- Create: `CP6.WebApi/Seed/I18nOaInboxUxScreenSeed.cs`
- Modify: `CP6.WebApi/Program.cs`（concat 链 :1819 之后加一行）

**Interfaces:**
- Consumes: `Sys_Lang`（`CP6.Entity.DomainModels.Sys`，五列 `ZhCN/ZhTW/En/Ja/Ko`）；concat 链既有双层去重（`.Where(!existingKeys)` + `GroupBy(LangKey)`，:1820-1821）。
- Produces: 前四波全部 `t('...')` 键。键名已与既有 seed 全量比对不冲突（`oa.notify.settings.*`/`oa.notify.title` 等在 `I18nOaNotifyScreenSeed`；`oa.transfer.*` 在既有 seed——本 seed 全部为新前缀 `oa.notify.matrix.*` / `oa.notify.type.*` / `oa.bt.*` / `oa.inbox.rowMode.*` / `oa.inbox.mobileFilter` / `oa.pref.errBadJson`）。

- [ ] **Step 1: 写 seed 文件**（照 `I18nOaInboxScreenSeed.cs` 逐字结构）

```csharp
// CP6.WebApi/Seed/I18nOaInboxUxScreenSeed.cs
using CP6.Entity.DomainModels.Sys;

namespace CP6.WebApi.Seed;

/// <summary>WFS 信箱体验增强（wfs-inbox-ux）五语键：通知矩阵 / 批量改派 / rowMode / 移动端。</summary>
public static class I18nOaInboxUxScreenSeed
{
    public static readonly Sys_Lang[] Items = new[]
    {
        // ── 通知矩阵（设置页 notify tab，A-T4）──
        new Sys_Lang { LangKey = "oa.notify.matrix.colType", ZhCN = "通知类型", ZhTW = "通知類型", En = "Type", Ja = "通知タイプ", Ko = "알림 유형" },
        new Sys_Lang { LangKey = "oa.notify.matrix.colInApp", ZhCN = "站内", ZhTW = "站內", En = "In-app", Ja = "アプリ内", Ko = "앱 내" },
        new Sys_Lang { LangKey = "oa.notify.matrix.colEmail", ZhCN = "邮件", ZhTW = "郵件", En = "Email", Ja = "メール", Ko = "이메일" },
        new Sys_Lang { LangKey = "oa.notify.matrix.unsupported", ZhCN = "该类型暂无此通道动作", ZhTW = "該類型暫無此通道動作", En = "No action wired for this channel", Ja = "このチャネルの動作は未接続です", Ko = "이 채널에 연결된 동작이 없습니다" },
        new Sys_Lang { LangKey = "oa.notify.matrix.reset", ZhCN = "恢复默认", ZhTW = "恢復預設", En = "Reset to default", Ja = "デフォルトに戻す", Ko = "기본값 복원" },
        new Sys_Lang { LangKey = "oa.notify.matrix.resetOk", ZhCN = "已恢复默认（全部开启）", ZhTW = "已恢復預設（全部開啟）", En = "Reset to default (all on)", Ja = "デフォルト（すべてオン）に戻しました", Ko = "기본값(모두 켜짐)으로 복원했습니다" },
        new Sys_Lang { LangKey = "oa.notify.matrix.saveOk", ZhCN = "通知设定已保存", ZhTW = "通知設定已儲存", En = "Notification settings saved", Ja = "通知設定を保存しました", Ko = "알림 설정을 저장했습니다" },

        // ── 通知类型行标签（数据驱动 t('oa.notify.type.'+typeKey)；branchPruned 预留：hardening 合入即用）──
        new Sys_Lang { LangKey = "oa.notify.type.todoCreated", ZhCN = "新待办", ZhTW = "新待辦", En = "New to-do", Ja = "新規TODO", Ko = "새 할 일" },
        new Sys_Lang { LangKey = "oa.notify.type.flowApproved", ZhCN = "签核通过", ZhTW = "簽核通過", En = "Flow approved", Ja = "承認完了", Ko = "승인 완료" },
        new Sys_Lang { LangKey = "oa.notify.type.flowRejected", ZhCN = "流程驳回", ZhTW = "流程駁回", En = "Flow rejected", Ja = "却下", Ko = "반려" },
        new Sys_Lang { LangKey = "oa.notify.type.timeout", ZhCN = "超时提醒", ZhTW = "超時提醒", En = "Timeout reminder", Ja = "タイムアウト通知", Ko = "시간 초과 알림" },
        new Sys_Lang { LangKey = "oa.notify.type.branchPruned", ZhCN = "分支剪枝", ZhTW = "分支剪枝", En = "Branch pruned", Ja = "ブランチ剪定", Ko = "분기 정리" },

        // ── 偏好保存 ──
        new Sys_Lang { LangKey = "oa.pref.errBadJson", ZhCN = "偏好格式错误", ZhTW = "偏好格式錯誤", En = "Invalid preferences payload", Ja = "設定の形式が不正です", Ko = "설정 형식이 잘못되었습니다" },

        // ── 批量改派（B-T3）──
        new Sys_Lang { LangKey = "oa.bt.entry", ZhCN = "批量改派", ZhTW = "批次改派", En = "Batch transfer", Ja = "一括再割当", Ko = "일괄 재배정" },
        new Sys_Lang { LangKey = "oa.bt.title", ZhCN = "在途批量改派", ZhTW = "在途批次改派", En = "Batch transfer pending tasks", Ja = "進行中タスクの一括再割当", Ko = "진행 중 작업 일괄 재배정" },
        new Sys_Lang { LangKey = "oa.bt.fromUser", ZhCN = "转出人", ZhTW = "轉出人", En = "From user", Ja = "移譲元", Ko = "이관자" },
        new Sys_Lang { LangKey = "oa.bt.toUser", ZhCN = "接收人", ZhTW = "接收人", En = "To user", Ja = "移譲先", Ko = "수신자" },
        new Sys_Lang { LangKey = "oa.bt.comment", ZhCN = "备注", ZhTW = "備註", En = "Comment", Ja = "コメント", Ko = "비고" },
        new Sys_Lang { LangKey = "oa.bt.commentHint", ZhCN = "例如：离职移交", ZhTW = "例如：離職移交", En = "e.g. offboarding handover", Ja = "例：退職引継ぎ", Ko = "예: 퇴사 인수인계" },
        new Sys_Lang { LangKey = "oa.bt.filterFlowKey", ZhCN = "限定流程", ZhTW = "限定流程", En = "Flow filter", Ja = "対象フロー", Ko = "대상 플로우" },
        new Sys_Lang { LangKey = "oa.bt.filterBefore", ZhCN = "此时间前发出", ZhTW = "此時間前發出", En = "Sent before", Ja = "この時刻以前に送付", Ko = "이 시각 이전 발송" },
        new Sys_Lang { LangKey = "oa.bt.preview", ZhCN = "预览待转清单", ZhTW = "預覽待轉清單", En = "Preview", Ja = "対象をプレビュー", Ko = "대상 미리보기" },
        new Sys_Lang { LangKey = "oa.bt.previewTotal", ZhCN = "待转 {n} 条", ZhTW = "待轉 {n} 條", En = "{n} task(s) to transfer", Ja = "移譲対象 {n} 件", Ko = "이관 대상 {n}건" },
        new Sys_Lang { LangKey = "oa.bt.previewEmpty", ZhCN = "无符合条件的待办", ZhTW = "無符合條件的待辦", En = "No matching pending tasks", Ja = "条件に合うタスクがありません", Ko = "조건에 맞는 작업이 없습니다" },
        new Sys_Lang { LangKey = "oa.bt.confirm", ZhCN = "确认改派", ZhTW = "確認改派", En = "Transfer", Ja = "再割当を実行", Ko = "재배정 실행" },
        new Sys_Lang { LangKey = "oa.bt.resultSummary", ZhCN = "共 {total} 条，成功 {ok} 条，失败 {fail} 条", ZhTW = "共 {total} 條，成功 {ok} 條，失敗 {fail} 條", En = "{total} total, {ok} succeeded, {fail} failed", Ja = "全 {total} 件、成功 {ok} 件、失敗 {fail} 件", Ko = "총 {total}건, 성공 {ok}건, 실패 {fail}건" },
        new Sys_Lang { LangKey = "oa.bt.allOk", ZhCN = "全部改派成功", ZhTW = "全部改派成功", En = "All transferred", Ja = "すべて再割当しました", Ko = "모두 재배정했습니다" },
        new Sys_Lang { LangKey = "oa.bt.colTask", ZhCN = "任务", ZhTW = "任務", En = "Task", Ja = "タスク", Ko = "작업" },
        new Sys_Lang { LangKey = "oa.bt.colFlow", ZhCN = "流程", ZhTW = "流程", En = "Flow", Ja = "フロー", Ko = "플로우" },
        new Sys_Lang { LangKey = "oa.bt.colError", ZhCN = "失败原因", ZhTW = "失敗原因", En = "Error", Ja = "失敗理由", Ko = "실패 사유" },
        new Sys_Lang { LangKey = "oa.bt.retry", ZhCN = "重试", ZhTW = "重試", En = "Retry", Ja = "再試行", Ko = "재시도" },
        new Sys_Lang { LangKey = "oa.bt.retryOk", ZhCN = "重试成功", ZhTW = "重試成功", En = "Retry succeeded", Ja = "再試行に成功しました", Ko = "재시도 성공" },
        new Sys_Lang { LangKey = "oa.bt.retryGone", ZhCN = "该任务已被办结或转走", ZhTW = "該任務已被辦結或轉走", En = "Task already handled or transferred", Ja = "タスクは既に処理済みまたは移譲済みです", Ko = "이미 처리되었거나 이관된 작업입니다" },
        new Sys_Lang { LangKey = "oa.bt.errSameUser", ZhCN = "转出人与接收人不能相同", ZhTW = "轉出人與接收人不能相同", En = "From and to user must differ", Ja = "移譲元と移譲先は同一にできません", Ko = "이관자와 수신자는 같을 수 없습니다" },
        new Sys_Lang { LangKey = "oa.bt.errTargetInvalid", ZhCN = "接收人不存在或已停用", ZhTW = "接收人不存在或已停用", En = "Target user missing or disabled", Ja = "移譲先が存在しないか無効です", Ko = "수신자가 없거나 비활성화되었습니다" },
        new Sys_Lang { LangKey = "oa.bt.errTooMany", ZhCN = "待转数量超过单批上限 500，请用过滤条件分批", ZhTW = "待轉數量超過單批上限 500，請用過濾條件分批", En = "Over the 500-per-batch cap, narrow with filters", Ja = "1回の上限500件を超えています。条件で分割してください", Ko = "1회 상한 500건을 초과했습니다. 필터로 나눠 주세요" },

        // ── rowMode + 移动端（C-T1 / D-T2）──
        new Sys_Lang { LangKey = "oa.inbox.rowMode.merged", ZhCN = "合并显示", ZhTW = "合併顯示", En = "Merged", Ja = "集約表示", Ko = "병합 표시" },
        new Sys_Lang { LangKey = "oa.inbox.rowMode.expanded", ZhCN = "逐任务显示", ZhTW = "逐任務顯示", En = "Per task", Ja = "タスク別表示", Ko = "작업별 표시" },
        new Sys_Lang { LangKey = "oa.inbox.mobileFilter", ZhCN = "筛选", ZhTW = "篩選", En = "Filters", Ja = "絞り込み", Ko = "필터" },
    };
}
```

- [ ] **Step 2: Program.cs concat** — :1819（`I18nOaServiceTaskScreenSeed` 行）之后插一行：

```csharp
            .Concat(CP6.WebApi.Seed.I18nOaInboxUxScreenSeed.Items)  // WFS 信箱体验增强：通知矩阵/批量改派/rowMode/移动端
```

（尾部 `.Where(!existingKeys)` + `GroupBy(LangKey)` 既有去重兜底；`SeedLangs` 幂等。）

- [ ] **Step 3: build 验证 + commit**

```bash
dotnet build CP6.WebApi/CP6.WebApi.csproj
git add -A && git commit -m "feat(wfs-inbox): E-T1 I18nOaInboxUxScreenSeed 五语39键+concat"
```

---


---
## 附: R6 i18n seed现状
- i18n seed：`CP6.WebApi/Seed/I18nOa*ScreenSeed.cs`（`Sys_Lang[] Items`，五列 `ZhCN/ZhTW/En/Ja/Ko`）；Program.cs concat 链 :1813-1819，尾部 `.Where(!existingKeys)` + `GroupBy(LangKey)` 双层去重；新 seed 插 :1819 之后。

## ⚠ 主控交接注记(binding)
1. 键面权威=四波实际t()引用: A-T4(oa.notify.*矩阵卡片, InboxSettings.vue+notifyMatrixModel.ts含oa.notify.type.branchPruned第5行) / B-T3(oa.bt.*批转对话框+入口, BatchTransferDialog.vue+FlowAdmin.vue) / C-T1/C-T2(oa.inbox.mobileFilter等移动端键) / D-T2(rowMode开关键, 提取时刚落地看InboxPending.vue) / 后端错误键(oa.bt.errSameUser/errTargetInvalid/errTooMany+oa.pref.errBadJson)。计划写39键——以grep双向对账为准(零缺零孤儿), 数量偏差如实记录(branchPruned行是计划后长出的第5行, 键数可能>39)。
2. seed文件I18nOaInboxUxScreenSeed.cs照I18nOaFlowTriggerScreenSeed先例, Program.cs concat链尾部追加(既有.Where(!existingKeys)+GroupBy双层去重)。
3. ja/ko真译非中文复制; 与既有seed全库grep去重(尤其oa.flowadmin.*/common.*可能已存)。

using CP6.Entity.DomainModels.Sys;

namespace CP6.WebApi.Seed;

/// <summary>
/// S 类 #4 字段级审计（Field Audit）五语词条（T6）：sec.audit.* 画面词条
/// （T7 前端：字段审计列表 + 变更时间线两视图消费）。
/// 只读模块——无错误码，无新增 sec.event.*。
/// </summary>
public static class I18nSecAuditScreenSeed
{
    public static readonly Sys_Lang[] Items = new[]
    {
        // ── sec.audit.* 画面词条（T7 前端：字段审计列表 + 变更时间线两视图）──
        new Sys_Lang { LangKey = "sec.audit.title", ZhCN = "字段审计", ZhTW = "欄位稽核", En = "Field Audit", Ja = "フィールド監査", Ko = "필드 감사" },
        new Sys_Lang { LangKey = "sec.audit.entityName", ZhCN = "实体", ZhTW = "實體", En = "Entity", Ja = "エンティティ", Ko = "엔터티" },
        new Sys_Lang { LangKey = "sec.audit.entityKey", ZhCN = "主键", ZhTW = "主鍵", En = "Primary Key", Ja = "主キー", Ko = "기본 키" },
        new Sys_Lang { LangKey = "sec.audit.operation", ZhCN = "操作", ZhTW = "操作", En = "Operation", Ja = "操作", Ko = "작업" },
        new Sys_Lang { LangKey = "sec.audit.op.added", ZhCN = "新增", ZhTW = "新增", En = "Added", Ja = "追加", Ko = "추가" },
        new Sys_Lang { LangKey = "sec.audit.op.modified", ZhCN = "修改", ZhTW = "修改", En = "Modified", Ja = "変更", Ko = "수정" },
        new Sys_Lang { LangKey = "sec.audit.op.deleted", ZhCN = "删除", ZhTW = "刪除", En = "Deleted", Ja = "削除", Ko = "삭제" },
        new Sys_Lang { LangKey = "sec.audit.operator", ZhCN = "操作人", ZhTW = "操作人", En = "Operator", Ja = "操作者", Ko = "작업자" },
        new Sys_Lang { LangKey = "sec.audit.changedAt", ZhCN = "变更时间", ZhTW = "變更時間", En = "Changed At", Ja = "変更日時", Ko = "변경 시간" },
        new Sys_Lang { LangKey = "sec.audit.changeCount", ZhCN = "变更字段数", ZhTW = "變更欄位數", En = "Changed Fields", Ja = "変更フィールド数", Ko = "변경 필드 수" },
        new Sys_Lang { LangKey = "sec.audit.field", ZhCN = "字段", ZhTW = "欄位", En = "Field", Ja = "フィールド", Ko = "필드" },
        new Sys_Lang { LangKey = "sec.audit.oldValue", ZhCN = "旧值", ZhTW = "舊值", En = "Old Value", Ja = "変更前の値", Ko = "이전 값" },
        new Sys_Lang { LangKey = "sec.audit.newValue", ZhCN = "新值", ZhTW = "新值", En = "New Value", Ja = "変更後の値", Ko = "새 값" },
        new Sys_Lang { LangKey = "sec.audit.viewTimeline", ZhCN = "查看时间线", ZhTW = "檢視時間軸", En = "View Timeline", Ja = "タイムラインを表示", Ko = "타임라인 보기" },
        new Sys_Lang { LangKey = "sec.audit.timelineTitle", ZhCN = "变更时间线", ZhTW = "變更時間軸", En = "Change Timeline", Ja = "変更タイムライン", Ko = "변경 타임라인" },
        new Sys_Lang { LangKey = "sec.audit.noChanges", ZhCN = "无变更", ZhTW = "無變更", En = "No Changes", Ja = "変更なし", Ko = "변경 없음" },
        new Sys_Lang { LangKey = "sec.audit.filterEntity", ZhCN = "实体名", ZhTW = "實體名稱", En = "Entity Name", Ja = "エンティティ名", Ko = "엔터티 이름" },
        new Sys_Lang { LangKey = "sec.audit.filterUser", ZhCN = "操作人", ZhTW = "操作人", En = "Operator", Ja = "操作者", Ko = "작업자" },
        new Sys_Lang { LangKey = "sec.audit.filterFrom", ZhCN = "起始时间", ZhTW = "起始時間", En = "From", Ja = "開始日時", Ko = "시작 시간" },
        new Sys_Lang { LangKey = "sec.audit.filterTo", ZhCN = "结束时间", ZhTW = "結束時間", En = "To", Ja = "終了日時", Ko = "종료 시간" },
    };
}

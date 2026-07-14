using CP6.Entity.DomainModels.Sys;

namespace CP6.WebApi.Seed;

/// <summary>
/// S 类 #5 多租户合规（T9）五语词条：E-SEC-031~038 错误码 + SecurityEventType 19~30 事件标签 +
/// platform.* 画面词条（前端平台区：租户管理 / 平台超管授撤 / impersonation / GDPR / 跨租户审计）。
/// 代码只放 key（错误码用码本身作 key，仿 I18nSecScreenSeed / I18nSec2faScreenSeed）。
/// 带外（out-of-band）：平台区不在 Sys_Menu RBAC 内，由 isPlatformAdmin 标志 + [RequirePlatformAdmin] 真闸门控制。
/// </summary>
public static class I18nTenantComplianceSeed
{
    public static readonly Sys_Lang[] Items = new[]
    {
        // ── 错误码 E-SEC-031~038（BizException 码即 key，spec §5）──
        new Sys_Lang { LangKey = "E-SEC-031", ZhCN = "无平台超管权限", ZhTW = "無平台超管權限", En = "Platform admin privilege required", Ja = "プラットフォーム管理者権限が必要です", Ko = "플랫폼 관리자 권한이 필요합니다" },
        new Sys_Lang { LangKey = "E-SEC-032", ZhCN = "用户或租户不存在", ZhTW = "使用者或租戶不存在", En = "User or tenant does not exist", Ja = "ユーザーまたはテナントが存在しません", Ko = "사용자 또는 테넌트가 존재하지 않습니다" },
        new Sys_Lang { LangKey = "E-SEC-033", ZhCN = "租户编码已存在", ZhTW = "租戶編碼已存在", En = "Tenant code already exists", Ja = "テナントコードは既に存在します", Ko = "테넌트 코드가 이미 존재합니다" },
        new Sys_Lang { LangKey = "E-SEC-034", ZhCN = "模拟登录期间禁止访问平台管理", ZhTW = "模擬登入期間禁止存取平台管理", En = "Platform administration is forbidden during impersonation", Ja = "なりすましログイン中はプラットフォーム管理にアクセスできません", Ko = "대리 로그인 중에는 플랫폼 관리에 접근할 수 없습니다" },
        new Sys_Lang { LangKey = "E-SEC-035", ZhCN = "目标租户无可用管理员", ZhTW = "目標租戶無可用管理員", En = "Target tenant has no available administrator", Ja = "対象テナントに利用可能な管理者がいません", Ko = "대상 테넌트에 사용 가능한 관리자가 없습니다" },
        new Sys_Lang { LangKey = "E-SEC-036", ZhCN = "禁止对平台租户或平台超管执行此操作", ZhTW = "禁止對平台租戶或平台超管執行此操作", En = "This operation is forbidden on the platform tenant or a platform admin", Ja = "プラットフォームテナントまたはプラットフォーム管理者に対してこの操作は禁止されています", Ko = "플랫폼 테넌트 또는 플랫폼 관리자에 대해 이 작업은 금지됩니다" },
        new Sys_Lang { LangKey = "E-SEC-037", ZhCN = "不能撤销或删除最后一个平台超管", ZhTW = "不能撤銷或刪除最後一個平台超管", En = "Cannot revoke or delete the last platform admin", Ja = "最後のプラットフォーム管理者を取り消し・削除することはできません", Ko = "마지막 플랫폼 관리자를 취소하거나 삭제할 수 없습니다" },
        new Sys_Lang { LangKey = "E-SEC-038", ZhCN = "缺少确认参数或参数无效", ZhTW = "缺少確認參數或參數無效", En = "Confirmation parameter is missing or invalid", Ja = "確認パラメーターが不足しているか無効です", Ko = "확인 매개변수가 없거나 유효하지 않습니다" },

        // ── SecurityEventType 19~30 事件标签（前端 EventType int → sec.event.{n}）──
        new Sys_Lang { LangKey = "sec.event.19", ZhCN = "租户创建", ZhTW = "租戶建立", En = "Tenant Created", Ja = "テナント作成", Ko = "테넌트 생성" },
        new Sys_Lang { LangKey = "sec.event.20", ZhCN = "租户更新", ZhTW = "租戶更新", En = "Tenant Updated", Ja = "テナント更新", Ko = "테넌트 업데이트" },
        new Sys_Lang { LangKey = "sec.event.21", ZhCN = "租户停用", ZhTW = "租戶停用", En = "Tenant Suspended", Ja = "テナント停止", Ko = "테넌트 중지" },
        new Sys_Lang { LangKey = "sec.event.22", ZhCN = "租户重启用", ZhTW = "租戶重新啟用", En = "Tenant Reactivated", Ja = "テナント再有効化", Ko = "테넌트 재활성화" },
        new Sys_Lang { LangKey = "sec.event.23", ZhCN = "平台超管授予", ZhTW = "平台超管授予", En = "Platform Admin Granted", Ja = "プラットフォーム管理者付与", Ko = "플랫폼 관리자 부여" },
        new Sys_Lang { LangKey = "sec.event.24", ZhCN = "平台超管撤销", ZhTW = "平台超管撤銷", En = "Platform Admin Revoked", Ja = "プラットフォーム管理者取消", Ko = "플랫폼 관리자 취소" },
        new Sys_Lang { LangKey = "sec.event.25", ZhCN = "开始模拟登录", ZhTW = "開始模擬登入", En = "Impersonation Started", Ja = "なりすましログイン開始", Ko = "대리 로그인 시작" },
        new Sys_Lang { LangKey = "sec.event.26", ZhCN = "结束模拟登录", ZhTW = "結束模擬登入", En = "Impersonation Ended", Ja = "なりすましログイン終了", Ko = "대리 로그인 종료" },
        new Sys_Lang { LangKey = "sec.event.27", ZhCN = "租户数据导出", ZhTW = "租戶資料匯出", En = "Tenant Data Exported", Ja = "テナントデータ書き出し", Ko = "테넌트 데이터 내보내기" },
        new Sys_Lang { LangKey = "sec.event.28", ZhCN = "主体数据导出", ZhTW = "主體資料匯出", En = "Subject Data Exported", Ja = "データ主体データ書き出し", Ko = "정보주체 데이터 내보내기" },
        new Sys_Lang { LangKey = "sec.event.29", ZhCN = "租户数据擦除", ZhTW = "租戶資料抹除", En = "Tenant Data Erased", Ja = "テナントデータ消去", Ko = "테넌트 데이터 삭제" },
        new Sys_Lang { LangKey = "sec.event.30", ZhCN = "主体数据擦除", ZhTW = "主體資料抹除", En = "Subject Data Erased", Ja = "データ主体データ消去", Ko = "정보주체 데이터 삭제" },

        // ── platform.* 画面词条（前端平台区，带外）──
        new Sys_Lang { LangKey = "platform.title", ZhCN = "平台管理", ZhTW = "平台管理", En = "Platform Administration", Ja = "プラットフォーム管理", Ko = "플랫폼 관리" },
        new Sys_Lang { LangKey = "platform.copy", ZhCN = "复制", ZhTW = "複製", En = "Copy", Ja = "コピー", Ko = "복사" },
        new Sys_Lang { LangKey = "platform.copied", ZhCN = "已复制", ZhTW = "已複製", En = "Copied", Ja = "コピーしました", Ko = "복사됨" },
        new Sys_Lang { LangKey = "platform.saved", ZhCN = "已保存", ZhTW = "已儲存", En = "Saved", Ja = "保存しました", Ko = "저장되었습니다" },

        // 租户管理
        new Sys_Lang { LangKey = "platform.tenant.title", ZhCN = "租户管理", ZhTW = "租戶管理", En = "Tenant Management", Ja = "テナント管理", Ko = "테넌트 관리" },
        new Sys_Lang { LangKey = "platform.tenant.create", ZhCN = "新建租户", ZhTW = "新建租戶", En = "Create Tenant", Ja = "テナント作成", Ko = "테넌트 생성" },
        new Sys_Lang { LangKey = "platform.tenant.code", ZhCN = "租户编码", ZhTW = "租戶編碼", En = "Tenant Code", Ja = "テナントコード", Ko = "테넌트 코드" },
        new Sys_Lang { LangKey = "platform.tenant.name", ZhCN = "租户名称", ZhTW = "租戶名稱", En = "Tenant Name", Ja = "テナント名", Ko = "테넌트 이름" },
        new Sys_Lang { LangKey = "platform.tenant.expire", ZhCN = "到期日期", ZhTW = "到期日期", En = "Expiry Date", Ja = "有効期限", Ko = "만료일" },
        new Sys_Lang { LangKey = "platform.tenant.remark", ZhCN = "备注", ZhTW = "備註", En = "Remark", Ja = "備考", Ko = "비고" },
        new Sys_Lang { LangKey = "platform.tenant.adminUser", ZhCN = "管理员账号", ZhTW = "管理員帳號", En = "Administrator Account", Ja = "管理者アカウント", Ko = "관리자 계정" },
        new Sys_Lang { LangKey = "platform.tenant.suspend", ZhCN = "停用", ZhTW = "停用", En = "Suspend", Ja = "停止", Ko = "중지" },
        new Sys_Lang { LangKey = "platform.tenant.reactivate", ZhCN = "重启用", ZhTW = "重新啟用", En = "Reactivate", Ja = "再有効化", Ko = "재활성화" },
        new Sys_Lang { LangKey = "platform.tenant.tempPassword", ZhCN = "临时密码", ZhTW = "臨時密碼", En = "Temporary Password", Ja = "仮パスワード", Ko = "임시 비밀번호" },
        new Sys_Lang { LangKey = "platform.tenant.tempPasswordHint", ZhCN = "此临时密码仅显示一次，请立即复制并妥善保管", ZhTW = "此臨時密碼僅顯示一次，請立即複製並妥善保管", En = "This temporary password is shown only once; copy and store it securely now", Ja = "この仮パスワードは一度だけ表示されます。今すぐコピーして安全に保管してください", Ko = "이 임시 비밀번호는 한 번만 표시됩니다. 지금 복사하여 안전하게 보관하세요" },
        new Sys_Lang { LangKey = "platform.tenant.userCount", ZhCN = "用户数", ZhTW = "使用者數", En = "Users", Ja = "ユーザー数", Ko = "사용자 수" },
        // WFS 波⑤ E-T2：租户时区（TenantListView.vue 编辑对话框时区下拉 + 自愈口径提示）
        new Sys_Lang { LangKey = "platform.tenant.timeZone", ZhCN = "时区", ZhTW = "時區", En = "Time Zone", Ja = "タイムゾーン", Ko = "시간대" },
        new Sys_Lang { LangKey = "platform.tenant.timeZonePlaceholder", ZhCN = "选择时区（留空＝沿用默认）", ZhTW = "選擇時區（留空＝沿用預設）", En = "Select time zone (empty = use default)", Ja = "タイムゾーンを選択（空欄＝既定を使用）", Ko = "시간대 선택(비우면 기본값 사용)" },
        new Sys_Lang { LangKey = "platform.tenant.timeZoneHint", ZhCN = "改时区不批量重算既有触发器到期时刻，下次发火后按新时区自愈（最多一次按旧时区发火）。", ZhTW = "改時區不批次重算既有觸發器到期時刻，下次發火後按新時區自癒（最多一次按舊時區發火）。", En = "Changing the time zone does not recompute existing triggers' due times; each self-heals to the new zone after its next fire (at most one fire on the old zone).", Ja = "タイムゾーンを変更しても既存トリガーの期限は一括再計算されません。次回発火後に新しいタイムゾーンで自己修復します（旧タイムゾーンでの発火は最大1回）。", Ko = "시간대를 변경해도 기존 트리거의 만료 시각은 일괄 재계산되지 않으며, 각 트리거는 다음 발화 후 새 시간대로 자가 치유됩니다(이전 시간대 발화는 최대 1회).", },

        // 平台超管授撤
        new Sys_Lang { LangKey = "platform.admin.title", ZhCN = "平台超管", ZhTW = "平台超管", En = "Platform Admins", Ja = "プラットフォーム管理者", Ko = "플랫폼 관리자" },
        new Sys_Lang { LangKey = "platform.admin.grant", ZhCN = "授予", ZhTW = "授予", En = "Grant", Ja = "付与", Ko = "부여" },
        new Sys_Lang { LangKey = "platform.admin.revoke", ZhCN = "撤销", ZhTW = "撤銷", En = "Revoke", Ja = "取消", Ko = "취소" },
        new Sys_Lang { LangKey = "platform.admin.confirmRevoke", ZhCN = "确定要撤销该平台超管权限吗？", ZhTW = "確定要撤銷該平台超管權限嗎？", En = "Are you sure you want to revoke this platform admin privilege?", Ja = "このプラットフォーム管理者権限を取り消してもよろしいですか？", Ko = "이 플랫폼 관리자 권한을 취소하시겠습니까?" },
        new Sys_Lang { LangKey = "platform.admin.lastOne", ZhCN = "不能撤销最后一个平台超管", ZhTW = "不能撤銷最後一個平台超管", En = "Cannot revoke the last platform admin", Ja = "最後のプラットフォーム管理者は取り消せません", Ko = "마지막 플랫폼 관리자는 취소할 수 없습니다" },

        // impersonation 模拟登录
        new Sys_Lang { LangKey = "platform.impersonation.title", ZhCN = "模拟登录", ZhTW = "模擬登入", En = "Impersonation", Ja = "なりすましログイン", Ko = "대리 로그인" },
        new Sys_Lang { LangKey = "platform.impersonation.start", ZhCN = "切入", ZhTW = "切入", En = "Start", Ja = "開始", Ko = "시작" },
        new Sys_Lang { LangKey = "platform.impersonation.end", ZhCN = "切出", ZhTW = "切出", En = "Exit", Ja = "終了", Ko = "종료" },
        new Sys_Lang { LangKey = "platform.impersonation.selectTenant", ZhCN = "选择租户", ZhTW = "選擇租戶", En = "Select Tenant", Ja = "テナントを選択", Ko = "테넌트 선택" },
        new Sys_Lang { LangKey = "platform.impersonation.selectUser", ZhCN = "选择用户（留空取首个管理员）", ZhTW = "選擇使用者（留空取首個管理員）", En = "Select User (empty = first administrator)", Ja = "ユーザーを選択（空欄で最初の管理者）", Ko = "사용자 선택 (비워두면 첫 관리자)" },
        new Sys_Lang { LangKey = "platform.impersonation.reason", ZhCN = "原因", ZhTW = "原因", En = "Reason", Ja = "理由", Ko = "사유" },
        new Sys_Lang { LangKey = "platform.impersonation.bannerActive", ZhCN = "正在以 {tenantName}/{userName} 身份操作", ZhTW = "正在以 {tenantName}/{userName} 身份操作", En = "Acting as {tenantName}/{userName}", Ja = "{tenantName}/{userName} として操作中", Ko = "{tenantName}/{userName} 신분으로 작업 중" },
        new Sys_Lang { LangKey = "platform.impersonation.countdown", ZhCN = "还剩 {min} 分钟", ZhTW = "還剩 {min} 分鐘", En = "{min} minutes left", Ja = "残り {min} 分", Ko = "{min}분 남음" },
        new Sys_Lang { LangKey = "platform.impersonation.autoEnded", ZhCN = "已自动切回平台超管会话", ZhTW = "已自動切回平台超管工作階段", En = "Automatically returned to the platform admin session", Ja = "自動的にプラットフォーム管理者セッションに戻りました", Ko = "플랫폼 관리자 세션으로 자동 복귀했습니다" },

        // GDPR 双粒度导出 / 擦除
        new Sys_Lang { LangKey = "platform.gdpr.title", ZhCN = "数据合规", ZhTW = "資料合規", En = "Data Compliance", Ja = "データコンプライアンス", Ko = "데이터 규정 준수" },
        new Sys_Lang { LangKey = "platform.gdpr.exportTenant", ZhCN = "导出租户数据", ZhTW = "匯出租戶資料", En = "Export Tenant Data", Ja = "テナントデータを書き出し", Ko = "테넌트 데이터 내보내기" },
        new Sys_Lang { LangKey = "platform.gdpr.exportSubject", ZhCN = "导出主体数据", ZhTW = "匯出主體資料", En = "Export Subject Data", Ja = "データ主体データを書き出し", Ko = "정보주체 데이터 내보내기" },
        new Sys_Lang { LangKey = "platform.gdpr.eraseSubject", ZhCN = "擦除主体数据", ZhTW = "抹除主體資料", En = "Erase Subject Data", Ja = "データ主体データを消去", Ko = "정보주체 데이터 삭제" },
        new Sys_Lang { LangKey = "platform.gdpr.eraseTenant", ZhCN = "擦除租户数据", ZhTW = "抹除租戶資料", En = "Erase Tenant Data", Ja = "テナントデータを消去", Ko = "테넌트 데이터 삭제" },
        new Sys_Lang { LangKey = "platform.gdpr.modeAnonymize", ZhCN = "匿名化", ZhTW = "匿名化", En = "Anonymize", Ja = "匿名化", Ko = "익명화" },
        new Sys_Lang { LangKey = "platform.gdpr.modePurge", ZhCN = "彻底清除", ZhTW = "徹底清除", En = "Purge", Ja = "完全削除", Ko = "완전 삭제" },
        new Sys_Lang { LangKey = "platform.gdpr.confirm", ZhCN = "确认", ZhTW = "確認", En = "Confirm", Ja = "確認", Ko = "확인" },
        new Sys_Lang { LangKey = "platform.gdpr.confirmHint", ZhCN = "此操作不可逆，请输入 CONFIRM 以继续", ZhTW = "此操作不可逆，請輸入 CONFIRM 以繼續", En = "This action is irreversible. Type CONFIRM to continue", Ja = "この操作は元に戻せません。続行するには CONFIRM と入力してください", Ko = "이 작업은 되돌릴 수 없습니다. 계속하려면 CONFIRM을 입력하세요" },

        // 跨租户审计
        new Sys_Lang { LangKey = "platform.audit.title", ZhCN = "跨租户审计", ZhTW = "跨租戶稽核", En = "Cross-Tenant Audit", Ja = "テナント横断監査", Ko = "테넌트 간 감사" },
        new Sys_Lang { LangKey = "platform.audit.tenantCode", ZhCN = "租户编码", ZhTW = "租戶編碼", En = "Tenant Code", Ja = "テナントコード", Ko = "테넌트 코드" },
        new Sys_Lang { LangKey = "platform.audit.eventType", ZhCN = "事件类型", ZhTW = "事件類型", En = "Event Type", Ja = "イベント種別", Ko = "이벤트 유형" },
        new Sys_Lang { LangKey = "platform.audit.from", ZhCN = "起始日期", ZhTW = "起始日期", En = "From", Ja = "開始日", Ko = "시작일" },
        new Sys_Lang { LangKey = "platform.audit.to", ZhCN = "结束日期", ZhTW = "結束日期", En = "To", Ja = "終了日", Ko = "종료일" },
    };
}

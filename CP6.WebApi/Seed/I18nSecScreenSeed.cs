using CP6.Entity.DomainModels.Sys;

namespace CP6.WebApi.Seed;

/// <summary>
/// S 类认证加固五语词条（T8）：E-SEC-001~010 错误码 + SecurityEventType 八枚举标签 +
/// 改密页 / 安全日志页画面词条。代码只放 key（错误码用码本身作 key，仿 FA001/E-PUR-*）。
/// 菜单名 nav.{MenuId} 词条随 T8 菜单 seed 一并补（见 Program.cs 菜单段）。
/// </summary>
public static class I18nSecScreenSeed
{
    public static readonly Sys_Lang[] Items = new[]
    {
        // ── 错误码 E-SEC-001~010（BizException 码即 key，spec §7）──
        new Sys_Lang { LangKey = "E-SEC-001", ZhCN = "用户名或密码错误", ZhTW = "使用者名稱或密碼錯誤", En = "Incorrect username or password", Ja = "ユーザー名またはパスワードが正しくありません", Ko = "사용자 이름 또는 비밀번호가 올바르지 않습니다" },
        new Sys_Lang { LangKey = "E-SEC-002", ZhCN = "账户已锁定，请稍后再试", ZhTW = "帳戶已鎖定，請稍後再試", En = "Account is locked, please try again later", Ja = "アカウントがロックされています。しばらくしてから再試行してください", Ko = "계정이 잠겼습니다. 잠시 후 다시 시도하세요" },
        new Sys_Lang { LangKey = "E-SEC-003", ZhCN = "账户已禁用", ZhTW = "帳戶已停用", En = "Account is disabled", Ja = "アカウントは無効です", Ko = "계정이 비활성화되었습니다" },
        new Sys_Lang { LangKey = "E-SEC-004", ZhCN = "密码不符合安全策略", ZhTW = "密碼不符合安全策略", En = "Password does not meet the security policy", Ja = "パスワードがセキュリティポリシーを満たしていません", Ko = "비밀번호가 보안 정책을 충족하지 않습니다" },
        new Sys_Lang { LangKey = "E-SEC-005", ZhCN = "新密码不能与最近若干次重复", ZhTW = "新密碼不能與最近數次重複", En = "New password must differ from recent ones", Ja = "新しいパスワードは直近のものと重複できません", Ko = "새 비밀번호는 최근 사용한 것과 중복될 수 없습니다" },
        new Sys_Lang { LangKey = "E-SEC-006", ZhCN = "原密码错误", ZhTW = "原密碼錯誤", En = "Current password is incorrect", Ja = "現在のパスワードが正しくありません", Ko = "현재 비밀번호가 올바르지 않습니다" },
        new Sys_Lang { LangKey = "E-SEC-007", ZhCN = "刷新令牌无效或已过期", ZhTW = "重新整理權杖無效或已過期", En = "Refresh token is invalid or expired", Ja = "リフレッシュトークンが無効または期限切れです", Ko = "갱신 토큰이 유효하지 않거나 만료되었습니다" },
        new Sys_Lang { LangKey = "E-SEC-008", ZhCN = "会话已失效，请重新登录", ZhTW = "工作階段已失效，請重新登入", En = "Session expired, please sign in again", Ja = "セッションが無効です。再度ログインしてください", Ko = "세션이 만료되었습니다. 다시 로그인하세요" },
        new Sys_Lang { LangKey = "E-SEC-009", ZhCN = "请先修改密码", ZhTW = "請先修改密碼", En = "Please change your password first", Ja = "先にパスワードを変更してください", Ko = "먼저 비밀번호를 변경하세요" },
        new Sys_Lang { LangKey = "E-SEC-010", ZhCN = "安全校验失败（CSRF）", ZhTW = "安全校驗失敗（CSRF）", En = "Security check failed (CSRF)", Ja = "セキュリティ検証に失敗しました（CSRF）", Ko = "보안 검증 실패(CSRF)" },

        // ── SecurityEventType 八枚举标签（前端 EventType int → sec.event.{n}）──
        new Sys_Lang { LangKey = "sec.event.1", ZhCN = "登录成功", ZhTW = "登入成功", En = "Login Success", Ja = "ログイン成功", Ko = "로그인 성공" },
        new Sys_Lang { LangKey = "sec.event.2", ZhCN = "登录失败", ZhTW = "登入失敗", En = "Login Failed", Ja = "ログイン失敗", Ko = "로그인 실패" },
        new Sys_Lang { LangKey = "sec.event.3", ZhCN = "账户锁定", ZhTW = "帳戶鎖定", En = "Account Locked", Ja = "アカウントロック", Ko = "계정 잠금" },
        new Sys_Lang { LangKey = "sec.event.4", ZhCN = "登出", ZhTW = "登出", En = "Logout", Ja = "ログアウト", Ko = "로그아웃" },
        new Sys_Lang { LangKey = "sec.event.5", ZhCN = "修改密码", ZhTW = "修改密碼", En = "Password Changed", Ja = "パスワード変更", Ko = "비밀번호 변경" },
        new Sys_Lang { LangKey = "sec.event.6", ZhCN = "令牌刷新", ZhTW = "權杖重新整理", En = "Token Refreshed", Ja = "トークン更新", Ko = "토큰 갱신" },
        new Sys_Lang { LangKey = "sec.event.7", ZhCN = "令牌重用检测", ZhTW = "權杖重用偵測", En = "Token Reuse Detected", Ja = "トークン再利用検出", Ko = "토큰 재사용 감지" },
        new Sys_Lang { LangKey = "sec.event.8", ZhCN = "越权拒绝", ZhTW = "越權拒絕", En = "Permission Denied", Ja = "権限拒否", Ko = "권한 거부" },

        // ── 修改密码页词条 ──
        new Sys_Lang { LangKey = "sec.changePwd.title", ZhCN = "修改密码", ZhTW = "修改密碼", En = "Change Password", Ja = "パスワード変更", Ko = "비밀번호 변경" },
        new Sys_Lang { LangKey = "sec.changePwd.current", ZhCN = "当前密码", ZhTW = "目前密碼", En = "Current Password", Ja = "現在のパスワード", Ko = "현재 비밀번호" },
        new Sys_Lang { LangKey = "sec.changePwd.new", ZhCN = "新密码", ZhTW = "新密碼", En = "New Password", Ja = "新しいパスワード", Ko = "새 비밀번호" },
        new Sys_Lang { LangKey = "sec.changePwd.confirm", ZhCN = "确认新密码", ZhTW = "確認新密碼", En = "Confirm New Password", Ja = "新しいパスワード（確認）", Ko = "새 비밀번호 확인" },
        new Sys_Lang { LangKey = "sec.changePwd.mismatch", ZhCN = "两次输入的新密码不一致", ZhTW = "兩次輸入的新密碼不一致", En = "The two new passwords do not match", Ja = "新しいパスワードが一致しません", Ko = "새 비밀번호가 일치하지 않습니다" },
        new Sys_Lang { LangKey = "sec.changePwd.success", ZhCN = "密码修改成功，请重新登录", ZhTW = "密碼修改成功，請重新登入", En = "Password changed, please sign in again", Ja = "パスワードを変更しました。再度ログインしてください", Ko = "비밀번호가 변경되었습니다. 다시 로그인하세요" },
        new Sys_Lang { LangKey = "sec.changePwd.required", ZhCN = "为了账户安全，请先修改初始密码", ZhTW = "為了帳戶安全，請先修改初始密碼", En = "For account security, please change your initial password", Ja = "アカウントの安全のため、初期パスワードを変更してください", Ko = "계정 보안을 위해 초기 비밀번호를 변경하세요" },

        // ── 安全日志页词条 ──
        new Sys_Lang { LangKey = "sec.log.title", ZhCN = "安全日志", ZhTW = "安全日誌", En = "Security Log", Ja = "セキュリティログ", Ko = "보안 로그" },
        new Sys_Lang { LangKey = "sec.log.subtitle", ZhCN = "登录/锁定/改密/刷新/重用检测等安全事件审计", ZhTW = "登入/鎖定/改密/重新整理/重用偵測等安全事件稽核", En = "Audit of login / lockout / password / refresh / reuse events", Ja = "ログイン/ロック/パスワード/更新/再利用などのセキュリティ監査", Ko = "로그인/잠금/비밀번호/갱신/재사용 등 보안 이벤트 감사" },
        new Sys_Lang { LangKey = "sec.log.eventType", ZhCN = "事件类型", ZhTW = "事件類型", En = "Event Type", Ja = "イベント種別", Ko = "이벤트 유형" },
        new Sys_Lang { LangKey = "sec.log.userName", ZhCN = "用户名", ZhTW = "使用者名稱", En = "Username", Ja = "ユーザー名", Ko = "사용자 이름" },
        new Sys_Lang { LangKey = "sec.log.clientIp", ZhCN = "客户端 IP", ZhTW = "用戶端 IP", En = "Client IP", Ja = "クライアント IP", Ko = "클라이언트 IP" },
        new Sys_Lang { LangKey = "sec.log.userAgent", ZhCN = "用户代理", ZhTW = "使用者代理", En = "User Agent", Ja = "ユーザーエージェント", Ko = "사용자 에이전트" },
        new Sys_Lang { LangKey = "sec.log.reason", ZhCN = "原因", ZhTW = "原因", En = "Reason", Ja = "理由", Ko = "사유" },
        new Sys_Lang { LangKey = "sec.log.tenantCode", ZhCN = "请求租户", ZhTW = "請求租戶", En = "Request Tenant", Ja = "リクエストテナント", Ko = "요청 테넌트" },
        new Sys_Lang { LangKey = "sec.log.createdAt", ZhCN = "发生时间", ZhTW = "發生時間", En = "Occurred At", Ja = "発生時刻", Ko = "발생 시각" },
        new Sys_Lang { LangKey = "sec.log.from", ZhCN = "起始时间", ZhTW = "起始時間", En = "From", Ja = "開始", Ko = "시작" },
        new Sys_Lang { LangKey = "sec.log.to", ZhCN = "结束时间", ZhTW = "結束時間", En = "To", Ja = "終了", Ko = "종료" },
    };
}

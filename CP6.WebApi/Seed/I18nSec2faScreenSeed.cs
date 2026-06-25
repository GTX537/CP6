using CP6.Entity.DomainModels.Sys;

namespace CP6.WebApi.Seed;

/// <summary>
/// S 类 #2 双因素认证（2FA）五语词条（T8）：E-SEC-011~019 错误码（含 012 非法策略值）+
/// SecurityEventType 9~14 六枚举标签 + sec.2fa.* 画面词条（T9 前端）。
/// 代码只放 key（错误码用码本身作 key，仿 I18nSecScreenSeed E-SEC-001~010）。
/// </summary>
public static class I18nSec2faScreenSeed
{
    public static readonly Sys_Lang[] Items = new[]
    {
        // ── 错误码 E-SEC-011~019（BizException 码即 key，spec §5）──
        new Sys_Lang { LangKey = "E-SEC-011", ZhCN = "验证码错误", ZhTW = "驗證碼錯誤", En = "Invalid verification code", Ja = "確認コードが正しくありません", Ko = "인증 코드가 올바르지 않습니다" },
        new Sys_Lang { LangKey = "E-SEC-012", ZhCN = "无效的双因素认证策略值", ZhTW = "無效的雙因素認證策略值", En = "Invalid two-factor authentication policy value", Ja = "無効な二要素認証ポリシー値です", Ko = "유효하지 않은 2단계 인증 정책 값입니다" },
        new Sys_Lang { LangKey = "E-SEC-013", ZhCN = "验证码会话已过期或失效，请重新登录", ZhTW = "驗證碼工作階段已過期或失效，請重新登入", En = "Verification session expired or invalid, please sign in again", Ja = "確認セッションが期限切れまたは無効です。再度ログインしてください", Ko = "인증 세션이 만료되었거나 유효하지 않습니다. 다시 로그인하세요" },
        new Sys_Lang { LangKey = "E-SEC-014", ZhCN = "请先完成双因素认证设置", ZhTW = "請先完成雙因素認證設定", En = "Please complete two-factor authentication setup first", Ja = "先に二要素認証の設定を完了してください", Ko = "먼저 2단계 인증 설정을 완료하세요" },
        new Sys_Lang { LangKey = "E-SEC-015", ZhCN = "当前账户未配置邮箱，无法使用邮件验证码", ZhTW = "目前帳戶未設定信箱，無法使用郵件驗證碼", En = "No email configured for this account, email code unavailable", Ja = "このアカウントにメールが設定されていないため、メール確認コードを利用できません", Ko = "이 계정에 이메일이 설정되지 않아 이메일 인증 코드를 사용할 수 없습니다" },
        new Sys_Lang { LangKey = "E-SEC-016", ZhCN = "验证码发送过于频繁，请稍后再试", ZhTW = "驗證碼傳送過於頻繁，請稍後再試", En = "Verification codes are being sent too frequently, please try again later", Ja = "確認コードの送信が頻繁すぎます。しばらくしてから再試行してください", Ko = "인증 코드 전송이 너무 잦습니다. 잠시 후 다시 시도하세요" },
        new Sys_Lang { LangKey = "E-SEC-017", ZhCN = "双因素认证已启用，请先重置后再重新设置", ZhTW = "雙因素認證已啟用，請先重置後再重新設定", En = "Two-factor authentication is already enabled, please reset before re-configuring", Ja = "二要素認証は既に有効です。リセットしてから再設定してください", Ko = "2단계 인증이 이미 활성화되어 있습니다. 재설정하려면 먼저 초기화하세요" },
        new Sys_Lang { LangKey = "E-SEC-018", ZhCN = "邮件验证码发送失败，请稍后重试", ZhTW = "郵件驗證碼傳送失敗，請稍後重試", En = "Failed to send email verification code, please try again later", Ja = "メール確認コードの送信に失敗しました。しばらくしてから再試行してください", Ko = "이메일 인증 코드 전송에 실패했습니다. 잠시 후 다시 시도하세요" },
        new Sys_Lang { LangKey = "E-SEC-019", ZhCN = "贵租户已强制启用双因素认证，不可自行关闭", ZhTW = "貴租戶已強制啟用雙因素認證，不可自行關閉", En = "Your tenant has enforced two-factor authentication; it cannot be disabled", Ja = "ご利用のテナントでは二要素認証が強制されているため、無効化できません", Ko = "귀하의 테넌트는 2단계 인증을 강제하므로 비활성화할 수 없습니다" },

        // ── SecurityEventType 9~14 六枚举标签（前端 EventType int → sec.event.{n}）──
        new Sys_Lang { LangKey = "sec.event.9", ZhCN = "双因素挑战", ZhTW = "雙因素挑戰", En = "Two-Factor Challenged", Ja = "二要素チャレンジ", Ko = "2단계 인증 요청" },
        new Sys_Lang { LangKey = "sec.event.10", ZhCN = "双因素验证成功", ZhTW = "雙因素驗證成功", En = "Two-Factor Verified", Ja = "二要素認証成功", Ko = "2단계 인증 성공" },
        new Sys_Lang { LangKey = "sec.event.11", ZhCN = "双因素验证失败", ZhTW = "雙因素驗證失敗", En = "Two-Factor Failed", Ja = "二要素認証失敗", Ko = "2단계 인증 실패" },
        new Sys_Lang { LangKey = "sec.event.12", ZhCN = "双因素绑定", ZhTW = "雙因素綁定", En = "Two-Factor Enrolled", Ja = "二要素登録", Ko = "2단계 인증 등록" },
        new Sys_Lang { LangKey = "sec.event.13", ZhCN = "双因素重置", ZhTW = "雙因素重置", En = "Two-Factor Reset", Ja = "二要素リセット", Ko = "2단계 인증 초기화" },
        new Sys_Lang { LangKey = "sec.event.14", ZhCN = "邮件验证码已发送", ZhTW = "郵件驗證碼已傳送", En = "Email OTP Sent", Ja = "メール確認コード送信", Ko = "이메일 인증 코드 전송" },

        // ── sec.2fa.* 画面词条（T9 前端：绑定 / 挑战 / 策略 三页）──
        new Sys_Lang { LangKey = "sec.2fa.title", ZhCN = "双因素认证", ZhTW = "雙因素認證", En = "Two-Factor Authentication", Ja = "二要素認証", Ko = "2단계 인증" },
        new Sys_Lang { LangKey = "sec.2fa.scanQr", ZhCN = "请使用验证器 App 扫描二维码", ZhTW = "請使用驗證器 App 掃描 QR 碼", En = "Scan the QR code with your authenticator app", Ja = "認証アプリで QR コードをスキャンしてください", Ko = "인증 앱으로 QR 코드를 스캔하세요" },
        new Sys_Lang { LangKey = "sec.2fa.enterCode", ZhCN = "请输入验证码", ZhTW = "請輸入驗證碼", En = "Enter verification code", Ja = "確認コードを入力してください", Ko = "인증 코드를 입력하세요" },
        new Sys_Lang { LangKey = "sec.2fa.useEmail", ZhCN = "改用邮件验证码", ZhTW = "改用郵件驗證碼", En = "Use email code instead", Ja = "メール確認コードを使う", Ko = "이메일 인증 코드 사용" },
        new Sys_Lang { LangKey = "sec.2fa.sendEmailCode", ZhCN = "发送邮件验证码", ZhTW = "傳送郵件驗證碼", En = "Send email code", Ja = "メール確認コードを送信", Ko = "이메일 인증 코드 전송" },
        new Sys_Lang { LangKey = "sec.2fa.emailSent", ZhCN = "验证码已发送至您的邮箱", ZhTW = "驗證碼已傳送至您的信箱", En = "A verification code has been sent to your email", Ja = "確認コードをメールに送信しました", Ko = "인증 코드가 이메일로 전송되었습니다" },
        new Sys_Lang { LangKey = "sec.2fa.enable", ZhCN = "启用", ZhTW = "啟用", En = "Enable", Ja = "有効化", Ko = "활성화" },
        new Sys_Lang { LangKey = "sec.2fa.disable", ZhCN = "关闭", ZhTW = "關閉", En = "Disable", Ja = "無効化", Ko = "비활성화" },
        new Sys_Lang { LangKey = "sec.2fa.status.on", ZhCN = "已启用", ZhTW = "已啟用", En = "Enabled", Ja = "有効", Ko = "활성화됨" },
        new Sys_Lang { LangKey = "sec.2fa.status.off", ZhCN = "未启用", ZhTW = "未啟用", En = "Disabled", Ja = "無効", Ko = "비활성화됨" },
        new Sys_Lang { LangKey = "sec.2fa.enrollTitle", ZhCN = "设置双因素认证", ZhTW = "設定雙因素認證", En = "Set Up Two-Factor Authentication", Ja = "二要素認証の設定", Ko = "2단계 인증 설정" },
        new Sys_Lang { LangKey = "sec.2fa.challengeTitle", ZhCN = "双因素验证", ZhTW = "雙因素驗證", En = "Two-Factor Verification", Ja = "二要素認証の確認", Ko = "2단계 인증 확인" },
        new Sys_Lang { LangKey = "sec.2fa.policyTitle", ZhCN = "双因素认证策略", ZhTW = "雙因素認證策略", En = "Two-Factor Policy", Ja = "二要素認証ポリシー", Ko = "2단계 인증 정책" },
        new Sys_Lang { LangKey = "sec.2fa.policy.off", ZhCN = "关闭", ZhTW = "關閉", En = "Off", Ja = "オフ", Ko = "사용 안 함" },
        new Sys_Lang { LangKey = "sec.2fa.policy.optional", ZhCN = "可选", ZhTW = "可選", En = "Optional", Ja = "任意", Ko = "선택" },
        new Sys_Lang { LangKey = "sec.2fa.policy.required", ZhCN = "强制", ZhTW = "強制", En = "Required", Ja = "必須", Ko = "필수" },
        new Sys_Lang { LangKey = "sec.2fa.confirmDisable", ZhCN = "确定要关闭双因素认证吗？", ZhTW = "確定要關閉雙因素認證嗎？", En = "Are you sure you want to disable two-factor authentication?", Ja = "二要素認証を無効化してもよろしいですか？", Ko = "2단계 인증을 비활성화하시겠습니까?" },
        new Sys_Lang { LangKey = "sec.2fa.currentPassword", ZhCN = "当前密码", ZhTW = "目前密碼", En = "Current Password", Ja = "現在のパスワード", Ko = "현재 비밀번호" },
        new Sys_Lang { LangKey = "sec.2fa.verify", ZhCN = "验证", ZhTW = "驗證", En = "Verify", Ja = "確認", Ko = "확인" },
        new Sys_Lang { LangKey = "sec.2fa.submit", ZhCN = "提交", ZhTW = "提交", En = "Submit", Ja = "送信", Ko = "제출" },
    };
}

using CP6.Entity.DomainModels.Sys;

namespace CP6.WebApi.Seed;

/// <summary>
/// S 类 #3 SSO 五语词条（T8）：E-SEC-020~029 错误码 + SecurityEventType 15~18 枚举标签 +
/// 登录 SSO 入口 / 落地屏 / 租户 SSO 配置页画面词条。仿 <see cref="I18nSecScreenSeed"/>（错误码用码本身作 key）。
/// 菜单名 nav.116 随菜单 seed 一并补（见 Program.cs 菜单段）。
/// </summary>
public static class I18nSecSsoScreenSeed
{
    public static readonly Sys_Lang[] Items = new[]
    {
        // ── 错误码 E-SEC-020~029（BizException 码即 key，spec §5）──
        new Sys_Lang { LangKey = "E-SEC-020", ZhCN = "该租户未配置或未启用 SSO", ZhTW = "該租戶未設定或未啟用 SSO", En = "SSO is not configured or not enabled for this tenant", Ja = "このテナントでは SSO が未設定または無効です", Ko = "이 테넌트에 SSO가 구성되지 않았거나 비활성화되어 있습니다" },
        new Sys_Lang { LangKey = "E-SEC-021", ZhCN = "该租户已强制 SSO，禁止密码登录", ZhTW = "該租戶已強制 SSO，禁止密碼登入", En = "This tenant enforces SSO; password login is disabled", Ja = "このテナントは SSO が必須です。パスワードログインは無効です", Ko = "이 테넌트는 SSO가 필수이며 비밀번호 로그인이 비활성화되어 있습니다" },
        new Sys_Lang { LangKey = "E-SEC-022", ZhCN = "SSO 登录状态无效或已过期，请重试", ZhTW = "SSO 登入狀態無效或已過期，請重試", En = "SSO state is invalid or expired, please retry", Ja = "SSO の状態が無効または期限切れです。再試行してください", Ko = "SSO 상태가 유효하지 않거나 만료되었습니다. 다시 시도하세요" },
        new Sys_Lang { LangKey = "E-SEC-023", ZhCN = "授权码换取令牌失败", ZhTW = "授權碼換取權杖失敗", En = "Failed to exchange authorization code for token", Ja = "認可コードからトークンへの交換に失敗しました", Ko = "인가 코드를 토큰으로 교환하지 못했습니다" },
        new Sys_Lang { LangKey = "E-SEC-024", ZhCN = "身份令牌验证失败", ZhTW = "身分權杖驗證失敗", En = "ID token validation failed", Ja = "ID トークンの検証に失敗しました", Ko = "ID 토큰 검증에 실패했습니다" },
        new Sys_Lang { LangKey = "E-SEC-025", ZhCN = "身份提供方未返回有效邮箱", ZhTW = "身分提供方未返回有效郵箱", En = "Identity provider did not return a verified email", Ja = "ID プロバイダーが有効なメールを返しませんでした", Ko = "ID 공급자가 인증된 이메일을 반환하지 않았습니다" },
        new Sys_Lang { LangKey = "E-SEC-026", ZhCN = "用户未预置，且未开启自动供给或未配置默认角色", ZhTW = "使用者未預置，且未開啟自動供給或未設定預設角色", En = "User not provisioned, and auto-provisioning is off or default role is not set", Ja = "ユーザーが未登録で、自動プロビジョニング無効または既定ロール未設定です", Ko = "사용자가 미등록 상태이며 자동 프로비저닝이 꺼져 있거나 기본 역할이 설정되지 않았습니다" },
        new Sys_Lang { LangKey = "E-SEC-027", ZhCN = "账号或租户已禁用", ZhTW = "帳號或租戶已停用", En = "Account or tenant is disabled", Ja = "アカウントまたはテナントが無効です", Ko = "계정 또는 테넌트가 비활성화되었습니다" },
        new Sys_Lang { LangKey = "E-SEC-028", ZhCN = "SSO 配置无效或身份提供方不可达", ZhTW = "SSO 設定無效或身分提供方不可達", En = "Invalid SSO configuration or identity provider unreachable", Ja = "SSO 設定が無効、または ID プロバイダーに到達できません", Ko = "SSO 구성이 잘못되었거나 ID 공급자에 연결할 수 없습니다" },
        new Sys_Lang { LangKey = "E-SEC-029", ZhCN = "联邦身份冲突（已绑定其他账号或邮箱命中多个账号）", ZhTW = "聯邦身分衝突（已綁定其他帳號或郵箱命中多個帳號）", En = "Federated identity conflict (already linked to another account, or email matches multiple accounts)", Ja = "フェデレーション ID の競合（他のアカウントに紐付け済み、またはメールが複数アカウントに一致）", Ko = "페더레이션 ID 충돌(다른 계정에 연결되어 있거나 이메일이 여러 계정과 일치)" },

        // ── SecurityEventType 15~18 枚举标签（前端 EventType int → sec.event.{n}）──
        new Sys_Lang { LangKey = "sec.event.15", ZhCN = "SSO 登录成功", ZhTW = "SSO 登入成功", En = "SSO Login Success", Ja = "SSO ログイン成功", Ko = "SSO 로그인 성공" },
        new Sys_Lang { LangKey = "sec.event.16", ZhCN = "SSO 登录失败", ZhTW = "SSO 登入失敗", En = "SSO Login Failed", Ja = "SSO ログイン失敗", Ko = "SSO 로그인 실패" },
        new Sys_Lang { LangKey = "sec.event.17", ZhCN = "SSO 用户已供给", ZhTW = "SSO 使用者已供給", En = "SSO User Provisioned", Ja = "SSO ユーザー登録", Ko = "SSO 사용자 프로비저닝" },
        new Sys_Lang { LangKey = "sec.event.18", ZhCN = "SSO 配置变更", ZhTW = "SSO 設定變更", En = "SSO Config Changed", Ja = "SSO 設定変更", Ko = "SSO 구성 변경" },

        // ── 登录 SSO 入口 + 落地屏词条 ──
        new Sys_Lang { LangKey = "sec.sso.loginButton", ZhCN = "使用 SSO 登录", ZhTW = "使用 SSO 登入", En = "Sign in with SSO", Ja = "SSO でログイン", Ko = "SSO로 로그인" },
        new Sys_Lang { LangKey = "sec.sso.tenantCodePrompt", ZhCN = "请先输入租户编码", ZhTW = "請先輸入租戶編碼", En = "Please enter your tenant code first", Ja = "先にテナントコードを入力してください", Ko = "먼저 테넌트 코드를 입력하세요" },
        new Sys_Lang { LangKey = "sec.sso.redirecting", ZhCN = "正在跳转至身份提供方…", ZhTW = "正在跳轉至身分提供方…", En = "Redirecting to identity provider…", Ja = "ID プロバイダーへリダイレクト中…", Ko = "ID 공급자로 이동 중…" },
        new Sys_Lang { LangKey = "sec.sso.landing", ZhCN = "正在完成登录…", ZhTW = "正在完成登入…", En = "Completing sign-in…", Ja = "ログインを完了しています…", Ko = "로그인 완료 중…" },
        new Sys_Lang { LangKey = "sec.sso.landingError", ZhCN = "SSO 登录失败", ZhTW = "SSO 登入失敗", En = "SSO sign-in failed", Ja = "SSO ログインに失敗しました", Ko = "SSO 로그인 실패" },
        new Sys_Lang { LangKey = "sec.sso.backToLogin", ZhCN = "返回登录", ZhTW = "返回登入", En = "Back to login", Ja = "ログインに戻る", Ko = "로그인으로 돌아가기" },

        // ── 租户 SSO 配置页词条 ──
        new Sys_Lang { LangKey = "sec.sso.settingsTitle", ZhCN = "SSO 配置", ZhTW = "SSO 設定", En = "SSO Configuration", Ja = "SSO 設定", Ko = "SSO 구성" },
        new Sys_Lang { LangKey = "sec.sso.settingsSubtitle", ZhCN = "配置本租户的 OIDC 单点登录身份提供方", ZhTW = "設定本租戶的 OIDC 單一登入身分提供方", En = "Configure this tenant's OIDC single sign-on identity provider", Ja = "このテナントの OIDC シングルサインオン ID プロバイダーを設定", Ko = "이 테넌트의 OIDC 싱글 사인온 ID 공급자 구성" },
        new Sys_Lang { LangKey = "sec.sso.authority", ZhCN = "颁发者地址（Authority）", ZhTW = "簽發者位址（Authority）", En = "Authority", Ja = "Authority（発行者）", Ko = "Authority(발급자)" },
        new Sys_Lang { LangKey = "sec.sso.clientId", ZhCN = "客户端 ID", ZhTW = "用戶端 ID", En = "Client ID", Ja = "クライアント ID", Ko = "클라이언트 ID" },
        new Sys_Lang { LangKey = "sec.sso.clientSecret", ZhCN = "客户端密钥", ZhTW = "用戶端密鑰", En = "Client Secret", Ja = "クライアントシークレット", Ko = "클라이언트 시크릿" },
        new Sys_Lang { LangKey = "sec.sso.secretSetHint", ZhCN = "已设置（留空则不修改）", ZhTW = "已設定（留空則不修改）", En = "Already set (leave blank to keep)", Ja = "設定済み（空欄のままで変更しません）", Ko = "이미 설정됨(비워두면 변경하지 않음)" },
        new Sys_Lang { LangKey = "sec.sso.scopes", ZhCN = "授权范围（Scopes）", ZhTW = "授權範圍（Scopes）", En = "Scopes", Ja = "スコープ", Ko = "스코프" },
        new Sys_Lang { LangKey = "sec.sso.emailClaim", ZhCN = "邮箱 Claim 名", ZhTW = "郵箱 Claim 名", En = "Email Claim", Ja = "メール Claim 名", Ko = "이메일 Claim 이름" },
        new Sys_Lang { LangKey = "sec.sso.enabled", ZhCN = "启用 SSO", ZhTW = "啟用 SSO", En = "Enable SSO", Ja = "SSO を有効化", Ko = "SSO 활성화" },
        new Sys_Lang { LangKey = "sec.sso.enforced", ZhCN = "强制 SSO（禁用密码登录）", ZhTW = "強制 SSO（停用密碼登入）", En = "Enforce SSO (disable password login)", Ja = "SSO を強制（パスワードログイン無効）", Ko = "SSO 강제(비밀번호 로그인 비활성화)" },
        new Sys_Lang { LangKey = "sec.sso.autoProvision", ZhCN = "自动供给新用户（JIT）", ZhTW = "自動供給新使用者（JIT）", En = "Auto-provision new users (JIT)", Ja = "新規ユーザーを自動プロビジョニング（JIT）", Ko = "신규 사용자 자동 프로비저닝(JIT)" },
        new Sys_Lang { LangKey = "sec.sso.defaultRole", ZhCN = "默认角色（自动供给时必填）", ZhTW = "預設角色（自動供給時必填）", En = "Default Role (required when auto-provisioning)", Ja = "既定ロール（自動プロビジョニング時は必須）", Ko = "기본 역할(자동 프로비저닝 시 필수)" },
        new Sys_Lang { LangKey = "sec.sso.save", ZhCN = "保存", ZhTW = "儲存", En = "Save", Ja = "保存", Ko = "저장" },
        new Sys_Lang { LangKey = "sec.sso.saveSuccess", ZhCN = "SSO 配置已保存", ZhTW = "SSO 設定已儲存", En = "SSO configuration saved", Ja = "SSO 設定を保存しました", Ko = "SSO 구성이 저장되었습니다" },

        // ── 菜单名 nav.116 ──
        new Sys_Lang { LangKey = "nav.116", ZhCN = "SSO 配置", ZhTW = "SSO 設定", En = "SSO Configuration", Ja = "SSO 設定", Ko = "SSO 구성" },
    };
}

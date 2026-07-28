using System.Text.Json;
using CP6.Client.Api;
using Microsoft.Extensions.Logging;

namespace CP6.Client.Core;

public interface ILanguageService
{
    string CurrentLanguage { get; }
    event EventHandler? LanguageChanged;
    Task LoadAsync(string language, CancellationToken ct = default);
    string this[string key] { get; }
}

public sealed class LanguageService : ILanguageService
{
    public static readonly IReadOnlySet<string> Supported =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "zh-CN", "zh-TW", "en", "ja", "ko" };

    private readonly Cp6ApiClient _api;
    private readonly ClientOptions _options;
    private readonly ILogger<LanguageService> _logger;
    private IReadOnlyDictionary<string, string> _strings = BuiltIn("zh-CN");

    public LanguageService(
        IHttpClientFactory clients,
        ClientOptions options,
        ILogger<LanguageService> logger)
    {
        _api = new Cp6ApiClient(clients.CreateClient(ClientServiceCollectionExtensions.RawClient));
        _options = options;
        _logger = logger;
    }

    public string CurrentLanguage { get; private set; } = "zh-CN";
    public event EventHandler? LanguageChanged;

    public string this[string key]
    {
        get
        {
            if (_strings.TryGetValue(key, out var value)) return value;
            _logger.LogDebug("Missing language key {LanguageKey} in {Language}", key, CurrentLanguage);
            return key;
        }
    }

    public async Task LoadAsync(string language, CancellationToken ct = default)
    {
        if (!Supported.Contains(language)) language = "zh-CN";
        Directory.CreateDirectory(_options.LanguageDirectory);
        Dictionary<string, string>? pack = null;
        try
        {
            var manifest = await _api.GetLanguageManifestAsync(ct);
            var cache = Path.Combine(
                _options.LanguageDirectory,
                $"{Safe(manifest.Version)}.{Safe(language)}.json");
            if (!string.IsNullOrWhiteSpace(manifest.Version))
            {
                pack = await _api.GetLanguagePackAsync(manifest.Version, language, ct);
                await File.WriteAllTextAsync(cache, JsonSerializer.Serialize(pack), ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Language pack download failed: {ErrorType}", ex.GetType().Name);
            var cache = Directory.GetFiles(
                    _options.LanguageDirectory,
                    $"*.{Safe(language)}.json")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (cache != null)
                pack = JsonSerializer.Deserialize<Dictionary<string, string>>(
                    await File.ReadAllTextAsync(cache, ct));
        }

        var merged = new Dictionary<string, string>(BuiltIn(language));
        if (pack != null)
        {
            foreach (var (key, value) in pack)
                merged[key] = value;
        }
        _strings = merged;
        CurrentLanguage = language;
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string Safe(string value)
        => string.Concat(value.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.'));

    internal static IReadOnlyDictionary<string, string> BuiltIn(string language)
    {
        var column = language.ToLowerInvariant() switch
        {
            "zh-tw" => 1,
            "en" => 2,
            "ja" => 3,
            "ko" => 4,
            _ => 0,
        };
        return NativeStrings.ToDictionary(
            pair => pair.Key,
            pair => pair.Value[column],
            StringComparer.Ordinal);
    }

    // Bootstrap strings keep the native shell usable before a newly published
    // server pack contains its client-specific keys. Server values override
    // these entries as soon as they are published.
    private static readonly IReadOnlyDictionary<string, string[]> NativeStrings =
        new Dictionary<string, string[]>
        {
            ["client.tenant"] = ["租户", "租戶", "Tenant", "テナント", "테넌트"],
            ["client.sso"] = ["使用企业 SSO", "使用企業 SSO", "Use company SSO", "会社 SSO を使用", "회사 SSO 사용"],
            ["client.twoFactor"] = ["双重验证", "雙重驗證", "Two-factor verification", "二要素認証", "2단계 인증"],
            ["client.verify"] = ["验证", "驗證", "Verify", "確認", "확인"],
            ["client.emailOtp"] = ["发送邮件验证码", "傳送郵件驗證碼", "Send email code", "メールコードを送信", "이메일 코드 보내기"],
            ["client.language"] = ["语言", "語言", "Language", "言語", "언어"],
            ["client.upgradeRequired"] = ["必须升级 CP6", "必須升級 CP6", "CP6 must be upgraded", "CP6 の更新が必要です", "CP6 업데이트가 필요합니다"],
            ["client.downloadUpdate"] = ["下载更新", "下載更新", "Download update", "更新をダウンロード", "업데이트 다운로드"],
            ["client.businessDisabled"] = ["升级前禁止进入业务页面", "升級前禁止進入業務頁面", "Business pages are disabled until upgraded", "更新まで業務画面は利用できません", "업데이트 전에는 업무 화면을 사용할 수 없습니다"],
            ["client.startupBlocked"] = ["无法验证客户端版本", "無法驗證用戶端版本", "Client version could not be verified", "クライアントのバージョンを確認できません", "클라이언트 버전을 확인할 수 없습니다"],
            ["client.retryStartup"] = ["重新检查", "重新檢查", "Check again", "再確認", "다시 확인"],
            ["client.currentVersion"] = ["当前版本", "目前版本", "Current version", "現在のバージョン", "현재 버전"],
            ["client.latestVersion"] = ["最新版本", "最新版本", "Latest version", "最新バージョン", "최신 버전"],
            ["client.minimumVersion"] = ["最低版本", "最低版本", "Minimum version", "最小バージョン", "최소 버전"],
            ["client.releaseHash"] = ["发布哈希", "發佈雜湊", "Release hash", "リリースハッシュ", "릴리스 해시"],
            ["client.taskControl"] = ["MOVE 任务控制", "MOVE 任務控制", "MOVE task control", "MOVE タスク管理", "MOVE 작업 관리"],
            ["client.assignee"] = ["操作员", "操作員", "Assignee", "担当者", "담당자"],
            ["client.assign"] = ["分配", "分配", "Assign", "割り当て", "할당"],
            ["client.openOnly"] = ["仅未完成", "僅未完成", "Open only", "未完了のみ", "미완료만"],
            ["client.applyFilters"] = ["应用筛选", "套用篩選", "Apply filters", "絞り込み", "필터 적용"],
            ["client.previous"] = ["上一页", "上一頁", "Previous", "前へ", "이전"],
            ["client.next"] = ["下一页", "下一頁", "Next", "次へ", "다음"],
            ["client.selectedTask"] = ["所选任务", "所選任務", "Selected task", "選択タスク", "선택한 작업"],
            ["client.createMove"] = ["创建 MOVE", "建立 MOVE", "Create MOVE", "MOVE を作成", "MOVE 생성"],
            ["client.claimStart"] = ["领取 / 开始", "領取 / 開始", "Claim / start", "取得 / 開始", "수령 / 시작"],
            ["client.simulatedScan"] = ["开始模拟扫码", "開始模擬掃碼", "Begin simulated scan", "模擬スキャンを開始", "모의 스캔 시작"],
            ["client.submitScan"] = ["提交扫码", "提交掃碼", "Submit scan", "スキャンを送信", "스캔 제출"],
            ["client.confirmQuantity"] = ["确认数量", "確認數量", "Confirm quantity", "数量を確認", "수량 확인"],
            ["client.completeMove"] = ["完成 MOVE", "完成 MOVE", "Complete MOVE", "MOVE を完了", "MOVE 완료"],
            ["client.reloadTask"] = ["重新查询任务", "重新查詢任務", "Reload task state", "タスクを再取得", "작업 다시 조회"],
            ["client.restartScan"] = ["重新扫码", "重新掃碼", "Restart scan", "スキャンをやり直す", "스캔 다시 시작"],
            ["client.scanSource"] = ["扫描源库位", "掃描來源庫位", "Scan source location", "移動元をスキャン", "출발 위치 스캔"],
            ["client.scanProduct"] = ["扫描产品", "掃描產品", "Scan product", "製品をスキャン", "제품 스캔"],
            ["client.scanTarget"] = ["扫描目标库位", "掃描目標庫位", "Scan target location", "移動先をスキャン", "도착 위치 스캔"],
            ["client.readyComplete"] = ["可以完成", "可以完成", "Ready to complete", "完了できます", "완료 준비됨"],
            ["client.completed"] = ["已完成", "已完成", "Completed", "完了", "완료됨"],
            ["login.username"] = ["用户名", "使用者名稱", "Username", "ユーザー名", "사용자 이름"],
            ["login.password"] = ["密码", "密碼", "Password", "パスワード", "비밀번호"],
            ["login.button"] = ["登录", "登入", "Sign in", "サインイン", "로그인"],
            ["layout.logout"] = ["退出登录", "登出", "Sign out", "サインアウト", "로그아웃"],
            ["wms.mobile.title"] = ["MOVE 任务", "MOVE 任務", "MOVE tasks", "MOVE タスク", "MOVE 작업"],
            ["wms.mobile.scan.ph"] = ["扫描或输入条码", "掃描或輸入條碼", "Scan or enter a barcode", "バーコードをスキャンまたは入力", "바코드를 스캔하거나 입력"],
            ["wms.common.refresh"] = ["刷新", "重新整理", "Refresh", "更新", "새로 고침"],
            ["wms.common.qty"] = ["数量", "數量", "Quantity", "数量", "수량"],
            ["wms.common.cancel"] = ["取消", "取消", "Cancel", "キャンセル", "취소"],
            ["wms.common.create"] = ["创建", "建立", "Create", "作成", "생성"],
            ["wms.common.status"] = ["状态", "狀態", "Status", "状態", "상태"],
            ["wms.common.warehouse"] = ["仓库", "倉庫", "Warehouse", "倉庫", "창고"],
            ["wms.common.from"] = ["来源", "來源", "From", "移動元", "출발"],
            ["wms.common.to"] = ["目标", "目標", "To", "移動先", "도착"],
            ["wms.common.product"] = ["产品", "產品", "Product", "製品", "제품"],
            ["wms.common.lot"] = ["批次", "批次", "Lot", "ロット", "로트"],
            ["client.signIn"] = ["登录", "登入", "Sign in", "サインイン", "로그인"],
            ["client.desktopTitle"] = ["CP6 WMS 桌面端", "CP6 WMS 桌面端", "CP6 WMS Desktop", "CP6 WMS デスクトップ", "CP6 WMS 데스크톱"],
            ["client.mobileTitle"] = ["CP6 WMS 移动端", "CP6 WMS 行動端", "CP6 WMS Mobile", "CP6 WMS モバイル", "CP6 WMS 모바일"],
            ["client.deviceActivation"] = ["设备激活", "裝置啟用", "Device activation", "デバイスの有効化", "기기 활성화"],
            ["client.activateWarehouseDevice"] = ["激活仓库设备", "啟用倉庫裝置", "Activate warehouse device", "倉庫デバイスを有効化", "창고 기기 활성화"],
            ["client.activateDevice"] = ["激活设备", "啟用裝置", "Activate device", "デバイスを有効化", "기기 활성화"],
            ["client.activationHelp"] = ["扫描管理员创建的一次性激活二维码。二维码包含可信服务器和租户。", "掃描管理員建立的一次性啟用 QR 碼。QR 碼包含可信伺服器與租戶。", "Scan the one-time activation QR created by an administrator. It supplies the trusted server and tenant.", "管理者が作成した一回限りの有効化 QR をスキャンしてください。信頼済みサーバーとテナントが設定されます。", "관리자가 만든 일회용 활성화 QR을 스캔하세요. 신뢰할 서버와 테넌트가 설정됩니다."],
            ["client.activationPayloadHint"] = ["粘贴 cp6-activate 二维码内容", "貼上 cp6-activate QR 碼內容", "Paste the cp6-activate QR payload", "cp6-activate QR の内容を貼り付け", "cp6-activate QR 내용을 붙여넣으세요"],
            ["client.deviceRecovery"] = ["设备激活 / 服务器恢复", "裝置啟用 / 伺服器復原", "Device activation / server recovery", "デバイス有効化 / サーバー復旧", "기기 활성화 / 서버 복구"],
            ["client.sharedQuickSwitch"] = ["共享设备快速切换", "共用裝置快速切換", "Shared device quick switch", "共有デバイスのクイック切替", "공용 기기 빠른 전환"],
            ["client.badge"] = ["工牌", "工牌", "Badge", "社員証", "사원증"],
            ["client.pin"] = ["6 位 PIN", "6 位 PIN", "6-digit PIN", "6 桁の PIN", "6자리 PIN"],
            ["client.quickSwitch"] = ["快速切换", "快速切換", "Quick switch", "クイック切替", "빠른 전환"],
            ["client.authenticatorSecret"] = ["验证器密钥：{0}", "驗證器密鑰：{0}", "Authenticator secret: {0}", "認証アプリのシークレット: {0}", "인증 앱 비밀 키: {0}"],
            ["client.verificationCode"] = ["验证码", "驗證碼", "Verification code", "確認コード", "인증 코드"],
            ["client.enrollmentRequired"] = ["需要绑定双重验证", "需要綁定雙重驗證", "Two-factor enrollment is required", "二要素認証の登録が必要です", "2단계 인증 등록이 필요합니다"],
            ["client.enterVerificationCode"] = ["请输入验证码", "請輸入驗證碼", "Enter the verification code", "確認コードを入力してください", "인증 코드를 입력하세요"],
            ["client.emailCodeSent"] = ["邮件验证码已发送", "郵件驗證碼已傳送", "Email verification code sent", "メール確認コードを送信しました", "이메일 인증 코드를 보냈습니다"],
            ["client.activatedDevice"] = ["设备已激活：{0}（{1}）", "裝置已啟用：{0}（{1}）", "Device activated: {0} ({1})", "デバイスを有効化しました: {0}（{1}）", "기기가 활성화되었습니다: {0} ({1})"],
            ["client.taskDetail"] = ["任务详情", "任務詳情", "Task detail", "タスク詳細", "작업 상세"],
            ["client.moveScanTitle"] = ["MOVE 扫码", "MOVE 掃碼", "MOVE scan", "MOVE スキャン", "MOVE 스캔"],
            ["client.camera"] = ["相机", "相機", "Camera", "カメラ", "카메라"],
            ["client.partialReason"] = ["短量完成时必须填写原因", "短量完成時必須填寫原因", "Reason required when completing a short quantity", "不足数量で完了する場合は理由が必要です", "부족 수량으로 완료할 때 사유가 필요합니다"],
            ["client.timeoutRetryGuidance"] = ["超时后请先重新查询任务状态再重试。在结果确认前，完成操作 ID 保持不变。", "逾時後請先重新查詢任務狀態再重試。在結果確認前，完成操作 ID 保持不變。", "After a timeout, reload task state before retrying. The completion operation ID is retained until the outcome is known.", "タイムアウト後は再試行前にタスク状態を再取得してください。結果が確定するまで完了操作 ID は保持されます。", "시간 초과 후 재시도하기 전에 작업 상태를 다시 조회하세요. 결과가 확인될 때까지 완료 작업 ID가 유지됩니다."],
            ["client.scanLot"] = ["扫描批次", "掃描批次", "Scan lot", "ロットをスキャン", "로트 스캔"],
            ["client.moveCompleted"] = ["MOVE 已完成", "MOVE 已完成", "MOVE completed", "MOVE が完了しました", "MOVE 완료"],
            ["client.updateOpened"] = ["已打开更新下载", "已開啟更新下載", "Update download opened", "更新のダウンロードを開きました", "업데이트 다운로드를 열었습니다"],
            ["client.startupChecking"] = ["正在检查客户端发布策略…", "正在檢查用戶端發佈策略…", "Checking client release policy…", "クライアントのリリースポリシーを確認中…", "클라이언트 릴리스 정책을 확인하는 중…"],
            ["client.minimumVersionValue"] = ["最低版本：{0}", "最低版本：{0}", "Minimum version: {0}", "最小バージョン: {0}", "최소 버전: {0}"],
            ["client.offline"] = ["离线", "離線", "Offline", "オフライン", "오프라인"],
            ["client.online"] = ["在线", "線上", "Online", "オンライン", "온라인"],
            ["client.reconnecting"] = ["正在重新连接", "正在重新連線", "Reconnecting", "再接続中", "다시 연결하는 중"],
            ["client.retrying"] = ["正在重试", "正在重試", "Retrying", "再試行中", "재시도 중"],
            ["client.rejected"] = ["设备已被拒绝", "裝置已被拒絕", "Device rejected", "デバイスが拒否されました", "기기가 거부되었습니다"],
            ["client.notActivated"] = ["未激活", "未啟用", "Not activated", "未有効化", "활성화되지 않음"],
            ["client.stopped"] = ["已停止", "已停止", "Stopped", "停止", "중지됨"],
            ["client.running"] = ["运行中", "執行中", "Running", "実行中", "실행 중"],
            ["client.allWarehouses"] = ["全部仓库", "全部倉庫", "All warehouses", "すべての倉庫", "모든 창고"],
            ["client.printGatewayStart"] = ["启动打印网关", "啟動列印閘道", "Start print gateway", "印刷ゲートウェイを開始", "인쇄 게이트웨이 시작"],
            ["client.printGatewayStop"] = ["停止打印网关", "停止列印閘道", "Stop print gateway", "印刷ゲートウェイを停止", "인쇄 게이트웨이 중지"],
            ["client.assigneeFilter"] = ["按操作员筛选", "依操作員篩選", "Filter by assignee", "担当者で絞り込み", "담당자 필터"],
            ["client.actionReason"] = ["操作原因", "操作原因", "Action reason", "操作理由", "작업 사유"],
            ["client.pause"] = ["暂停", "暫停", "Pause", "一時停止", "일시 중지"],
            ["client.release"] = ["释放", "釋放", "Release", "解放", "해제"],
            ["client.takeover"] = ["接管", "接管", "Take over", "引き継ぐ", "인수"],
            ["client.exception"] = ["异常", "異常", "Exception", "例外", "예외"],
            ["client.productionControls"] = ["生产控制", "生產控制", "Production controls", "本番運用管理", "운영 제어"],
            ["client.loadProductionOverview"] = ["加载设备、条码与分析", "載入裝置、條碼與分析", "Load devices, barcodes, and analytics", "デバイス、バーコード、分析を読み込む", "기기, 바코드 및 분석 불러오기"],
            ["client.productionOverviewNotLoaded"] = ["尚未加载生产概览", "尚未載入生產概覽", "Production overview not loaded", "本番運用の概要は未読込です", "운영 개요를 불러오지 않았습니다"],
            ["client.controlledDevices"] = ["受控设备", "受控裝置", "Controlled devices", "管理対象デバイス", "관리 기기"],
            ["client.toggleDevice"] = ["启用 / 停用所选设备", "啟用 / 停用所選裝置", "Enable / disable selected device", "選択デバイスの有効 / 無効を切替", "선택한 기기 활성화 / 비활성화"],
            ["client.barcodeMapping"] = ["条码映射", "條碼對應", "Barcode mapping", "バーコード対応", "바코드 매핑"],
            ["client.saveBarcodeBinding"] = ["保存条码绑定", "儲存條碼綁定", "Save barcode binding", "バーコード紐付けを保存", "바코드 연결 저장"],
            ["client.productionSummary"] = ["已创建 {0} · 已完成 {1} · 短量 {2} · 异常 {3} · 逾期 {4} · 平均 {5:F1} 分钟", "已建立 {0} · 已完成 {1} · 短量 {2} · 異常 {3} · 逾期 {4} · 平均 {5:F1} 分鐘", "Created {0} · Completed {1} · Partial {2} · Exceptions {3} · Overdue {4} · Avg {5:F1} min", "作成 {0} · 完了 {1} · 一部 {2} · 例外 {3} · 期限超過 {4} · 平均 {5:F1} 分", "생성 {0} · 완료 {1} · 부분 {2} · 예외 {3} · 기한 초과 {4} · 평균 {5:F1}분"],
        };
}

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

    private static IReadOnlyDictionary<string, string> BuiltIn(string language)
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
        };
}

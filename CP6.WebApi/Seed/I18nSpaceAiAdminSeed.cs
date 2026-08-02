using CP6.Entity.DomainModels;

namespace CP6.WebApi.Seed;

public static class I18nSpaceAiAdminSeed
{
    public static readonly Sys_Lang[] Items =
    [
        L("space.aiAdmin.title", "AI 策略、预算与用量", "AI policy, budgets, and usage", "AI ポリシー・予算・使用量", "AI 정책, 예산 및 사용량"),
        L("space.aiAdmin.refresh", "刷新", "Refresh", "更新", "새로 고침"),
        L("space.aiAdmin.safetyTitle", "部署托管的 Provider", "Deployment-managed providers", "デプロイ管理プロバイダー", "배포 관리 공급자"),
        L("space.aiAdmin.safetyDescription", "租户只能选择已批准的别名；Endpoint、URL 和密钥不在此页面收集。", "Tenants may select approved aliases only. Endpoints, URLs, and keys are never collected here.", "テナントは承認済みエイリアスのみ選択できます。URL やキーは収集しません。", "테넌트는 승인된 별칭만 선택할 수 있으며 URL과 키는 수집하지 않습니다."),
        L("space.aiAdmin.totalRuns", "运行总数", "Total runs", "実行総数", "총 실행 수"),
        L("space.aiAdmin.units", "输入 {input} / 输出 {output}", "Input {input} / output {output}", "入力 {input} / 出力 {output}", "입력 {input} / 출력 {output}"),
        L("space.aiAdmin.dailyBudget", "每日预算余额", "Daily budget remaining", "日次予算残高", "일일 예산 잔액"),
        L("space.aiAdmin.monthlyBudget", "每月预算余额", "Monthly budget remaining", "月次予算残高", "월 예산 잔액"),
        L("space.aiAdmin.costStatus", "费用状态", "Cost status", "コスト状態", "비용 상태"),
        L("space.aiAdmin.includesUnpriced", "包含未定价用量", "Includes unpriced usage", "未価格使用量あり", "가격 미정 사용량 포함"),
        L("space.aiAdmin.priced", "用量均已定价", "All usage is priced", "全使用量が価格設定済み", "모든 사용량 가격 책정됨"),
        L("space.aiAdmin.actualVsEstimated", "实际费用优先；缺失时显示估算", "Actual cost is preferred; estimated cost is shown when absent", "実コストを優先し、未取得時は見積を表示", "실제 비용을 우선하고 없으면 예상 비용 표시"),
        L("space.aiAdmin.policyTitle", "租户 AI 策略", "Tenant AI policy", "テナント AI ポリシー", "테넌트 AI 정책"),
        L("space.aiAdmin.policyVersion", "当前版本：{version}", "Current version: {version}", "現在のバージョン: {version}", "현재 버전: {version}"),
        L("space.aiAdmin.dataPolicy", "数据策略", "Data policy", "データポリシー", "데이터 정책"),
        L("space.aiAdmin.allowedSites", "允许的站点", "Allowed sites", "許可サイト", "허용 사이트"),
        L("space.aiAdmin.chooseSites", "选择可使用 AI 的站点", "Select sites that may use AI", "AI を使用できるサイトを選択", "AI 사용 사이트 선택"),
        L("space.aiAdmin.providers", "批准的 Provider 别名", "Approved provider aliases", "承認済みプロバイダーエイリアス", "승인된 공급자 별칭"),
        L("space.aiAdmin.chooseProviders", "选择部署侧批准的别名", "Select deployment-approved aliases", "デプロイ承認済みエイリアスを選択", "배포 승인 별칭 선택"),
        L("space.aiAdmin.noProviders", "当前部署没有批准的 Provider；策略只能保持关闭。", "No providers are approved for this deployment; the policy must remain disabled.", "承認済みプロバイダーがないため、ポリシーは無効のままです。", "승인된 공급자가 없어 정책을 비활성 상태로 유지해야 합니다."),
        L("space.aiAdmin.concurrency", "最大并发运行数", "Maximum concurrent runs", "最大同時実行数", "최대 동시 실행 수"),
        L("space.aiAdmin.externalProvider", "外部 Provider", "External provider", "外部プロバイダー", "외부 공급자"),
        L("space.aiAdmin.externalDerived", "由所选批准别名自动确定。", "Derived automatically from selected approved aliases.", "選択した承認済みエイリアスから自動判定します。", "선택한 승인 별칭에서 자동 결정됩니다."),
        L("space.aiAdmin.dailyLimit", "每日预算（最小货币单位）", "Daily budget (minor currency units)", "日次予算（通貨最小単位）", "일일 예산(최소 통화 단위)"),
        L("space.aiAdmin.monthlyLimit", "每月预算（最小货币单位）", "Monthly budget (minor currency units)", "月次予算（通貨最小単位）", "월 예산(최소 통화 단위)"),
        L("space.aiAdmin.currency", "币种", "Currency", "通貨", "통화"),
        L("space.aiAdmin.minorUnits", "预算使用 ISO 4217 币种及最小货币单位；留空表示不设金额上限。", "Budgets use an ISO 4217 currency and minor units. Leave both limits empty for no monetary cap.", "予算は ISO 4217 通貨と最小単位を使用します。空欄は上限なしです。", "예산은 ISO 4217 통화와 최소 단위를 사용하며 비워 두면 금액 제한이 없습니다."),
        L("space.aiAdmin.savePolicy", "保存策略", "Save policy", "ポリシーを保存", "정책 저장"),
        L("space.aiAdmin.usageTitle", "最近 30 天用量", "Usage in the last 30 days", "過去 30 日間の使用量", "최근 30일 사용량"),
        L("space.aiAdmin.usageDescription", "按 Provider 和结果筛选；列表不会显示请求正文或敏感配置。", "Filter by provider and outcome. Request bodies and sensitive configuration are never shown.", "プロバイダーと結果で絞り込みます。本文や機密設定は表示しません。", "공급자와 결과로 필터링하며 요청 본문과 민감한 설정은 표시하지 않습니다."),
        L("space.aiAdmin.allProviders", "全部 Provider", "All providers", "全プロバイダー", "모든 공급자"),
        L("space.aiAdmin.allOutcomes", "全部结果", "All outcomes", "全結果", "모든 결과"),
        L("space.aiAdmin.succeeded", "成功", "Succeeded", "成功", "성공"),
        L("space.aiAdmin.failed", "失败", "Failed", "失敗", "실패"),
        L("space.aiAdmin.unknown", "未知", "Unknown", "不明", "알 수 없음"),
        L("space.aiAdmin.apply", "应用筛选", "Apply filters", "フィルターを適用", "필터 적용"),
        L("space.aiAdmin.recordedAt", "记录时间", "Recorded at", "記録日時", "기록 시간"),
        L("space.aiAdmin.provider", "Provider", "Provider", "プロバイダー", "공급자"),
        L("space.aiAdmin.model", "模型", "Model", "モデル", "모델"),
        L("space.aiAdmin.usageUnits", "输入 / 输出单位", "Input / output units", "入力 / 出力単位", "입력 / 출력 단위"),
        L("space.aiAdmin.cost", "费用", "Cost", "コスト", "비용"),
        L("space.aiAdmin.latency", "延迟（毫秒）", "Latency (ms)", "レイテンシ (ms)", "지연 시간(ms)"),
        L("space.aiAdmin.outcome", "结果", "Outcome", "結果", "결과"),
        L("space.aiAdmin.noUsage", "暂无用量记录", "No usage records", "使用量記録はありません", "사용량 기록 없음"),
        L("space.aiAdmin.noLimit", "未设置上限", "No limit", "上限なし", "제한 없음"),
        L("space.aiAdmin.remaining", "剩余 {amount}", "{amount} remaining", "残り {amount}", "잔액 {amount}"),
        L("space.aiAdmin.consumed", "已用 {amount}", "{amount} consumed", "使用済み {amount}", "사용 {amount}"),
        L("space.aiAdmin.unpriced", "未定价：仅显示单位", "Unpriced: units only", "未価格: 単位のみ", "가격 미정: 단위만 표시"),
        L("space.aiAdmin.estimated", "估算 {amount}", "Estimated {amount}", "見積 {amount}", "예상 {amount}"),
        L("space.aiAdmin.actual", "实际 {amount}", "Actual {amount}", "実績 {amount}", "실제 {amount}"),
        L("space.aiAdmin.saved", "AI 策略已保存", "AI policy saved", "AI ポリシーを保存しました", "AI 정책이 저장되었습니다"),
        L("space.aiAdmin.policyDisabled", "关闭", "Disabled", "無効", "비활성"),
        L("space.aiAdmin.metadataOnly", "仅元数据", "Metadata only", "メタデータのみ", "메타데이터만"),
        L("space.aiAdmin.structuredFeatures", "结构化特征", "Structured features", "構造化特徴", "구조화된 특징"),
    ];

    private static Sys_Lang L(
        string key,
        string zh,
        string en,
        string ja,
        string ko) =>
        new()
        {
            LangKey = key,
            ZhCN = zh,
            ZhTW = zh,
            En = en,
            Ja = ja,
            Ko = ko,
        };
}

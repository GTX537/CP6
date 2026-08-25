export type LoginLocale = 'zh-CN' | 'zh-TW' | 'en' | 'ja' | 'ko'

export interface LoginFlowNodeCopy {
  title: string
  detail: string
}

export interface LoginExperienceCopy {
  accessStatus: string
  heroLine: string
  heroAccent: string
  leadPrefix: string
  leadStrong: string
  leadSuffix: string
  flowTitle: string
  connected: string
  flowNodes: readonly LoginFlowNodeCopy[]
  foundations: readonly string[]
  capabilities: readonly string[]
  tenantLabel: string
  tenantAutomatic: string
  collapseTenant: string
  secureTransport: string
  twoFactorPolicy: string
  divider: string
  securityItems: readonly string[]
  accessNotice: string
  platformTagline: string
  showPassword: string
  hidePassword: string
}

const copies: Record<LoginLocale, LoginExperienceCopy> = {
  'zh-CN': {
    accessStatus: '安全访问入口',
    heroLine: '让订单、生产、仓储与财务',
    heroAccent: '在同一条业务链上运行',
    leadPrefix: 'CP6 是面向',
    leadStrong: '纸箱包装制造',
    leadSuffix: '的核心运营平台，连接销售受注、物料计划、生产执行、库存物流、采购、财务与协同审批，并以 Space 数字底座承载仓库空间与运行态。',
    flowTitle: 'CORE OPERATING FLOW · 核心业务链',
    connected: '已连接',
    flowNodes: [
      { title: 'ERP 受注', detail: '销售 · 客户 · 价格' },
      { title: 'MRP 计划', detail: '物料 · 采购建议' },
      { title: 'MES 生产', detail: '排程 · 实绩 · 品质' },
      { title: 'WMS 物流', detail: '出入库 · 库存 · 作业' },
      { title: 'FIN 财务', detail: '应收 · 应付 · 成本' },
    ],
    foundations: ['采购 · PR / RFQ / PO', 'OA · PUB · 多租户', 'Space · 2D / 3D 数字底座'],
    capabilities: ['Web 管理端', 'Windows 调度端', 'Android 作业端', 'SSO · 2FA · 审计'],
    tenantLabel: '企业空间 / TENANT',
    tenantAutomatic: '自动识别',
    collapseTenant: '收起',
    secureTransport: '凭据加密传输',
    twoFactorPolicy: '登录后按策略进行 2FA',
    divider: '或',
    securityItems: ['多租户隔离', '权限与审计', '五种语言'],
    accessNotice: '仅限已授权用户访问。系统操作将依据租户、角色与数据范围进行记录。',
    platformTagline: '包装制造一体化运营平台',
    showPassword: '显示密码',
    hidePassword: '隐藏密码',
  },
  'zh-TW': {
    accessStatus: '安全存取入口',
    heroLine: '讓訂單、生產、倉儲與財務',
    heroAccent: '在同一條業務鏈上運行',
    leadPrefix: 'CP6 是面向',
    leadStrong: '紙箱包裝製造',
    leadSuffix: '的核心營運平台，連接銷售受注、物料計畫、生產執行、庫存物流、採購、財務與協同審批，並以 Space 數位底座承載倉庫空間與運行態。',
    flowTitle: 'CORE OPERATING FLOW · 核心業務鏈',
    connected: '已連接',
    flowNodes: [
      { title: 'ERP 受注', detail: '銷售 · 客戶 · 價格' },
      { title: 'MRP 計畫', detail: '物料 · 採購建議' },
      { title: 'MES 生產', detail: '排程 · 實績 · 品質' },
      { title: 'WMS 物流', detail: '出入庫 · 庫存 · 作業' },
      { title: 'FIN 財務', detail: '應收 · 應付 · 成本' },
    ],
    foundations: ['採購 · PR / RFQ / PO', 'OA · PUB · 多租戶', 'Space · 2D / 3D 數位底座'],
    capabilities: ['Web 管理端', 'Windows 調度端', 'Android 作業端', 'SSO · 2FA · 稽核'],
    tenantLabel: '企業空間 / TENANT',
    tenantAutomatic: '自動識別',
    collapseTenant: '收起',
    secureTransport: '憑證加密傳輸',
    twoFactorPolicy: '登入後依策略進行 2FA',
    divider: '或',
    securityItems: ['多租戶隔離', '權限與稽核', '五種語言'],
    accessNotice: '僅限已授權使用者存取。系統操作將依租戶、角色與資料範圍進行記錄。',
    platformTagline: '包裝製造一體化營運平台',
    showPassword: '顯示密碼',
    hidePassword: '隱藏密碼',
  },
  en: {
    accessStatus: 'Secure access portal',
    heroLine: 'Orders, production, warehouse and finance',
    heroAccent: 'running on one connected chain',
    leadPrefix: 'CP6 is the core operations platform for',
    leadStrong: 'corrugated packaging manufacturing',
    leadSuffix: ', connecting sales orders, material planning, production execution, warehouse logistics, procurement, finance and approvals, with Space as the digital foundation for warehouse operations.',
    flowTitle: 'CORE OPERATING FLOW',
    connected: 'Connected',
    flowNodes: [
      { title: 'ERP Orders', detail: 'Sales · Customer · Price' },
      { title: 'MRP Planning', detail: 'Materials · Purchasing' },
      { title: 'MES Production', detail: 'Schedule · Output · Quality' },
      { title: 'WMS Logistics', detail: 'Inbound · Stock · Tasks' },
      { title: 'FIN Finance', detail: 'AR · AP · Cost' },
    ],
    foundations: ['Procurement · PR / RFQ / PO', 'OA · PUB · Multi-tenant', 'Space · 2D / 3D foundation'],
    capabilities: ['Web administration', 'Windows dispatch', 'Android operations', 'SSO · 2FA · Audit'],
    tenantLabel: 'WORKSPACE / TENANT',
    tenantAutomatic: 'Detected automatically',
    collapseTenant: 'Collapse',
    secureTransport: 'Encrypted credential transport',
    twoFactorPolicy: '2FA follows workspace policy',
    divider: 'OR',
    securityItems: ['Tenant isolation', 'Access & audit', 'Five languages'],
    accessNotice: 'Authorized users only. Activity is recorded according to workspace, role and data scope.',
    platformTagline: 'Integrated packaging operations platform',
    showPassword: 'Show password',
    hidePassword: 'Hide password',
  },
  ja: {
    accessStatus: 'セキュアアクセス',
    heroLine: '受注・生産・倉庫・財務を',
    heroAccent: 'ひとつの業務チェーンで運用',
    leadPrefix: 'CP6 は',
    leadStrong: '段ボール包装製造',
    leadSuffix: '向けの中核業務プラットフォームです。販売受注、資材計画、生産実行、在庫物流、購買、財務、承認をつなぎ、Space が倉庫空間と稼働状況のデジタル基盤を担います。',
    flowTitle: 'CORE OPERATING FLOW · 中核業務チェーン',
    connected: '接続済み',
    flowNodes: [
      { title: 'ERP 受注', detail: '販売 · 顧客 · 価格' },
      { title: 'MRP 計画', detail: '資材 · 購買提案' },
      { title: 'MES 生産', detail: '日程 · 実績 · 品質' },
      { title: 'WMS 物流', detail: '入出庫 · 在庫 · 作業' },
      { title: 'FIN 財務', detail: '売掛 · 買掛 · 原価' },
    ],
    foundations: ['購買 · PR / RFQ / PO', 'OA · PUB · マルチテナント', 'Space · 2D / 3D デジタル基盤'],
    capabilities: ['Web 管理', 'Windows 配車・指示', 'Android 現場作業', 'SSO · 2FA · 監査'],
    tenantLabel: '企業ワークスペース / TENANT',
    tenantAutomatic: '自動判定',
    collapseTenant: '閉じる',
    secureTransport: '認証情報を暗号化して送信',
    twoFactorPolicy: 'ログイン後にポリシーに従い 2FA',
    divider: 'または',
    securityItems: ['テナント分離', '権限と監査', '5 言語対応'],
    accessNotice: '許可されたユーザーのみアクセスできます。操作はテナント、権限、データ範囲に基づいて記録されます。',
    platformTagline: '包装製造統合オペレーション基盤',
    showPassword: 'パスワードを表示',
    hidePassword: 'パスワードを隠す',
  },
  ko: {
    accessStatus: '보안 접속',
    heroLine: '주문·생산·창고·재무를',
    heroAccent: '하나의 업무 체인으로 운영',
    leadPrefix: 'CP6는',
    leadStrong: '골판지 포장 제조',
    leadSuffix: '를 위한 핵심 운영 플랫폼입니다. 영업 주문, 자재 계획, 생산 실행, 재고 물류, 구매, 재무와 승인을 연결하며 Space가 창고 공간과 운영 상태의 디지털 기반을 제공합니다.',
    flowTitle: 'CORE OPERATING FLOW · 핵심 업무 체인',
    connected: '연결됨',
    flowNodes: [
      { title: 'ERP 주문', detail: '영업 · 고객 · 가격' },
      { title: 'MRP 계획', detail: '자재 · 구매 제안' },
      { title: 'MES 생산', detail: '일정 · 실적 · 품질' },
      { title: 'WMS 물류', detail: '입출고 · 재고 · 작업' },
      { title: 'FIN 재무', detail: '매출 · 매입 · 원가' },
    ],
    foundations: ['구매 · PR / RFQ / PO', 'OA · PUB · 멀티테넌트', 'Space · 2D / 3D 디지털 기반'],
    capabilities: ['Web 관리', 'Windows 배차', 'Android 현장 작업', 'SSO · 2FA · 감사'],
    tenantLabel: '기업 워크스페이스 / TENANT',
    tenantAutomatic: '자동 식별',
    collapseTenant: '접기',
    secureTransport: '인증 정보 암호화 전송',
    twoFactorPolicy: '로그인 후 정책에 따라 2FA 진행',
    divider: '또는',
    securityItems: ['테넌트 격리', '권한 및 감사', '5개 언어'],
    accessNotice: '승인된 사용자만 접근할 수 있습니다. 작업은 테넌트, 역할과 데이터 범위에 따라 기록됩니다.',
    platformTagline: '포장 제조 통합 운영 플랫폼',
    showPassword: '비밀번호 표시',
    hidePassword: '비밀번호 숨기기',
  },
}

export function getLoginExperienceCopy(locale: string): LoginExperienceCopy {
  if (locale === 'pseudo') return copies.en
  return copies[locale as LoginLocale] ?? copies.en
}

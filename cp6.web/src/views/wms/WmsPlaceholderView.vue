<template>
  <div class="wms-placeholder">
    <el-empty :description="' '">
      <template #image>
        <div class="phase-icon">🚧</div>
      </template>
      <template #description>
        <div class="title">{{ menuName }}</div>
        <div class="subtitle">{{ phaseLabel }} {{ t('wms.placeholder.comingSoon') }}</div>
      </template>

      <div class="info">
        <p>{{ t('wms.placeholder.inDev') }}</p>
        <p v-if="hint" class="hint">{{ hint }}</p>
      </div>

      <el-space>
        <el-button @click="$router.push('/wms/dashboard')" type="primary">{{ t('wms.placeholder.toDashboard') }}</el-button>
        <el-button @click="$router.back()">{{ t('wms.placeholder.back') }}</el-button>
      </el-space>
    </el-empty>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'

const route = useRoute()
const { t } = useI18n()

const pathPhaseMap: Record<string, { phase: string; hint?: string }> = {
  '/wms/product-inbound':      { phase: 'Phase WM-3', hint: '入庫実績画面 (Phase WM-2) で SourceType=PRODUCTION を選択すると同等の操作が可能です。' },
  '/wms/shipping-order-list':  { phase: 'Phase WM-3', hint: '出庫指示 一覧 で「区分=出荷」フィルタを使用してください。' },
  '/wms/shipping-order':       { phase: 'Phase WM-3', hint: '出庫指示 登録 で「出庫区分=出荷」を選択してください。' },
  '/wms/picking':              { phase: 'Phase WM-3', hint: 'ピッキング作業は出庫指示画面の「引当実行」→「出庫確定」フローで実施できます。' },
  '/wms/packaging':            { phase: 'Phase WM-3', hint: '梱包は出庫指示画面の「出庫確定」時に自動採番されます。' },
  '/wms/inspection':           { phase: 'Phase WM-5', hint: 'AQL 抽样検査による入荷検品機能。' },
  '/wms/slotting':             { phase: 'Phase WM-5', hint: 'ABC 分析 + 重量バランスによる棚配置最適化。' },
  '/wms/replenish':            { phase: 'Phase WM-5', hint: 'ピッキング棚 ← 保管棚 の補充指示自動生成。' },
  '/wms/cross-dock':           { phase: 'Phase WM-5', hint: '入庫即出荷（在庫を経由しない）の高効率モード。' },
  '/wms/kit':                  { phase: 'Phase WM-5', hint: 'キット品（複数 SKU を 1 つの製品として梱包）の組立・バラシ。' },
  '/wms/rma':                  { phase: 'Phase WM-5', hint: 'RMA 返品受付 → 検査 → 再販/修理/廃棄 のワークフロー。' },
  '/wms/lot-trace':            { phase: 'Phase WM-5', hint: 'ロットNO の上下流追溯（仕入 → 製造 → 出荷顧客）。' },
  '/wms/expiry':               { phase: 'Phase WM-5', hint: '賞味期限 FEFO 引当 + 期限切れ自動廃棄。' },
  '/wms/paper-roll':           { phase: 'Phase WM-6', hint: '原紙ロール（巾/流れ/残米長）専用管理。スリッター巾割り対応。' },
  '/wms/remnant':              { phase: 'Phase WM-6', hint: '裁断残・端材の再利用引当。' },
  '/wms/plate-mold-stock':     { phase: 'Phase WM-6', hint: '印版・木型の物理保管位置 + 貸出履歴管理。' },
  '/wms/ink-lot':              { phase: 'Phase WM-6', hint: 'インキ・接着剤の色番号・混合ロット管理。' },
  '/wms/pallet':               { phase: 'Phase WM-6', hint: 'パレット単位の段積み・出荷待機ゾーン管理。' },
  '/wms/vmi':                  { phase: 'Phase WM-6', hint: '客先預り在庫（所有権分離）+ 月次保管料計算。' },
  '/wms/sample-stock':         { phase: 'Phase WM-6', hint: '試作・営業サンプルの貸出ボード。' },
  '/wms/mobile-task':          { phase: 'Phase WM-7', hint: 'RF ハンディ / タブレット作業指示。PWA + オフライン対応予定。' },
  '/wms/wcs-task':             { phase: 'Phase WM-7', hint: 'AGV / AS-RS 自動倉庫との MQTT 連携。' },
  '/wms/carrier':              { phase: 'Phase WM-7', hint: 'ヤマト / 佐川 / 福通 の送り状 API 連携。' },
  '/wms/iot-monitor':          { phase: 'Phase WM-7', hint: 'IoT 温湿度センサーのリアルタイム監視 + 警報。' },
  '/wms/report-center':        { phase: 'Phase WM-8', hint: '在庫月報 / ABC 分析 / 滞留品 / 在庫評価 / ロット追溯 など 10 種類の帳票。' },
}

const menuName = computed(() => {
  const title = route.meta?.title
  if (typeof title === 'string') return title
  return route.path.split('/').pop() || 'Coming Soon'
})

const phaseInfo = computed(() => pathPhaseMap[route.path] || { phase: 'Phase 未定' })
const phaseLabel = computed(() => phaseInfo.value.phase)
const hint = computed(() => phaseInfo.value.hint)
</script>

<style scoped>
.wms-placeholder { display: flex; align-items: center; justify-content: center; min-height: calc(100vh - 100px); padding: 24px; }
.phase-icon { font-size: 80px; line-height: 1; margin-bottom: 8px; }
.title { font-size: 22px; font-weight: 700; color: #303133; margin-bottom: 4px; }
.subtitle { font-size: 14px; color: #909399; margin-bottom: 16px; }
.info { margin: 12px 0 24px; text-align: center; color: #606266; }
.info .hint { margin-top: 12px; padding: 12px 16px; background: #f4f7fa; border-left: 3px solid #409eff; border-radius: 4px; max-width: 600px; text-align: left; }
</style>

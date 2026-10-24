<template>
  <CpPageShell title="出庫ルーティング原記録" :count="result?.total || 0">
    <template #actions><el-button :disabled="!canRead" :loading="loading" @click="refresh">再取得</el-button></template>
    <el-alert type="info" :closable="false" title="現在参照できる既存のルールを表示します。正式なルート方針の承認・有効化・候補プレビューは、連携の準備が整うまで利用できません。" />
    <p v-if="!canRead" role="status">ルーティングと倉庫の参照権限が必要です。</p>
    <div class="filters">
      <label>引当先倉庫
        <el-select v-model="warehouseId" clearable filterable remote :remote-method="loadWarehouses" :loading="warehouseLoading" :disabled="!canRead" aria-label="引当先倉庫">
          <el-option v-for="w in warehouses" :key="w.id" :value="w.id" :label="`${w.warehouseCd} ${w.nameReadable ? w.warehouseName || '' : '名称参照不可'}`" />
        </el-select>
      </label>
      <label>旧ルール状態 <select v-model="enabled" :disabled="!canRead" aria-label="旧ルール状態"><option value="all">すべて</option><option value="true">有効</option><option value="false">無効</option></select></label>
      <el-button :disabled="!canRead" @click="load(1)">検索</el-button>
    </div>
    <p v-if="warehouseTotal > warehouses.length">倉庫候補は先頭200件です。コードを絞って検索してください。</p>
    <p v-if="warehouseFailed">倉庫候補を取得できません。</p>
    <p v-if="error" role="alert">{{ error }}</p>
    <template v-if="result">
      <p>現在の権限・条件で {{ result.total }} 件。全社共通の一覧や固定時点のスナップショットではありません。</p>
      <p>既存の多倉庫解決機能：{{ result.legacyResolverEnabled ? '有効' : '無効（単一倉庫の従来動作）' }}。下記の有効フラグは正式な方針の承認・稼働状態を示しません。</p>
      <el-table :data="result.items" row-key="id" @row-click="openDetail">
        <el-table-column label="ルール名"><template #default="{ row }">{{ row.nameReadable ? row.ruleName || '—' : '参照不可' }}</template></el-table-column>
        <el-table-column prop="sortOrder" label="旧並び順" />
        <el-table-column prop="targetWarehouseCd" label="引当先倉庫" />
        <el-table-column label="旧有効フラグ"><template #default="{ row }">{{ row.legacyEnabled ? '有効' : '無効' }}</template></el-table-column>
        <el-table-column label="確認"><template #default="{ row }"><el-button link @click.stop="openDetail(row)">原記録</el-button></template></el-table-column>
      </el-table>
      <el-pagination :current-page="page" :page-size="50" :total="result.total" layout="prev, pager, next, total" @current-change="load" />
    </template>
    <p v-if="detailLoading">原記録を確認しています。</p>
    <section v-if="detail" aria-label="路由原記録">
      <h3>{{ detail.nameReadable ? detail.ruleName || '名称なし' : '名称参照不可' }}</h3>
      <p>得意先条件：{{ condition(detail.customerCondition, detail.customerCd) }}</p>
      <p>製品条件：{{ condition(detail.productCondition, detail.productCdPrefix) }}</p>
      <p>旧出庫区分：{{ detail.outboundType === null ? '旧条件指定なし' : detail.outboundType }} ／ 引当先倉庫：{{ detail.targetWarehouseCd }}</p>
      <p>備考：{{ detail.remarksReadable ? detail.remarks || '—' : '参照不可' }}</p>
      <p>適用ドメインは記録されていません。正式な方針バージョンと稼働先の選択は未確認です。</p>
    </section>
  </CpPageShell>
</template>

<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import CpPageShell from '@/components/templates/CpPageShell.vue'
import { usePermissionStore } from '@/stores/permission'
import { useOperationsIntent } from '@/composables/useOperationsIntent'
import { useWarehouseOwnerChoices } from '@/composables/useWarehouseOwnerChoices'
import { routingOwnerApi as api } from '@/api/wms/routingOwner'
import { checkRoutingPage, checkRoutingRow } from '@/types/wms/routingOwner'
import type { RoutingObservation, RoutingPage, RoutingQuery } from '@/types/wms/routingOwner'

const permission = usePermissionStore()
const canRead = computed(() => permission.loaded && permission.has('wms-outbound-routing:query') && permission.has('wms-warehouse:query'))
const warehouseId = ref(''), enabled = ref('all'), page = ref(1), result = ref<RoutingPage | null>(null), detail = ref<RoutingObservation | null>(null)
const loading = ref(false), detailLoading = ref(false), error = ref('')
let reads = 0, details = 0
function clearRead() { reads++; details++; result.value = null; detail.value = null; loading.value = false; detailLoading.value = false; error.value = '' }
function clearAll() { clearRead(); warehouseId.value = ''; enabled.value = 'all'; page.value = 1 }
const intent = useOperationsIntent(clearAll)
const { warehouses, total: warehouseTotal, loading: warehouseLoading, failed: warehouseFailed, loadWarehouses } = useWarehouseOwnerChoices(() => { warehouseId.value = ''; clearRead() })
watch([warehouseId, enabled], () => { clearRead(); page.value = 1 }, { flush: 'sync' })
watch(canRead, () => { intent.invalidate(); clearAll() }, { flush: 'sync' })
watch(() => permission.actionKeys, () => { intent.invalidate(); clearRead() }, { deep: true, flush: 'sync' })
async function load(next = 1) {
  clearRead(); page.value = next
  if (!canRead.value || !Number.isSafeInteger(next) || next < 1) return
  const n = reads, token = intent.capture(), q: RoutingQuery = { page: next, pageSize: 50, targetWarehouseId: warehouseId.value || undefined, enabled: enabled.value === 'all' ? undefined : enabled.value === 'true' }
  loading.value = true
  try {
    const response = await api.search(q)
    if (!intent.current(token) || n !== reads || !canRead.value) return
    if (response.code !== 0) throw new Error('READ_FAILED')
    result.value = checkRoutingPage(response.data, q)
  } catch { if (intent.current(token) && n === reads) error.value = '一覧を取得できません。現在の権限と条件を確認して再取得してください。' }
  finally { if (intent.current(token) && n === reads) loading.value = false }
}
async function openDetail(row: RoutingObservation) {
  const n = ++details, token = intent.capture(); detail.value = null; detailLoading.value = false; error.value = ''
  if (!canRead.value || !result.value?.items.some(x => x.id === row.id)) return
  detailLoading.value = true
  try {
    const response = await api.get(row.id)
    if (!intent.current(token) || n !== details || !canRead.value) return
    if (response.code !== 0) throw new Error('READ_FAILED')
    detail.value = checkRoutingRow(response.data, row.id)
  } catch { if (intent.current(token) && n === details) error.value = '原記録を取得できません。現在の権限と対象を確認してください。' }
  finally { if (intent.current(token) && n === details) detailLoading.value = false }
}
function condition(kind: string, value: string | null) {
  return kind === 'HIDDEN' ? '参照不可' : kind === 'LEGACY_ANY' ? '旧条件指定なし（正式ドメインの ANY を示しません）'
    : kind === 'LEGACY_PREFIX' ? `旧接頭辞：${value}` : `旧完全一致：${value === '' ? '空文字' : value}`
}
async function refresh() { await load(page.value); if (canRead.value) await loadWarehouses() }
onMounted(refresh)
</script>

<style scoped>
.filters { display: flex; align-items: center; flex-wrap: wrap; gap: var(--cp-space-4, 16px); margin-block: var(--cp-space-4, 16px); }
section { margin-top: var(--cp-space-4, 16px); }
</style>

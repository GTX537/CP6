<template>
  <CpPageShell title="サンプル原記録" :count="result?.total || 0">
    <template #actions><el-button :disabled="!canRead" :loading="loading" @click="load(page)">再取得</el-button></template>
    <el-alert type="info" :closable="false" title="既存の登録・借出・返却情報を確認できます。数量と保管場所は旧記録の申告値です。正式な在庫との照合が済むまで、新規登録や借出・返却・失効の変更は利用できません。" />
    <p v-if="!canRead" role="status">サンプルの参照権限が必要です。</p>
    <div class="filters">
      <label>番号 <input v-model="sampleNo" :disabled="!canRead" aria-label="番号" /></label>
      <label>旧状態 <select v-model="status" :disabled="!canRead" aria-label="旧状態"><option value="all">すべて</option><option value="0">保管中</option><option value="1">貸出中</option><option value="2">返却済</option><option value="3">失効</option></select></label>
      <label><input v-model="awaiting" type="checkbox" :disabled="!canRead" />返却記録の確認が必要なもの（失効を含む）</label>
      <el-button :disabled="!canRead" @click="load(1)">検索</el-button>
    </div>
    <p v-if="error" role="alert">{{ error }}</p>
    <template v-if="result">
      <p>現在の権限・条件で {{ result.total }} 件。過去の全借用履歴や在庫残高を示す一覧ではありません。</p>
      <el-table :data="result.items" row-key="id" @row-click="openDetail">
        <el-table-column prop="sampleNo" label="番号" />
        <el-table-column prop="sampleType" label="旧種別" />
        <el-table-column label="旧申告数量"><template #default="{ row }">{{ row.declaredQuantity }} {{ row.unitCd }}</template></el-table-column>
        <el-table-column label="旧状態"><template #default="{ row }">{{ statusLabel(row.legacyStatus) }}</template></el-table-column>
        <el-table-column label="返却確認"><template #default="{ row }">{{ evidenceLabel(row.returnEvidenceState) }}</template></el-table-column>
        <el-table-column label="確認"><template #default="{ row }"><el-button link @click.stop="openDetail(row)">原記録</el-button></template></el-table-column>
      </el-table>
      <el-pagination :current-page="page" :page-size="50" :total="result.total" layout="prev, pager, next, total" @current-change="load" />
    </template>
    <p v-if="detailLoading">原記録を確認しています。</p>
    <section v-if="detail" aria-label="サンプル原記録">
      <h3>{{ detail.sampleNo }}</h3>
      <p>旧申告数量：{{ detail.declaredQuantity }} {{ detail.unitCd }} ／ 旧状態：{{ statusLabel(detail.legacyStatus) }}</p>
      <p>返却確認：{{ evidenceLabel(detail.returnEvidenceState) }}。未返却数量は未確定です。</p>
      <p>得意先：{{ visible(detail.customerCdReadable, detail.customerCd) }} ／ {{ visible(detail.customerNameReadable, detail.customerName) }}</p>
      <p>製品：{{ visible(detail.productCdReadable, detail.productCd) }} ／ {{ visible(detail.productNameReadable, detail.productName) }}</p>
      <p>旧倉庫・場所：{{ detail.warehouseCd ?? '記録なし' }} ／ {{ detail.locationCd ?? '記録なし' }}（正式な在庫所在は未確認）</p>
      <p>最終借用者の旧記録：{{ visible(detail.borrowerReadable, detail.lentTo) }}（本人・連絡先との照合は未確認）</p>
      <p>旧借出時刻：{{ detail.lentAt ?? '記録なし' }} ／ 旧返却予定：{{ detail.expectedReturnDate ?? '記録なし' }} ／ 旧返却時刻：{{ detail.returnedAt ?? '記録なし' }}</p>
      <p>登録時刻：{{ detail.registeredAt }} ／ 備考：{{ visible(detail.remarksReadable, detail.remarks) }}</p>
      <p>過去の借用履歴は不完全です。旧返却時刻だけでは正式な入庫・義務の解消を証明できません。拠点の日付基準が未確認のため、正式な期限・逾期判定は表示しません。</p>
    </section>
  </CpPageShell>
</template>

<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import CpPageShell from '@/components/templates/CpPageShell.vue'
import { usePermissionStore } from '@/stores/permission'
import { useOperationsIntent } from '@/composables/useOperationsIntent'
import { sampleOwnerApi as api } from '@/api/wms/sampleOwner'
import { checkSamplePage, checkSampleRow } from '@/types/wms/sampleOwner'
import type { SampleObservation, SamplePage, SampleQuery } from '@/types/wms/sampleOwner'

const permission = usePermissionStore()
const canRead = computed(() => permission.loaded && permission.has('wms-sample-stock:query'))
const sampleNo = ref(''), status = ref('all'), awaiting = ref(false), page = ref(1)
const result = ref<SamplePage | null>(null), detail = ref<SampleObservation | null>(null)
const loading = ref(false), detailLoading = ref(false), error = ref('')
let reads = 0, details = 0
function clearRead() { reads++; details++; result.value = null; detail.value = null; loading.value = false; detailLoading.value = false; error.value = '' }
function clearAll() { clearRead(); sampleNo.value = ''; status.value = 'all'; awaiting.value = false; page.value = 1 }
const intent = useOperationsIntent(clearAll)
watch([sampleNo, status, awaiting], () => { clearRead(); page.value = 1 }, { flush: 'sync' })
watch(canRead, () => { intent.invalidate(); clearAll() }, { flush: 'sync' })
watch(() => permission.actionKeys, () => { intent.invalidate(); clearRead() }, { deep: true, flush: 'sync' })
async function load(next = 1) {
  clearRead(); page.value = next
  if (!canRead.value || !Number.isSafeInteger(next) || next < 1) return
  const n = reads, token = intent.capture(), q: SampleQuery = { page: next, pageSize: 50, sampleNo: sampleNo.value || undefined, legacyStatus: status.value === 'all' ? undefined : Number(status.value), awaitingReturnEvidence: awaiting.value || undefined }
  loading.value = true
  try {
    const response = await api.search(q)
    if (!intent.current(token) || n !== reads || !canRead.value) return
    if (response.code !== 0) throw new Error('READ_FAILED')
    result.value = checkSamplePage(response.data, q)
  } catch { if (intent.current(token) && n === reads) error.value = '一覧を取得できません。現在の権限と条件を確認して再取得してください。' }
  finally { if (intent.current(token) && n === reads) loading.value = false }
}
async function openDetail(row: SampleObservation) {
  const n = ++details, token = intent.capture(); detail.value = null; detailLoading.value = false; error.value = ''
  if (!canRead.value || !result.value?.items.some(x => x.id === row.id)) return
  detailLoading.value = true
  try {
    const response = await api.get(row.id)
    if (!intent.current(token) || n !== details || !canRead.value) return
    if (response.code !== 0) throw new Error('READ_FAILED')
    detail.value = checkSampleRow(response.data, row.id)
  } catch { if (intent.current(token) && n === details) error.value = '原記録を取得できません。現在の権限と対象を確認してください。' }
  finally { if (intent.current(token) && n === details) detailLoading.value = false }
}
function visible(read: boolean, value: string | null) { return read ? value ?? '記録なし' : '参照不可' }
function statusLabel(value: number) { return ({ 0: '保管中', 1: '貸出中', 2: '返却済', 3: '失効' } as Record<number, string>)[value] ?? `旧状態 ${value}` }
function evidenceLabel(value: string) { return value === 'OUTSTANDING_UNKNOWN' ? '返却記録の確認が必要' : value === 'RETURN_RECORDED_UNVERIFIED' ? '返却時刻あり・実在庫未照合' : '借出記録なし・履歴未照合' }
onMounted(() => load())
</script>

<style scoped>
.filters { display: flex; align-items: center; flex-wrap: wrap; gap: var(--cp-space-4, 16px); margin-block: var(--cp-space-4, 16px); }
section { margin-top: var(--cp-space-4, 16px); }
</style>

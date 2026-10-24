<template>
  <div class="wms-pack">
    <el-alert type="info" :closable="false" title="既存の出庫単位で出荷を確定する画面です。履歴の梱包記録は、正式な封箱・承運受付・配達完了の証明ではありません。" />
    <p v-if="!canQueue" role="status">出庫単の参照権限が必要です。</p>
    <p v-if="queueError" role="alert">{{ queueError }}</p>
    <p v-if="intent.error.value" role="alert">{{ intent.error.value }}</p>
    <el-row :gutter="12">
      <!-- 左：梱包待ち（status=3 Picking）一覧 -->
      <el-col :xs="24" :md="9">
        <el-card shadow="never" class="queue-card">
          <template #header>
            <div class="card-hd">
              <el-icon><Box /></el-icon>
              <span>{{ t('wms.pack.title.queue') }}</span>
              <el-button text :icon="Refresh" :disabled="!canQueue || saving" @click="reloadQueue" size="small" style="margin-left: auto" />
            </div>
          </template>
          <el-empty v-if="queue.length === 0" :description="t('wms.pack.msg.noPackages')" :image-size="80" />
          <div v-for="o in queue" :key="o.outboundNo"
            class="queue-item" :class="{ active: current?.outboundNo === o.outboundNo }"
            @click="loadOrder(o)">
            <div class="queue-no">{{ o.outboundNo }}</div>
            <div class="queue-meta">
              <el-tag size="small" type="warning">{{ statusName }}</el-tag>
              <el-tag v-if="o.priority === 3" size="small" type="danger">{{ t('急') }}</el-tag>
            </div>
            <div class="queue-info">{{ o.customerName || o.workOrderNo }}</div>
            <div class="queue-info">📦 {{ (o.details || []).length }} {{ t('wms.pick.fld.lineNo') }}</div>
          </div>
        </el-card>
      </el-col>

      <!-- 右：梱包・出荷確定エリア -->
      <el-col :xs="24" :md="15">
        <el-card shadow="never" v-if="!current">
          <el-empty :description="t('wms.pack.msg.noPackages')" />
        </el-card>

        <template v-else>
          <!-- ヘッダ -->
          <el-card shadow="never" class="hd-card">
            <div class="hd-row">
              <div>
                <h2 style="margin: 0">{{ current.outboundNo }}</h2>
                <div style="color: var(--cp-muted); font-size: 13px">{{ current.customerName || current.workOrderNo }}</div>
              </div>
              <div style="margin-left: auto">
                <el-tag type="warning" size="large">{{ (current.details || []).length }} {{ t('wms.pick.fld.lineNo') }}</el-tag>
              </div>
            </div>
          </el-card>

          <!-- 商品リスト -->
          <el-card shadow="never" class="items-card">
            <template #header>
              <div class="card-hd">
                <span>{{ t('wms.common.detail') }}</span>
              </div>
            </template>
            <el-table :data="current.details" border stripe size="small" max-height="300">
              <el-table-column prop="lineNo" :label="t('wms.pick.fld.lineNo')" width="60" />
              <el-table-column prop="productCd" :label="t('wms.common.product')" width="120" />
              <el-table-column prop="productName" :label="t('wms.common.productName')" min-width="140" show-overflow-tooltip />
              <el-table-column prop="lotNo" :label="t('wms.common.lot')" width="120" />
              <el-table-column prop="locationCd" :label="t('wms.common.location')" width="120" />
              <el-table-column prop="allocatedQty" :label="t('wms.pack.fld.qty')" width="100" align="right">
                <template #default="{ row }">{{ formatQty(row.allocatedQty) }}</template>
              </el-table-column>
            </el-table>
          </el-card>

          <!-- 梱包入力フォーム -->
          <el-card shadow="never" class="ship-card">
            <template #header>
              <div class="card-hd">
                <el-icon><Van /></el-icon>
                <span>{{ t('wms.pack.btn.confirmShip') }}</span>
              </div>
            </template>
            <el-form :model="shipForm" :disabled="!canShip || saving || intent.uncertain.value" label-width="140px" size="default">
              <el-row :gutter="12">
                <el-col :span="8"><el-form-item :label="t('wms.pack.fld.packageNo') + t('(自動)')">
                  <el-input v-model="autoPkgNo" disabled :placeholder="t('wms.common.optional')" />
                </el-form-item></el-col>
                <el-col :span="8"><el-form-item :label="'CaseQty'">
                  <el-input-number v-model="shipForm.caseQty" :min="1" controls-position="right" style="width: 100%" />
                </el-form-item></el-col>
                <el-col :span="8"><el-form-item :label="t('wms.pack.fld.weightKg')">
                  <el-input-number v-model="shipForm.totalWeightKg" :min="0" :precision="3" controls-position="right" style="width: 100%" />
                </el-form-item></el-col>
                <el-col :span="8"><el-form-item :label="'Volume(m³)'">
                  <el-input-number v-model="shipForm.totalVolumeM3" :min="0" :precision="4" controls-position="right" style="width: 100%" />
                </el-form-item></el-col>
                <el-col :span="8"><el-form-item :label="t('wms.pack.fld.carrier')">
                  <el-select v-model="shipForm.carrierCd" clearable style="width: 100%">
                    <el-option v-for="(l, v) in carrierMap" :key="v" :label="l" :value="v" />
                  </el-select>
                </el-form-item></el-col>
                <el-col :span="8"><el-form-item :label="t('wms.pack.fld.trackingNo')">
                  <el-input v-model="shipForm.trackingNo" maxlength="50" placeholder="実際の送り状番号" />
                </el-form-item></el-col>
                <el-col :span="24"><el-form-item :label="t('wms.common.remarks')">
                  <el-input v-model="shipForm.remarks" type="textarea" :rows="2" />
                </el-form-item></el-col>
              </el-row>
              <div style="margin-top: 8px">
                <el-button v-permission="'wms-outbound-order:ship'" type="success" size="large" style="width: 100%; font-size: 18px; height: 56px"
                  @click="onShip" :disabled="!canShip || saving || intent.uncertain.value" :loading="saving" :icon="Check">
                  {{ t('wms.pack.btn.confirmShip') }}
                </el-button>
              </div>
            </el-form>
          </el-card>
        </template>
      </el-col>
    </el-row>

    <!-- 履歴：直近 出荷済パッケージ -->
    <el-card shadow="never" style="margin-top: 12px">
      <template #header>
        <div class="card-hd">
          <el-icon><List /></el-icon>
          <span>{{ t('wms.common.history') }}</span>
          <el-button text :icon="Refresh" :disabled="!canHistory" @click="reloadHistory(historyPage)" size="small" style="margin-left: auto" />
        </div>
      </template>
      <p v-if="!canHistory" role="status">梱包と原出庫単の参照権限が必要です。</p>
      <p v-if="historyError" role="alert">{{ historyError }}</p>
      <label>梱包番号 <input v-model="packageFilter" :disabled="!canHistory" aria-label="梱包番号" /></label>
      <el-button :disabled="!canHistory" @click="reloadHistory(1)">検索</el-button>
      <p v-if="historyResult">現在参照可能な記録 {{ historyResult.total }} 件。日時は保存当時の値で、タイムゾーンは記録されていません。</p>
      <el-table :data="historyResult?.items || []" border stripe size="small" max-height="350" @row-click="loadPackage">
        <el-table-column prop="packageNo" :label="t('wms.pack.fld.packageNo')" width="180" />
        <el-table-column prop="outboundNo" :label="t('wms.pick.fld.outboundNo')" width="180" />
        <el-table-column prop="caseQty" label="Cases" width="80" align="right" />
        <el-table-column prop="totalWeightKg" :label="t('wms.pack.fld.weightKg')" width="100" align="right">
          <template #default="{ row }">{{ row.totalWeightKg ?? '—' }}</template>
        </el-table-column>
        <el-table-column :label="t('wms.pack.fld.carrier')" width="120">
          <template #default="{ row }">{{ row.carrierReadable ? carrierMap[row.carrierCd] || row.carrierCd || '—' : '参照不可' }}</template>
        </el-table-column>
        <el-table-column :label="t('wms.pack.fld.trackingNo')" width="180"><template #default="{ row }">{{ row.trackingReadable ? row.trackingNo ?? '—' : '参照不可' }}</template></el-table-column>
        <el-table-column prop="departureTime" label="Departure (保存値)" width="180" />
      </el-table>
      <el-pagination v-if="historyResult" :current-page="historyPage" :page-size="30" :total="historyResult.total" layout="prev, pager, next, total" @current-change="reloadHistory" />
      <section v-if="packageDetail" aria-label="梱包原記録">
        <h3>{{ packageDetail.packageNo }}</h3><p>原出庫単：{{ packageDetail.outboundNo }}</p>
        <p>重量(kg)：{{ packageDetail.totalWeightKg ?? '未記録' }} ／ 容積(m³)：{{ packageDetail.totalVolumeM3 ?? '未記録' }}</p>
        <p>備考：{{ packageDetail.remarksReadable ? packageDetail.remarks ?? '—' : '参照不可' }}</p>
      </section>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, computed, onMounted, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Box, Van, Check, List, Refresh } from '@element-plus/icons-vue'
import { useI18n } from 'vue-i18n'
import { outboundOrderApi } from '@/api/wms/outboundOrder'
import { packagingOwnerApi } from '@/api/wms/packagingOwner'
import { checkPackagingPage, checkPackagingRecord } from '@/types/wms/packagingOwner'
import type { PackagingPage, PackagingRecord, PackagingQuery } from '@/types/wms/packagingOwner'
import type { OutboundOrder, ShipRequest } from '@/types/wms/wms'
import { formatQty as fmtQty } from '@/utils/format'
import { usePermissionStore } from '@/stores/permission'
import { useOperationsIntent } from '@/composables/useOperationsIntent'
import { identityStamp } from '@/auth/identity'

const { t } = useI18n(), permission = usePermissionStore()
const canQueue = computed(() => permission.loaded && permission.has('wms-outbound-order:query'))
const canHistory = computed(() => canQueue.value && permission.has('wms-packaging:query'))
const canShip = computed(() => canQueue.value && permission.has('wms-outbound-order:ship'))
const queue = ref<OutboundOrder[]>([]), current = ref<OutboundOrder | null>(null), autoPkgNo = ref('—')
const defaults = (): ShipRequest => ({ caseQty: 1, totalWeightKg: undefined, totalVolumeM3: undefined, carrierCd: '', trackingNo: '', remarks: '' })
const shipForm = reactive<ShipRequest>(defaults())
const historyResult = ref<PackagingPage | null>(null), historyPage = ref(1), packageFilter = ref(''), packageDetail = ref<PackagingRecord | null>(null)
const queueError = ref(''), historyError = ref('')
let queues = 0, orders = 0, histories = 0, packages = 0
let sentIdentity: string | null = null
function clearOrder() { orders++; current.value = null; autoPkgNo.value = '—'; Object.assign(shipForm, defaults()) }
function clearHistory() { histories++; packages++; historyResult.value = null; packageDetail.value = null; historyError.value = '' }
function clearAll() { queues++; queue.value = []; queueError.value = ''; clearOrder(); clearHistory(); packageFilter.value = ''; historyPage.value = 1 }
const intent = useOperationsIntent(clearAll), saving = intent.busy
const statusName = t('wms.pick.status.picking')
const carrierMap: Record<string, string> = { YAMATO: t('wms.pack.carrier.yamato'), SAGAWA: t('wms.pack.carrier.sagawa'), JP: t('wms.pack.carrier.jp'), SELF: t('wms.pack.carrier.self'), OTHER: t('wms.pack.carrier.other') }
function formatQty(n: number | undefined | null) { return n == null ? '' : fmtQty(n, 4) }
watch(() => [permission.loaded, permission.actionKeys], () => {
  const unresolved = sentIdentity === identityStamp(), notice = intent.error.value
  intent.invalidate(); clearAll()
  if (unresolved) { intent.uncertain.value = true; intent.error.value = notice || '出荷の結果が未確認です。再送せず、原出庫単と梱包履歴を確認してください。' }
}, { deep: true, flush: 'sync' })
watch(packageFilter, () => { clearHistory(); historyPage.value = 1 }, { flush: 'sync' })
async function reloadQueue() {
  if (saving.value) return
  const n = ++queues, token = intent.capture(); queue.value = []; queueError.value = ''; clearOrder()
  if (!canQueue.value) return
  try {
    const r = await outboundOrderApi.search({ status: 3, outboundType: 2, pageSize: 50 })
    if (!intent.current(token) || n !== queues || !canQueue.value) return
    if (r.code !== 0 || !Array.isArray(r.data) || r.data.some(x => !x.outboundNo || x.status !== 3 || x.outboundType !== 2)) throw new Error('QUEUE_INVALID')
    queue.value = r.data
  } catch { if (intent.current(token) && n === queues) queueError.value = '現在の出庫一覧を取得できません。' }
}
async function loadOrder(o: OutboundOrder) {
  if (saving.value || intent.uncertain.value || !canQueue.value || !queue.value.some(x => x.outboundNo === o.outboundNo)) return
  clearOrder(); const n = orders, token = intent.capture(), number = o.outboundNo!
  try {
    const r = await outboundOrderApi.get(number)
    if (!intent.current(token) || n !== orders || !canQueue.value) return
    if (r.code !== 0 || !r.data || r.data.outboundNo !== number || r.data.status !== 3 || r.data.outboundType !== 2) throw new Error('ORDER_CHANGED')
    current.value = r.data; shipForm.carrierCd = r.data.carrierCd || ''; shipForm.caseQty = r.data.details?.length || 1
  } catch { if (intent.current(token) && n === orders) queueError.value = '原出庫単の最新状態を確認できません。' }
}
async function onShip() {
  if (!canShip.value || !current.value) return
  const token = intent.begin(); if (!token) return
  const number = current.value.outboundNo!, selected = orders, body = { ...shipForm }
  try {
    await ElMessageBox.confirm(t('wms.pack.msg.confirmShipAsk'), t('wms.common.confirm'), { type: 'success' })
    if (!intent.current(token) || !canShip.value || orders !== selected || current.value?.outboundNo !== number) return
    sentIdentity = token.identity
    const r = await outboundOrderApi.ship(number, body)
    if (!intent.current(token)) return
    if (r.code !== 0 || !r.data || typeof r.data.packageNo !== 'string' || !r.data.packageNo) throw new Error('SHIP_OUTCOME_UNKNOWN')
    sentIdentity = null
    ElMessage.success(`${t('wms.common.success')}: ${r.data.packageNo}`); clearOrder()
    intent.finish(token); await reloadQueue(); await reloadHistory(historyPage.value)
  } catch (e) { intent.failed(token, e); if (intent.current(token) && !intent.uncertain.value) sentIdentity = null }
  finally { intent.finish(token) }
}
async function reloadHistory(next = 1) {
  clearHistory(); historyPage.value = next
  if (!canHistory.value || !Number.isSafeInteger(next) || next < 1) return
  const n = histories, token = intent.capture(), q: PackagingQuery = { page: next, pageSize: 30, packageNo: packageFilter.value || undefined }
  try {
    const r = await packagingOwnerApi.search(q)
    if (!intent.current(token) || n !== histories || !canHistory.value) return
    if (r.code !== 0) throw new Error('READ_FAILED')
    historyResult.value = checkPackagingPage(r.data, q)
  } catch { if (intent.current(token) && n === histories) historyError.value = '現在の梱包記録を取得できません。権限と検索条件を確認してください。' }
}
async function loadPackage(row: PackagingRecord) {
  const n = ++packages, token = intent.capture(); packageDetail.value = null
  if (!canHistory.value || !historyResult.value?.items.some(x => x.id === row.id)) return
  try {
    const r = await packagingOwnerApi.get(row.packageNo)
    if (!intent.current(token) || n !== packages || !canHistory.value) return
    if (r.code !== 0) throw new Error('READ_FAILED')
    const record = checkPackagingRecord(r.data, row.packageNo)
    if (record.id !== row.id || record.outboundId !== row.outboundId) throw new Error('IDENTITY_CHANGED')
    packageDetail.value = record
  } catch { if (intent.current(token) && n === packages) historyError.value = '原梱包記録を取得できません。' }
}
onMounted(() => { reloadQueue(); reloadHistory() })
</script>

<style scoped>
.wms-pack { padding: 12px; }
.card-hd { display: flex; align-items: center; gap: 8px; }
.queue-card :deep(.el-card__body) { padding: 8px; max-height: 720px; overflow-y: auto; }
.queue-item {
  padding: 10px 12px; border: 1px solid var(--cp-line); border-radius: var(--cp-r-sm); margin-bottom: 6px; cursor: pointer;
  transition: all var(--cp-t-base);
}
.queue-item:hover { background: var(--cp-bg-hover); border-color: var(--cp-faint); }
.queue-item.active { background: var(--cp-warn-bg); border-color: var(--cp-warn); }
.queue-no { font-weight: bold; font-size: 14px; }
.queue-meta { display: flex; gap: 4px; margin: 4px 0; }
.queue-info { font-size: 12px; color: var(--cp-text); }

.hd-card { margin-bottom: 8px; }
.hd-row { display: flex; align-items: center; }
.items-card { margin-bottom: 8px; }
.ship-card :deep(.el-card__header) { background: var(--cp-brand-bg); }
</style>

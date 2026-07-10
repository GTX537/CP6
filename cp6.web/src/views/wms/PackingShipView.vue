<template>
  <div class="wms-pack">
    <el-row :gutter="12">
      <!-- 左：梱包待ち（status=3 Picking）一覧 -->
      <el-col :xs="24" :md="9">
        <el-card shadow="never" class="queue-card">
          <template #header>
            <div class="card-hd">
              <el-icon><Box /></el-icon>
              <span>{{ t('wms.pack.title.queue') }}</span>
              <el-button text :icon="Refresh" @click="reloadQueue" size="small" style="margin-left: auto" />
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
            <el-form :model="shipForm" label-width="140px" size="default">
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
                  <el-input v-model="shipForm.trackingNo" maxlength="50">
                    <template #append>
                      <el-button @click="genTracking">{{ t('wms.pack.btn.assignTracking') }}</el-button>
                    </template>
                  </el-input>
                </el-form-item></el-col>
                <el-col :span="24"><el-form-item :label="t('wms.common.remarks')">
                  <el-input v-model="shipForm.remarks" type="textarea" :rows="2" />
                </el-form-item></el-col>
              </el-row>
              <div style="margin-top: 8px">
                <el-button v-permission="'wms-outbound-order:ship'" type="success" size="large" style="width: 100%; font-size: 18px; height: 56px"
                  @click="onShip" :loading="saving" :icon="Check">
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
          <el-button text :icon="Refresh" @click="reloadHistory" size="small" style="margin-left: auto" />
        </div>
      </template>
      <el-table :data="history" border stripe size="small" max-height="350">
        <el-table-column prop="packageNo" :label="t('wms.pack.fld.packageNo')" width="180" />
        <el-table-column prop="outboundNo" :label="t('wms.pick.fld.outboundNo')" width="180" />
        <el-table-column prop="caseQty" label="Cases" width="80" align="right" />
        <el-table-column prop="totalWeightKg" :label="t('wms.pack.fld.weightKg')" width="100" align="right">
          <template #default="{ row }">{{ formatQty(row.totalWeightKg) }}</template>
        </el-table-column>
        <el-table-column :label="t('wms.pack.fld.carrier')" width="120">
          <template #default="{ row }">{{ carrierMap[row.carrierCd] || row.carrierCd || '—' }}</template>
        </el-table-column>
        <el-table-column prop="trackingNo" :label="t('wms.pack.fld.trackingNo')" width="180" />
        <el-table-column prop="departureTime" label="Departure" width="170" />
      </el-table>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, computed, onMounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Box, Van, Check, List, Refresh } from '@element-plus/icons-vue'
import { useI18n } from 'vue-i18n'
import http from '@/api/http'
import { outboundOrderApi } from '@/api/wms/outboundOrder'
import type { OutboundOrder, ShipRequest, WmsApi } from '@/types/wms/wms'
import { formatQty as fmtQty } from '@/utils/format'

const { t } = useI18n()
const saving = ref(false)

const queue = ref<OutboundOrder[]>([])
const current = ref<OutboundOrder | null>(null)

const autoPkgNo = ref('—')

const shipForm = reactive<ShipRequest>({
  caseQty: 1,
  totalWeightKg: undefined,
  totalVolumeM3: undefined,
  carrierCd: 'YAMATO',
  trackingNo: '',
  remarks: '',
})

const history = ref<any[]>([])

const statusName = t('wms.pick.status.picking')

const carrierMap: Record<string, string> = {
  YAMATO: t('wms.pack.carrier.yamato'),
  SAGAWA: t('wms.pack.carrier.sagawa'),
  JP:     t('wms.pack.carrier.jp'),
  SELF:   t('wms.pack.carrier.self'),
  OTHER:  t('wms.pack.carrier.other'),
}

function formatQty(n: number | undefined | null) {
  if (n == null) return ''
  return fmtQty(n, 4)
}

async function reloadQueue() {
  try {
    const r = await outboundOrderApi.search({ status: 3, outboundType: 2, pageSize: 50 })
    queue.value = r.data || []
  } catch { /* */ }
}

async function loadOrder(o: OutboundOrder) {
  try {
    const r = await outboundOrderApi.get(o.outboundNo!)
    current.value = r.data
    autoPkgNo.value = '—'
    shipForm.carrierCd = current.value.carrierCd || 'YAMATO'
    shipForm.trackingNo = ''
    shipForm.caseQty = (current.value.details?.length || 1)
    shipForm.totalWeightKg = undefined
    shipForm.totalVolumeM3 = undefined
    shipForm.remarks = ''
  } catch { /* */ }
}

function genTracking() {
  const d = new Date()
  const ymd = `${d.getFullYear()}${String(d.getMonth() + 1).padStart(2, '0')}${String(d.getDate()).padStart(2, '0')}`
  const rnd = Math.floor(Math.random() * 900000) + 100000
  shipForm.trackingNo = `${shipForm.carrierCd}-${ymd}-${rnd}`
}

async function onShip() {
  if (!current.value) return
  try {
    await ElMessageBox.confirm(t('wms.pack.msg.confirmShipAsk'), t('wms.common.confirm'), { type: 'success' })
    saving.value = true
    const res = await outboundOrderApi.ship(current.value.outboundNo!, shipForm)
    if (res.data?.packageNo) {
      autoPkgNo.value = res.data.packageNo
      ElMessage.success(`${t('wms.common.success')}: ${res.data.packageNo}`)
    } else {
      ElMessage.success(t('wms.common.success'))
    }
    // 一覧から外す + 履歴更新
    current.value = null
    await reloadQueue()
    await reloadHistory()
  } catch (e: any) {
    if (e?.response) ElMessage.error(e.response?.data?.message || 'Error')
    // ElMessageBox cancel → 静默
  } finally { saving.value = false }
}

async function reloadHistory() {
  try {
    const r: any = await http.get<any, WmsApi<any>>('/wms/shipping/packages', { params: { page: 1, pageSize: 30 } })
    history.value = r?.data?.items || []
  } catch { /* */ }
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

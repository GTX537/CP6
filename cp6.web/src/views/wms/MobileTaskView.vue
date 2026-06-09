<template>
  <div class="wms-mobile">
    <el-card shadow="never" class="hd-card">
      <div class="hd-row">
        <div>
          <h2 style="margin: 0">{{ t('wms.mobile.title') }}</h2>
          <div style="font-size: 12px; color: #909399">{{ new Date().toLocaleString('ja-JP') }}</div>
        </div>
        <el-button :icon="Refresh" circle @click="reload" :loading="loading" />
      </div>

      <!-- スキャンバー（バーコード解決） -->
      <div class="scan-bar">
        <el-input
          v-model="scanCode"
          :placeholder="t('wms.mobile.scan.ph')"
          size="large"
          clearable
          @keyup.enter="doScan"
        >
          <template #prepend>📷</template>
          <template #append>
            <el-button :loading="scanning" @click="doScan">{{ t('wms.mobile.scan.btn') }}</el-button>
          </template>
        </el-input>
      </div>
      <div v-if="scanResult" class="scan-result" :class="scanResult.kind.toLowerCase()">
        <template v-if="scanResult.kind === 'LOCATION'">
          <div class="sr-hd">
            <el-tag type="success" size="small">{{ t('wms.mobile.scan.location') }}</el-tag>
            <b>{{ scanResult.locationCd }}</b>
            <span v-if="scanResult.locationName" class="sr-sub">{{ scanResult.locationName }}</span>
            <el-tag v-if="scanResult.isBlocked" type="danger" size="small">{{ t('wms.mobile.scan.blocked') }}</el-tag>
          </div>
        </template>
        <template v-else-if="scanResult.kind === 'PRODUCT'">
          <div class="sr-hd">
            <el-tag size="small">{{ t('wms.mobile.scan.product') }}</el-tag>
            <b>{{ scanResult.productCd }}</b>
          </div>
        </template>
        <template v-else>
          <div class="sr-hd">
            <el-tag type="info" size="small">{{ t('wms.mobile.scan.unknown') }}</el-tag>
            <span>{{ scanResult.barcode }}</span>
          </div>
        </template>

        <el-table v-if="scanResult.stocks?.length" :data="scanResult.stocks" size="small" class="sr-stk">
          <el-table-column prop="locationCd" :label="t('wms.mobile.scan.location')" min-width="90" />
          <el-table-column prop="productCd" :label="t('wms.mobile.fld.product')" min-width="90" />
          <el-table-column prop="lotNo" :label="t('wms.mobile.fld.lot')" min-width="80" />
          <el-table-column prop="physicalQty" :label="t('wms.mobile.scan.onhand')" align="right" width="80" />
        </el-table>
        <div v-else-if="scanResult.kind !== 'UNKNOWN'" class="sr-empty">{{ t('wms.mobile.scan.empty') }}</div>
      </div>

      <el-row :gutter="8" style="margin-top: 12px">
        <el-col :span="5"><div class="stat"><div class="stat-num">{{ taskCount }}</div><div class="stat-lbl">{{ t('wms.mobile.tab.task') }}</div></div></el-col>
        <el-col :span="5"><div class="stat"><div class="stat-num">{{ pickCount }}</div><div class="stat-lbl">{{ t('wms.mobile.tab.picking') }}</div></div></el-col>
        <el-col :span="5"><div class="stat"><div class="stat-num">{{ packCount }}</div><div class="stat-lbl">{{ t('wms.mobile.tab.packing') }}</div></div></el-col>
        <el-col :span="5"><div class="stat"><div class="stat-num">{{ inCount }}</div><div class="stat-lbl">{{ t('wms.mobile.tab.inbound') }}</div></div></el-col>
        <el-col :span="4"><div class="stat"><div class="stat-num">{{ stCount }}</div><div class="stat-lbl">{{ t('wms.mobile.tab.stocktake') }}</div></div></el-col>
      </el-row>
    </el-card>

    <el-tabs v-model="activeTab" class="task-tabs">
      <!-- WM300 専用：配信された作業指示 -->
      <el-tab-pane name="task" :label="t('wms.mobile.tab.task') + ' (' + taskCount + ')'">
        <el-empty v-if="mobileTasks.length === 0" :description="t('wms.mobile.msg.nothing')" :image-size="60" />
        <div v-for="m in mobileTasks" :key="m.mobileTaskNo" class="task-card">
          <div class="task-hd">
            <span class="task-no">{{ m.mobileTaskNo }}</span>
            <div>
              <el-tag v-if="m.priority === 1" type="danger" size="small" effect="dark" style="margin-right: 4px">至急</el-tag>
              <el-tag size="small" :type="mtTagType(m.status)">{{ t('wms.mobile.st.' + m.status) }}</el-tag>
            </div>
          </div>
          <div class="task-sub">
            <el-tag size="small" effect="plain" style="margin-right: 6px">{{ t('wms.mobile.type.' + m.taskType) }}</el-tag>
            {{ m.instruction || m.productName || m.productCd || '—' }}
          </div>
          <div class="task-grid">
            <span v-if="m.productCd">📦 {{ m.productCd }}<span v-if="m.lotNo"> / {{ m.lotNo }}</span></span>
            <span v-if="m.fromLocationCd">⬅ {{ m.fromLocationCd }}</span>
            <span v-if="m.toLocationCd">➡ {{ m.toLocationCd }}</span>
            <span v-if="m.qty">× {{ m.qty }} {{ m.unitCd || '' }}</span>
          </div>
          <div class="task-actions" v-if="m.status !== 2 && m.status !== 9">
            <el-button v-if="m.status === 0" size="small" @click="startTask(m)">{{ t('wms.mobile.act.start') }}</el-button>
            <el-button size="small" type="primary" @click="doneTask(m)">{{ t('wms.mobile.act.done') }}</el-button>
            <el-button size="small" text type="info" @click="cancelTask(m)">{{ t('wms.mobile.act.cancel') }}</el-button>
          </div>
        </div>
      </el-tab-pane>

      <el-tab-pane name="pick" :label="t('wms.mobile.tab.picking') + ' (' + pickCount + ')'">
        <el-empty v-if="pickTasks.length === 0" :description="t('wms.mobile.msg.nothing')" :image-size="60" />
        <div v-for="o in pickTasks" :key="o.outboundNo" class="task-card" @click="goPick(o)">
          <div class="task-hd">
            <span class="task-no">{{ o.outboundNo }}</span>
            <el-tag size="small" :type="o.status === 2 ? 'info' : 'warning'">{{ statusLabel(o.status) }}</el-tag>
          </div>
          <div class="task-sub">{{ o.customerName || o.workOrderNo || '—' }}</div>
          <div class="task-foot">📅 {{ o.plannedDate?.slice(0,10) }} · {{ (o.details || []).length }} {{ t('wms.pick.fld.lineNo') }}</div>
        </div>
      </el-tab-pane>
      <el-tab-pane name="pack" :label="t('wms.mobile.tab.packing') + ' (' + packCount + ')'">
        <el-empty v-if="packTasks.length === 0" :description="t('wms.mobile.msg.nothing')" :image-size="60" />
        <div v-for="o in packTasks" :key="o.outboundNo" class="task-card" @click="goPack(o)">
          <div class="task-hd">
            <span class="task-no">{{ o.outboundNo }}</span>
            <el-tag size="small" type="success">{{ t('wms.mobile.tag.ready') }}</el-tag>
          </div>
          <div class="task-sub">{{ o.customerName || '—' }}</div>
          <div class="task-foot">{{ (o.details || []).length }} {{ t('wms.pick.fld.lineNo') }}</div>
        </div>
      </el-tab-pane>
      <el-tab-pane name="inbound" :label="t('wms.mobile.tab.inbound') + ' (' + inCount + ')'">
        <el-empty v-if="inboundTasks.length === 0" :description="t('wms.mobile.msg.nothing')" :image-size="60" />
        <div v-for="r in inboundTasks" :key="r.receiptNo" class="task-card" @click="goInbound()">
          <div class="task-hd">
            <span class="task-no">{{ r.receiptNo }}</span>
            <el-tag size="small">{{ r.sourceType }}</el-tag>
          </div>
          <div class="task-sub">{{ r.workOrderNo || '—' }}</div>
          <div class="task-foot">{{ r.receiveDateTime?.slice(0, 16) }}</div>
        </div>
      </el-tab-pane>
      <el-tab-pane name="stocktake" :label="t('wms.mobile.tab.stocktake') + ' (' + stCount + ')'">
        <el-empty :description="t('wms.mobile.msg.nothing')" :image-size="60" />
      </el-tab-pane>
    </el-tabs>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { Refresh } from '@element-plus/icons-vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { outboundOrderApi } from '@/api/wms/outboundOrder'
import { inboundReceiptApi } from '@/api/wms/inboundReceipt'
import { mobileApi } from '@/api/wms/mobile'
import type { OutboundOrder, InboundReceipt, MobileTask, MobileScanResult } from '@/types/wms/wms'

const { t } = useI18n()
const router = useRouter()
const loading = ref(false)
const activeTab = ref('task')

const mobileTasks = ref<MobileTask[]>([])
const pickTasks = ref<OutboundOrder[]>([])
const packTasks = ref<OutboundOrder[]>([])
const inboundTasks = ref<InboundReceipt[]>([])

const taskCount = computed(() => mobileTasks.value.filter(m => m.status === 0 || m.status === 1).length)
const pickCount = computed(() => pickTasks.value.length)
const packCount = computed(() => packTasks.value.length)
const inCount = computed(() => inboundTasks.value.length)
const stCount = computed(() => 0)

// ───── スキャン ─────
const scanCode = ref('')
const scanning = ref(false)
const scanResult = ref<MobileScanResult | null>(null)

async function doScan() {
  const code = scanCode.value.trim()
  if (!code) return
  scanning.value = true
  try {
    const res = await mobileApi.scan({ barcode: code })
    scanResult.value = res.data
    if (res.data.kind === 'UNKNOWN') ElMessage.warning(t('wms.mobile.scan.unknown'))
  } finally {
    scanning.value = false
  }
}

function statusLabel(s: number) {
  return s === 2 ? t('wms.mobile.tag.allocated') : t('wms.mobile.tag.picking')
}
function mtTagType(s: number): 'info' | 'warning' | 'success' | 'danger' {
  return s === 0 ? 'info' : s === 1 ? 'warning' : s === 2 ? 'success' : 'danger'
}

async function startTask(m: MobileTask) {
  await mobileApi.start(m.mobileTaskNo)
  ElMessage.success(t('wms.mobile.msg.startOk'))
  reload()
}

async function doneTask(m: MobileTask) {
  try {
    const { value } = await ElMessageBox.prompt(
      t('wms.mobile.fld.qty'), t('wms.mobile.done.title'),
      { inputValue: String(m.qty || ''), inputPattern: /^\d*\.?\d+$/, confirmButtonText: t('wms.mobile.act.done') },
    )
    await mobileApi.done(m.mobileTaskNo, { scannedQty: Number(value) })
    ElMessage.success(t('wms.mobile.msg.doneOk'))
    reload()
  } catch (e) {
    if (e !== 'cancel') ElMessage.error(String((e as any)?.message ?? e))
  }
}

async function cancelTask(m: MobileTask) {
  await mobileApi.cancel(m.mobileTaskNo)
  ElMessage.info(t('wms.mobile.msg.cancelOk'))
  reload()
}

async function reload() {
  loading.value = true
  try {
    const [mt, r1, r2, r3, r4] = await Promise.all([
      mobileApi.tasks({ openOnly: true, pageSize: 50 }),
      outboundOrderApi.search({ status: 2, pageSize: 30 }), // Allocated
      outboundOrderApi.search({ status: 3, pageSize: 30 }), // Picking
      outboundOrderApi.search({ status: 3, outboundType: 2, pageSize: 30 }), // Pack ready
      inboundReceiptApi.search({ pageSize: 20 }),
    ])
    mobileTasks.value = mt.data || []
    pickTasks.value = [...(r1.data || []), ...(r2.data || [])]
    packTasks.value = (r3.data || [])
    inboundTasks.value = (r4.data || []).slice(0, 20)
  } finally { loading.value = false }
}

function goPick(_o: OutboundOrder) { router.push('/wms/picking') }
function goPack(_o: OutboundOrder) { router.push('/wms/packaging') }
function goInbound() { router.push('/wms/inbound-receipt') }

onMounted(reload)
</script>

<style scoped>
.wms-mobile { padding: 12px; max-width: 600px; margin: 0 auto; }
.hd-card { margin-bottom: 12px; }
.hd-row { display: flex; align-items: center; justify-content: space-between; }
.scan-bar { margin-top: 12px; }
.scan-result {
  margin-top: 8px; padding: 8px 10px; border-radius: 8px;
  background: #f5f7fa; border-left: 4px solid #909399;
}
.scan-result.location { border-left-color: #67c23a; }
.scan-result.product { border-left-color: #409eff; }
.scan-result.unknown { border-left-color: #e6a23c; }
.sr-hd { display: flex; align-items: center; gap: 6px; flex-wrap: wrap; }
.sr-sub { font-size: 12px; color: #909399; }
.sr-stk { margin-top: 6px; }
.sr-empty { font-size: 12px; color: #909399; margin-top: 6px; }
.stat { text-align: center; padding: 6px; border-radius: 6px; background: #f5f7fa; }
.stat-num { font-size: 22px; font-weight: bold; color: #409eff; }
.stat-lbl { font-size: 11px; color: #606266; }
.task-tabs :deep(.el-tabs__content) { padding-top: 6px; }
.task-card {
  background: #fff; border: 1px solid #ebeef5; border-radius: 8px;
  padding: 10px 12px; margin-bottom: 8px; cursor: pointer;
  transition: all 0.15s;
}
.task-card:active { background: #ecf5ff; }
.task-hd { display: flex; align-items: center; justify-content: space-between; }
.task-no { font-weight: bold; font-size: 14px; color: #303133; }
.task-sub { font-size: 12px; color: #606266; margin: 4px 0; }
.task-grid { display: flex; flex-wrap: wrap; gap: 10px; font-size: 12px; color: #606266; margin-bottom: 6px; }
.task-actions { display: flex; gap: 6px; justify-content: flex-end; }
.task-foot { font-size: 11px; color: #909399; }
</style>

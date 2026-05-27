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
      <el-row :gutter="8" style="margin-top: 12px">
        <el-col :span="6"><div class="stat"><div class="stat-num">{{ pickCount }}</div><div class="stat-lbl">{{ t('wms.mobile.tab.picking') }}</div></div></el-col>
        <el-col :span="6"><div class="stat"><div class="stat-num">{{ packCount }}</div><div class="stat-lbl">{{ t('wms.mobile.tab.packing') }}</div></div></el-col>
        <el-col :span="6"><div class="stat"><div class="stat-num">{{ inCount }}</div><div class="stat-lbl">{{ t('wms.mobile.tab.inbound') }}</div></div></el-col>
        <el-col :span="6"><div class="stat"><div class="stat-num">{{ stCount }}</div><div class="stat-lbl">{{ t('wms.mobile.tab.stocktake') }}</div></div></el-col>
      </el-row>
    </el-card>

    <el-tabs v-model="activeTab" class="task-tabs">
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
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { outboundOrderApi } from '@/api/wms/outboundOrder'
import { inboundReceiptApi } from '@/api/wms/inboundReceipt'
import type { OutboundOrder, InboundReceipt } from '@/types/wms'

const { t } = useI18n()
const router = useRouter()
const loading = ref(false)
const activeTab = ref('pick')

const pickTasks = ref<OutboundOrder[]>([])
const packTasks = ref<OutboundOrder[]>([])
const inboundTasks = ref<InboundReceipt[]>([])

const pickCount = computed(() => pickTasks.value.length)
const packCount = computed(() => packTasks.value.length)
const inCount = computed(() => inboundTasks.value.length)
const stCount = computed(() => 0)

function statusLabel(s: number) {
  return s === 2 ? t('wms.mobile.tag.allocated') : t('wms.mobile.tag.picking')
}

async function reload() {
  loading.value = true
  try {
    const [r1, r2, r3, r4] = await Promise.all([
      outboundOrderApi.search({ status: 2, pageSize: 30 }), // Allocated
      outboundOrderApi.search({ status: 3, pageSize: 30 }), // Picking
      outboundOrderApi.search({ status: 3, outboundType: 2, pageSize: 30 }), // Pack ready
      inboundReceiptApi.search({ pageSize: 20 }),
    ])
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
.task-foot { font-size: 11px; color: #909399; }
</style>

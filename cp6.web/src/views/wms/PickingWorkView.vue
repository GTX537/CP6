<template>
  <div class="wms-picking">
    <el-row :gutter="12">
      <!-- 左：タスク一覧 -->
      <el-col :xs="24" :md="8">
        <el-card shadow="never" class="task-card">
          <template #header>
            <div class="card-hd">
              <el-icon><List /></el-icon>
              <span>{{ t('wms.pick.title.tasks') }}</span>
              <el-button text :icon="Refresh" @click="reloadTasks" size="small" style="margin-left: auto" />
            </div>
          </template>

          <el-empty v-if="tasks.length === 0" :description="t('wms.pick.msg.noTask')" :image-size="80" />

          <div v-for="task in tasks" :key="task.outboundNo"
            class="task-item" :class="{ active: current?.outboundNo === task.outboundNo }"
            @click="loadTask(task)">
            <div class="task-no">{{ task.outboundNo }}</div>
            <div class="task-meta">
              <el-tag size="small" :type="task.status === 3 ? 'warning' : 'success'">{{ statusMap[task.status] }}</el-tag>
              <el-tag v-if="task.priority === 3" size="small" type="danger">急</el-tag>
              <el-tag v-else-if="task.priority === 2" size="small" type="warning">↑</el-tag>
            </div>
            <div class="task-info">{{ task.customerName || task.workOrderNo || task.warehouseCd }}</div>
            <div class="task-info">📅 {{ task.plannedDate?.slice(0, 10) }}</div>
          </div>
        </el-card>
      </el-col>

      <!-- 右：作業エリア -->
      <el-col :xs="24" :md="16">
        <el-card shadow="never" v-if="!current">
          <el-empty :description="t('wms.pick.msg.noTask')" />
        </el-card>

        <template v-else>
          <!-- ヘッダ情報 -->
          <el-card shadow="never" class="hd-card">
            <div class="hd-row">
              <div>
                <h2 style="margin: 0">{{ current.outboundNo }}</h2>
                <div style="color: #666; font-size: 13px">
                  {{ current.customerName || current.workOrderNo }}
                </div>
              </div>
              <div class="hd-progress">
                <span style="font-size: 12px; color: #666">{{ t('wms.pick.fld.pickedQty') }}</span>
                <el-progress :percentage="progressPct" :stroke-width="14" />
                <div style="font-size: 11px; color: #666">{{ pickedLines }} / {{ current.details?.length || 0 }} {{ t('wms.pick.fld.lineNo') }}</div>
              </div>
              <div>
                <el-button v-if="current.status === 2" type="warning" size="large" @click="onStart">{{ t('wms.pick.btn.start') }}</el-button>
                <el-button v-if="current.status === 3 && allDone" type="success" size="large" @click="onComplete">{{ t('wms.pick.btn.complete') }}</el-button>
              </div>
            </div>
          </el-card>

          <!-- 明細リスト -->
          <el-card shadow="never">
            <template #header>
              <div class="card-hd">
                <span>{{ t('wms.pick.title.task') }}</span>
                <el-tag style="margin-left: auto">{{ pendingLines.length }} / {{ current.details?.length || 0 }}</el-tag>
              </div>
            </template>

            <el-alert v-if="allDone" :title="t('wms.pick.msg.allDone')" type="success" :closable="false" show-icon style="margin-bottom: 12px" />

            <div v-for="line in current.details" :key="line.lineNo"
              class="line-item" :class="{ done: lineState[line.lineNo]?.done, active: activeLine?.lineNo === line.lineNo }">
              <div class="line-hd">
                <span class="line-no">#{{ line.lineNo }}</span>
                <el-tag v-if="lineState[line.lineNo]?.done" type="success" size="small">✓ {{ t('wms.pick.fld.pickedQty') }}</el-tag>
                <el-tag v-else-if="lineState[line.lineNo]?.short" type="danger" size="small">{{ t('wms.pick.status.shortage') }}</el-tag>
                <el-tag v-else type="info" size="small">{{ t('wms.pick.status.allocated') }}</el-tag>
              </div>
              <el-row :gutter="8" class="line-row">
                <el-col :span="6"><b>{{ t('wms.pick.fld.fromLoc') }}</b><div>{{ line.locationCd || '—' }}</div></el-col>
                <el-col :span="6"><b>{{ t('wms.pick.fld.product') }}</b><div>{{ line.productCd }}<br/><span style="font-size:11px;color:#888">{{ line.productName || '' }}</span></div></el-col>
                <el-col :span="6"><b>{{ t('wms.pick.fld.lot') }}</b><div>{{ line.lotNo || '—' }}</div></el-col>
                <el-col :span="6">
                  <b>{{ t('wms.pick.fld.reqQty') }}</b>
                  <div>{{ formatQty(line.requiredQty) }} → <b style="color:#67c23a">{{ formatQty(lineState[line.lineNo]?.actualQty ?? 0) }}</b></div>
                </el-col>
              </el-row>
              <div v-if="!lineState[line.lineNo]?.done && !lineState[line.lineNo]?.short && current.status === 3" class="line-actions">
                <el-button size="small" type="primary" @click="openPickDialog(line)">{{ t('wms.pick.btn.confirmLine') }}</el-button>
                <el-button size="small" type="warning" @click="openShortDialog(line)">{{ t('wms.pick.btn.short') }}</el-button>
              </div>
            </div>
          </el-card>
        </template>
      </el-col>
    </el-row>

    <!-- ピッキング確認 Dialog -->
    <el-dialog v-model="pickDialog" :title="t('wms.pick.btn.confirmLine') + ' — #' + activeLine?.lineNo" width="500">
      <el-form v-if="activeLine" label-width="120px" size="large">
        <el-form-item :label="t('wms.pick.fld.fromLoc')">
          <el-input :placeholder="t('wms.pick.msg.scanLoc') + ': ' + (activeLine.locationCd || '')" v-model="scan.location" />
          <el-alert v-if="scan.location && scan.location !== activeLine.locationCd"
            :title="t('wms.pick.msg.locMismatch')" type="error" :closable="false" show-icon style="margin-top: 4px" />
        </el-form-item>
        <el-form-item :label="t('wms.pick.fld.product')">
          <el-input :placeholder="t('wms.pick.msg.scanProduct') + ': ' + activeLine.productCd" v-model="scan.product" />
          <el-alert v-if="scan.product && scan.product !== activeLine.productCd"
            :title="t('wms.pick.msg.productMismatch')" type="error" :closable="false" show-icon style="margin-top: 4px" />
        </el-form-item>
        <el-form-item :label="t('wms.pick.fld.actualQty')">
          <el-input-number v-model="scan.qty" :min="0" :max="activeLine.requiredQty" :precision="2"
            controls-position="right" style="width: 100%" size="large" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="pickDialog = false">{{ t('wms.common.cancel') }}</el-button>
        <el-button type="primary" size="large" :disabled="!canConfirmPick" @click="confirmPick">{{ t('wms.common.confirm') }}</el-button>
      </template>
    </el-dialog>

    <!-- 短缺報告 Dialog -->
    <el-dialog v-model="shortDialog" :title="t('wms.pick.btn.short') + ' — #' + activeLine?.lineNo" width="420">
      <el-form label-width="120px" size="default">
        <el-form-item :label="t('wms.pick.fld.actualQty')">
          <el-input-number v-model="shortForm.actualQty" :min="0" :max="activeLine?.requiredQty || 0" :precision="2"
            controls-position="right" style="width: 100%" />
        </el-form-item>
        <el-form-item :label="t('wms.pick.msg.shortReason')">
          <el-input v-model="shortForm.reason" type="textarea" :rows="2" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="shortDialog = false">{{ t('wms.common.cancel') }}</el-button>
        <el-button type="warning" @click="confirmShort">{{ t('wms.common.confirm') }}</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, computed, onMounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { List, Refresh } from '@element-plus/icons-vue'
import { useI18n } from 'vue-i18n'
import { outboundOrderApi } from '@/api/wms/outboundOrder'
import type { OutboundOrder, OutboundOrderDetail } from '@/types/wms'

const { t } = useI18n()

const tasks = ref<OutboundOrder[]>([])
const current = ref<OutboundOrder | null>(null)

// 行ごとの作業状態（client-side のみ：done/short/actualQty）
const lineState = reactive<Record<number, { done?: boolean; short?: boolean; actualQty?: number; reason?: string }>>({})

// pick dialog
const pickDialog = ref(false)
const activeLine = ref<OutboundOrderDetail | null>(null)
const scan = reactive({ location: '', product: '', qty: 0 })

// short dialog
const shortDialog = ref(false)
const shortForm = reactive({ actualQty: 0, reason: '' })

const statusMap: Record<number, string> = {
  2: t('wms.pick.status.allocated'),
  3: t('wms.pick.status.picking'),
}

const pendingLines = computed(() =>
  (current.value?.details || []).filter(d => !lineState[d.lineNo]?.done && !lineState[d.lineNo]?.short))

const pickedLines = computed(() =>
  Object.values(lineState).filter(s => s.done || s.short).length)

const progressPct = computed(() => {
  const tot = current.value?.details?.length || 0
  return tot === 0 ? 0 : Math.round((pickedLines.value / tot) * 100)
})

const allDone = computed(() => current.value && pendingLines.value.length === 0 && (current.value.details?.length || 0) > 0)

const canConfirmPick = computed(() =>
  !!activeLine.value
  && scan.location === activeLine.value.locationCd
  && scan.product === activeLine.value.productCd
  && scan.qty > 0)

function formatQty(n: number | undefined | null) {
  if (n == null) return '0'
  return Number(n).toLocaleString('ja-JP', { maximumFractionDigits: 4 })
}

async function reloadTasks() {
  // 状态 2 (Allocated) + 3 (Picking) の出庫指示を取得
  try {
    const [r1, r2] = await Promise.all([
      outboundOrderApi.search({ status: 2, pageSize: 50 }),
      outboundOrderApi.search({ status: 3, pageSize: 50 }),
    ])
    const all = [...(r1.data || []), ...(r2.data || [])]
    // priority 高い順 + plannedDate 早い順
    tasks.value = all.sort((a, b) => (b.priority - a.priority) || a.plannedDate.localeCompare(b.plannedDate))
  } catch { /* */ }
}

async function loadTask(t: OutboundOrder) {
  try {
    const r = await outboundOrderApi.get(t.outboundNo!)
    current.value = r.data
    // reset line state
    Object.keys(lineState).forEach(k => delete lineState[Number(k)])
    ;(r.data.details || []).forEach(d => {
      lineState[d.lineNo] = { done: false, short: false, actualQty: 0 }
    })
  } catch { /* */ }
}

async function onStart() {
  if (!current.value) return
  try {
    await outboundOrderApi.startPicking(current.value.outboundNo!)
    ElMessage.success(t('wms.common.success'))
    await reloadTasks()
    await loadTask(current.value)
  } catch (e: any) { ElMessage.error(e?.response?.data?.message || 'Error') }
}

function openPickDialog(line: OutboundOrderDetail) {
  activeLine.value = line
  scan.location = ''
  scan.product = ''
  scan.qty = line.requiredQty
  pickDialog.value = true
}

function confirmPick() {
  if (!activeLine.value || !canConfirmPick.value) return
  lineState[activeLine.value.lineNo] = { done: true, actualQty: scan.qty }
  pickDialog.value = false
  ElMessage.success(`✓ #${activeLine.value.lineNo}`)
}

function openShortDialog(line: OutboundOrderDetail) {
  activeLine.value = line
  shortForm.actualQty = 0
  shortForm.reason = ''
  shortDialog.value = true
}

function confirmShort() {
  if (!activeLine.value) return
  lineState[activeLine.value.lineNo] = { short: true, actualQty: shortForm.actualQty, reason: shortForm.reason }
  shortDialog.value = false
  ElMessage.warning(`⚠ #${activeLine.value.lineNo} ${t('wms.pick.status.shortage')}`)
}

async function onComplete() {
  if (!current.value) return
  try {
    await ElMessageBox.confirm(`${t('wms.pick.btn.complete')}: ${current.value.outboundNo}`, t('wms.common.confirm'), { type: 'success' })
    ElMessage.success(`${t('wms.pick.msg.allDone')} — ${t('wms.pack.title')} へ移動`)
    // ピッキング完了 — Ship/Pack 画面へ遷移するのが理想だが、ここではタスクから外す
    current.value = null
    await reloadTasks()
  } catch { /* */ }
}

onMounted(reloadTasks)
</script>

<style scoped>
.wms-picking { padding: 12px; }
.card-hd { display: flex; align-items: center; gap: 8px; }
.task-card :deep(.el-card__body) { padding: 8px; max-height: 720px; overflow-y: auto; }
.task-item {
  padding: 10px 12px; border: 1px solid #ebeef5; border-radius: 6px; margin-bottom: 6px; cursor: pointer;
  transition: all 0.15s;
}
.task-item:hover { background: #f5f7fa; border-color: #c0c4cc; }
.task-item.active { background: #ecf5ff; border-color: #409eff; }
.task-no { font-weight: bold; font-size: 14px; }
.task-meta { display: flex; gap: 4px; margin: 4px 0; }
.task-info { font-size: 12px; color: #606266; }

.hd-card { margin-bottom: 12px; }
.hd-row { display: flex; align-items: center; gap: 20px; }
.hd-progress { flex: 1; }

.line-item {
  padding: 12px; border: 1px solid #ebeef5; border-radius: 8px; margin-bottom: 8px;
  background: #fafafa;
}
.line-item.done { background: #f0f9eb; border-color: #95d475; opacity: 0.75; }
.line-item.active { border-color: #409eff; box-shadow: 0 0 6px rgba(64, 158, 255, 0.3); }
.line-hd { display: flex; align-items: center; gap: 8px; margin-bottom: 6px; }
.line-no { font-weight: bold; font-size: 16px; color: #303133; }
.line-row b { color: #606266; font-size: 12px; display: block; margin-bottom: 2px; }
.line-row > .el-col { padding: 4px 0; }
.line-actions { margin-top: 8px; display: flex; gap: 6px; }
</style>

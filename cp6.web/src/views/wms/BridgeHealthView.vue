<template>
  <div class="bridge-health">
    <div class="toolbar">
      <div>
        <h2>{{ t('wms.bridgeHealth.title') }}</h2>
        <span class="window">{{ formatRange(metrics.windowStartUtc, metrics.windowEndUtc) }}</span>
      </div>
      <el-button :icon="Refresh" circle :loading="loading" @click="loadMetrics" />
    </div>

    <el-row :gutter="12">
      <el-col :xs="24" :sm="8">
        <el-card shadow="never" class="kpi-card">
          <component :is="CircleCheckFilled" class="kpi-icon success" />
          <div>
            <div class="kpi-label">{{ t('wms.bridgeHealth.successRate') }}</div>
            <div class="kpi-value">{{ formatPercent(overallSuccessRate) }}</div>
          </div>
        </el-card>
      </el-col>
      <el-col :xs="24" :sm="8">
        <el-card shadow="never" class="kpi-card" :class="{ warn: metrics.queueDepth > 0 }">
          <component :is="WarningFilled" class="kpi-icon warning" />
          <div>
            <div class="kpi-label">{{ t('wms.bridgeHealth.queueDepth') }}</div>
            <div class="kpi-value">{{ metrics.queueDepth }}</div>
          </div>
        </el-card>
      </el-col>
      <el-col :xs="24" :sm="8">
        <el-card shadow="never" class="kpi-card" :class="{ danger: metrics.deadLetterCount > 0 }">
          <component :is="BellFilled" class="kpi-icon danger" />
          <div>
            <div class="kpi-label">{{ t('wms.bridgeHealth.deadLetterCount') }}</div>
            <div class="kpi-value">{{ metrics.deadLetterCount }}</div>
          </div>
        </el-card>
      </el-col>
    </el-row>

    <el-card shadow="never" class="panel">
      <template #header>
        <div class="panel-header">
          <span>{{ t('wms.bridgeHealth.hooks') }}</span>
          <el-tag size="small" type="info">{{ metrics.hooks.length }}</el-tag>
        </div>
      </template>
      <el-table :data="metrics.hooks" border stripe size="small" v-loading="loading">
        <el-table-column prop="hookName" :label="t('wms.bridgeHealth.hookName')" min-width="220" show-overflow-tooltip />
        <el-table-column :label="t('wms.bridgeHealth.sourceTarget')" width="150">
          <template #default="{ row }">
            <el-tag size="small" type="info">{{ row.sourceModule }}</el-tag>
            <span class="arrow">→</span>
            <el-tag size="small">{{ row.targetModule }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="totalCount" :label="t('wms.bridgeHealth.totalCount')" width="90" align="right" />
        <el-table-column :label="t('wms.bridgeHealth.successRate')" width="160" align="right">
          <template #default="{ row }">
            <el-progress
              :percentage="progressPercent(row.successRate)"
              :status="progressStatus(row.successRate)"
              :stroke-width="10"
            />
          </template>
        </el-table-column>
        <el-table-column prop="skippedCount" :label="t('wms.bridgeHealth.skippedCount')" width="90" align="right" />
        <el-table-column prop="failedCount" :label="t('wms.bridgeHealth.failedCount')" width="90" align="right">
          <template #default="{ row }"><span :class="{ bad: row.failedCount > 0 }">{{ row.failedCount }}</span></template>
        </el-table-column>
        <el-table-column prop="deadLetterCount" :label="t('wms.bridgeHealth.deadCount')" width="90" align="right">
          <template #default="{ row }"><span :class="{ bad: row.deadLetterCount > 0 }">{{ row.deadLetterCount }}</span></template>
        </el-table-column>
      </el-table>
    </el-card>

    <el-card shadow="never" class="panel">
      <template #header>
        <div class="panel-header">
          <span>{{ t('wms.bridgeHealth.latestDeadLetters') }}</span>
          <el-tag size="small" type="danger">{{ metrics.deadLetters.length }}</el-tag>
        </div>
      </template>
      <el-table :data="metrics.deadLetters" border stripe size="small" v-loading="loading" empty-text=" ">
        <el-table-column prop="hookName" :label="t('wms.bridgeHealth.hookName')" min-width="200" show-overflow-tooltip />
        <el-table-column prop="sourceNo" :label="t('wms.bridgeHealth.sourceNo')" width="140" show-overflow-tooltip />
        <el-table-column :label="t('wms.bridgeHealth.status')" width="110">
          <template #default>
            <el-tag type="danger" size="small">{{ t('wms.bridgeHealth.status.DEAD') }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="attempts" :label="t('wms.bridgeHealth.attempts')" width="90" align="right" />
        <el-table-column prop="lastError" :label="t('wms.bridgeHealth.lastError')" min-width="260" show-overflow-tooltip />
        <el-table-column :label="t('wms.bridgeHealth.createDate')" width="170">
          <template #default="{ row }">{{ formatDateTime(row.createDate) }}</template>
        </el-table-column>
        <el-table-column :label="t('wms.bridgeHealth.action')" width="150" fixed="right">
          <template #default="{ row }">
            <el-button
              link
              type="primary"
              size="small"
              :loading="compensatingId === row.eventId"
              @click="compensate(row.eventId)"
            >
              {{ t('wms.bridgeHealth.compensateBtn') }}
            </el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, onUnmounted, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import { BellFilled, CircleCheckFilled, Refresh, WarningFilled } from '@element-plus/icons-vue'
import { bridgeHealthApi } from '@/api/wms/bridgeHealth'
import type { BridgeHealthMetrics } from '@/types/wms/wms'

const { t } = useI18n()

const metrics = reactive<BridgeHealthMetrics>({
  windowStartUtc: '',
  windowEndUtc: '',
  hooks: [],
  queueDepth: 0,
  deadLetterCount: 0,
  deadLetters: [],
})
const loading = ref(false)
const compensatingId = ref('')
let refreshTimer: number | undefined

const overallSuccessRate = computed(() => {
  const total = metrics.hooks.reduce((sum, hook) => sum + hook.totalCount, 0)
  if (total <= 0) return 0
  const success = metrics.hooks.reduce((sum, hook) => sum + hook.successCount, 0)
  return success / total
})

function progressPercent(rate: number): number {
  return Math.round(Number(rate || 0) * 1000) / 10
}

function progressStatus(rate: number): 'success' | 'warning' | 'exception' | undefined {
  if (rate >= 0.98) return 'success'
  if (rate >= 0.9) return 'warning'
  return 'exception'
}

function formatPercent(rate: number): string {
  return `${progressPercent(rate).toFixed(1)}%`
}

function formatDateTime(value?: string): string {
  return value ? value.replace('T', ' ').slice(0, 19) : ''
}

function formatRange(start?: string, end?: string): string {
  if (!start || !end) return ''
  return `${t('wms.bridgeHealth.window')}: ${formatDateTime(start)} - ${formatDateTime(end)}`
}

async function loadMetrics() {
  loading.value = true
  try {
    const res = await bridgeHealthApi.metrics()
    Object.assign(metrics, res.data)
  } finally {
    loading.value = false
  }
}

async function compensate(eventId: string) {
  try {
    await ElMessageBox.confirm(
      t('wms.bridgeHealth.compensateConfirm'),
      t('wms.common.confirm'),
      { type: 'warning' },
    )
    compensatingId.value = eventId
    await bridgeHealthApi.compensate(eventId)
    ElMessage.success(t('wms.bridgeHealth.compensateSuccess'))
    await loadMetrics()
  } catch (err) {
    if (err !== 'cancel' && err !== 'close') throw err
  } finally {
    compensatingId.value = ''
  }
}

onMounted(() => {
  loadMetrics()
  refreshTimer = window.setInterval(loadMetrics, 30000)
})

onUnmounted(() => {
  if (refreshTimer) window.clearInterval(refreshTimer)
})
</script>

<style scoped>
.bridge-health {
  padding: 12px;
}

.toolbar {
  align-items: center;
  display: flex;
  justify-content: space-between;
  margin-bottom: 12px;
}

.toolbar h2 {
  font-size: 18px;
  font-weight: 600;
  line-height: 1.2;
  margin: 0 0 4px;
}

.window {
  color: #606266;
  font-size: 12px;
}

.kpi-card {
  align-items: center;
  border-left: 4px solid #409eff;
  display: flex;
  gap: 12px;
  min-height: 88px;
}

.kpi-card.warn {
  border-left-color: #e6a23c;
}

.kpi-card.danger {
  border-left-color: #f56c6c;
}

.kpi-icon {
  flex: 0 0 auto;
  height: 30px;
  width: 30px;
}

.kpi-icon.success {
  color: #67c23a;
}

.kpi-icon.warning {
  color: #e6a23c;
}

.kpi-icon.danger {
  color: #f56c6c;
}

.kpi-label {
  color: #606266;
  font-size: 12px;
  margin-bottom: 4px;
}

.kpi-value {
  color: #303133;
  font-size: 26px;
  font-weight: 650;
  line-height: 1;
}

.panel {
  margin-top: 12px;
}

.panel-header {
  align-items: center;
  display: flex;
  font-weight: 600;
  justify-content: space-between;
}

.arrow {
  color: #909399;
  margin: 0 6px;
}

.bad {
  color: #d93026;
  font-weight: 600;
}

@media (max-width: 767px) {
  .bridge-health {
    padding: 8px;
  }

  .kpi-card {
    margin-bottom: 8px;
  }
}
</style>

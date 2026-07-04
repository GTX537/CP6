<!--
  ブリッジ健全性モニタ —— 監視系特殊页（30s ポーリング）。模板不强套：KPI→CpStatCard、
  パネルヘッダ→CpSectionHeader、状態 el-tag→CpTag、token 化。setInterval/clearInterval は原様保持。
-->
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
        <CpStatCard :label="t('wms.bridgeHealth.successRate')" :value="formatPercent(overallSuccessRate)" tone="brand">
          <template #icon><CircleCheckFilled /></template>
        </CpStatCard>
      </el-col>
      <el-col :xs="24" :sm="8">
        <CpStatCard :label="t('wms.bridgeHealth.queueDepth')" :value="metrics.queueDepth" :tone="metrics.queueDepth > 0 ? 'warn' : 'info'">
          <template #icon><WarningFilled /></template>
        </CpStatCard>
      </el-col>
      <el-col :xs="24" :sm="8">
        <CpStatCard :label="t('wms.bridgeHealth.deadLetterCount')" :value="metrics.deadLetterCount" :tone="metrics.deadLetterCount > 0 ? 'danger' : 'info'">
          <template #icon><BellFilled /></template>
        </CpStatCard>
      </el-col>
    </el-row>

    <el-card shadow="never" class="panel">
      <template #header>
        <CpSectionHeader :title="t('wms.bridgeHealth.hooks')">
          <template #extra><CpTag tone="info">{{ metrics.hooks.length }}</CpTag></template>
        </CpSectionHeader>
      </template>
      <el-table :data="metrics.hooks" border stripe size="small" v-loading="loading">
        <el-table-column prop="hookName" :label="t('wms.bridgeHealth.hookName')" min-width="220" show-overflow-tooltip />
        <el-table-column :label="t('wms.bridgeHealth.sourceTarget')" width="150">
          <template #default="{ row }">
            <CpTag tone="info">{{ row.sourceModule }}</CpTag>
            <span class="arrow">→</span>
            <CpTag tone="muted">{{ row.targetModule }}</CpTag>
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
        <CpSectionHeader :title="t('wms.bridgeHealth.latestDeadLetters')">
          <template #extra><CpTag tone="danger">{{ metrics.deadLetters.length }}</CpTag></template>
        </CpSectionHeader>
      </template>
      <el-table :data="metrics.deadLetters" border stripe size="small" v-loading="loading" empty-text=" ">
        <el-table-column prop="hookName" :label="t('wms.bridgeHealth.hookName')" min-width="200" show-overflow-tooltip />
        <el-table-column prop="sourceNo" :label="t('wms.bridgeHealth.sourceNo')" width="140" show-overflow-tooltip />
        <el-table-column :label="t('wms.bridgeHealth.status')" width="110">
          <template #default>
            <CpTag tone="danger">{{ t('wms.bridgeHealth.status.DEAD') }}</CpTag>
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
import CpStatCard from '@/components/templates/CpStatCard.vue'
import CpSectionHeader from '@/components/base/CpSectionHeader.vue'
import CpTag from '@/components/base/CpTag.vue'
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
  font-size: var(--cp-fs-xl);
  font-weight: 600;
  line-height: 1.2;
  margin: 0 0 4px;
}

.window {
  color: var(--cp-muted);
  font-size: var(--cp-fs-xs);
}

.panel {
  margin-top: 12px;
}

.arrow {
  color: var(--cp-muted);
  margin: 0 6px;
}

.bad {
  color: var(--cp-danger);
  font-weight: 600;
}

@media (max-width: 767px) {
  .bridge-health {
    padding: 8px;
  }
}
</style>

<template>
  <div class="dashboard" :class="{ 'is-ready': dashboardReady }">
    <transition name="el-fade-in">
      <el-alert
        v-if="latestLog"
        :title="`[实时] ${latestLog.userName} ${latestLog.httpMethod} ${latestLog.requestUrl}`"
        type="info"
        show-icon
        :closable="true"
        @close="latestLog = null"
        style="margin-bottom: 12px"
      />
    </transition>

    <el-row :gutter="16" class="stat-cards">
      <el-col
        v-for="(card, index) in statCards"
        :key="card.key"
        :xs="12"
        :sm="12"
        :md="6"
        :lg="6"
        class="reveal-item"
        :style="{ '--delay': `${index * 80}ms` }"
      >
        <el-card shadow="hover" class="stat-card" :body-style="{ padding: '20px' }">
          <div class="stat-card-inner">
            <div>
              <div class="stat-label">{{ t('dashboard.' + card.key) }}</div>
              <div class="stat-value">{{ card.value }}</div>
            </div>
            <el-icon :size="40" :color="card.color"><component :is="card.icon" /></el-icon>
          </div>
        </el-card>
      </el-col>
    </el-row>

    <el-row :gutter="16" style="margin-top: 16px">
      <el-col :xs="24" :sm="24" :md="8" :lg="8" class="dash-col reveal-item" style="--delay: 280ms">
        <el-card shadow="hover" class="dash-panel">
          <template #header>{{ t('dashboard.topControllers') }}</template>

          <el-table v-if="topControllers.length && !isMobile" :data="topControllers" stripe size="small">
            <el-table-column prop="name" label="Controller" />
            <el-table-column prop="count" :label="t('dashboard.count')" width="80">
              <template #default="{ row }">
                <el-tag type="primary" size="small">{{ row.count }}</el-tag>
              </template>
            </el-table-column>
          </el-table>

          <div v-else-if="topControllers.length && isMobile" class="simple-list">
            <div v-for="item in topControllers" :key="item.name" class="simple-row">
              <span class="simple-row-name">{{ item.name }}</span>
              <el-tag type="primary" size="small">{{ item.count }}</el-tag>
            </div>
          </div>

          <el-empty v-else :description="t('dashboard.noData')" :image-size="60" />
        </el-card>
      </el-col>

      <el-col :xs="24" :sm="24" :md="8" :lg="8" class="dash-col reveal-item" style="--delay: 360ms">
        <el-card shadow="hover" class="dash-panel">
          <template #header>{{ t('dashboard.trend') }}</template>

          <div v-if="trend.length" class="trend-chart">
            <div v-for="item in trendFilled" :key="item.date" class="trend-bar-wrap">
              <div class="trend-bar" :style="{ height: `${barHeight(item.count)}px` }">
                <span class="trend-count">{{ item.count }}</span>
              </div>
              <div class="trend-date">{{ item.date.slice(5) }}</div>
            </div>
          </div>

          <el-empty v-else :description="t('dashboard.noData')" :image-size="60" />
        </el-card>
      </el-col>

      <el-col :xs="24" :sm="24" :md="8" :lg="8" class="dash-col reveal-item" style="--delay: 440ms">
        <el-card shadow="hover" class="dash-panel">
          <template #header>{{ t('dashboard.methodDist') }}</template>

          <div v-if="methods.length">
            <div v-for="item in methods" :key="item.name" class="method-row">
              <el-tag :type="methodColor(item.name)" size="small" style="width: 60px; text-align: center">
                {{ item.name }}
              </el-tag>
              <el-progress
                :percentage="methodPercent(item.count)"
                :stroke-width="16"
                :show-text="true"
                :format="() => String(item.count)"
                style="flex: 1; margin-left: 12px"
              />
            </div>
          </div>

          <el-empty v-else :description="t('dashboard.noData')" :image-size="60" />
        </el-card>
      </el-col>
    </el-row>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { dashboardApi } from '@/api/dashboard'
import { getConnection, startConnection } from '@/utils/signalr'
import { useBreakpoint } from '@/composables/useBreakpoint'

const { t } = useI18n()
const { isMobile } = useBreakpoint()

const summary = ref({ todayOps: 0, weekOps: 0, totalOps: 0, totalUsers: 0, totalArticles: 0 })
const latestLog = ref<any>(null)
const topControllers = ref<{ name: string; count: number }[]>([])
const trend = ref<{ date: string; count: number }[]>([])
const methods = ref<{ name: string; count: number }[]>([])
const dashboardReady = ref(false)

const statCards = computed(() => [
  { key: 'todayOps', value: summary.value.todayOps, icon: 'Sunny', color: '#409eff' },
  { key: 'weekOps', value: summary.value.weekOps, icon: 'Calendar', color: '#67c23a' },
  { key: 'totalUsers', value: summary.value.totalUsers, icon: 'User', color: '#e6a23c' },
  { key: 'totalArticles', value: summary.value.totalArticles, icon: 'Document', color: '#f56c6c' }
])

const trendFilled = computed(() => {
  const map = new Map(trend.value.map(item => [item.date, item.count]))
  const result: { date: string; count: number }[] = []

  for (let i = 6; i >= 0; i--) {
    const date = new Date()
    date.setDate(date.getDate() - i)
    const key = date.toISOString().slice(0, 10)
    result.push({ date: key, count: map.get(key) || 0 })
  }

  return result
})

const maxTrend = computed(() => Math.max(...trendFilled.value.map(item => item.count), 1))
const totalMethods = computed(() => methods.value.reduce((sum, item) => sum + item.count, 0) || 1)

function barHeight(count: number) {
  return Math.max((count / maxTrend.value) * 120, 4)
}

function methodPercent(count: number) {
  return Math.round((count / totalMethods.value) * 100)
}

function methodColor(method: string) {
  const map: Record<string, string> = {
    POST: 'success',
    PUT: 'warning',
    DELETE: 'danger'
  }

  return (map[method] || 'info') as any
}

function revealDashboard() {
  if (dashboardReady.value) return

  requestAnimationFrame(() => {
    dashboardReady.value = true
  })
}

async function loadData() {
  try {
    const res = await dashboardApi.getSummary() as any
    summary.value = res.summary
    topControllers.value = res.topControllers
    trend.value = res.trend
    methods.value = res.methods
  } finally {
    revealDashboard()
  }
}

onMounted(async () => {
  await loadData()

  try {
    await startConnection()
    const conn = getConnection()

    conn.on('NewOperLog', (log: any) => {
      latestLog.value = log
      window.setTimeout(() => {
        latestLog.value = null
      }, 5000)
      loadData()
    })
  } catch {
    // SignalR failures should not block the dashboard shell.
  }
})

onUnmounted(() => {
  const conn = getConnection()
  conn.off('NewOperLog')
})
</script>

<style scoped>
.stat-cards {
  margin-bottom: 0;
}

.dashboard {
  position: relative;
}

.reveal-item {
  opacity: 0;
  transform: translateY(22px) scale(0.985);
  filter: blur(10px);
}

.dashboard.is-ready .reveal-item {
  animation: dashboard-rise 0.7s cubic-bezier(0.22, 1, 0.36, 1) forwards;
  animation-delay: var(--delay, 0ms);
}

.stat-card,
.dash-panel {
  border: 1px solid rgba(255, 255, 255, 0.72);
  background: linear-gradient(180deg, rgba(255, 255, 255, 0.96), rgba(248, 251, 255, 0.92));
  box-shadow:
    0 18px 40px rgba(148, 163, 184, 0.12),
    inset 0 1px 0 rgba(255, 255, 255, 0.8);
  backdrop-filter: blur(14px);
  transition: transform 0.24s ease, box-shadow 0.24s ease;
}

.stat-card:hover,
.dash-panel:hover {
  transform: translateY(-4px);
  box-shadow:
    0 24px 52px rgba(148, 163, 184, 0.16),
    inset 0 1px 0 rgba(255, 255, 255, 0.86);
}

.stat-card-inner {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.stat-label {
  color: #909399;
  font-size: 14px;
  margin-bottom: 8px;
}

.stat-value {
  font-size: 28px;
  font-weight: bold;
  color: #303133;
}

.trend-chart {
  display: flex;
  align-items: flex-end;
  justify-content: space-around;
  height: 160px;
  padding-top: 16px;
}

.trend-bar-wrap {
  display: flex;
  flex-direction: column;
  align-items: center;
}

.trend-bar {
  width: 32px;
  background: linear-gradient(180deg, #409eff 0%, #79bbff 100%);
  border-radius: 4px 4px 0 0;
  position: relative;
  display: flex;
  justify-content: center;
  min-height: 4px;
}

.trend-count {
  font-size: 11px;
  color: #303133;
  position: absolute;
  top: -18px;
  font-weight: bold;
}

.trend-date {
  font-size: 11px;
  color: #909399;
  margin-top: 4px;
}

.method-row {
  display: flex;
  align-items: center;
  margin-bottom: 12px;
}

.simple-list {
  display: flex;
  flex-direction: column;
}

.simple-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 10px 0;
  border-bottom: 1px solid #f0f2f5;
  font-size: 14px;
}

.simple-row:last-child {
  border-bottom: none;
}

.simple-row-name {
  flex: 1;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  color: #303133;
  margin-right: 8px;
}

@keyframes dashboard-rise {
  from {
    opacity: 0;
    transform: translateY(22px) scale(0.985);
    filter: blur(10px);
  }

  to {
    opacity: 1;
    transform: translateY(0) scale(1);
    filter: blur(0);
  }
}

@media (prefers-reduced-motion: reduce) {
  .reveal-item,
  .dashboard.is-ready .reveal-item,
  .stat-card,
  .dash-panel {
    animation: none !important;
    transition: none !important;
    transform: none !important;
    filter: none !important;
    opacity: 1 !important;
  }
}

@media (max-width: 767px) {
  .stat-cards :deep(.el-col) {
    margin-bottom: 12px;
  }

  .dash-col {
    margin-bottom: 12px;
  }

  .stat-value {
    font-size: 22px;
  }

  .stat-label {
    font-size: 12px;
  }

  .trend-bar {
    width: 22px;
  }

  .trend-chart {
    height: 140px;
  }

  .method-row {
    margin-bottom: 10px;
  }
}
</style>

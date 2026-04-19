<template>
  <div class="dashboard">
    <!-- 实时通知横幅 -->
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

    <!-- 顶部统计卡片 -->
    <el-row :gutter="16" class="stat-cards">
      <el-col :span="6" v-for="card in statCards" :key="card.key">
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
      <!-- 操作排行 TOP 5 -->
      <el-col :span="8">
        <el-card shadow="hover">
          <template #header>{{ t('dashboard.topControllers') }}</template>
          <el-table :data="topControllers" stripe size="small" v-if="topControllers.length">
            <el-table-column prop="name" label="Controller" />
            <el-table-column prop="count" :label="t('dashboard.count')" width="80">
              <template #default="{ row }">
                <el-tag type="primary" size="small">{{ row.count }}</el-tag>
              </template>
            </el-table-column>
          </el-table>
          <el-empty v-else :description="t('dashboard.noData')" :image-size="60" />
        </el-card>
      </el-col>

      <!-- 7日趋势 -->
      <el-col :span="8">
        <el-card shadow="hover">
          <template #header>{{ t('dashboard.trend') }}</template>
          <div v-if="trend.length" class="trend-chart">
            <div v-for="item in trendFilled" :key="item.date" class="trend-bar-wrap">
              <div class="trend-bar" :style="{ height: barHeight(item.count) + 'px' }">
                <span class="trend-count">{{ item.count }}</span>
              </div>
              <div class="trend-date">{{ item.date.slice(5) }}</div>
            </div>
          </div>
          <el-empty v-else :description="t('dashboard.noData')" :image-size="60" />
        </el-card>
      </el-col>

      <!-- 方法分布 -->
      <el-col :span="8">
        <el-card shadow="hover">
          <template #header>{{ t('dashboard.methodDist') }}</template>
          <div v-if="methods.length">
            <div v-for="item in methods" :key="item.name" class="method-row">
              <el-tag :type="methodColor(item.name)" size="small" style="width: 60px; text-align: center">{{ item.name }}</el-tag>
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
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { dashboardApi } from '@/api/dashboard'
import { getConnection, startConnection } from '@/utils/signalr'

const { t } = useI18n()

const summary = ref({ todayOps: 0, weekOps: 0, totalOps: 0, totalUsers: 0, totalArticles: 0 })
const latestLog = ref<any>(null)
const topControllers = ref<{ name: string; count: number }[]>([])
const trend = ref<{ date: string; count: number }[]>([])
const methods = ref<{ name: string; count: number }[]>([])

const statCards = computed(() => [
  { key: 'todayOps', value: summary.value.todayOps, icon: 'Sunny', color: '#409eff' },
  { key: 'weekOps', value: summary.value.weekOps, icon: 'Calendar', color: '#67c23a' },
  { key: 'totalUsers', value: summary.value.totalUsers, icon: 'User', color: '#e6a23c' },
  { key: 'totalArticles', value: summary.value.totalArticles, icon: 'Document', color: '#f56c6c' }
])

// 补全最近7天（没有数据的日期填0）
const trendFilled = computed(() => {
  const map = new Map(trend.value.map(t => [t.date, t.count]))
  const result = []
  for (let i = 6; i >= 0; i--) {
    const d = new Date()
    d.setDate(d.getDate() - i)
    const key = d.toISOString().slice(0, 10)
    result.push({ date: key, count: map.get(key) || 0 })
  }
  return result
})

const maxTrend = computed(() => Math.max(...trendFilled.value.map(t => t.count), 1))
function barHeight(count: number) {
  return Math.max((count / maxTrend.value) * 120, 4)
}

const totalMethods = computed(() => methods.value.reduce((s, m) => s + m.count, 0) || 1)
function methodPercent(count: number) {
  return Math.round((count / totalMethods.value) * 100)
}

function methodColor(method: string) {
  const map: Record<string, string> = { POST: 'success', PUT: 'warning', DELETE: 'danger' }
  return (map[method] || 'info') as any
}

async function loadData() {
  const res = await dashboardApi.getSummary()
  summary.value = res.data.summary
  topControllers.value = res.data.topControllers
  trend.value = res.data.trend
  methods.value = res.data.methods
}

onMounted(async () => {
  await loadData()

  // SignalR: 监听服务端推送的 NewOperLog 事件
  await startConnection()
  const conn = getConnection()
  conn.on('NewOperLog', (log: any) => {
    // 显示实时通知
    latestLog.value = log
    // 5秒后自动隐藏
    setTimeout(() => { latestLog.value = null }, 5000)
    // 重新加载统计数据
    loadData()
  })
})

onUnmounted(() => {
  const conn = getConnection()
  conn.off('NewOperLog')
})
</script>

<style scoped>
.stat-cards { margin-bottom: 0; }
.stat-card-inner {
  display: flex;
  justify-content: space-between;
  align-items: center;
}
.stat-label { color: #909399; font-size: 14px; margin-bottom: 8px; }
.stat-value { font-size: 28px; font-weight: bold; color: #303133; }

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
</style>

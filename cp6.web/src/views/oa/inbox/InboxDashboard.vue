<template>
  <div class="inbox-dashboard">
    <!-- 4 stat cards -->
    <el-row :gutter="12" class="stat-row">
      <el-col :span="6">
        <CpStatCard :label="t('oa.dashboard.pending')" :value="stats?.pendingCount ?? 0" tone="warn" />
      </el-col>
      <el-col :span="6">
        <CpStatCard :label="t('oa.dashboard.running')" :value="stats?.runningCount ?? 0" tone="info" />
      </el-col>
      <el-col :span="6">
        <CpStatCard :label="t('oa.dashboard.doneThisMonth')" :value="stats?.doneThisMonth ?? 0" tone="brand" />
      </el-col>
      <el-col :span="6">
        <CpStatCard :label="t('oa.dashboard.rejected')" :value="stats?.rejectedBackToMe ?? 0" tone="danger" />
      </el-col>
    </el-row>

    <!-- 7-day trend bars (no chart lib) -->
    <el-card shadow="never" class="trend-card">
      <template #header><CpSectionHeader :title="t('oa.dashboard.trend')" /></template>
      <div v-if="stats?.trend?.length" class="trend-bars">
        <div v-for="p in stats.trend" :key="p.date" class="trend-col">
          <span class="bar-count">{{ p.count }}</span>
          <div class="bar-track">
            <div class="bar-fill" :style="{ height: barHeight(p.count) + '%' }" />
          </div>
          <span class="bar-date">{{ p.date.slice(5) }}</span>
        </div>
      </div>
      <CpEmpty v-else :text="t('oa.dashboard.noTrend')" />
    </el-card>

    <!-- Recent pending -->
    <el-card shadow="never" class="recent-card">
      <template #header><CpSectionHeader :title="t('oa.dashboard.recentPending')" /></template>
      <el-table
        :data="stats?.recentPending ?? []"
        stripe
        size="small"
        v-loading="loading"
        style="cursor: pointer"
        @row-click="onRecentClick"
      >
        <el-table-column prop="flowName" :label="t('oa.col.flowName')" min-width="160" />
        <el-table-column prop="starterName" :label="t('oa.col.starter')" width="120" />
        <el-table-column :label="t('oa.col.sentAt')" width="170">
          <template #default="{ row }">{{ formatTime(row.sentAt) }}</template>
        </el-table-column>
      </el-table>
      <CpEmpty
        v-if="!loading && !(stats?.recentPending?.length)"
        :text="t('oa.dashboard.noPending')"
      />
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { inboxApi } from '@/api/oa/inbox'
import CpStatCard from '@/components/templates/CpStatCard.vue'
import CpSectionHeader from '@/components/base/CpSectionHeader.vue'
import CpEmpty from '@/components/base/CpEmpty.vue'
import type { InboxStats, PendingItem } from '@/types/oa/inbox'

const { t } = useI18n()
const emit = defineEmits<{ 'open-detail': [id: string] }>()

const stats = ref<InboxStats | null>(null)
const loading = ref(false)

async function load() {
  loading.value = true
  try {
    const res = await inboxApi.stats()
    stats.value = (res as any).data as InboxStats
  } finally {
    loading.value = false
  }
}

function barHeight(count: number): number {
  const trend = stats.value?.trend ?? []
  const max = Math.max(...trend.map((p) => p.count), 1)
  return Math.round((count / max) * 100)
}

function onRecentClick(row: PendingItem) {
  emit('open-detail', row.instanceId)
}

function formatTime(s: string): string {
  return s ? s.replace('T', ' ').slice(0, 19) : ''
}

onMounted(load)
</script>

<style scoped>
.inbox-dashboard {
  display: flex;
  flex-direction: column;
  gap: 14px;
}
.stat-row {
  margin: 0 !important;
}
.trend-card,
.recent-card {
  margin: 0;
}
.trend-bars {
  display: flex;
  align-items: flex-end;
  gap: 8px;
  padding: 4px 0;
}
.trend-col {
  flex: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 3px;
}
.bar-track {
  width: 100%;
  height: 60px;
  background: var(--cp-line-soft);
  border-radius: var(--cp-r-sm);
  display: flex;
  flex-direction: column;
  justify-content: flex-end;
  overflow: hidden;
}
.bar-fill {
  background: var(--cp-brand); /* cp-chart-color */
  width: 100%;
  min-height: 2px;
  border-radius: var(--cp-r-sm) var(--cp-r-sm) 0 0;
  transition: height 0.3s;
}
.bar-count {
  font-size: 10px;
  color: var(--cp-muted);
  line-height: 1;
}
.bar-date {
  font-size: 10px;
  color: var(--cp-faint);
  white-space: nowrap;
  line-height: 1;
}
</style>

<template>
  <div class="inbox-running">
    <div class="table-toolbar">
      <CpTag>{{ t('共 {n} 条', { n: rows.length }) }}</CpTag>
      <el-button :icon="Refresh" circle size="small" :loading="loading" @click="load" />
    </div>

    <el-table
      v-if="!isMobile"
      :data="rows"
      border
      stripe
      size="small"
      max-height="620"
      v-loading="loading"
      style="cursor: pointer"
      @row-click="onRowClick"
    >
      <el-table-column prop="flowName" :label="t('oa.col.flowName')" min-width="160" />
      <el-table-column prop="currentNode" :label="t('oa.col.currentNode')" width="140" />
      <el-table-column :label="t('oa.col.handlers')" width="180" show-overflow-tooltip>
        <template #default="{ row }">{{ row.currentHandlers.join('、') }}</template>
      </el-table-column>
      <el-table-column :label="t('oa.col.status')" width="110">
        <template #default="{ row }">
          <CpTag :tone="instanceStatusTone(row.status)">
            {{ t(instanceStatusText(row.status)) }}
          </CpTag>
        </template>
      </el-table-column>
      <el-table-column :label="t('oa.col.createDate')" width="170">
        <template #default="{ row }">{{ formatTime(row.createDate) }}</template>
      </el-table-column>
    </el-table>

    <div v-if="isMobile" class="mobile-list" v-loading="loading">
      <div v-for="row in rows" :key="row.instanceId" class="mobile-row" @click="onRowClick(row)">
        <div class="mobile-main">
          <span class="mobile-flow">{{ row.flowName }}</span>
          <CpTag :tone="instanceStatusTone(row.status)">{{ t(instanceStatusText(row.status)) }}</CpTag>
        </div>
        <div class="mobile-meta">
          <span>{{ row.currentNode }} · {{ row.currentHandlers.join('、') }}</span>
          <span>{{ formatTime(row.createDate) }}</span>
        </div>
      </div>
    </div>
    <CpEmpty v-if="!loading && !rows.length" :text="t('oa.running.empty')" />
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { Refresh } from '@element-plus/icons-vue'
import { inboxApi } from '@/api/oa/inbox'
import { instanceStatusText } from '@/views/oa/inbox/inboxModel'
import CpTag, { type Tone } from '@/components/base/CpTag.vue'
import CpEmpty from '@/components/base/CpEmpty.vue'
import type { RunningItem } from '@/types/oa/inbox'
import { useBreakpoint } from '@/composables/useBreakpoint'

const { t } = useI18n()
const emit = defineEmits<{ 'open-detail': [id: string] }>()
const { isMobile } = useBreakpoint()

/** 実例状態码 → CpTag 色調（对齐 inboxModel.instanceStatusType：warning/success/danger/info/info）。 */
function instanceStatusTone(s: number): Tone {
  return (['warn', 'ok', 'danger', 'info', 'info'] as Tone[])[s] ?? 'info'
}

const rows = ref<RunningItem[]>([])
const loading = ref(false)

async function load() {
  loading.value = true
  try {
    const res = await inboxApi.running()
    rows.value = ((res as any).data as RunningItem[]) || []
  } finally {
    loading.value = false
  }
}

function onRowClick(row: RunningItem) {
  emit('open-detail', row.instanceId)
}

function formatTime(s: string): string {
  return s ? s.replace('T', ' ').slice(0, 19) : ''
}

onMounted(load)
</script>

<style scoped>
.inbox-running {
  padding: 0;
}
.table-toolbar {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-bottom: 8px;
}

.mobile-list {
  display: flex;
  flex-direction: column;
}

.mobile-row {
  padding: 12px 2px;
  border-bottom: 1px solid var(--cp-line);
  cursor: pointer;
}

.mobile-row:last-child {
  border-bottom: none;
}

.mobile-main {
  display: flex;
  align-items: center;
  gap: 8px;
  color: var(--cp-ink);
  font-size: 14px;
  margin-bottom: 6px;
}

.mobile-flow {
  flex: 1;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.mobile-meta {
  display: flex;
  justify-content: space-between;
  gap: 10px;
  color: var(--cp-muted);
  font-size: 12px;
}
</style>

<template>
  <div class="inbox-running">
    <div class="table-toolbar">
      <el-tag size="small">{{ t('共 {n} 条', { n: rows.length }) }}</el-tag>
      <el-button :icon="Refresh" circle size="small" :loading="loading" @click="load" />
    </div>

    <el-table
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
          <el-tag :type="(instanceStatusType(row.status) as any)" size="small">
            {{ t(instanceStatusText(row.status)) }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column :label="t('oa.col.createDate')" width="170">
        <template #default="{ row }">{{ formatTime(row.createDate) }}</template>
      </el-table-column>
    </el-table>
    <el-empty v-if="!loading && !rows.length" :image-size="80" :description="t('oa.running.empty')" />
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { Refresh } from '@element-plus/icons-vue'
import { inboxApi } from '@/api/oa/inbox'
import { instanceStatusType, instanceStatusText } from '@/views/oa/inbox/inboxModel'
import type { RunningItem } from '@/types/oa/inbox'

const { t } = useI18n()
const emit = defineEmits<{ 'open-detail': [id: string] }>()

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
</style>

<template>
  <div class="inbox-done">
    <!-- Controls: month picker + tabs -->
    <div class="done-controls">
      <el-date-picker
        v-model="selectedMonth"
        type="month"
        value-format="YYYY-MM"
        :placeholder="t('oa.done.allMonths')"
        clearable
        size="small"
        style="width: 150px"
        @change="load"
      />
      <el-tabs v-model="activeTab" class="done-tabs" @tab-change="load">
        <el-tab-pane :label="t('oa.done.mine')" name="mine" />
        <el-tab-pane :label="t('oa.done.all')" name="all" />
        <el-tab-pane :label="t('oa.done.cc')" name="cc" />
      </el-tabs>
    </div>

    <div class="table-toolbar">
      <CpTag>{{ t('共 {n} 条', { n: rows.length }) }}</CpTag>
      <el-button :icon="Refresh" circle size="small" :loading="loading" @click="load" />
    </div>

    <el-table
      :data="rows"
      border
      stripe
      size="small"
      max-height="560"
      v-loading="loading"
      style="cursor: pointer"
      @row-click="onRowClick"
    >
      <el-table-column prop="flowName" :label="t('oa.col.flowName')" min-width="160" />
      <el-table-column prop="starterName" :label="t('oa.col.starter')" width="120" />
      <el-table-column :label="t('oa.col.status')" width="110">
        <template #default="{ row }">
          <CpTag :tone="formToStatusTone(row.formToStatus)">
            {{ t(formToStatusText(row.formToStatus)) }}
          </CpTag>
        </template>
      </el-table-column>
      <el-table-column :label="t('oa.col.doneAt')" width="170">
        <template #default="{ row }">{{ formatTime(row.doneAt) }}</template>
      </el-table-column>
    </el-table>
    <CpEmpty v-if="!loading && !rows.length" :text="t('oa.done.empty')" />
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { Refresh } from '@element-plus/icons-vue'
import { inboxApi } from '@/api/oa/inbox'
import { formToStatusText } from '@/views/oa/inbox/inboxModel'
import CpTag, { type Tone } from '@/components/base/CpTag.vue'
import CpEmpty from '@/components/base/CpEmpty.vue'
import type { DoneItem } from '@/types/oa/inbox'

const { t } = useI18n()
const emit = defineEmits<{ 'open-detail': [id: string] }>()

const rows = ref<DoneItem[]>([])
const loading = ref(false)
// Default: current month, mine tab
const now = new Date()
const selectedMonth = ref<string>(
  `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}`
)
const activeTab = ref('mine')

async function load() {
  loading.value = true
  try {
    let year: number | undefined
    let month: number | undefined
    if (selectedMonth.value) {
      const parts = selectedMonth.value.split('-')
      year = Number(parts[0])
      month = Number(parts[1])
    }
    const res = await inboxApi.done({ year, month, tab: activeTab.value })
    rows.value = ((res as any).data as DoneItem[]) || []
  } finally {
    loading.value = false
  }
}

function onRowClick(row: DoneItem) {
  emit('open-detail', row.instanceId)
}

// 0=pending(warn) 1=approved(ok) 2=rejected(danger) 3+=info（对齐原 formToTagType：warning/success/danger/info…）
function formToStatusTone(s: number): Tone {
  const map: Tone[] = ['warn', 'ok', 'danger', 'info', 'info', 'info', 'info']
  return map[s] ?? 'info'
}

function formatTime(s: string): string {
  return s ? s.replace('T', ' ').slice(0, 19) : ''
}

onMounted(load)
</script>

<style scoped>
.inbox-done {
  padding: 0;
}
.done-controls {
  display: flex;
  align-items: center;
  gap: 16px;
  margin-bottom: 0;
}
.done-tabs {
  flex: 1;
}
.done-tabs :deep(.el-tabs__header) {
  margin-bottom: 0;
}
.table-toolbar {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-bottom: 8px;
  margin-top: 8px;
}
</style>

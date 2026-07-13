<template>
  <div class="inbox-done">
    <!-- Controls: month picker + tabs -->
    <div v-if="!isMobile" class="done-controls">
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
      <el-button v-if="isMobile" :icon="Filter" size="small" round @click="filterDrawer = true">
        {{ t('oa.inbox.mobileFilter') }}
      </el-button>
    </div>

    <!-- 移动端：筛选底部抽屉 -->
    <el-drawer v-model="filterDrawer" direction="btt" size="40%" :title="t('oa.inbox.mobileFilter')">
      <el-form label-width="90px">
        <el-form-item :label="t('oa.done.allMonths')">
          <el-date-picker v-model="selectedMonth" type="month" value-format="YYYY-MM"
            :placeholder="t('oa.done.allMonths')" clearable style="width: 100%" @change="load" />
        </el-form-item>
        <el-form-item>
          <el-radio-group v-model="activeTab" @change="load">
            <el-radio-button label="mine">{{ t('oa.done.mine') }}</el-radio-button>
            <el-radio-button label="all">{{ t('oa.done.all') }}</el-radio-button>
            <el-radio-button label="cc">{{ t('oa.done.cc') }}</el-radio-button>
          </el-radio-group>
        </el-form-item>
      </el-form>
    </el-drawer>

    <el-table
      v-if="!isMobile"
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

    <div v-if="isMobile" class="mobile-list" v-loading="loading">
      <div v-for="row in rows" :key="row.instanceId" class="mobile-row" @click="onRowClick(row)">
        <div class="mobile-main">
          <span class="mobile-flow">{{ row.flowName }}</span>
          <CpTag :tone="formToStatusTone(row.formToStatus)">{{ t(formToStatusText(row.formToStatus)) }}</CpTag>
        </div>
        <div class="mobile-meta">
          <span>{{ row.starterName }}</span>
          <span>{{ formatTime(row.doneAt) }}</span>
        </div>
      </div>
    </div>
    <CpEmpty v-if="!loading && !rows.length" :text="t('oa.done.empty')" />
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { Filter, Refresh } from '@element-plus/icons-vue'
import { inboxApi } from '@/api/oa/inbox'
import { formToStatusText } from '@/views/oa/inbox/inboxModel'
import CpTag, { type Tone } from '@/components/base/CpTag.vue'
import CpEmpty from '@/components/base/CpEmpty.vue'
import type { DoneItem } from '@/types/oa/inbox'
import { useBreakpoint } from '@/composables/useBreakpoint'

const { t } = useI18n()
const emit = defineEmits<{ 'open-detail': [id: string] }>()
const { isMobile } = useBreakpoint()
const filterDrawer = ref(false)

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

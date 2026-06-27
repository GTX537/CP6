<template>
  <div class="flow-admin">
    <div class="page-header"><h2>{{ t('oa.flowadmin.title') }}</h2></div>

    <el-alert type="info" :closable="false" show-icon class="hint-alert">
      {{ t('oa.flowadmin.uniqueHint') }}
    </el-alert>

    <el-card shadow="never" class="table-card">
      <div class="table-toolbar">
        <el-tag size="small">{{ t('共 {n} 条', { n: rows.length }) }}</el-tag>
        <el-button :icon="Refresh" circle size="small" :loading="loading" @click="load" />
      </div>

      <el-table :data="rows" border stripe size="small" max-height="620" v-loading="loading">
        <el-table-column prop="flowKey" :label="t('oa.flowadmin.col.flowKey')" width="200" />
        <el-table-column prop="flowName" :label="t('oa.flowadmin.col.flowName')" min-width="160" />
        <el-table-column prop="formKey" :label="t('oa.flowadmin.col.formKey')" width="180" />
        <el-table-column prop="version" :label="t('oa.flowadmin.col.version')" width="80" />
        <el-table-column :label="t('oa.flowadmin.col.enable')" width="110">
          <template #default="{ row }">
            <el-switch
              v-model="row.enable"
              :loading="toggling.has(row.flowKey)"
              :disabled="toggling.has(row.flowKey)"
              @change="(val: boolean | string | number) => toggleEnable(row, val as boolean)"
            />
          </template>
        </el-table-column>
      </el-table>

      <el-empty
        v-if="!rows.length && !loading"
        :description="t('oa.flowadmin.empty')"
        :image-size="80"
      />
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import { Refresh } from '@element-plus/icons-vue'
import { flowAdminApi } from '@/api/oa/flowAdmin'
import type { FlowAdminItem } from '@/types/oa/inbox'

const { t } = useI18n()

const rows = ref<FlowAdminItem[]>([])
const loading = ref(false)
// per-row guard: flowKeys currently being toggled — prevents races and double-clicks
const toggling = reactive(new Set<string>())

async function load() {
  loading.value = true
  try {
    const res = await flowAdminApi.list()
    rows.value = res.data ?? []
  } finally {
    loading.value = false
  }
}

async function toggleEnable(row: FlowAdminItem, newVal: boolean) {
  // Guard against concurrent calls for the same row
  if (toggling.has(row.flowKey)) return
  toggling.add(row.flowKey)
  try {
    await flowAdminApi.enable(row.flowKey, newVal)
    ElMessage.success(newVal ? t('oa.flowadmin.enabled') : t('oa.flowadmin.disabled'))
    // Reload to reflect any server-side side-effects (e.g. another flow auto-disabled)
    await load()
  } catch {
    // The http interceptor (http.ts line 89) already toasts the error message for 400
    // responses (including E-WF-008 conflicts). We only need to revert the switch.
    row.enable = !newVal
  } finally {
    toggling.delete(row.flowKey)
  }
}

onMounted(load)
</script>

<style scoped>
.flow-admin {
  padding: 16px;
}
.page-header {
  margin-bottom: 12px;
}
.page-header h2 {
  margin: 0;
  font-size: 20px;
  font-weight: 650;
  color: #303133;
}
.hint-alert {
  margin-bottom: 12px;
}
.table-toolbar {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-bottom: 8px;
}
</style>

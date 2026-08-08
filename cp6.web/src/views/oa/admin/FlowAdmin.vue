<!--
  流程管理（OA Phase B，菜单734）—— CpPageShell + CpListPage 迁移。
  单一 flowAdminApi.list() 全量（无搜索/无分页 → :paginated="false"，onMounted 自动 fetch）；
  启停开关走 col-enable 插槽（行内 el-switch，per-row toggling 守卫 + 乐观回滚保留），
  切换成功后 listRef.reload() 反映服务端联动（唯一启用互斥）；件数走 CpPageShell :count←@total-change。
  el-alert 唯一性提示无 Cp 等价物，作为壳内首个子项保留。
-->
<template>
  <CpPageShell :title="t('oa.flowadmin.title')" :count="activeTab === 'flows' ? total : undefined">
    <template #actions>
      <el-button v-if="activeTab === 'flows'" v-permission="'oa-inbox:batch-transfer'" type="warning" plain @click="batchTransferVisible = true">
        {{ t('oa.bt.entry') }}
      </el-button>
      <el-button v-if="activeTab === 'flows'" :icon="Refresh" circle :loading="refreshing" @click="refresh" />
    </template>

    <el-tabs v-model="activeTab">
      <el-tab-pane :label="t('oa.flowadmin.tab.flows')" name="flows">
        <el-alert type="info" :closable="false" show-icon>
          {{ t('oa.flowadmin.uniqueHint') }}
        </el-alert>

        <CpListPage
          ref="listRef"
          :columns="columns"
          :fetch="fetchFlows"
          :paginated="false"
          :empty-text="t('oa.flowadmin.empty')"
          @total-change="total = $event"
        >
          <template #col-enable="{ row }">
            <el-switch
              v-permission="'oa-flow-admin:enable'"
              v-model="(row as FlowAdminItem).enable"
              :loading="toggling.has((row as FlowAdminItem).flowKey)"
              :disabled="toggling.has((row as FlowAdminItem).flowKey)"
              @change="(val: boolean | string | number) => toggleEnable(row as FlowAdminItem, val as boolean)"
            />
          </template>
        </CpListPage>
      </el-tab-pane>
      <el-tab-pane :label="t('oa.flowtrigger.tab')" name="triggers" lazy>
        <FlowTriggerPanel />
      </el-tab-pane>
      <el-tab-pane :label="t('oa.connector.tab')" name="connectors" lazy>
        <WfConnectorPanel />
      </el-tab-pane>
    </el-tabs>

    <BatchTransferDialog v-model="batchTransferVisible" />
  </CpPageShell>
</template>

<script setup lang="ts">
import { computed, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import { Refresh } from '@element-plus/icons-vue'
import CpPageShell from '@/components/templates/CpPageShell.vue'
import CpListPage, { type ListColumn, type ListFetch, type ListPageExpose } from '@/components/templates/CpListPage.vue'
import FlowTriggerPanel from './FlowTriggerPanel.vue'
import WfConnectorPanel from './WfConnectorPanel.vue'
import BatchTransferDialog from './BatchTransferDialog.vue'
import { flowAdminApi } from '@/api/oa/flowAdmin'
import type { FlowAdminItem } from '@/types/oa/inbox'

const { t } = useI18n()

const activeTab = ref<'flows' | 'triggers' | 'connectors'>('flows')
const batchTransferVisible = ref(false)
const total = ref<number>()
const listRef = ref<ListPageExpose | null>(null)
const refreshing = ref(false)
// per-row guard: flowKeys currently being toggled — prevents races and double-clicks
const toggling = reactive(new Set<string>())

const columns = computed<ListColumn[]>(() => [
  { prop: 'flowKey', label: t('oa.flowadmin.col.flowKey'), width: 200, kind: 'mono' },
  { prop: 'flowName', label: t('oa.flowadmin.col.flowName'), minWidth: 160 },
  { prop: 'formKey', label: t('oa.flowadmin.col.formKey'), width: 180 },
  { prop: 'version', label: t('oa.flowadmin.col.version'), width: 80 },
  { prop: 'enable', label: t('oa.flowadmin.col.enable'), width: 110 },
])

// 单一取数：flowAdminApi.list() 返回全量（无 total）→ 客户端 total = 数组长度，配 :paginated="false"
const fetchFlows: ListFetch = async () => {
  const res = await flowAdminApi.list()
  const data = res.data ?? []
  return { rows: data, total: data.length }
}

async function refresh() {
  refreshing.value = true
  try {
    await listRef.value?.reload()
  } finally {
    refreshing.value = false
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
    await listRef.value?.reload()
  } catch {
    // The http interceptor (http.ts line 89) already toasts the error message for 400
    // responses (including E-WF-008 conflicts). We only need to revert the switch.
    row.enable = !newVal
  } finally {
    toggling.delete(row.flowKey)
  }
}
</script>

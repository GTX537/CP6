<!--
  连接器管理 tab（OA 流程管理页，D-T2）—— 当前租户 Wf_Connector CRUD + 启停。
  列表掩码（凭证列仅显示「已配置/无」徽标，明文绝不回显）；新建/编辑经 WfConnectorDialog；
  启停走行内 el-switch（per-row toggling 守卫 + 失败回滚）。零硬编码色（CpTag tone）。
-->
<template>
  <div class="wf-connector-panel">
    <div class="panel-actions">
      <el-button type="primary" @click="openCreate">{{ t('oa.connector.new') }}</el-button>
      <el-button :icon="Refresh" circle @click="reload" />
    </div>

    <el-table :data="rows" v-loading="loading" :empty-text="t('oa.connector.empty')">
      <el-table-column prop="name" :label="t('oa.connector.col.name')" width="160" />
      <el-table-column prop="displayName" :label="t('oa.connector.col.displayName')" min-width="140" />
      <el-table-column prop="baseUrl" :label="t('oa.connector.col.baseUrl')" min-width="220" show-overflow-tooltip />
      <el-table-column prop="timeoutSec" :label="t('oa.connector.col.timeout')" width="110">
        <template #default="{ row }">{{ row.timeoutSec }}s</template>
      </el-table-column>
      <el-table-column :label="t('oa.connector.col.auth')" width="120">
        <template #default="{ row }">
          <CpTag :tone="row.hasAuth ? 'ok' : 'muted'">
            {{ row.hasAuth ? t('oa.connector.authYes') : t('oa.connector.authNo') }}
          </CpTag>
        </template>
      </el-table-column>
      <el-table-column :label="t('oa.connector.col.enabled')" width="90">
        <template #default="{ row }">
          <el-switch :model-value="row.enabled" :loading="toggling.has(row.id)"
                     @change="(v: boolean | string | number) => toggleEnable(row, v as boolean)" />
        </template>
      </el-table-column>
      <el-table-column :label="t('oa.connector.col.actions')" width="120" fixed="right">
        <template #default="{ row }">
          <el-button link type="primary" @click="openEdit(row)">{{ t('common.edit') }}</el-button>
        </template>
      </el-table-column>
    </el-table>

    <WfConnectorDialog v-model="dialogVisible" :editing="editing" @saved="onSaved" />
  </div>
</template>

<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { Refresh } from '@element-plus/icons-vue'
import CpTag from '@/components/base/CpTag.vue'
import WfConnectorDialog from './WfConnectorDialog.vue'
import { wfConnectorApi, type WfConnectorItem } from '@/api/oa/wfConnector'

const { t } = useI18n()
const rows = ref<WfConnectorItem[]>([])
const loading = ref(false)
const toggling = reactive(new Set<string>())
const dialogVisible = ref(false)
const editing = ref<WfConnectorItem | null>(null)

async function reload() {
  loading.value = true
  try { rows.value = await wfConnectorApi.list() } finally { loading.value = false }
}
onMounted(reload)

function openCreate() { editing.value = null; dialogVisible.value = true }
function openEdit(row: WfConnectorItem) { editing.value = row; dialogVisible.value = true }
async function onSaved() { await reload() }

async function toggleEnable(row: WfConnectorItem, v: boolean) {
  if (toggling.has(row.id)) return
  toggling.add(row.id)
  try { await wfConnectorApi.enable(row.id, v); row.enabled = v }
  catch {
    // http 拦截器已 toast，无需重复提示
  }
  finally { toggling.delete(row.id); await reload() }
}
</script>

<style scoped>
.panel-actions { display: flex; justify-content: flex-end; gap: 8px; margin-bottom: 12px; }
</style>

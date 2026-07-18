<template>
  <div class="flow-trigger-panel">
    <div class="panel-actions">
      <el-button v-permission="'oa-flow-admin:FlowTrigger.Edit'" type="primary" @click="openCreate">{{ t('oa.flowtrigger.new') }}</el-button>
      <el-button :icon="Refresh" circle @click="reload" />
    </div>

    <el-table :data="rows" v-loading="loading" :empty-text="t('oa.flowtrigger.empty')">
      <el-table-column prop="triggerType" :label="t('oa.flowtrigger.col.type')" width="110">
        <template #default="{ row }">
          <CpTag :tone="typeTone(row.triggerType)">{{ t(typeLabelKey(row.triggerType)) }}</CpTag>
        </template>
      </el-table-column>
      <el-table-column prop="flowKey" :label="t('oa.flowtrigger.col.flowKey')" min-width="160" />
      <el-table-column prop="eventKey" :label="t('oa.flowtrigger.col.eventKey')" min-width="180">
        <template #default="{ row }">{{ row.eventKey ?? '—' }}</template>
      </el-table-column>
      <el-table-column :label="t('oa.flowtrigger.col.enabled')" width="90">
        <template #default="{ row }">
          <el-switch v-permission="'oa-flow-admin:FlowTrigger.Edit'" :model-value="row.enabled" :loading="toggling.has(row.id)"
                     @change="(v: boolean | string | number) => toggleEnable(row, v as boolean)" />
        </template>
      </el-table-column>
      <el-table-column prop="nextDueUtc" :label="t('oa.flowtrigger.col.nextDue')" width="170">
        <template #default="{ row }">{{ fmtUtc(row.nextDueUtc) }}</template>
      </el-table-column>
      <el-table-column prop="lastFiredUtc" :label="t('oa.flowtrigger.col.lastFired')" width="170">
        <template #default="{ row }">{{ fmtUtc(row.lastFiredUtc) }}</template>
      </el-table-column>
      <el-table-column :label="t('oa.flowtrigger.col.actions')" width="280" fixed="right">
        <template #default="{ row }">
          <el-button v-permission="'oa-flow-admin:FlowTrigger.Edit'" link type="primary" @click="openEdit(row)">{{ t('common.edit') }}</el-button>
          <el-button v-permission="'oa-flow-admin:FlowTrigger.Edit'" link type="primary" @click="manualFire(row)">{{ t('oa.flowtrigger.manualFire') }}</el-button>
          <el-button link @click="openFires(row)">{{ t('oa.flowtrigger.fires') }}</el-button>
          <el-button v-if="row.triggerType === 2" v-permission="'oa-flow-admin:FlowTrigger.Edit'" link type="danger" @click="resetKey(row)">
            {{ t('oa.flowtrigger.resetKey') }}
          </el-button>
        </template>
      </el-table-column>
    </el-table>

    <FlowTriggerDialog v-model="dialogVisible" :editing="editing" @saved="onSaved" />

    <!-- 流水抽屉（spec §4：最近 N 条 时间/结果/实例链接/错误） -->
    <el-drawer v-model="firesVisible" :title="t('oa.flowtrigger.fires')" size="480px">
      <el-table :data="fires" v-loading="firesLoading">
        <el-table-column prop="firedUtc" :label="t('oa.flowtrigger.fire.time')" width="170">
          <template #default="{ row }">{{ fmtUtc(row.firedUtc) }}</template>
        </el-table-column>
        <el-table-column :label="t('oa.flowtrigger.fire.result')" width="90">
          <template #default="{ row }">
            <CpTag :tone="row.instanceId ? 'ok' : row.error ? 'warn' : 'muted'">
              {{ row.instanceId ? t('oa.flowtrigger.fire.ok') : row.error ? t('oa.flowtrigger.fire.fail') : t('oa.flowtrigger.fire.pending') }}
            </CpTag>
          </template>
        </el-table-column>
        <el-table-column :label="t('oa.flowtrigger.fire.instance')" min-width="140">
          <template #default="{ row }">
            <router-link v-if="row.instanceId" :to="`/oa/inbox?instanceId=${row.instanceId}`">{{ row.instanceId.slice(0, 8) }}…</router-link>
            <span v-else>—</span>
          </template>
        </el-table-column>
        <el-table-column prop="error" :label="t('oa.flowtrigger.fire.error')" min-width="160" show-overflow-tooltip />
      </el-table>
    </el-drawer>
  </div>
</template>

<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Refresh } from '@element-plus/icons-vue'
import CpTag from '@/components/base/CpTag.vue'
import FlowTriggerDialog from './FlowTriggerDialog.vue'
import { flowTriggerApi, type FlowTriggerItem, type TriggerFireItem } from '@/api/oa/flowTrigger'
import { typeTone, TRIGGER_TYPES } from './flowTriggerModel'

const { t } = useI18n()
const rows = ref<FlowTriggerItem[]>([])
const loading = ref(false)
const toggling = reactive(new Set<string>())
const dialogVisible = ref(false)
const editing = ref<FlowTriggerItem | null>(null)
const firesVisible = ref(false)
const fires = ref<TriggerFireItem[]>([])
const firesLoading = ref(false)

const typeLabelKey = (v: number) => TRIGGER_TYPES.find(x => x.value === v)?.labelKey ?? 'oa.flowtrigger.type.timer'
const fmtUtc = (s?: string | null) => (s ? new Date(s).toLocaleString() : '—')

async function reload() {
  loading.value = true
  try { rows.value = await flowTriggerApi.list() } finally { loading.value = false }
}
onMounted(reload)

function openCreate() { editing.value = null; dialogVisible.value = true }
function openEdit(row: FlowTriggerItem) { editing.value = row; dialogVisible.value = true }

async function onSaved(apiKeyPlain?: string | null) {
  if (apiKeyPlain) showKeyOnce(apiKeyPlain)
  await reload()
}

/** key 一次性显示（spec §3.4：明文只此一次） */
function showKeyOnce(plain: string) {
  ElMessageBox.alert(plain, t('oa.flowtrigger.keyTitle'), {
    confirmButtonText: t('common.ok'),
    message: `${t('oa.flowtrigger.keyOnce')}\n\n${plain}`,
  })
}

async function toggleEnable(row: FlowTriggerItem, v: boolean) {
  if (toggling.has(row.id)) return
  toggling.add(row.id)
  try { await flowTriggerApi.enable(row.id, v); row.enabled = v }
  catch {
    // http 拦截器已 toast，无需重复提示
  }
  finally { toggling.delete(row.id); await reload() }
}

async function manualFire(row: FlowTriggerItem) {
  try {
    const r = await flowTriggerApi.manualFire(row.id)
    ElMessage.success(`${t('oa.flowtrigger.fired')}: ${r.instanceId ?? ''}`)
    await reload()
  } catch {
    // http 拦截器已 toast，无需重复提示
  }
}

async function resetKey(row: FlowTriggerItem) {
  try { await ElMessageBox.confirm(t('oa.flowtrigger.resetKeyConfirm'), t('oa.flowtrigger.resetKey')) }
  catch {
    // 取消即静默返回（confirm 取消以 reject 收场，不接住会抛 unhandled rejection）
    return
  }
  const r = await flowTriggerApi.resetKey(row.id)
  showKeyOnce(r.apiKeyPlain)
}

async function openFires(row: FlowTriggerItem) {
  firesVisible.value = true
  firesLoading.value = true
  try { fires.value = await flowTriggerApi.fires(row.id, 20) } finally { firesLoading.value = false }
}
</script>

<style scoped>
.panel-actions { display: flex; justify-content: flex-end; gap: 8px; margin-bottom: 12px; }
</style>

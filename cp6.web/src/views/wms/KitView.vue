<!--
  キット (Kit) —— WMS 迁移批次8。el-tabs 双模块（マスタ管理 / 組立指示），各模块 list+detail 双态。
  list 态 → CpListPage（状態/種別/ON-OFF=kind:'tag'+map；数量/実行日時=col slot；新規=toolbar slot）；
    list を v-if で detail 时卸载 → 戻る时重挂 auto-fetch（RmaView 先例のフレッシュネス）。
  detail 态（新規/閲覧兼用の編集フォーム + BOM 編集テーブル）→ 特殊エディタ領域：ヘッダ状態を CpTag 化、
    action-bar を token 化、BOM は el-table を維持（行内編集の子表は模板表达不能）。
  組立指示の kitSku ドロップダウンはマスタ一覧と別ソース（activeMasters）を onMounted ＋ マスタ変更後にロード。
-->
<template>
  <div class="wms-kit">
    <el-tabs v-model="activeTab">
      <!-- ─── マスタ管理 ─── -->
      <el-tab-pane :label="t('wms.kit.tab.master')" name="master">
        <!-- 一覧 -->
        <CpListPage
          v-if="masterMode === 'list'"
          ref="masterListRef"
          :columns="masterColumns"
          :fetch="fetchMasters"
          :search-fields="masterSearchFields"
          :filter-labels="filterLabels"
        >
          <template #toolbar>
            <el-button @click="openMasterCreate">{{ t('wms.common.create') }}</el-button>
          </template>
          <template #col-_action="{ row }">
            <el-button link type="primary" size="small" @click="openMasterEdit(row.kitSku)">{{ t('wms.common.open') }}</el-button>
            <el-button v-permission="'wms-kitting:del'" link type="danger" size="small" @click="onMasterDelete(row.kitSku)">{{ t('wms.common.delete') }}</el-button>
          </template>
        </CpListPage>

        <!-- 詳細 / 新規 -->
        <template v-else-if="masterMode === 'detail' && currentMaster">
          <el-card shadow="never">
            <template #header>
              <span class="hd-title">
                {{ masterIsNew ? t('wms.kit.titleNew') : `${t('wms.kit.title')} [${currentMaster.kitSku}]` }}
              </span>
            </template>
            <el-form :model="currentMaster" label-width="160px" size="small">
              <el-row :gutter="16">
                <el-col :span="8">
                  <el-form-item :label="t('wms.kit.fld.kitSku')" required>
                    <el-input v-model="currentMaster.kitSku" :disabled="!masterIsNew" maxlength="20" />
                  </el-form-item>
                </el-col>
                <el-col :span="8">
                  <el-form-item :label="t('wms.kit.fld.kitName')" required>
                    <el-input v-model="currentMaster.kitName" maxlength="100" />
                  </el-form-item>
                </el-col>
                <el-col :span="8">
                  <el-form-item :label="t('wms.kit.fld.defaultWh')">
                    <el-input v-model="currentMaster.defaultWarehouseCd" maxlength="10" />
                  </el-form-item>
                </el-col>
                <el-col :span="8">
                  <el-form-item :label="t('wms.kit.fld.active')">
                    <el-switch v-model="currentMaster.activeFlg" />
                  </el-form-item>
                </el-col>
                <el-col :span="16">
                  <el-form-item :label="t('wms.common.remarks')">
                    <el-input v-model="currentMaster.remarks" type="textarea" :rows="2" />
                  </el-form-item>
                </el-col>
              </el-row>
            </el-form>
          </el-card>

          <el-card shadow="never" style="margin-top: 12px">
            <template #header>
              <div class="card-hd hd-between">
                <span class="hd-title">{{ t('wms.kit.bom.title') }}</span>
                <el-button type="primary" size="small" @click="addBomLine">{{ t('wms.common.addLine') }}</el-button>
              </div>
            </template>
            <el-table :data="currentMaster.components" border size="small">
              <el-table-column type="index" :label="t('wms.common.line')" width="60" align="center" />
              <el-table-column :label="t('wms.kit.bom.componentCd')" min-width="140">
                <template #default="{ row }"><el-input v-model="row.componentProductCd" maxlength="20" /></template>
              </el-table-column>
              <el-table-column :label="t('wms.kit.bom.componentName')" min-width="180">
                <template #default="{ row }"><el-input v-model="row.componentName" maxlength="100" /></template>
              </el-table-column>
              <el-table-column :label="t('wms.kit.bom.requiredQty')" width="140">
                <template #default="{ row }">
                  <el-input-number v-model="row.requiredQty" :min="0" :precision="4" controls-position="right" style="width: 100%" />
                </template>
              </el-table-column>
              <el-table-column :label="t('wms.common.unit')" width="100">
                <template #default="{ row }"><el-input v-model="row.unitCd" maxlength="10" /></template>
              </el-table-column>
              <el-table-column :label="t('wms.common.action')" width="80" fixed="right">
                <template #default="{ $index }">
                  <el-button link type="danger" size="small" @click="removeBomLine($index)">{{ t('wms.common.delete') }}</el-button>
                </template>
              </el-table-column>
            </el-table>
          </el-card>

          <el-affix position="bottom" :offset="0">
            <div class="action-bar">
              <el-button @click="masterMode = 'list'">{{ t('wms.common.back') }}</el-button>
              <el-button type="primary" @click="onMasterSave" :loading="saving">{{ t('wms.common.save') }}</el-button>
            </div>
          </el-affix>
        </template>
      </el-tab-pane>

      <!-- ─── 組立指示 ─── -->
      <el-tab-pane :label="t('wms.kit.tab.order')" name="order" lazy>
        <!-- 一覧 -->
        <CpListPage
          v-if="orderMode === 'list'"
          ref="orderListRef"
          :columns="orderColumns"
          :fetch="fetchOrders"
          :search-fields="orderSearchFields"
          :filter-labels="filterLabels"
        >
          <template #toolbar>
            <el-button @click="openOrderCreate">{{ t('wms.common.create') }}</el-button>
          </template>
          <template #col-qty="{ row }">{{ formatQty(row.qty) }}</template>
          <template #col-executedAt="{ row }">{{ row.executedAt?.replace('T', ' ').slice(0, 16) || '—' }}</template>
          <template #col-_action="{ row }">
            <el-button link type="primary" size="small" @click="openOrderDetail(row.kitOrderNo)">{{ t('wms.common.open') }}</el-button>
          </template>
        </CpListPage>

        <!-- 詳細 / 新規 -->
        <template v-else-if="orderMode === 'detail' && currentOrder">
          <el-card shadow="never">
            <template #header>
              <div class="card-hd">
                <span class="hd-title">
                  {{ isNewOrder ? t('wms.kit.orderTitleNew') : `${t('wms.kit.orderTitle')} [${currentOrder.kitOrderNo}]` }}
                </span>
                <CpTag v-if="!isNewOrder" :tone="orderStatusTone(currentOrder.status)">{{ orderStatusMap[currentOrder.status] }}</CpTag>
                <CpTag :tone="currentOrder.direction === 'ASSEMBLE' ? 'ok' : 'warn'">{{ directionMap[currentOrder.direction] }}</CpTag>
              </div>
            </template>
            <el-form :model="currentOrder" label-width="160px" size="small">
              <el-row :gutter="16">
                <el-col :span="8">
                  <el-form-item :label="t('wms.kit.fld.kitSku')" required>
                    <el-select v-model="currentOrder.kitSku" :disabled="!isNewOrder" filterable style="width: 100%">
                      <el-option v-for="m in activeMasters" :key="m.kitSku" :label="`${m.kitSku} - ${m.kitName}`" :value="m.kitSku" />
                    </el-select>
                  </el-form-item>
                </el-col>
                <el-col :span="8">
                  <el-form-item :label="t('wms.kit.fld.direction')" required>
                    <el-select v-model="currentOrder.direction" :disabled="!isNewOrder">
                      <el-option v-for="(l, v) in directionMap" :key="v" :label="l" :value="v" />
                    </el-select>
                  </el-form-item>
                </el-col>
                <el-col :span="8">
                  <el-form-item :label="t('wms.common.qty')" required>
                    <el-input-number v-model="currentOrder.qty" :min="0" :precision="2" :disabled="!isNewOrder" controls-position="right" style="width: 100%" />
                  </el-form-item>
                </el-col>
                <el-col :span="8">
                  <el-form-item :label="t('wms.common.warehouse')" required>
                    <el-input v-model="currentOrder.warehouseCd" :disabled="!isNewOrder" maxlength="10" />
                  </el-form-item>
                </el-col>
                <el-col :span="8">
                  <el-form-item :label="t('wms.kit.fld.kitLoc')" required>
                    <el-input v-model="currentOrder.kitLocationCd" :disabled="!isNewOrder" maxlength="30" />
                  </el-form-item>
                </el-col>
                <el-col :span="8">
                  <el-form-item :label="t('wms.kit.fld.kitLot')">
                    <el-input v-model="currentOrder.kitLotNo" :disabled="!isNewOrder" :placeholder="kitLotHint" maxlength="30" />
                  </el-form-item>
                </el-col>
                <el-col :span="24">
                  <el-form-item :label="t('wms.common.remarks')">
                    <el-input v-model="currentOrder.remarks" type="textarea" :rows="2" :disabled="!isNewOrder" />
                  </el-form-item>
                </el-col>
              </el-row>
              <el-alert v-if="currentOrder.executedTxnNos" type="success" :closable="false" style="margin-top: 8px">
                <template #title>
                  {{ t('wms.kit.msg.txnCount', { n: currentOrder.executedTxnNos.split(';').length }) }}
                </template>
                <div class="txn-list">{{ currentOrder.executedTxnNos }}</div>
              </el-alert>
            </el-form>
          </el-card>

          <el-affix position="bottom" :offset="0">
            <div class="action-bar">
              <el-button @click="orderMode = 'list'">{{ t('wms.common.back') }}</el-button>
              <el-button v-if="isNewOrder" type="primary" @click="onOrderSave" :loading="saving">{{ t('wms.common.save') }}</el-button>
              <el-button v-if="canExecute" v-permission="'wms-kitting:execute'" type="success" @click="onOrderExecute" :loading="saving">{{ t('wms.kit.btn.execute') }}</el-button>
              <el-button v-if="canCancelOrder" type="danger" plain @click="onOrderCancel">{{ t('wms.outbound.btn.cancel') }}</el-button>
            </div>
          </el-affix>
        </template>
      </el-tab-pane>
    </el-tabs>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useI18n } from 'vue-i18n'
import CpListPage, { type ListColumn, type ListFetch, type ListPageExpose } from '@/components/templates/CpListPage.vue'
import { type FilterField } from '@/components/templates/CpFilterBar.vue'
import CpTag, { type Tone } from '@/components/base/CpTag.vue'
import { kittingApi } from '@/api/wms/kitting'
import type { KitMaster, KitMasterComponent, KitOrder, KitOrderSearchQuery } from '@/types/wms/wms'
import { formatQty } from '@/utils/format'

const { t } = useI18n()

const activeTab = ref<'master' | 'order'>('master')
const saving = ref(false)

const filterLabels = computed(() => ({ search: t('wms.common.search'), reset: t('wms.common.clear') }))

// —— 码值映射（i18n 反応式）——
const directionMap = computed<Record<string, string>>(() => ({
  ASSEMBLE: t('wms.kit.dir.assemble'),
  DISASSEMBLE: t('wms.kit.dir.disassemble'),
}))
const orderStatusMap = computed<Record<number, string>>(() => ({
  0: t('wms.kit.status.draft'),
  1: t('wms.kit.status.executed'),
  9: t('wms.kit.status.cancelled'),
}))
// 原 orderStatusTagOf(info/success/danger) → 设计系统 Tone（info=グレー→muted で保色）
function orderStatusTone(s: number): Tone {
  return ({ 0: 'muted', 1: 'ok', 9: 'danger' } as const)[s as 0] || 'muted'
}
function codeLabel(m: Record<number, string>, v: unknown): string {
  return m[v as number] || (v == null ? '' : String(v))
}

// —— 組立指示 kitSku ドロップダウン用の有効マスタ（一覧とは別ソース）——
const activeMasters = ref<KitMaster[]>([])
async function loadActiveMasters() {
  activeMasters.value = ((await kittingApi.searchMasters()).data || []).filter(m => m.activeFlg)
}

// ─────────────────────── マスタ ───────────────────────
const masterMode = ref<'list' | 'detail'>('list')
const masterIsNew = ref(false)
const currentMaster = ref<KitMaster | null>(null)
const masterListRef = ref<ListPageExpose>()

const masterColumns = computed<ListColumn<KitMaster>[]>(() => [
  { prop: 'kitSku', label: t('wms.kit.fld.kitSku'), kind: 'mono', width: 180 },
  { prop: 'kitName', label: t('wms.kit.fld.kitName'), minWidth: 200, overflowTooltip: true },
  { prop: 'defaultWarehouseCd', label: t('wms.kit.fld.defaultWh'), width: 140 },
  { prop: 'activeFlg', label: t('wms.kit.fld.active'), width: 100, align: 'center', kind: 'tag',
    map: (v) => ({ label: v ? 'ON' : 'OFF', tone: v ? 'ok' : 'muted' }) },
  { prop: '_action', label: t('wms.common.action'), width: 160, fixed: 'right' },
])

const masterSearchFields = computed<FilterField[]>(() => [
  { key: 'kitSku', label: t('wms.kit.fld.kitSku'), type: 'text' },
])

const fetchMasters: ListFetch<KitMaster> = async ({ page, size, filters }) => {
  const f = filters as Record<string, unknown>
  const kw = f.kitSku ? String(f.kitSku) : undefined
  const all = (await kittingApi.searchMasters(kw)).data || []
  const start = (page - 1) * size
  return { rows: all.slice(start, start + size), total: all.length }
}

function openMasterCreate() {
  currentMaster.value = { kitSku: '', kitName: '', activeFlg: true, components: [] }
  masterIsNew.value = true
  masterMode.value = 'detail'
}

async function openMasterEdit(kitSku: string) {
  const res = await kittingApi.getMaster(kitSku)
  currentMaster.value = res.data
  masterIsNew.value = false
  masterMode.value = 'detail'
}

function addBomLine() {
  if (!currentMaster.value) return
  currentMaster.value.components.push({
    lineNo: currentMaster.value.components.length + 1,
    componentProductCd: '', requiredQty: 0,
  } as KitMasterComponent)
}
function removeBomLine(idx: number) {
  if (!currentMaster.value) return
  currentMaster.value.components.splice(idx, 1)
  currentMaster.value.components.forEach((c, i) => (c.lineNo = i + 1))
}

async function onMasterSave() {
  if (!currentMaster.value) return
  if (!currentMaster.value.kitSku || !currentMaster.value.kitName) { ElMessage.warning(t('wms.common.required')); return }
  if (currentMaster.value.components.length === 0) { ElMessage.warning(t('wms.inbound.msg.noDetail')); return }
  saving.value = true
  try {
    if (masterIsNew.value) {
      await kittingApi.createMaster(currentMaster.value)
    } else {
      await kittingApi.updateMaster(currentMaster.value.kitSku, currentMaster.value)
    }
    ElMessage.success(t('wms.common.success'))
    masterMode.value = 'list' // v-if 再挂 auto-fetch
    await loadActiveMasters()
  } finally { saving.value = false }
}

async function onMasterDelete(kitSku: string) {
  try {
    await ElMessageBox.confirm(`${t('wms.common.confirmDelete')} [${kitSku}]`, t('wms.common.confirm'), { type: 'warning' })
    await kittingApi.deleteMaster(kitSku)
    ElMessage.success(t('wms.common.success'))
    masterListRef.value?.reload()
    await loadActiveMasters()
  } catch { /* */ }
}

// ─────────────────────── 指示 ───────────────────────
const orderMode = ref<'list' | 'detail'>('list')
const currentOrder = ref<KitOrder | null>(null)
const orderListRef = ref<ListPageExpose>()

const isNewOrder = computed(() => currentOrder.value !== null && !currentOrder.value.kitOrderNo)
const canExecute = computed(() => currentOrder.value && currentOrder.value.kitOrderNo && currentOrder.value.status === 0)
const canCancelOrder = computed(() => currentOrder.value && currentOrder.value.kitOrderNo && currentOrder.value.status === 0)

const kitLotHint = computed(() => currentOrder.value?.direction === 'DISASSEMBLE'
  ? t('wms.kit.msg.kitLotRequiredDisassemble')
  : t('wms.kit.msg.kitLotAutoGen'))

const orderColumns = computed<ListColumn<KitOrder>[]>(() => [
  { prop: 'kitOrderNo', label: t('wms.kit.fld.orderNo'), kind: 'mono', width: 180 },
  { prop: 'direction', label: t('wms.kit.fld.direction'), width: 120, kind: 'tag',
    map: (v) => ({ label: directionMap.value[v as string] ?? '', tone: v === 'ASSEMBLE' ? 'ok' : 'warn' }) },
  { prop: 'status', label: t('wms.common.status'), width: 100, kind: 'tag',
    map: (v) => ({ label: codeLabel(orderStatusMap.value, v), tone: orderStatusTone(v as number) }) },
  { prop: 'kitSku', label: t('wms.kit.fld.kitSku'), width: 160 },
  { prop: 'kitName', label: t('wms.kit.fld.kitName'), minWidth: 180, overflowTooltip: true },
  { prop: 'qty', label: t('wms.common.qty'), width: 100, align: 'right' },
  { prop: 'warehouseCd', label: t('wms.common.warehouse'), width: 90 },
  { prop: 'kitLocationCd', label: t('wms.kit.fld.kitLoc'), width: 140 },
  { prop: 'kitLotNo', label: t('wms.kit.fld.kitLot'), width: 160 },
  { prop: 'executedAt', label: t('wms.kit.fld.executedAt'), width: 160 },
  { prop: '_action', label: t('wms.common.action'), width: 100, fixed: 'right' },
])

const orderSearchFields = computed<FilterField[]>(() => [
  { key: 'kitOrderNo', label: t('wms.kit.fld.orderNo'), type: 'text' },
  { key: 'kitSku', label: t('wms.kit.fld.kitSku'), type: 'text' },
  {
    key: 'direction', label: t('wms.kit.fld.direction'), type: 'select',
    options: Object.entries(directionMap.value).map(([v, l]) => ({ label: l, value: v })),
  },
  {
    key: 'status', label: t('wms.common.status'), type: 'select',
    options: Object.entries(orderStatusMap.value).map(([v, l]) => ({ label: l, value: Number(v) })),
  },
])

const fetchOrders: ListFetch<KitOrder> = async ({ page, size, filters }) => {
  const f = filters as Record<string, unknown>
  const q: KitOrderSearchQuery = { pageSize: 100 }
  if (f.kitOrderNo) q.kitOrderNo = String(f.kitOrderNo)
  if (f.kitSku) q.kitSku = String(f.kitSku)
  if (f.direction) q.direction = String(f.direction)
  if (f.status !== undefined && f.status !== '') q.status = Number(f.status)
  const all = (await kittingApi.searchOrders(q)).data || []
  const start = (page - 1) * size
  return { rows: all.slice(start, start + size), total: all.length }
}

function openOrderCreate() {
  currentOrder.value = {
    kitSku: '', qty: 0, direction: 'ASSEMBLE',
    warehouseCd: '', kitLocationCd: '', status: 0,
  }
  orderMode.value = 'detail'
}

async function openOrderDetail(no?: string) {
  if (!no) return
  const res = await kittingApi.getOrder(no)
  currentOrder.value = res.data
  orderMode.value = 'detail'
}

async function onOrderSave() {
  if (!currentOrder.value) return
  if (!currentOrder.value.kitSku || !currentOrder.value.warehouseCd || !currentOrder.value.kitLocationCd) {
    ElMessage.warning(t('wms.common.required')); return
  }
  if (currentOrder.value.qty <= 0) { ElMessage.warning(t('wms.common.required')); return }
  saving.value = true
  try {
    const res = await kittingApi.createOrder(currentOrder.value)
    ElMessage.success(`${t('wms.common.success')}: ${res.data.kitOrderNo}`)
    await openOrderDetail(res.data.kitOrderNo)
  } finally { saving.value = false }
}

async function onOrderExecute() {
  if (!currentOrder.value) return
  try {
    await ElMessageBox.confirm(t('wms.kit.msg.executeAsk'), t('wms.common.confirm'), { type: 'warning' })
    saving.value = true
    await kittingApi.execute(currentOrder.value.kitOrderNo!)
    ElMessage.success(t('wms.common.success'))
    await openOrderDetail(currentOrder.value.kitOrderNo!)
  } catch { /* */ }
  finally { saving.value = false }
}

async function onOrderCancel() {
  if (!currentOrder.value) return
  try {
    await ElMessageBox.confirm(t('wms.inbound.msg.cancelAsk'), t('wms.common.confirm'), { type: 'warning' })
    await kittingApi.cancel(currentOrder.value.kitOrderNo!)
    ElMessage.success(t('wms.common.success'))
    await openOrderDetail(currentOrder.value.kitOrderNo!)
  } catch { /* */ }
}

// ─── ライフサイクル ───
onMounted(loadActiveMasters)
</script>

<style scoped>
.wms-kit { padding: 16px; padding-bottom: 60px; }
.card-hd { display: flex; align-items: center; gap: 12px; }
.hd-between { justify-content: space-between; }
.hd-title { font-weight: 600; }
.txn-list { font-size: var(--cp-fs-xs); color: var(--cp-muted); word-break: break-all; }
.action-bar { background: var(--cp-card); border-top: 1px solid var(--cp-line-soft); padding: 12px 16px; text-align: right; }
.action-bar > * { margin-left: 8px; }
</style>

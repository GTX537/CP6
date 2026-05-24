<template>
  <div class="wms-kit">
    <el-tabs v-model="activeTab">
      <!-- ─── マスタ管理 ─── -->
      <el-tab-pane :label="t('wms.kit.tab.master')" name="master">
        <el-card v-if="masterMode === 'list'" shadow="never" class="search-card">
          <el-form inline size="small">
            <el-form-item :label="t('wms.kit.fld.kitSku')"><el-input v-model="masterKeyword" clearable style="width: 200px" /></el-form-item>
            <el-form-item>
              <el-button type="primary" @click="reloadMasters" :loading="masterLoading">{{ t('wms.common.search') }}</el-button>
              <el-button @click="openMasterCreate">{{ t('wms.common.create') }}</el-button>
            </el-form-item>
          </el-form>
        </el-card>

        <el-card v-if="masterMode === 'list'" shadow="never">
          <el-table :data="masters" border stripe size="small" max-height="500" highlight-current-row>
            <el-table-column prop="kitSku" :label="t('wms.kit.fld.kitSku')" width="180" />
            <el-table-column prop="kitName" :label="t('wms.kit.fld.kitName')" min-width="200" />
            <el-table-column prop="defaultWarehouseCd" :label="t('wms.kit.fld.defaultWh')" width="140" />
            <el-table-column :label="t('wms.kit.fld.active')" width="100" align="center">
              <template #default="{ row }">
                <el-tag v-if="row.activeFlg" type="success" size="small">ON</el-tag>
                <el-tag v-else type="info" size="small">OFF</el-tag>
              </template>
            </el-table-column>
            <el-table-column :label="t('wms.common.action')" width="160" fixed="right">
              <template #default="{ row }">
                <el-button link type="primary" size="small" @click="openMasterEdit(row.kitSku)">{{ t('wms.common.open') }}</el-button>
                <el-button link type="danger" size="small" @click="onMasterDelete(row.kitSku)">{{ t('wms.common.delete') }}</el-button>
              </template>
            </el-table-column>
          </el-table>
        </el-card>

        <template v-if="masterMode === 'detail' && currentMaster">
          <el-card shadow="never">
            <template #header>
              <span style="font-weight: 600">
                {{ isNewMaster ? t('wms.kit.titleNew') : `${t('wms.kit.title')} [${currentMaster.kitSku}]` }}
              </span>
            </template>
            <el-form :model="currentMaster" label-width="160px" size="small">
              <el-row :gutter="16">
                <el-col :span="8">
                  <el-form-item :label="t('wms.kit.fld.kitSku')" required>
                    <el-input v-model="currentMaster.kitSku" :disabled="!isNewMaster" maxlength="20" />
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
              <div style="display: flex; justify-content: space-between; align-items: center">
                <span style="font-weight: 600">{{ t('wms.kit.bom.title') }}</span>
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
      <el-tab-pane :label="t('wms.kit.tab.order')" name="order">
        <el-card v-if="orderMode === 'list'" shadow="never" class="search-card">
          <el-form :model="orderQuery" inline size="small">
            <el-form-item :label="t('wms.kit.fld.orderNo')"><el-input v-model="orderQuery.kitOrderNo" clearable style="width: 180px" /></el-form-item>
            <el-form-item :label="t('wms.kit.fld.kitSku')"><el-input v-model="orderQuery.kitSku" clearable style="width: 160px" /></el-form-item>
            <el-form-item :label="t('wms.kit.fld.direction')">
              <el-select v-model="orderQuery.direction" clearable style="width: 140px">
                <el-option v-for="(l, v) in directionMap" :key="v" :label="l" :value="v" />
              </el-select>
            </el-form-item>
            <el-form-item :label="t('wms.common.status')">
              <el-select v-model="orderQuery.status" clearable style="width: 120px">
                <el-option v-for="(l, v) in orderStatusMap" :key="v" :label="l" :value="Number(v)" />
              </el-select>
            </el-form-item>
            <el-form-item>
              <el-button type="primary" @click="reloadOrders" :loading="orderLoading">{{ t('wms.common.search') }}</el-button>
              <el-button @click="openOrderCreate">{{ t('wms.common.create') }}</el-button>
            </el-form-item>
          </el-form>
        </el-card>

        <el-card v-if="orderMode === 'list'" shadow="never">
          <el-table :data="orders" border stripe size="small" max-height="500" highlight-current-row>
            <el-table-column prop="kitOrderNo" :label="t('wms.kit.fld.orderNo')" width="180" />
            <el-table-column :label="t('wms.kit.fld.direction')" width="120">
              <template #default="{ row }">
                <el-tag :type="row.direction === 'ASSEMBLE' ? 'success' : 'warning'" size="small">{{ directionMap[row.direction] }}</el-tag>
              </template>
            </el-table-column>
            <el-table-column :label="t('wms.common.status')" width="100">
              <template #default="{ row }">
                <el-tag :type="orderStatusTagOf(row.status)" size="small">{{ orderStatusMap[row.status] }}</el-tag>
              </template>
            </el-table-column>
            <el-table-column prop="kitSku" :label="t('wms.kit.fld.kitSku')" width="160" />
            <el-table-column prop="kitName" :label="t('wms.kit.fld.kitName')" min-width="180" show-overflow-tooltip />
            <el-table-column prop="qty" :label="t('wms.common.qty')" width="100" align="right">
              <template #default="{ row }">{{ formatQty(row.qty) }}</template>
            </el-table-column>
            <el-table-column prop="warehouseCd" :label="t('wms.common.warehouse')" width="90" />
            <el-table-column prop="kitLocationCd" :label="t('wms.kit.fld.kitLoc')" width="140" />
            <el-table-column prop="kitLotNo" :label="t('wms.kit.fld.kitLot')" width="160" />
            <el-table-column prop="executedAt" :label="t('wms.kit.fld.executedAt')" width="160">
              <template #default="{ row }">{{ row.executedAt?.replace('T', ' ').slice(0, 16) || '—' }}</template>
            </el-table-column>
            <el-table-column :label="t('wms.common.action')" width="100" fixed="right">
              <template #default="{ row }">
                <el-button link type="primary" size="small" @click="openOrderDetail(row.kitOrderNo)">{{ t('wms.common.open') }}</el-button>
              </template>
            </el-table-column>
          </el-table>
        </el-card>

        <template v-if="orderMode === 'detail' && currentOrder">
          <el-card shadow="never">
            <template #header>
              <div style="display: flex; align-items: center; gap: 12px">
                <span style="font-weight: 600">
                  {{ isNewOrder ? t('wms.kit.orderTitleNew') : `${t('wms.kit.orderTitle')} [${currentOrder.kitOrderNo}]` }}
                </span>
                <el-tag v-if="!isNewOrder" :type="orderStatusTagOf(currentOrder.status)" size="small">{{ orderStatusMap[currentOrder.status] }}</el-tag>
                <el-tag :type="currentOrder.direction === 'ASSEMBLE' ? 'success' : 'warning'" size="small">{{ directionMap[currentOrder.direction] }}</el-tag>
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
                <div style="font-size: 12px; color: #606266; word-break: break-all">{{ currentOrder.executedTxnNos }}</div>
              </el-alert>
            </el-form>
          </el-card>

          <el-affix position="bottom" :offset="0">
            <div class="action-bar">
              <el-button @click="orderMode = 'list'">{{ t('wms.common.back') }}</el-button>
              <el-button v-if="isNewOrder" type="primary" @click="onOrderSave" :loading="saving">{{ t('wms.common.save') }}</el-button>
              <el-button v-if="canExecute" type="success" @click="onOrderExecute" :loading="saving">{{ t('wms.kit.btn.execute') }}</el-button>
              <el-button v-if="canCancelOrder" type="danger" plain @click="onOrderCancel">{{ t('wms.outbound.btn.cancel') }}</el-button>
            </div>
          </el-affix>
        </template>
      </el-tab-pane>
    </el-tabs>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, computed, onMounted, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useI18n } from 'vue-i18n'
import { kittingApi } from '@/api/wms/kitting'
import type { KitMaster, KitMasterComponent, KitOrder, KitOrderSearchQuery } from '@/types/wms'

const { t } = useI18n()

const activeTab = ref<'master' | 'order'>('master')
const saving = ref(false)

const directionMap = computed<Record<string, string>>(() => ({
  ASSEMBLE: t('wms.kit.dir.assemble'),
  DISASSEMBLE: t('wms.kit.dir.disassemble'),
}))
const orderStatusMap = computed<Record<number, string>>(() => ({
  0: t('wms.kit.status.draft'),
  1: t('wms.kit.status.executed'),
  9: t('wms.kit.status.cancelled'),
}))

// ─── マスタ ───
const masterMode = ref<'list' | 'detail'>('list')
const masterKeyword = ref('')
const masters = ref<KitMaster[]>([])
const masterLoading = ref(false)
const currentMaster = ref<KitMaster | null>(null)
const isNewMaster = computed(() => currentMaster.value !== null && !masters.value.some(m => m.kitSku === currentMaster.value!.kitSku))

const activeMasters = computed(() => masters.value.filter(m => m.activeFlg))

async function reloadMasters() {
  masterLoading.value = true
  try { masters.value = (await kittingApi.searchMasters(masterKeyword.value || undefined)).data || [] }
  finally { masterLoading.value = false }
}

function openMasterCreate() {
  currentMaster.value = { kitSku: '', kitName: '', activeFlg: true, components: [] }
  masterMode.value = 'detail'
}

async function openMasterEdit(kitSku: string) {
  const res = await kittingApi.getMaster(kitSku)
  currentMaster.value = res.data
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
    if (isNewMaster.value) {
      await kittingApi.createMaster(currentMaster.value)
    } else {
      await kittingApi.updateMaster(currentMaster.value.kitSku, currentMaster.value)
    }
    ElMessage.success(t('wms.common.success'))
    masterMode.value = 'list'
    await reloadMasters()
  } finally { saving.value = false }
}

async function onMasterDelete(kitSku: string) {
  try {
    await ElMessageBox.confirm(`${t('wms.common.confirmDelete')} [${kitSku}]`, t('wms.common.confirm'), { type: 'warning' })
    await kittingApi.deleteMaster(kitSku)
    ElMessage.success(t('wms.common.success'))
    await reloadMasters()
  } catch { /* */ }
}

// ─── 指示 ───
const orderMode = ref<'list' | 'detail'>('list')
const orderQuery = reactive<KitOrderSearchQuery>({ pageSize: 100 })
const orders = ref<KitOrder[]>([])
const orderLoading = ref(false)
const currentOrder = ref<KitOrder | null>(null)
const isNewOrder = computed(() => currentOrder.value !== null && !currentOrder.value.kitOrderNo)
const canExecute = computed(() => currentOrder.value && currentOrder.value.kitOrderNo && currentOrder.value.status === 0)
const canCancelOrder = computed(() => currentOrder.value && currentOrder.value.kitOrderNo && currentOrder.value.status === 0)

const kitLotHint = computed(() => currentOrder.value?.direction === 'DISASSEMBLE'
  ? t('wms.kit.msg.kitLotRequiredDisassemble')
  : t('wms.kit.msg.kitLotAutoGen'))

function orderStatusTagOf(s: number): 'info' | 'success' | 'danger' {
  return ({ 0: 'info', 1: 'success', 9: 'danger' } as const)[s as 0] || 'info'
}
function formatQty(n: number) { return Number(n || 0).toLocaleString('ja-JP', { maximumFractionDigits: 4 }) }

async function reloadOrders() {
  orderLoading.value = true
  try { orders.value = (await kittingApi.searchOrders(orderQuery)).data || [] }
  finally { orderLoading.value = false }
}

function openOrderCreate() {
  currentOrder.value = {
    kitSku: '', qty: 0, direction: 'ASSEMBLE',
    warehouseCd: '', kitLocationCd: '', status: 0,
  }
  orderMode.value = 'detail'
}

async function openOrderDetail(no: string) {
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
onMounted(reloadMasters)
watch(activeTab, (v) => {
  if (v === 'order' && orders.value.length === 0) reloadOrders()
})
</script>

<style scoped>
.wms-kit { padding: 16px; padding-bottom: 60px; }
.search-card { margin-bottom: 12px; }
.action-bar { background: var(--el-bg-color); border-top: 1px solid var(--el-border-color-lighter); padding: 12px 16px; text-align: right; }
.action-bar > * { margin-left: 8px; }
</style>

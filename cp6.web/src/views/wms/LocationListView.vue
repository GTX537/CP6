<template>
  <div class="wms-location">
    <el-row :gutter="12" class="wrap">
      <el-col :span="6">
        <el-card shadow="never" class="left-card">
          <template #header>
            <div class="card-header">
              <span style="font-weight: 600">{{ t('wms.location.warehouseList') }}</span>
              <el-button type="primary" size="small" @click="loadWarehouses" :loading="whLoading">{{ t('wms.common.refresh') }}</el-button>
            </div>
          </template>
          <el-table :data="warehouses" size="small" max-height="600" highlight-current-row
                    @current-change="onWarehouseChange" :row-class-name="rowClassName">
            <el-table-column prop="warehouseCd" :label="t('wms.common.code')" width="80" />
            <el-table-column prop="warehouseName" :label="t('wms.warehouse.fld.name')" show-overflow-tooltip />
            <el-table-column :label="t('wms.warehouse.fld.type')" width="80">
              <template #default="{ row }">{{ warehouseTypeMap[row.warehouseType] || row.warehouseType }}</template>
            </el-table-column>
          </el-table>
        </el-card>
      </el-col>

      <el-col :span="18">
        <el-card shadow="never" class="right-card">
          <template #header>
            <div class="card-header">
              <span style="font-weight: 600">
                {{ t('wms.location.locationList') }}
                <span v-if="selectedWh" style="color: #909399; font-weight: normal">
                  — {{ selectedWh.warehouseCd }} {{ selectedWh.warehouseName }}
                </span>
              </span>
              <div>
                <el-button type="primary" size="small" :disabled="!selectedWh" @click="openCreate">+ {{ t('wms.common.create') }}</el-button>
                <el-button size="small" @click="loadLocations" :disabled="!selectedWh" :loading="locLoading">{{ t('wms.common.refresh') }}</el-button>
              </div>
            </div>
          </template>

          <div v-if="!selectedWh" class="empty">{{ t('wms.location.selectWh') }}</div>
          <div v-else-if="locations.length === 0" class="empty">{{ t('wms.location.noLocations') }}</div>

          <el-table v-else :data="locations" border stripe size="small" max-height="600" highlight-current-row>
            <el-table-column prop="locationCd" :label="t('wms.location.fld.cd')" width="180" />
            <el-table-column prop="locationName" :label="t('wms.location.fld.displayName')" min-width="160" />
            <el-table-column :label="t('wms.location.fld.level')" width="100" align="center">
              <template #default="{ row }">
                <el-tag size="small">{{ levelMap[row.locationLevel] || `L${row.locationLevel}` }}</el-tag>
              </template>
            </el-table-column>
            <el-table-column prop="parentLocationCd" :label="t('wms.location.fld.parentCd')" width="160" />
            <el-table-column :label="t('wms.location.fld.coord')" width="120" align="center">
              <template #default="{ row }">
                <span v-if="row.xCoord != null || row.yCoord != null">
                  ({{ row.xCoord ?? '-' }}, {{ row.yCoord ?? '-' }}, {{ row.zCoord ?? '-' }})
                </span>
                <span v-else>—</span>
              </template>
            </el-table-column>
            <el-table-column :label="t('wms.location.fld.capacity')" width="100" align="right">
              <template #default="{ row }">{{ row.capacityQty > 0 ? row.capacityQty : t('wms.location.capacityUnlimited') }}</template>
            </el-table-column>
            <el-table-column :label="t('wms.stock.col.flag')" width="120" align="center">
              <template #default="{ row }">
                <el-tag v-if="!row.isPickable" size="small" type="info">{{ t('wms.location.flag.notPickable') }}</el-tag>
                <el-tag v-if="row.isBlocked" size="small" type="danger" style="margin-left: 4px">{{ t('wms.location.flag.frozen') }}</el-tag>
                <span v-if="row.isPickable && !row.isBlocked">—</span>
              </template>
            </el-table-column>
            <el-table-column prop="barcode" :label="t('wms.location.fld.barcode')" width="140" />
            <el-table-column :label="t('wms.common.action')" width="140" fixed="right">
              <template #default="{ row }">
                <el-button link type="primary" size="small" @click="openEdit(row)">{{ t('wms.common.edit') }}</el-button>
                <el-button link type="danger" size="small" @click="onDelete(row)">{{ t('wms.common.delete') }}</el-button>
              </template>
            </el-table-column>
          </el-table>
        </el-card>
      </el-col>
    </el-row>

    <el-dialog v-model="dialogVisible" :title="isNew ? t('wms.location.dlg.create') : t('wms.location.dlg.edit')" width="540">
      <el-form v-if="editing" :model="editing" label-width="160px" size="small">
        <el-form-item :label="t('wms.location.fld.cd')" required>
          <el-input v-model="editing.locationCd" :disabled="!isNew" maxlength="30" />
        </el-form-item>
        <el-form-item :label="t('wms.common.warehouse')"><el-input v-model="editing.warehouseCd" disabled /></el-form-item>
        <el-form-item :label="t('wms.location.fld.parentCd')">
          <el-input v-model="editing.parentLocationCd" :placeholder="t('wms.location.fld.parentHint')" maxlength="30" />
        </el-form-item>
        <el-form-item :label="t('wms.location.fld.level')">
          <el-select v-model="editing.locationLevel" style="width: 100%">
            <el-option v-for="(l, v) in levelMap" :key="v" :label="l" :value="Number(v)" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('wms.location.fld.displayName')"><el-input v-model="editing.locationName" maxlength="100" /></el-form-item>
        <el-row :gutter="8">
          <el-col :span="8"><el-form-item label="X"><el-input-number v-model="editing.xCoord" :precision="0" controls-position="right" style="width: 100%" /></el-form-item></el-col>
          <el-col :span="8"><el-form-item label="Y"><el-input-number v-model="editing.yCoord" :precision="0" controls-position="right" style="width: 100%" /></el-form-item></el-col>
          <el-col :span="8"><el-form-item label="Z"><el-input-number v-model="editing.zCoord" :precision="0" controls-position="right" style="width: 100%" /></el-form-item></el-col>
        </el-row>
        <el-form-item :label="t('wms.location.fld.capacity')">
          <el-input-number v-model="editing.capacityQty" :min="0" :precision="2" controls-position="right" style="width: 100%" />
          <span style="margin-left: 8px; color: #999">{{ t('wms.location.fld.capacityHint') }}</span>
        </el-form-item>
        <el-form-item :label="t('wms.location.fld.allowedType')"><el-input v-model="editing.allowedProductType" :placeholder="t('wms.location.fld.allowedHint')" maxlength="50" /></el-form-item>
        <el-form-item :label="t('wms.location.fld.pickable')"><el-switch v-model="editing.isPickable" /></el-form-item>
        <el-form-item :label="t('wms.location.fld.blocked')"><el-switch v-model="editing.isBlocked" /></el-form-item>
        <el-form-item :label="t('wms.location.fld.barcode')"><el-input v-model="editing.barcode" maxlength="50" /></el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">{{ t('wms.common.cancel') }}</el-button>
        <el-button type="primary" @click="onSave" :loading="saving">{{ t('wms.common.save') }}</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useI18n } from 'vue-i18n'
import { warehouseApi } from '@/api/wms/warehouse'
import type { Warehouse, WmsLocation } from '@/types/wms'

const { t } = useI18n()

const warehouses = ref<Warehouse[]>([])
const selectedWh = ref<Warehouse | null>(null)
const locations = ref<WmsLocation[]>([])
const whLoading = ref(false)
const locLoading = ref(false)

const dialogVisible = ref(false)
const editing = ref<WmsLocation | null>(null)
const saving = ref(false)
const isNew = computed(() => editing.value !== null && !locations.value.some(l => l.locationCd === editing.value!.locationCd))

const warehouseTypeMap = computed<Record<number, string>>(() => ({
  1: t('wms.warehouse.type.raw'),
  2: t('wms.warehouse.type.wip'),
  3: t('wms.warehouse.type.finished'),
  4: t('wms.warehouse.type.defective'),
  5: t('wms.warehouse.type.external'),
}))

const levelMap = computed<Record<number, string>>(() => ({
  1: t('wms.location.level.zone'),
  2: t('wms.location.level.aisle'),
  3: t('wms.location.level.shelf'),
  4: t('wms.location.level.tier'),
  5: t('wms.location.level.bin'),
}))

function rowClassName({ row }: { row: Warehouse }) {
  return row.warehouseCd === selectedWh.value?.warehouseCd ? 'is-selected' : ''
}

async function loadWarehouses() {
  whLoading.value = true
  try {
    const res = await warehouseApi.search()
    warehouses.value = res.data || []
  } finally { whLoading.value = false }
}

async function loadLocations() {
  if (!selectedWh.value) return
  locLoading.value = true
  try {
    const res = await warehouseApi.getLocationTree(selectedWh.value.warehouseCd)
    locations.value = res.data || []
  } finally { locLoading.value = false }
}

function onWarehouseChange(wh: Warehouse | null) {
  selectedWh.value = wh
  if (wh) loadLocations()
  else locations.value = []
}

function openCreate() {
  if (!selectedWh.value) return
  editing.value = {
    locationCd: '', warehouseCd: selectedWh.value.warehouseCd,
    locationLevel: 5, capacityQty: 0, isPickable: true, isBlocked: false,
  }
  dialogVisible.value = true
}

function openEdit(loc: WmsLocation) {
  editing.value = { ...loc }
  dialogVisible.value = true
}

async function onSave() {
  if (!editing.value) return
  if (!editing.value.locationCd) { ElMessage.warning(t('wms.common.required')); return }
  saving.value = true
  try {
    if (isNew.value) {
      await warehouseApi.createLocation(editing.value)
    } else {
      await warehouseApi.updateLocation(editing.value.locationCd, editing.value)
    }
    ElMessage.success(t('wms.common.success'))
    dialogVisible.value = false
    loadLocations()
  } finally { saving.value = false }
}

async function onDelete(loc: WmsLocation) {
  try {
    await ElMessageBox.confirm(`${t('wms.common.confirmDelete')} [${loc.locationCd}]`, t('wms.common.confirm'), { type: 'warning' })
    await warehouseApi.deleteLocation(loc.locationCd)
    ElMessage.success(t('wms.common.success'))
    loadLocations()
  } catch { /* */ }
}

onMounted(loadWarehouses)
</script>

<style scoped>
.wms-location { padding: 16px; }
.wrap { min-height: calc(100vh - 100px); }
.left-card, .right-card { height: 100%; }
.card-header { display: flex; justify-content: space-between; align-items: center; }
.empty { padding: 60px 0; text-align: center; color: #909399; }
:deep(.is-selected td) { background: #ecf5ff !important; }
</style>

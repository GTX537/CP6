<template>
  <el-card shadow="never" v-if="detail">
    <div style="margin-bottom: 12px;">
      <el-tag size="small">{{ t('明細 No.{no} - {cd}', { no: detail.webOrderDetailNo, cd: detail.productCd }) }}</el-tag>
      <el-button size="small" type="primary" :icon="Download" :disabled="!store.canEdit" style="margin-left: 12px" @click="onLookupProcesses">
        {{ t('製品工程マスタから引入') }}
      </el-button>
      <el-button size="small" :icon="Download" :disabled="!store.canEdit" @click="onLookupMaterials">
        {{ t('製品材料マスタから引入') }}
      </el-button>
    </div>

    <!-- 工程情報 -->
    <el-divider content-position="left">{{ t('工程情報') }}</el-divider>
    <div style="margin-bottom: 8px">
      <el-button-group>
        <el-button :icon="Plus" size="small" :disabled="!store.canEdit" @click="addProcess">{{ t('行追加') }}</el-button>
        <el-button :icon="Delete" size="small" :disabled="!store.canEdit || selectedProcIdx < 0" @click="removeProcess">{{ t('行削除') }}</el-button>
      </el-button-group>
    </div>
    <el-table
      :data="detail.processes"
      border stripe size="small" highlight-current-row
      style="width: 100%"
      @current-change="onProcCurrent"
    >
      <el-table-column :label="t('工程CD')" width="120">
        <template #default="{ row }">
          <el-input v-model="row.processCd" :disabled="!store.canEdit" size="small" />
        </template>
      </el-table-column>
      <el-table-column :label="t('作業CD')" width="120">
        <template #default="{ row }">
          <el-input v-model="row.operationCd" :disabled="!store.canEdit" size="small" />
        </template>
      </el-table-column>
      <el-table-column label="WG" width="100">
        <template #default="{ row }">
          <el-input v-model="row.workingGroupCd" :disabled="!store.canEdit" size="small" />
        </template>
      </el-table-column>
      <el-table-column :label="t('号機/外注先')" width="120">
        <template #default="{ row }">
          <el-input v-model="row.machineOrVendor" :disabled="!store.canEdit" size="small" />
        </template>
      </el-table-column>
      <el-table-column :label="t('仕入単価')" width="110">
        <template #default="{ row }">
          <el-input-number v-model="row.purchaseUnitPrice" :disabled="!store.canEdit" :precision="2" :controls="false" size="small" style="width: 100px" />
        </template>
      </el-table-column>
      <el-table-column :label="t('ロス率')" width="90">
        <template #default="{ row }">
          <el-input-number v-model="row.lossRate" :disabled="!store.canEdit" :precision="2" :controls="false" size="small" style="width: 80px" />
        </template>
      </el-table-column>
      <el-table-column :label="t('台数')" width="80">
        <template #default="{ row }">
          <el-input-number v-model="row.machineCount" :disabled="!store.canEdit" :precision="2" :controls="false" size="small" style="width: 70px" />
        </template>
      </el-table-column>
      <el-table-column :label="t('LT(日)')" width="80">
        <template #default="{ row }">
          <el-input-number v-model="row.leadTimeDays" :disabled="!store.canEdit" :precision="0" :controls="false" size="small" style="width: 70px" />
        </template>
      </el-table-column>
      <el-table-column :label="t('加工予定日')" width="120">
        <template #default="{ row }">
          <span>{{ row.scheduledDate ?? '-' }}</span>
        </template>
      </el-table-column>
    </el-table>

    <!-- 工程備考 -->
    <el-divider content-position="left">{{ t('工程備考') }}</el-divider>
    <el-table :data="detail.processNotes" border stripe size="small" style="width: 100%">
      <el-table-column :label="t('作業CD')" width="120">
        <template #default="{ row }">
          <el-input v-model="row.operationCd" :disabled="!store.canEdit" size="small" />
        </template>
      </el-table-column>
      <el-table-column :label="t('工程備考1')" min-width="200">
        <template #default="{ row }">
          <el-input v-model="row.note1" :disabled="!store.canEdit" size="small" />
        </template>
      </el-table-column>
      <el-table-column :label="t('工程備考2')" min-width="200">
        <template #default="{ row }">
          <el-input v-model="row.note2" :disabled="!store.canEdit" size="small" />
        </template>
      </el-table-column>
    </el-table>

    <!-- 材料設定 -->
    <el-divider content-position="left">{{ t('材料設定') }}</el-divider>
    <div style="margin-bottom: 8px">
      <el-button-group>
        <el-button :icon="Plus" size="small" :disabled="!store.canEdit" @click="addMaterial">{{ t('行追加') }}</el-button>
        <el-button :icon="Delete" size="small" :disabled="!store.canEdit || selectedMatIdx < 0" @click="removeMaterial">{{ t('行削除') }}</el-button>
      </el-button-group>
    </div>
    <el-table :data="detail.materials" border stripe size="small" highlight-current-row style="width: 100%" @current-change="onMatCurrent">
      <el-table-column :label="t('工程CD')" width="120">
        <template #default="{ row }">
          <el-input v-model="row.processCd" :disabled="!store.canEdit" size="small" />
        </template>
      </el-table-column>
      <el-table-column :label="t('材料CD')" width="120">
        <template #default="{ row }">
          <el-input v-model="row.materialCd" :disabled="!store.canEdit" size="small" />
        </template>
      </el-table-column>
      <el-table-column :label="t('材料区分')" width="120">
        <template #default="{ row }">
          <el-select v-model="row.materialTypeDiv" :disabled="!store.canEdit" size="small" style="width: 110px">
            <el-option :label="t('仕掛品(1)')" value="1" />
            <el-option :label="t('連産品(2)')" value="2" />
            <el-option :label="t('原料(3)')" value="3" />
            <el-option :label="t('印刷原紙(4)')" value="4" />
          </el-select>
        </template>
      </el-table-column>
      <el-table-column :label="t('受給区分')" width="120">
        <template #default="{ row }">
          <el-select v-model="row.supplyDiv" :disabled="!store.canEdit" size="small" style="width: 110px">
            <el-option :label="t('無償支給(1)')" value="1" />
            <el-option :label="t('有償支給(2)')" value="2" />
          </el-select>
        </template>
      </el-table-column>
      <el-table-column :label="t('受給単価')" width="120">
        <template #default="{ row }">
          <el-input-number v-model="row.supplyUnitPrice" :disabled="!store.canEdit" :precision="2" :controls="false" size="small" style="width: 110px" />
        </template>
      </el-table-column>
      <el-table-column :label="t('並び順')" width="80">
        <template #default="{ row }">
          <el-input-number v-model="row.sortOrder" :disabled="!store.canEdit" :precision="0" :controls="false" size="small" style="width: 70px" />
        </template>
      </el-table-column>
    </el-table>

    <!-- 明細切替 -->
    <el-divider content-position="left">{{ t('明細切替') }}</el-divider>
    <el-radio-group v-model="store.currentDetailIndex" size="small">
      <el-radio-button v-for="(d, i) in store.order.details" :key="d.webOrderDetailNo" :value="i">
        No.{{ d.webOrderDetailNo }} {{ d.productCd }}
      </el-radio-button>
    </el-radio-group>
  </el-card>
  <el-empty v-else :description="t('部材一覧で行を選択してください')" />
</template>

<script setup lang="ts">
import { useI18n } from 'vue-i18n'
const { t } = useI18n()
import { computed, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { Plus, Delete, Download } from '@element-plus/icons-vue'
import { useOrderStore } from '@/stores/order'
import { orderApi } from '@/api/erp/order'
import type { OrderProcessDto, OrderMaterialDto } from '@/types/erp/order'

const store = useOrderStore()
const detail = computed(() => store.currentDetail)
const selectedProcIdx = ref(-1)
const selectedMatIdx = ref(-1)

function onProcCurrent(row: OrderProcessDto | null) {
  if (!detail.value || !row) { selectedProcIdx.value = -1; return }
  selectedProcIdx.value = detail.value.processes.indexOf(row)
}
function onMatCurrent(row: OrderMaterialDto | null) {
  if (!detail.value || !row) { selectedMatIdx.value = -1; return }
  selectedMatIdx.value = detail.value.materials.indexOf(row)
}

function addProcess() {
  if (!detail.value) return
  detail.value.processes.push({
    productCd: detail.value.productCd,
    operationCd: '',
    processCd: '',
    machineFixedFlg: false,
    lossRate: 0,
    machineCount: 1,
    leadTimeDays: 0,
    sortOrder: detail.value.processes.length + 1,
  })
  store.markDirty()
}
function removeProcess() {
  if (!detail.value || selectedProcIdx.value < 0) return
  detail.value.processes.splice(selectedProcIdx.value, 1)
  selectedProcIdx.value = -1
  store.markDirty()
}

function addMaterial() {
  if (!detail.value) return
  detail.value.materials.push({
    productCd: detail.value.productCd,
    processCd: '',
    materialCd: '',
    materialTypeDiv: '3',
    supplyDiv: '1',
    supplyUnitPrice: 0,
    sortOrder: detail.value.materials.length + 1,
  })
  store.markDirty()
}
function removeMaterial() {
  if (!detail.value || selectedMatIdx.value < 0) return
  detail.value.materials.splice(selectedMatIdx.value, 1)
  selectedMatIdx.value = -1
  store.markDirty()
}

async function onLookupProcesses() {
  if (!detail.value?.productCd) {
    ElMessage.warning(t('製品 CD を入力してください'))
    return
  }
  try {
    const res = await orderApi.lookupProductProcesses(detail.value.productCd)
    if (res.code === 0 && res.data) {
      detail.value.processes = res.data
      store.markDirty()
      ElMessage.success(t('{n} 件の工程を引入しました', { n: res.data.length }))
    }
  } catch { /* */ }
}

async function onLookupMaterials() {
  if (!detail.value?.productCd) {
    ElMessage.warning(t('製品 CD を入力してください'))
    return
  }
  try {
    const res = await orderApi.lookupProductMaterials(detail.value.productCd)
    if (res.code === 0 && res.data) {
      detail.value.materials = res.data
      store.markDirty()
      ElMessage.success(t('{n} 件の材料を引入しました', { n: res.data.length }))
    }
  } catch { /* */ }
}
</script>

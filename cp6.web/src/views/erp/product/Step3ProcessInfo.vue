<template>
  <el-card shadow="never">
    <div style="margin-bottom: 8px; display: flex; gap: 8px; align-items: center">
      <el-button
        type="primary"
        :icon="Plus"
        size="small"
        :disabled="!store.canEdit"
        @click="addProcess"
      >工程行追加</el-button>
      <el-button
        size="small"
        :disabled="!store.canEdit || store.processes.length === 0"
        @click="reSort"
      >並び順再採番</el-button>
      <span style="color: #909399; font-size: 12px">
        ※ 工程CD 0600/0601/0602（トムソン系）は連産品ボタンが活性化
      </span>
    </div>

    <el-table
      :data="store.processes"
      border
      stripe
      style="width: 100%"
      size="small"
      max-height="600"
    >
      <el-table-column prop="sortOrder" label="No" width="60" align="center" />
      <el-table-column :label="t('作業CD')" width="100">
        <template #default="{ row }">
          <el-input v-model="row.taskCd" :disabled="!store.canEdit" size="small" maxlength="4" />
        </template>
      </el-table-column>
      <el-table-column :label="t('工程CD')" width="100">
        <template #default="{ row }">
          <el-input v-model="row.processCd" :disabled="!store.canEdit" size="small" maxlength="4" />
        </template>
      </el-table-column>
      <el-table-column label="WG" width="80">
        <template #default="{ row }">
          <el-input v-model="row.wgCd" :disabled="!store.canEdit" size="small" />
        </template>
      </el-table-column>
      <el-table-column :label="t('機械/外注')" width="160">
        <template #default="{ row }">
          <el-input v-model="row.machineOrVendor" :disabled="!store.canEdit" size="small" />
        </template>
      </el-table-column>
      <el-table-column :label="t('機械固定')" width="80" align="center">
        <template #default="{ row }">
          <el-checkbox v-model="row.machineFixedFlg" :disabled="!store.canEdit" />
        </template>
      </el-table-column>
      <el-table-column :label="t('納入区分')" width="100">
        <template #default="{ row }">
          <el-input v-model="row.cpDeliveryDiv" :disabled="!store.canEdit" size="small" />
        </template>
      </el-table-column>
      <!-- 工程仕様 1〜10 折叠到一个 popover -->
      <el-table-column :label="t('仕様 1〜10')" width="120" align="center">
        <template #default="{ row, $index }">
          <el-popover :width="320" trigger="click" placement="bottom-start">
            <template #reference>
              <el-button size="small" link type="primary">
                {{ specsSummary(row) }}
              </el-button>
            </template>
            <div>
              <el-form label-width="60px" size="small" :disabled="!store.canEdit">
                <el-form-item v-for="i in 10" :key="i" :label="`仕様${i}`">
                  <el-input v-model="row.specs[i - 1]" size="small" />
                </el-form-item>
              </el-form>
            </div>
            <span style="display: none">{{ $index }}</span>
          </el-popover>
        </template>
      </el-table-column>
      <el-table-column :label="t('製版1')" width="100">
        <template #default="{ row }">
          <el-input v-model="row.plateNo1" :disabled="!store.canEdit" size="small" />
        </template>
      </el-table-column>
      <el-table-column :label="t('製版2')" width="100">
        <template #default="{ row }">
          <el-input v-model="row.plateNo2" :disabled="!store.canEdit" size="small" />
        </template>
      </el-table-column>
      <el-table-column :label="t('副資材1')" width="100">
        <template #default="{ row }">
          <el-input v-model="row.consumable1" :disabled="!store.canEdit" size="small" />
        </template>
      </el-table-column>
      <el-table-column :label="t('購入単価')" width="100">
        <template #default="{ row }">
          <el-input-number
            v-model="row.purchasePrice"
            :disabled="!store.canEdit"
            size="small"
            :controls="false"
            :precision="2"
            style="width: 100%"
          />
        </template>
      </el-table-column>
      <el-table-column :label="t('固定費')" width="100">
        <template #default="{ row }">
          <el-input-number
            v-model="row.fixedPrice"
            :disabled="!store.canEdit"
            size="small"
            :controls="false"
            :precision="2"
            style="width: 100%"
          />
        </template>
      </el-table-column>
      <el-table-column :label="t('ロス率%')" width="90">
        <template #default="{ row }">
          <el-input-number
            v-model="row.lossRate"
            :disabled="!store.canEdit"
            size="small"
            :controls="false"
            :precision="2"
            style="width: 100%"
          />
        </template>
      </el-table-column>
      <el-table-column :label="t('台数')" width="80">
        <template #default="{ row }">
          <el-input-number
            v-model="row.machineCount"
            :disabled="!store.canEdit"
            size="small"
            :controls="false"
            :min="0"
            style="width: 100%"
          />
        </template>
      </el-table-column>
      <el-table-column :label="t('LT(日)')" width="80">
        <template #default="{ row }">
          <el-input-number
            v-model="row.leadTime"
            :disabled="!store.canEdit"
            size="small"
            :controls="false"
            :min="0"
            style="width: 100%"
          />
        </template>
      </el-table-column>
      <el-table-column :label="t('保管先')" width="100">
        <template #default="{ row }">
          <el-input v-model="row.storageDest" :disabled="!store.canEdit" size="small" />
        </template>
      </el-table-column>
      <!-- 製造順優先 1〜8 折叠 -->
      <el-table-column :label="t('製造順 1〜8')" width="120" align="center">
        <template #default="{ row, $index }">
          <el-popover :width="320" trigger="click" placement="bottom-start">
            <template #reference>
              <el-button size="small" link type="primary">
                {{ priosSummary(row) }}
              </el-button>
            </template>
            <div>
              <el-form label-width="80px" size="small" :disabled="!store.canEdit">
                <el-form-item v-for="i in 8" :key="i" :label="`優先${i}`">
                  <el-input v-model="row.manufOrderPrios[i - 1]" size="small" />
                </el-form-item>
              </el-form>
            </div>
            <span style="display: none">{{ $index }}</span>
          </el-popover>
        </template>
      </el-table-column>
      <el-table-column :label="t('連産品')" width="100" align="center" fixed="right">
        <template #default="{ row }">
          <el-button
            size="small"
            link
            type="primary"
            :disabled="!isCoProductable(row)"
            @click="openCoProductDialog(row)"
          >開く</el-button>
        </template>
      </el-table-column>
      <el-table-column :label="t('操作')" width="100" align="center" fixed="right">
        <template #default="{ row, $index }">
          <el-button
            link
            type="danger"
            size="small"
            :disabled="!store.canEdit"
            @click="removeProcess($index)"
          >削除</el-button>
        </template>
      </el-table-column>
    </el-table>

    <!-- ============== 連産品 Popup ============== -->
    <el-dialog
      v-model="coDlgVisible"
      :title="`連産品 - 工程CD ${currentProcessCd}`"
      width="780px"
      :close-on-click-modal="false"
    >
      <div style="margin-bottom: 8px">
        <el-button
          type="primary"
          :icon="Plus"
          size="small"
          :disabled="!store.canEdit"
          @click="addCoProduct"
        >行追加</el-button>
        <el-tag
          :type="coRatioOk ? 'success' : 'danger'"
          size="small"
          style="margin-left: 12px"
        >
          QtyRatio合計: {{ coRatioSum.toFixed(4) }} {{ coRatioOk ? '✓' : '(=1.0 必須)' }}
        </el-tag>
      </div>
      <el-table :data="currentCoList" border stripe size="small" style="width: 100%">
        <el-table-column prop="rowNo" label="No" width="60" align="center" />
        <el-table-column :label="t('連産品名')" min-width="180">
          <template #default="{ row }">
            <el-input v-model="row.coProductName" :disabled="!store.canEdit" size="small" />
          </template>
        </el-table-column>
        <el-table-column :label="t('比率')" width="120">
          <template #default="{ row }">
            <el-input-number
              v-model="row.qtyRatio"
              :disabled="!store.canEdit"
              :precision="4"
              :controls="false"
              :min="0"
              :max="1"
              size="small"
              style="width: 100%"
            />
          </template>
        </el-table-column>
        <el-table-column :label="t('次工程CD')" width="120">
          <template #default="{ row }">
            <el-input v-model="row.nextProcessCd" :disabled="!store.canEdit" size="small" />
          </template>
        </el-table-column>
        <el-table-column :label="t('操作')" width="80" align="center">
          <template #default="{ row }">
            <el-button
              link
              type="danger"
              size="small"
              :disabled="!store.canEdit"
              @click="removeCoProduct(row.rowNo)"
            >削除</el-button>
          </template>
        </el-table-column>
      </el-table>
      <template #footer>
        <el-button @click="coDlgVisible = false">閉じる</el-button>
      </template>
    </el-dialog>
  </el-card>
</template>

<script setup lang="ts">
import { useI18n } from 'vue-i18n'
const { t } = useI18n()
import { ref, computed } from 'vue'
import { ElMessage } from 'element-plus'
import { Plus } from '@element-plus/icons-vue'
import { useProductMasterStore } from '@/stores/productMaster'
import type { ProductProcessDto, ProductCoProductDto } from '@/types/productMaster'

const store = useProductMasterStore()

// トムソン系工程 CD（仕様書 §10 / §第3页 Popup 制約）
const COPRODUCT_PROCESS_CDS = ['0600', '0601', '0602']
function isCoProductable(row: ProductProcessDto): boolean {
  return !!row.processCd && COPRODUCT_PROCESS_CDS.includes(row.processCd)
}

function specsSummary(row: ProductProcessDto): string {
  const filled = (row.specs || []).filter(s => s != null && s !== '').length
  return filled === 0 ? '未入力' : `${filled} / 10`
}
function priosSummary(row: ProductProcessDto): string {
  const filled = (row.manufOrderPrios || []).filter(s => s != null && s !== '').length
  return filled === 0 ? '未入力' : `${filled} / 8`
}

function addProcess() {
  const nextSort = store.processes.length === 0
    ? 10
    : Math.max(...store.processes.map(p => p.sortOrder)) + 10
  store.processes.push({
    taskCd: '',
    processCd: '',
    machineFixedFlg: false,
    specs: Array(10).fill(undefined),
    manufOrderPrios: Array(8).fill(undefined),
    sortOrder: nextSort,
  })
  store.markDirty()
}

function removeProcess(idx: number) {
  store.processes.splice(idx, 1)
  store.markDirty()
}

function reSort() {
  store.processes.forEach((p, i) => {
    p.sortOrder = (i + 1) * 10
  })
  store.markDirty()
  ElMessage.success('並び順を再採番しました')
}

// ============== 連産品 Dialog ==============
const coDlgVisible = ref(false)
const currentProcessCd = ref<string>('')

const currentCoList = computed<ProductCoProductDto[]>(() =>
  store.coProducts.filter(c => c.processCd === currentProcessCd.value),
)
const coRatioSum = computed(() =>
  currentCoList.value.reduce((sum, c) => sum + (c.qtyRatio || 0), 0),
)
const coRatioOk = computed(() =>
  Math.abs(coRatioSum.value - 1) < 0.0001 || currentCoList.value.length === 0,
)

function openCoProductDialog(row: ProductProcessDto) {
  if (!isCoProductable(row)) return
  currentProcessCd.value = row.processCd
  coDlgVisible.value = true
}

function addCoProduct() {
  const list = currentCoList.value
  const nextRowNo = list.length === 0 ? 1 : Math.max(...list.map(c => c.rowNo)) + 1
  store.coProducts.push({
    processCd: currentProcessCd.value,
    rowNo: nextRowNo,
    qtyRatio: 0,
  })
  store.markDirty()
}

function removeCoProduct(rowNo: number) {
  const idx = store.coProducts.findIndex(
    c => c.processCd === currentProcessCd.value && c.rowNo === rowNo,
  )
  if (idx >= 0) {
    store.coProducts.splice(idx, 1)
    store.markDirty()
  }
}

defineExpose({
  validate(): boolean {
    // 全工程の連産品 ratio 合計チェック
    const groupSums = new Map<string, number>()
    for (const c of store.coProducts) {
      groupSums.set(c.processCd, (groupSums.get(c.processCd) ?? 0) + (c.qtyRatio || 0))
    }
    for (const [pcd, sum] of groupSums) {
      if (Math.abs(sum - 1) > 0.0001) {
        ElMessage.error(`工程CD ${pcd} の連産品比率合計が 1.0 ではありません（現:${sum.toFixed(4)}）`)
        return false
      }
    }
    return true
  },
})
</script>

<style scoped>
:deep(.el-input-number .el-input__inner) {
  text-align: right;
}
</style>

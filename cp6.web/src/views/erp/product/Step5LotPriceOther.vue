<template>
  <el-card shadow="never">
    <el-divider content-position="left">ロット別単価</el-divider>

    <div style="margin-bottom: 8px; display: flex; gap: 8px; align-items: center">
      <el-button
        type="primary"
        :icon="Plus"
        size="small"
        :disabled="!store.canEdit"
        @click="addLotPrice"
      >行追加</el-button>
      <span style="color: #909399; font-size: 12px">
        ※ 現在価格は表示用、新価格は次回適用 — 適用日付を必ず入力
      </span>
    </div>

    <el-table
      :data="store.lotPrices"
      border
      stripe
      style="width: 100%"
      size="small"
      max-height="500"
    >
      <el-table-column prop="detailNo" label="No" width="60" align="center" />
      <el-table-column label="ロット数量" width="120">
        <template #default="{ row }">
          <el-input-number
            v-model="row.lotQty"
            :disabled="!store.canEdit"
            :precision="0"
            :controls="false"
            :min="0"
            size="small"
            style="width: 100%"
          />
        </template>
      </el-table-column>
      <el-table-column label="現単価" width="120">
        <template #default="{ row }">
          <el-input-number
            v-model="row.currentUnitPrice"
            :disabled="!store.canEdit"
            :precision="4"
            :controls="false"
            size="small"
            style="width: 100%"
          />
        </template>
      </el-table-column>
      <el-table-column label="現セット価格" width="130">
        <template #default="{ row }">
          <el-input-number
            v-model="row.currentSetPrice"
            :disabled="!store.canEdit"
            :precision="4"
            :controls="false"
            size="small"
            style="width: 100%"
          />
        </template>
      </el-table-column>
      <el-table-column label="現価格適用日" width="140">
        <template #default="{ row }">
          <el-date-picker
            v-model="row.currentPriceDate"
            :disabled="!store.canEdit"
            type="date"
            value-format="YYYY-MM-DD"
            size="small"
            style="width: 100%"
          />
        </template>
      </el-table-column>
      <el-table-column label="新単価" width="120">
        <template #default="{ row }">
          <el-input-number
            v-model="row.newUnitPrice"
            :disabled="!store.canEdit"
            :precision="4"
            :controls="false"
            size="small"
            style="width: 100%"
          />
        </template>
      </el-table-column>
      <el-table-column label="新セット価格" width="130">
        <template #default="{ row }">
          <el-input-number
            v-model="row.newSetPrice"
            :disabled="!store.canEdit"
            :precision="4"
            :controls="false"
            size="small"
            style="width: 100%"
          />
        </template>
      </el-table-column>
      <el-table-column label="新価格適用日" width="140">
        <template #default="{ row }">
          <el-date-picker
            v-model="row.newPriceDate"
            :disabled="!store.canEdit"
            type="date"
            value-format="YYYY-MM-DD"
            size="small"
            style="width: 100%"
          />
        </template>
      </el-table-column>
      <el-table-column label="購入単価" width="120">
        <template #default="{ row }">
          <el-input-number
            v-model="row.purchasePrice"
            :disabled="!store.canEdit"
            :precision="4"
            :controls="false"
            size="small"
            style="width: 100%"
          />
        </template>
      </el-table-column>
      <el-table-column label="価格根拠" min-width="160">
        <template #default="{ row }">
          <el-input v-model="row.priceApplyBasis" :disabled="!store.canEdit" size="small" />
        </template>
      </el-table-column>
      <el-table-column label="操作" width="80" align="center" fixed="right">
        <template #default="{ $index }">
          <el-button
            link
            type="danger"
            size="small"
            :disabled="!store.canEdit"
            @click="removeLotPrice($index)"
          >削除</el-button>
        </template>
      </el-table-column>
    </el-table>

    <el-divider content-position="left">その他情報</el-divider>

    <el-form
      :model="store.basicInfo"
      label-width="140px"
      :disabled="!store.canEdit"
      size="small"
      label-position="right"
    >
      <el-row :gutter="16">
        <el-col :span="8">
          <el-form-item label="購入先業者">
            <el-input v-model="store.basicInfo.purchaseVendor" />
          </el-form-item>
        </el-col>
        <el-col :span="8">
          <el-form-item label="固定出荷区分">
            <el-input v-model="store.basicInfo.fixedShipment" />
          </el-form-item>
        </el-col>
        <el-col :span="8">
          <el-form-item label="納入予備">
            <el-input-number
              v-model="store.basicInfo.deliveryReserve"
              :precision="0"
              :controls="false"
              :min="0"
              style="width: 100%"
            />
          </el-form-item>
        </el-col>
        <el-col :span="8">
          <el-form-item label="営業サンプル">
            <el-input-number
              v-model="store.basicInfo.salesSample"
              :precision="0"
              :controls="false"
              :min="0"
              style="width: 100%"
            />
          </el-form-item>
        </el-col>
        <el-col :span="8">
          <el-form-item label="設計提案 NO">
            <el-input v-model="store.basicInfo.designProposalNo" />
          </el-form-item>
        </el-col>
        <el-col :span="8">
          <el-form-item label="受注種別">
            <el-input v-model="store.basicInfo.orderType" />
          </el-form-item>
        </el-col>
        <el-col :span="8">
          <el-form-item label="商品大分類">
            <el-input v-model="store.basicInfo.productCatBig" />
          </el-form-item>
        </el-col>
        <el-col :span="8">
          <el-form-item label="商品中分類">
            <el-input v-model="store.basicInfo.productCatMid" />
          </el-form-item>
        </el-col>
        <el-col :span="8">
          <el-form-item label="商品小分類">
            <el-input v-model="store.basicInfo.productCatSml" />
          </el-form-item>
        </el-col>
      </el-row>
    </el-form>

    <!-- 仕掛チェック 結果（mcframe7 連携無し時は常に level=0） -->
    <el-divider content-position="left">仕掛チェック</el-divider>
    <el-alert
      v-if="store.wipCheckResult"
      :type="alertType"
      :title="alertTitle"
      :description="store.wipCheckResult.message ?? ''"
      :closable="false"
      show-icon
    >
      <template v-if="store.wipCheckResult.wipHandleNumbers?.length">
        <div style="margin-top: 8px; font-size: 12px">
          関連仕掛番号: {{ store.wipCheckResult.wipHandleNumbers.join(', ') }}
        </div>
      </template>
    </el-alert>
    <el-empty v-else description="未実行（保存時に自動実行）" :image-size="60" />
  </el-card>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { ElMessage } from 'element-plus'
import { Plus } from '@element-plus/icons-vue'
import { useProductMasterStore } from '@/stores/productMaster'

const store = useProductMasterStore()

const alertType = computed<'success' | 'warning' | 'error'>(() => {
  const lv = store.wipCheckResult?.level ?? 0
  if (lv === 0) return 'success'
  if (lv === 1) return 'warning'
  return 'error'
})
const alertTitle = computed(() => {
  const lv = store.wipCheckResult?.level ?? 0
  if (lv === 0) return '仕掛なし — 保存可能'
  if (lv === 1) return '仕掛あり（警告）— 確認のうえ続行'
  if (lv === 2) return 'エラー — 仕掛確定済'
  return 'エラー — 仕掛指図済'
})

function addLotPrice() {
  const nextNo = store.lotPrices.length === 0
    ? 1
    : Math.max(...store.lotPrices.map(p => p.detailNo)) + 1
  store.lotPrices.push({
    detailNo: nextNo,
    lotQty: 0,
  })
  store.markDirty()
}

function removeLotPrice(idx: number) {
  store.lotPrices.splice(idx, 1)
  store.markDirty()
}

defineExpose({
  validate(): boolean {
    // ロット数量が昇順であるかをチェック
    const qtys = store.lotPrices.map(p => p.lotQty)
    for (let i = 1; i < qtys.length; i++) {
      if ((qtys[i] ?? 0) <= (qtys[i - 1] ?? 0)) {
        ElMessage.error(`ロット別単価: ロット数量は昇順である必要があります（行${i + 1}）`)
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

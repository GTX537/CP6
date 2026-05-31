<template>
  <el-card shadow="never" v-if="detail">
    <div style="margin-bottom: 12px;">
      <el-tag size="small">明細 No.{{ detail.webOrderDetailNo }} - {{ detail.productCd || '製品 CD 未選択' }}</el-tag>
      <el-tag v-if="!isCompositionEditable" type="info" size="small" style="margin-left: 8px;">
        構成・工程 参照のみ（受注区分: {{ store.order.orderType }}）
      </el-tag>
      <el-tag v-else type="success" size="small" style="margin-left: 8px;">
        シート受注 → 構成・工程編集可
      </el-tag>
    </div>

    <!-- 基本情報 -->
    <el-divider content-position="left">基本情報</el-divider>
    <el-form :model="detail" :disabled="store.isPageReadOnly" label-width="110px" size="small" inline>
      <el-form-item label="納品先 CD">
        <el-input v-model="detail.deliveryCd" :disabled="!store.canEdit" style="width: 160px" />
      </el-form-item>
      <el-form-item label="納品先名">
        <el-input v-model="detail.deliveryName" :disabled="!store.canEdit" style="width: 200px" />
      </el-form-item>
      <el-form-item label="客先納期">
        <el-date-picker v-model="detail.customerDeliveryDate" type="date" value-format="YYYY-MM-DD" :disabled="!store.canEdit" style="width: 150px" />
      </el-form-item>
      <el-form-item label="顧客品名 1">
        <el-input v-model="detail.customerItemName1" :disabled="!store.canEdit" style="width: 200px" />
      </el-form-item>
      <el-form-item label="顧客品名 2">
        <el-input v-model="detail.customerItemName2" :disabled="!store.canEdit" style="width: 200px" />
      </el-form-item>
      <el-form-item label="顧客品番">
        <el-input v-model="detail.customerPartNo" :disabled="!store.canEdit" style="width: 160px" />
      </el-form-item>
      <el-form-item label="CP品名 1">
        <el-input v-model="detail.cpItemName1" :disabled="!store.canEdit" style="width: 200px" />
      </el-form-item>
      <el-form-item label="CP品名 2">
        <el-input v-model="detail.cpItemName2" :disabled="!store.canEdit" style="width: 200px" />
      </el-form-item>
      <el-form-item label="預り売上数">
        <el-input-number v-model="detail.consignedSalesQty" :disabled="!store.canEdit" :precision="2" :controls="false" style="width: 130px" />
      </el-form-item>
      <el-form-item label="売上理由">
        <el-input v-model="detail.salesReason" :disabled="!store.canEdit" style="width: 200px" />
      </el-form-item>
      <el-form-item label="不適合手配 NO">
        <el-input v-model="detail.defectiveHaibaiNo" :disabled="!store.canEdit" style="width: 160px" />
      </el-form-item>
    </el-form>

    <!-- 構成情報（13 フィールド + 寸法） -->
    <el-divider content-position="left">構成情報</el-divider>
    <el-form :model="detail" :disabled="store.isPageReadOnly || !isCompositionEditable" label-width="110px" size="small" inline>
      <el-form-item label="シート段">
        <el-input v-model="detail.sheetFlute" style="width: 100px" />
      </el-form-item>
      <el-form-item label="原紙 表/中/裏">
        <el-input v-model="detail.paperCdF" placeholder="表" style="width: 100px" />
        <el-input v-model="detail.paperCdC" placeholder="中" style="width: 100px; margin-left: 4px" />
        <el-input v-model="detail.paperCdB" placeholder="裏" style="width: 100px; margin-left: 4px" />
      </el-form-item>
      <el-form-item label="印刷 表/中/裏">
        <el-input v-model="detail.printCdF" placeholder="表" style="width: 100px" />
        <el-input v-model="detail.printCdC" placeholder="中" style="width: 100px; margin-left: 4px" />
        <el-input v-model="detail.printCdB" placeholder="裏" style="width: 100px; margin-left: 4px" />
      </el-form-item>
      <el-form-item label="エンボス 表/中/裏">
        <el-input v-model="detail.embossCdF" placeholder="表" style="width: 100px" />
        <el-input v-model="detail.embossCdC" placeholder="中" style="width: 100px; margin-left: 4px" />
        <el-input v-model="detail.embossCdB" placeholder="裏" style="width: 100px; margin-left: 4px" />
      </el-form-item>
      <el-form-item label="メーカ 表/中/裏">
        <el-input v-model="detail.makerCdF" placeholder="表" style="width: 100px" />
        <el-input v-model="detail.makerCdC" placeholder="中" style="width: 100px; margin-left: 4px" />
        <el-input v-model="detail.makerCdB" placeholder="裏" style="width: 100px; margin-left: 4px" />
      </el-form-item>
      <el-form-item label="刃渡 巾">
        <el-input-number v-model="detail.bladeWidth" :precision="2" :controls="false" style="width: 110px" />
      </el-form-item>
      <el-form-item label="刃渡 流れ">
        <el-input-number v-model="detail.bladeFlow" :precision="2" :controls="false" style="width: 110px" />
      </el-form-item>
      <el-form-item label="シート 巾">
        <el-input-number v-model="detail.sheetDimW" :precision="2" :controls="false" style="width: 110px" />
      </el-form-item>
      <el-form-item label="シート 流れ">
        <el-input-number v-model="detail.sheetDimF" :precision="2" :controls="false" style="width: 110px" />
      </el-form-item>
      <el-form-item label="売上巾">
        <el-input-number v-model="detail.salesWidth" :precision="2" :controls="false" style="width: 110px" />
      </el-form-item>
    </el-form>

    <!-- 仕入情報 -->
    <el-divider content-position="left">仕入情報</el-divider>
    <el-form :model="detail" :disabled="store.isPageReadOnly" label-width="110px" size="small" inline>
      <el-form-item label="発注先">
        <el-input v-model="detail.purchaseVendor" :disabled="!store.canEdit" style="width: 180px" />
      </el-form-item>
      <el-form-item label="発注単価">
        <el-input-number v-model="detail.purchaseUnitPrice" :disabled="!store.canEdit" :precision="4" :controls="false" style="width: 130px" />
      </el-form-item>
      <el-form-item label="発注単位">
        <el-input v-model="detail.purchaseUnit" :disabled="!store.canEdit" style="width: 100px" />
      </el-form-item>
      <el-form-item label="巻きM">
        <el-input-number v-model="detail.rollMeter" :disabled="!store.canEdit" :precision="2" :controls="false" style="width: 130px" />
      </el-form-item>
    </el-form>

    <!-- 備考（7種） -->
    <el-divider content-position="left">備考</el-divider>
    <el-form :model="detail" :disabled="store.isPageReadOnly" label-width="110px" size="small">
      <el-row :gutter="8">
        <el-col :span="12"><el-form-item label="印刷備考"><el-input v-model="detail.printNote" :disabled="!store.canEdit" /></el-form-item></el-col>
        <el-col :span="12"><el-form-item label="製造備考"><el-input v-model="detail.mfgNote" :disabled="!store.canEdit" /></el-form-item></el-col>
        <el-col :span="12"><el-form-item label="再製造備考"><el-input v-model="detail.remfgNote" :disabled="!store.canEdit" /></el-form-item></el-col>
        <el-col :span="12"><el-form-item label="伝票備考"><el-input v-model="detail.slipNote" :disabled="!store.canEdit" /></el-form-item></el-col>
        <el-col :span="12"><el-form-item label="納入備考"><el-input v-model="detail.deliveryNote" :disabled="!store.canEdit" /></el-form-item></el-col>
        <el-col :span="12"><el-form-item label="出荷備考1"><el-input v-model="detail.shipNote1" :disabled="!store.canEdit" /></el-form-item></el-col>
        <el-col :span="12"><el-form-item label="出荷備考2"><el-input v-model="detail.shipNote2" :disabled="!store.canEdit" /></el-form-item></el-col>
      </el-row>
    </el-form>

    <!-- 明細選択 -->
    <el-divider content-position="left">明細切替</el-divider>
    <el-radio-group v-model="store.currentDetailIndex" size="small">
      <el-radio-button v-for="(d, i) in store.order.details" :key="d.webOrderDetailNo" :value="i">
        No.{{ d.webOrderDetailNo }} {{ d.productCd }}
      </el-radio-button>
    </el-radio-group>
  </el-card>
  <el-empty v-else description="部材一覧で行を選択してください" />
</template>

<script setup lang="ts">
import { computed, watch } from 'vue'
import { useOrderStore } from '@/stores/order'
import { orderApi } from '@/api/order'

const store = useOrderStore()
const detail = computed(() => store.currentDetail)

const isCompositionEditable = computed(() => {
  // 仕様書 §1：受注区分 × 製品区分 × 品目採番 から判定
  // 簡略：シート受注(20)・原紙(40)・輪切り(80) は編集可
  return ['20', '40', '80'].includes(store.order.orderType)
})

// 受注区分が変わったら isEditable を再計算（API 呼び出し）
watch(() => store.order.orderType, async ot => {
  if (!ot) return
  try {
    const res = await orderApi.calcIsEditable(ot, detail.value?.productCatBig, detail.value?.productCd)
    if (res.code === 0 && res.data) store.isEditableFlag = res.data.isEditable
  } catch { /* */ }
})
</script>

<template>
  <el-form :model="store.bp" label-width="140px" size="small" :disabled="store.isPageReadOnly" inline>
    <el-divider content-position="left">発注先パターン</el-divider>
    <el-form-item label="発注先パターン" required>
      <el-select v-model="store.bp.supplierPattern" :disabled="!store.canEdit" clearable style="width: 200px">
        <el-option label="原材料" value="1" />
        <el-option label="資材" value="2" />
        <el-option label="副資材" value="3" />
        <el-option label="購買品" value="4" />
        <el-option label="外注品" value="5" />
        <el-option label="他" value="9" />
      </el-select>
    </el-form-item>
    <el-form-item label="下請対象">
      <el-checkbox v-model="store.bp.subcontractTargetFlg" :disabled="!store.canEdit || !store.isOutsourcing">下請対象</el-checkbox>
    </el-form-item>
    <el-form-item label="外注単価区分">
      <el-input v-model="store.bp.subcontractPriceDiv" :disabled="!store.canEdit || !store.isOutsourcing" style="width: 140px" />
    </el-form-item>
    <el-form-item label="支給計上区分">
      <el-input v-model="store.bp.supplyPostingDiv" :disabled="!store.canEdit || !store.isOutsourcing" style="width: 140px" />
    </el-form-item>
    <el-form-item label="支給単価変更許可">
      <el-checkbox v-model="store.bp.supplyPriceChangeAllowFlg" :disabled="!store.canEdit || !store.isOutsourcing">支給単価変更許可</el-checkbox>
    </el-form-item>

    <el-divider content-position="left">共通初期値</el-divider>
    <el-form-item label="納期回答確認"><el-checkbox v-model="store.bp.deliveryConfirmFlg" :disabled="!store.canEdit">確認FLG</el-checkbox></el-form-item>
    <el-form-item label="仕入金額端数"><el-input v-model="store.bp.purchaseFractionDiv" :disabled="!store.canEdit" style="width: 100px" /></el-form-item>
    <el-form-item label="仕入税額端数"><el-input v-model="store.bp.purchaseTaxFractionDiv" :disabled="!store.canEdit" style="width: 100px" /></el-form-item>
    <el-form-item label="仕入税"><el-input v-model="store.bp.purchaseTaxCd" :disabled="!store.canEdit" style="width: 120px" /></el-form-item>
    <el-form-item label="仕入税優先">
      <el-checkbox v-model="store.bp.purchaseTaxPriorityFlg" :disabled="!store.canEdit">優先FLG</el-checkbox>
    </el-form-item>
    <el-form-item label="発注先カレンダ"><el-input v-model="store.bp.supplierCalendarCd" :disabled="!store.canEdit" style="width: 120px" /></el-form-item>
    <el-form-item label="支給積送管理区分"><el-input v-model="store.bp.supplyConsignDiv" :disabled="!store.canEdit" style="width: 100px" /></el-form-item>
    <el-form-item label="仕入ロット分割"><el-input v-model="store.bp.purchaseLotSplitDiv" :disabled="!store.canEdit" style="width: 100px" /></el-form-item>
    <el-form-item label="仕入計上区分"><el-input v-model="store.bp.purchasePostingDiv" :disabled="!store.canEdit" style="width: 100px" /></el-form-item>

    <el-divider content-position="left">有償支給連動</el-divider>
    <el-form-item label="支給金額端数"><el-input v-model="store.bp.paidSupplyAmountFractionDiv" :disabled="!store.canEdit || !store.bp.paidSupplyFlg" style="width: 100px" /></el-form-item>
    <el-form-item label="支給税額端数"><el-input v-model="store.bp.paidSupplyTaxFractionDiv" :disabled="!store.canEdit || !store.bp.paidSupplyFlg" style="width: 100px" /></el-form-item>
    <el-form-item label="支給税計算区分"><el-input v-model="store.bp.paidSupplyTaxCalcDiv" :disabled="!store.canEdit || !store.bp.paidSupplyFlg" style="width: 100px" /></el-form-item>
    <el-form-item label="支給税CD"><el-input v-model="store.bp.paidSupplyTaxCd" :disabled="!store.canEdit || !store.bp.paidSupplyFlg || !store.bp.rebuyObligationFlg" style="width: 120px" /></el-form-item>

    <el-divider content-position="left">メーカ連動</el-divider>
    <el-form-item label="FSC認証先区分"><el-input v-model="store.bp.fscCertificationDiv" :disabled="!store.canEdit || !store.bp.makerFlg" style="width: 140px" /></el-form-item>
  </el-form>
</template>

<script setup lang="ts">
import { useBpStore } from '@/stores/businessPartner'
const props = defineProps<{ store: ReturnType<typeof useBpStore> }>()
const store = props.store
</script>
<style scoped>:deep(.el-form-item){margin-bottom:8px;}</style>

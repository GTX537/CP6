<template>
  <el-form :model="store.bp" label-width="120px" size="small" :disabled="store.isPageReadOnly" inline>
    <el-divider content-position="left">{{ t('得意先基本') }}</el-divider>
    <el-form-item :label="t('得意先 CD(親)')"><el-input v-model="store.bp.parentCustomerCd" :disabled="!store.canEdit" style="width: 200px" /></el-form-item>
    <el-form-item :label="t('ユーザー区分')">
      <el-select v-model="store.bp.userConverterDiv" :disabled="!store.canEdit" clearable style="width: 140px">
        <el-option :label="t('ユーザー')" value="1" /><el-option :label="t('コンバーター')" value="2" />
      </el-select>
    </el-form-item>
    <el-form-item :label="t('売掛先')" required><el-input v-model="store.bp.accountsReceivableCd" :disabled="!store.canEdit" style="width: 200px" /></el-form-item>
    <el-form-item :label="t('与信管理先')"><el-input v-model="store.bp.creditMgmtCd" :disabled="!store.canEdit" style="width: 200px" /></el-form-item>
    <el-form-item :label="t('得意先部署')"><el-input v-model="store.bp.customerDept" :disabled="!store.canEdit" style="width: 200px" /></el-form-item>
    <el-form-item :label="t('得意先担当者')"><el-input v-model="store.bp.customerContact" :disabled="!store.canEdit" style="width: 180px" /></el-form-item>
    <el-form-item :label="t('敬称')"><el-input v-model="store.bp.customerContactTitle" :disabled="!store.canEdit" style="width: 120px" /></el-form-item>
    <el-form-item :label="t('容リ法対象')">
      <el-select v-model="store.bp.recyclingTarget" :disabled="!store.canEdit" clearable style="width: 120px">
        <el-option :label="t('対象')" value="1" /><el-option :label="t('非対象')" value="0" />
      </el-select>
    </el-form-item>

    <el-divider content-position="left">{{ t('得意先(売上計算) — 19 項目') }}</el-divider>
    <el-form-item :label="t('売上計上区分')">
      <el-select v-model="store.bp.salesPostingDiv" :disabled="!store.canEdit" clearable style="width: 120px">
        <el-option label="㎡" value="1" /><el-option :label="t('枚')" value="2" />
      </el-select>
    </el-form-item>
    <el-form-item :label="t('シート枚売上計算')">
      <el-input v-model="store.bp.sheetSalesCalcMethod" :disabled="!store.canEdit || store.bp.salesPostingDiv !== '2'" style="width: 140px" />
    </el-form-item>
    <el-form-item :label="t('売上計算(㎡)')"><el-input v-model="store.bp.salesCalcDivM2" :disabled="!store.canEdit" style="width: 140px" /></el-form-item>
    <el-form-item :label="t('売上計算(枚)')"><el-input v-model="store.bp.salesCalcDivPiece" :disabled="!store.canEdit" style="width: 140px" /></el-form-item>
    <el-form-item :label="t('端数計算')"><el-input v-model="store.bp.fractionCalcDiv" :disabled="!store.canEdit" style="width: 120px" /></el-form-item>
    <el-form-item :label="t('全判売区分')"><el-input v-model="store.bp.fullSheetSalesDiv" :disabled="!store.canEdit" style="width: 120px" /></el-form-item>
    <el-form-item :label="t('スリッター請求')"><el-input v-model="store.bp.slitterBillingDiv" :disabled="!store.canEdit" style="width: 120px" /></el-form-item>
    <el-form-item :label="t('スリッター単価')"><el-input-number v-model="store.bp.slitterBillingUnitPrice" :disabled="!store.canEdit" :precision="4" :controls="false" style="width: 130px" /></el-form-item>
    <el-form-item :label="t('スリッター最大流')"><el-input-number v-model="store.bp.slitterMaxFlow" :disabled="!store.canEdit" :precision="2" :controls="false" style="width: 130px" /></el-form-item>
    <el-form-item :label="t('印刷最低請求')"><el-input v-model="store.bp.printMinBilling" :disabled="!store.canEdit" style="width: 120px" /></el-form-item>
    <el-form-item :label="t('印刷最低未満')"><el-input-number v-model="store.bp.printMinBillingBelow" :disabled="!store.canEdit" :precision="2" :controls="false" style="width: 130px" /></el-form-item>
    <el-form-item :label="t('印刷最低単位')">
      <el-select v-model="store.bp.printMinBillingUnit" :disabled="!store.canEdit" clearable style="width: 100px">
        <el-option label="㎡" value="1" /><el-option label="m" value="8" />
      </el-select>
    </el-form-item>
    <el-form-item :label="t('貼合最低請求')"><el-input v-model="store.bp.laminateMinBilling" :disabled="!store.canEdit" style="width: 120px" /></el-form-item>
    <el-form-item :label="t('貼合最低未満')"><el-input-number v-model="store.bp.laminateMinBillingBelow" :disabled="!store.canEdit" :precision="2" :controls="false" style="width: 130px" /></el-form-item>
    <el-form-item :label="t('貼合最低単位')">
      <el-select v-model="store.bp.laminateMinBillingUnit" :disabled="!store.canEdit" clearable style="width: 100px">
        <el-option label="㎡" value="1" /><el-option label="m" value="8" />
      </el-select>
    </el-form-item>
    <el-form-item :label="t('貼合増@')"><el-input-number v-model="store.bp.laminateAddRate" :disabled="!store.canEdit" :precision="4" :controls="false" style="width: 130px" /></el-form-item>
    <el-form-item :label="t('貼合増表示')"><el-input v-model="store.bp.laminateAddDisplay" :disabled="!store.canEdit" style="width: 120px" /></el-form-item>
    <el-form-item :label="t('加工見積最低')"><el-input v-model="store.bp.processingMinEstimate" :disabled="!store.canEdit" style="width: 120px" /></el-form-item>
    <el-form-item :label="t('新シート単価')"><el-input v-model="store.bp.newSheetUnitPriceBase" :disabled="!store.canEdit" style="width: 140px" /></el-form-item>

    <el-divider content-position="left">{{ t('納品書/見積計算書 — 5 項目') }}</el-divider>
    <el-form-item :label="t('納品書出力区分')"><el-input v-model="store.bp.deliverySlipOutDiv" :disabled="!store.canEdit" style="width: 120px" /></el-form-item>
    <el-form-item :label="t('納品書発行区分')"><el-input v-model="store.bp.deliverySlipIssueDiv" :disabled="!store.canEdit" style="width: 120px" /></el-form-item>
    <el-form-item :label="t('特伝区分')"><el-input v-model="store.bp.specialSlipDiv" :disabled="!store.canEdit" style="width: 120px" /></el-form-item>
    <el-form-item :label="t('夜積区分')"><el-input v-model="store.bp.nightLoadDiv" :disabled="!store.canEdit" style="width: 120px" /></el-form-item>
    <el-form-item :label="t('寸法印字区分')"><el-input v-model="store.bp.sizePrintDiv" :disabled="!store.canEdit" style="width: 120px" /></el-form-item>

    <el-divider content-position="left">{{ t('納品計算書 — 10 項目') }}</el-divider>
    <el-form-item :label="t('納品計算出力区分')"><el-input v-model="store.bp.deliveryCalcOutDiv" :disabled="!store.canEdit" style="width: 120px" /></el-form-item>
    <el-form-item :label="t('納品計算発行区分')"><el-input v-model="store.bp.deliveryCalcIssueDiv" :disabled="!store.canEdit" style="width: 120px" /></el-form-item>
    <el-form-item :label="t('納品計算出力2')"><el-input v-model="store.bp.deliveryCalcOutDiv2" :disabled="!store.canEdit" style="width: 120px" /></el-form-item>
    <el-form-item :label="t('宛先')"><el-input v-model="store.bp.deliveryCalcAddressee" :disabled="!store.canEdit" style="width: 200px" /></el-form-item>
    <el-form-item :label="t('送付担当者')"><el-input v-model="store.bp.deliveryCalcSender" :disabled="!store.canEdit" style="width: 160px" /></el-form-item>
    <el-form-item :label="t('郵便番号')"><el-input v-model="store.bp.deliveryCalcZipCd" :disabled="!store.canEdit" style="width: 130px" /></el-form-item>
    <el-form-item :label="t('都道府県')"><el-input v-model="store.bp.deliveryCalcAddr1" :disabled="!store.canEdit" style="width: 200px" /></el-form-item>
    <el-form-item :label="t('市区町村')"><el-input v-model="store.bp.deliveryCalcAddr2" :disabled="!store.canEdit" style="width: 200px" /></el-form-item>
    <el-form-item :label="t('町域・番地')"><el-input v-model="store.bp.deliveryCalcAddr3" :disabled="!store.canEdit" style="width: 240px" /></el-form-item>
    <el-form-item :label="t('建物など')"><el-input v-model="store.bp.deliveryCalcAddr4" :disabled="!store.canEdit" style="width: 240px" /></el-form-item>
  </el-form>
</template>

<script setup lang="ts">
import { useI18n } from 'vue-i18n'
const { t } = useI18n()
import { useBpStore } from '@/stores/businessPartner'
const props = defineProps<{ store: ReturnType<typeof useBpStore> }>()
const store = props.store
</script>

<style scoped>:deep(.el-form-item) { margin-bottom: 8px; }</style>

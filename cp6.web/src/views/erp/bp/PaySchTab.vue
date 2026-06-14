<template>
  <el-form :model="store.bp" label-width="160px" size="small" :disabled="store.isPageReadOnly" inline>
    <el-form-item :label="t('支払先')" required>
      <el-input v-model="store.bp.paymentCd" :disabled="!store.canEdit" style="width: 200px" />
    </el-form-item>
    <el-form-item :label="t('担当部門 CD')" required>
      <el-input v-model="store.bp.paymentScheduleDeptCd" :disabled="!store.canEdit" style="width: 200px" />
    </el-form-item>
    <el-form-item :label="t('都度支払 FLG')">
      <el-checkbox v-model="store.bp.paidEachTimeFlg" :disabled="!store.canEdit">{{ t('都度支払') }}</el-checkbox>
    </el-form-item>
    <el-form-item :label="t('支払締日 1')" :required="!store.bp.paidEachTimeFlg">
      <el-input-number v-model="store.bp.paymentClosingDay1" :disabled="!store.canEdit || store.bp.paidEachTimeFlg" :min="1" :max="99" :controls="false" style="width: 110px" />
      <span style="margin-left: 6px; font-size: 12px; color: #909399;">{{ t('1〜31, 99') }}</span>
    </el-form-item>
    <el-form-item :label="t('支払税計算 FLG')">
      <el-checkbox v-model="store.bp.paymentTaxCalcFlg" :disabled="!store.canEdit">{{ t('税計算') }}</el-checkbox>
    </el-form-item>
    <el-form-item :label="t('支払税額端数')" :required="store.bp.paymentTaxCalcFlg">
      <el-input v-model="store.bp.paymentTaxFractionDiv" :disabled="!store.canEdit || !store.bp.paymentTaxCalcFlg" style="width: 100px" />
    </el-form-item>
    <el-form-item :label="t('バッチ予定締 FLG')">
      <el-checkbox v-model="store.bp.batchPaymentScheduleFlg" :disabled="!store.canEdit">{{ t('バッチ予定締') }}</el-checkbox>
    </el-form-item>
    <el-form-item :label="t('支払予定締遅延日数')" :required="store.bp.batchPaymentScheduleFlg">
      <el-input-number v-model="store.bp.paymentScheduleDelayDays" :disabled="!store.canEdit || !store.bp.batchPaymentScheduleFlg" :precision="0" :controls="false" style="width: 110px" />
    </el-form-item>
  </el-form>
</template>

<script setup lang="ts">
import { useI18n } from 'vue-i18n'
const { t } = useI18n()
import { useBpStore } from '@/stores/businessPartner'
const props = defineProps<{ store: ReturnType<typeof useBpStore> }>()
const store = props.store
</script>
<style scoped>:deep(.el-form-item){margin-bottom:8px;}</style>

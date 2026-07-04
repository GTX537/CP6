<template>
  <el-form :model="store.bp" label-width="120px" size="small" :disabled="store.isPageReadOnly" inline>
    <el-form-item :label="t('入金先')" required><el-input v-model="store.bp.receiptCd" :disabled="!store.canEdit" style="width: 200px" /></el-form-item>
    <el-form-item :label="t('入金先名')"><el-input v-model="store.bp.receiptName" :disabled="!store.canEdit" style="width: 240px" /></el-form-item>
    <el-form-item :label="t('債権与信管理先')"><el-input v-model="store.bp.creditMgmtArCd" :disabled="!store.canEdit" style="width: 200px" /></el-form-item>
    <el-form-item :label="t('請求締日 1')">
      <el-input-number v-model="store.bp.billingClosingDay1" :disabled="!store.canEdit" :min="1" :max="99" :controls="false" style="width: 110px" />
      <span class="hint-text">1〜31, 99</span>
    </el-form-item>
    <el-form-item :label="t('請求書印刷区分')"><el-input v-model="store.bp.billingPrintDiv" :disabled="!store.canEdit" style="width: 120px" /></el-form-item>
    <el-form-item :label="t('請求書封緘区分')"><el-input v-model="store.bp.billingSealDiv" :disabled="!store.canEdit" style="width: 120px" /></el-form-item>
    <el-form-item :label="t('電子請求')"><el-input v-model="store.bp.electronicBilling" :disabled="!store.canEdit" style="width: 120px" /></el-form-item>
    <el-divider content-position="left">{{ t('請求書 送付先') }}</el-divider>
    <el-form-item :label="t('宛先')"><el-input v-model="store.bp.billingAddressee" :disabled="!store.canEdit" style="width: 200px" /></el-form-item>
    <el-form-item :label="t('送付担当者')"><el-input v-model="store.bp.billingSender" :disabled="!store.canEdit" style="width: 160px" /></el-form-item>
    <el-form-item :label="t('郵便番号')"><el-input v-model="store.bp.billingZipCd" :disabled="!store.canEdit" style="width: 130px" /></el-form-item>
    <el-form-item :label="t('都道府県')"><el-input v-model="store.bp.billingAddr1" :disabled="!store.canEdit" style="width: 200px" /></el-form-item>
    <el-form-item :label="t('市区町村')"><el-input v-model="store.bp.billingAddr2" :disabled="!store.canEdit" style="width: 200px" /></el-form-item>
    <el-form-item :label="t('町域・番地')"><el-input v-model="store.bp.billingAddr3" :disabled="!store.canEdit" style="width: 240px" /></el-form-item>
    <el-form-item :label="t('建物など')"><el-input v-model="store.bp.billingAddr4" :disabled="!store.canEdit" style="width: 240px" /></el-form-item>
  </el-form>
</template>

<script setup lang="ts">
import { useI18n } from 'vue-i18n'
const { t } = useI18n()
import { useBpStore } from '@/stores/businessPartner'
const props = defineProps<{ store: ReturnType<typeof useBpStore> }>()
const store = props.store
</script>
<style scoped>
:deep(.el-form-item){margin-bottom:8px;}
.hint-text { margin-left: 6px; font-size: 12px; color: var(--cp-muted); }
</style>

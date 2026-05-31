<template>
  <el-form :model="store.bp" label-width="120px" size="small" :disabled="store.isPageReadOnly" inline>
    <el-form-item label="振込人名"><el-input v-model="store.bp.remittanceName" :disabled="!store.canEdit" style="width: 200px" /></el-form-item>
    <el-form-item label="銀行(入金先)"><el-input v-model="store.bp.bankCd" :disabled="!store.canEdit" style="width: 160px" /></el-form-item>
    <el-form-item label="銀行支店"><el-input v-model="store.bp.bankBranchCd" :disabled="!store.canEdit" style="width: 160px" /></el-form-item>
    <el-form-item label="振込元口座"><el-input v-model="store.bp.remittanceAccount" :disabled="!store.canEdit" style="width: 200px" /></el-form-item>
    <el-form-item label="仮口座区分"><el-input v-model="store.bp.tempAccountDiv" :disabled="!store.canEdit" style="width: 120px" /></el-form-item>
    <el-form-item label="本口座登録日"><el-date-picker v-model="store.bp.mainAccountRegDate" type="date" value-format="YYYY-MM-DD" :disabled="!store.canEdit" style="width: 150px" /></el-form-item>
    <el-form-item label="振出人"><el-input v-model="store.bp.drawer" :disabled="!store.canEdit" style="width: 200px" /></el-form-item>
    <el-form-item label="集金区分"><el-input v-model="store.bp.collectionDiv" :disabled="!store.canEdit" style="width: 120px" /></el-form-item>
    <el-form-item label="集金予定日">
      <el-input-number v-model="store.bp.collectionPlannedDay" :disabled="!store.canEdit" :min="1" :max="99" :controls="false" style="width: 110px" />
      <span style="margin-left: 6px; font-size: 12px; color: #909399;">1〜31, 99</span>
    </el-form-item>
    <el-form-item label="手形回り区分"><el-input v-model="store.bp.billRotationDiv" :disabled="!store.canEdit" style="width: 120px" /></el-form-item>
    <el-divider content-position="left">領収書 送付先</el-divider>
    <el-form-item label="宛先"><el-input v-model="store.bp.receiptAddressee" :disabled="!store.canEdit" style="width: 200px" /></el-form-item>
    <el-form-item label="送付担当者"><el-input v-model="store.bp.receiptSender" :disabled="!store.canEdit" style="width: 160px" /></el-form-item>
    <el-form-item label="郵便番号"><el-input v-model="store.bp.receiptZipCd" :disabled="!store.canEdit" style="width: 130px" /></el-form-item>
    <el-form-item label="都道府県"><el-input v-model="store.bp.receiptAddr1" :disabled="!store.canEdit" style="width: 200px" /></el-form-item>
    <el-form-item label="市区町村"><el-input v-model="store.bp.receiptAddr2" :disabled="!store.canEdit" style="width: 200px" /></el-form-item>
    <el-form-item label="町域・番地"><el-input v-model="store.bp.receiptAddr3" :disabled="!store.canEdit" style="width: 240px" /></el-form-item>
    <el-form-item label="建物など"><el-input v-model="store.bp.receiptAddr4" :disabled="!store.canEdit" style="width: 240px" /></el-form-item>
    <el-form-item label="回収備考"><el-input v-model="store.bp.collectionNote" :disabled="!store.canEdit" type="textarea" :rows="2" style="width: 480px" /></el-form-item>
  </el-form>
</template>

<script setup lang="ts">
import { useBpStore } from '@/stores/businessPartner'
const props = defineProps<{ store: ReturnType<typeof useBpStore> }>()
const store = props.store
</script>
<style scoped>:deep(.el-form-item){margin-bottom:8px;}</style>

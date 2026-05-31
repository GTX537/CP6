<template>
  <el-form :model="store.bp" label-width="130px" size="small" :disabled="store.isPageReadOnly" inline>
    <el-form-item label="物流グループCD" required><el-input v-model="store.bp.logisticsGroupCd" :disabled="!store.canEdit" style="width: 160px" /></el-form-item>
    <el-form-item label="物流グループ名"><el-input v-model="store.bp.logisticsGroupName" :disabled="!store.canEdit" style="width: 200px" /></el-form-item>
    <el-form-item label="納品先部署"><el-input v-model="store.bp.deliveryDept" :disabled="!store.canEdit" style="width: 200px" /></el-form-item>
    <el-form-item label="納品先担当者"><el-input v-model="store.bp.deliveryContact" :disabled="!store.canEdit" style="width: 180px" /></el-form-item>
    <el-form-item label="敬称"><el-input v-model="store.bp.deliveryContactTitle" :disabled="!store.canEdit" style="width: 120px" /></el-form-item>
    <el-form-item label="トラック長制限">
      <el-input-number v-model="store.bp.truckLengthLimit" :disabled="!store.canEdit" :precision="2" :controls="false" style="width: 130px" />
    </el-form-item>
    <el-form-item label="納入時間 FROM"><el-input v-model="store.bp.deliveryTimeFrom" placeholder="HH:MM" :disabled="!store.canEdit" style="width: 100px" /></el-form-item>
    <el-form-item label="納入時間 TO"><el-input v-model="store.bp.deliveryTimeTo" placeholder="HH:MM" :disabled="!store.canEdit" style="width: 100px" /></el-form-item>
    <el-form-item label="出荷予定時間"><el-input v-model="store.bp.plannedShipTime" placeholder="HH:MM" :disabled="!store.canEdit" style="width: 100px" /></el-form-item>
  </el-form>
</template>

<script setup lang="ts">
import { useBpStore } from '@/stores/businessPartner'
const props = defineProps<{ store: ReturnType<typeof useBpStore> }>()
const store = props.store
</script>
<style scoped>:deep(.el-form-item){margin-bottom:8px;}</style>

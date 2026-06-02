<template>
  <el-form :model="store.bp" label-width="140px" size="small" :disabled="store.isPageReadOnly" inline>
    <el-form-item :label="t('支払予定管理先')" required>
      <el-input v-model="store.bp.paymentScheduleCd" :disabled="!store.canEdit" style="width: 200px" />
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

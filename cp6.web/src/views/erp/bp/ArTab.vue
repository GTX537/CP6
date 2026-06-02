<template>
  <el-form :model="store.bp" label-width="120px" size="small" :disabled="store.isPageReadOnly" inline>
    <el-form-item :label="t('請求先')" required>
      <el-input v-model="store.bp.billingCd" :disabled="!store.canEdit" style="width: 200px" />
    </el-form-item>
    <el-form-item :label="t('請求先名')">
      <el-input v-model="store.bp.billingName" :disabled="!store.canEdit" style="width: 240px" />
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

<template>
  <el-form :model="store.bp" label-width="120px" size="small" :disabled="store.isPageReadOnly" inline>
    <el-alert type="info" :closable="false" show-icon>{{ t('支払先 Tab：取引先 CD（自身）が支払先として機能。詳細フィールドは買掛先 / 支払予定管理先 Tab に集約。') }}</el-alert>
  </el-form>
</template>

<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { useBpStore } from '@/stores/businessPartner'
const { t } = useI18n()
const props = defineProps<{ store: ReturnType<typeof useBpStore> }>()
const store = props.store
</script>

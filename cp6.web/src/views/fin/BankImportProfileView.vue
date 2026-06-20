<template>
  <div class="bank-import-profile">
    <el-empty :description="t('bankrecon.profile')" />
  </div>
</template>

<script setup lang="ts">
import { useI18n } from 'vue-i18n'
const { t } = useI18n()
</script>

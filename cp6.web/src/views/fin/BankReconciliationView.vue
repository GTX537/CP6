<template>
  <div class="bank-recon-workbench">
    <el-empty :description="t('bankrecon.workbench')" />
  </div>
</template>

<script setup lang="ts">
import { useI18n } from 'vue-i18n'
const { t } = useI18n()
</script>

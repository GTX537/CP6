<template>
  <div>
    <VolTable :columns="columns" :api="articleApi" />
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import VolTable from '@/components/VolTable.vue'
import type { ColumnConfig } from '@/components/VolTable.vue'
import { articleApi } from '@/api/article'

const { t } = useI18n()

const columns = computed<ColumnConfig[]>(() => [
  { prop: 'title', label: t('article.title'), required: true },
  { prop: 'author', label: t('article.author'), width: 120 },
  { prop: 'content', label: t('article.content'), formType: 'textarea' },
  { prop: 'isPublished', label: t('article.isPublished'), width: 100, type: 'switch', formType: 'switch' },
  { prop: 'createDate', label: t('article.createDate'), width: 180, formType: 'none' }
])
</script>

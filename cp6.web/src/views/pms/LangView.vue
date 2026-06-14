<template>
  <div>
    <!-- i18n 优化 P4：发布模式管理条（实时模式下发布为可选；点发布=导出版本化静态包） -->
    <div class="lang-toolbar">
      <span class="lang-ver">{{ t('lang.currentVersion') }}：{{ currentVersion || t('lang.notPublished') }}</span>
      <el-button size="small" :loading="reviewing" @click="onReviewDrafts">{{ t('lang.reviewDrafts') }}</el-button>
      <el-button type="primary" size="small" :loading="publishing" @click="onPublish">{{ t('lang.publish') }}</el-button>
    </div>
    <VolTable :columns="columns" :api="langApi" />
  </div>
</template>

<script setup lang="ts">
import { computed, ref, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import VolTable from '@/components/VolTable.vue'
import type { ColumnConfig } from '@/components/VolTable.vue'
import { langApi } from '@/api/sys/lang'

const { t } = useI18n()

const columns = computed<ColumnConfig[]>(() => [
  { prop: 'langKey', label: t('lang.langKey'), required: true },
  { prop: 'zhCN', label: t('lang.zhCN') },
  { prop: 'zhTW', label: t('lang.zhTW'), tableHidden: true },
  { prop: 'en', label: t('lang.en') },
  { prop: 'ja', label: t('lang.ja'), tableHidden: true },
  { prop: 'ko', label: t('lang.ko'), tableHidden: true },
  { prop: 'status', label: t('lang.status') },
])

const currentVersion = ref('')
const publishing = ref(false)
const reviewing = ref(false)

async function onReviewDrafts() {
  reviewing.value = true
  try {
    const res: any = await langApi.review([])
    ElMessage.success(t('lang.reviewDone', { n: res?.data?.count ?? 0 }))
  } catch {
    ElMessage.error(t('lang.reviewFailed'))
  } finally {
    reviewing.value = false
  }
}

async function loadVersion() {
  try {
    const m: any = await langApi.manifest()
    currentVersion.value = m?.version || ''
  } catch {
    /* 忽略 */
  }
}

async function onPublish() {
  publishing.value = true
  try {
    const res: any = await langApi.publish()
    currentVersion.value = res?.data?.version || currentVersion.value
    ElMessage.success(t('lang.publishSuccess'))
  } catch {
    ElMessage.error(t('lang.publishFailed'))
  } finally {
    publishing.value = false
  }
}

onMounted(loadVersion)
</script>

<style scoped>
.lang-toolbar {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 8px;
}
.lang-ver {
  color: var(--el-text-color-secondary);
  font-size: 13px;
}
</style>

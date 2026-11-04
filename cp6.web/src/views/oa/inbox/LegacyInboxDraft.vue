<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'
import { legacyPersonalDrafts, type LegacyPersonalDraft } from '@/api/oa/legacyPersonalDrafts'

const rows = ref<LegacyPersonalDraft[]>([]), total = ref(0), page = ref(1), loading = ref(false), error = ref('')
let generation = 0, disposed = false, request: AbortController | undefined
async function load() {
  request?.abort(); const controller = new AbortController(); request = controller; const current = ++generation
  loading.value = true; error.value = ''; rows.value = []; total.value = 0
  try {
    const value = await legacyPersonalDrafts(page.value, controller.signal)
    if (disposed || current !== generation) return
    rows.value = value.items; total.value = value.total
  } catch {
    if (disposed || current !== generation) return
    error.value = '历史草稿读取失败，请重试。'
  } finally { if (!disposed && current === generation) loading.value = false }
}
function state(value: number) { return value === 0 ? '未提交（历史）' : value === 1 ? '已提交（历史）' : `历史状态 ${value}` }
onMounted(load)
onBeforeUnmount(() => { disposed = true; generation++; request?.abort(); rows.value = [] })
</script>

<template>
  <section class="legacy-drafts" data-test="legacy-drafts">
    <p>以下为保留的历史草稿记录，不能从此处修改、提交或删除。历史内容的读取授权尚待确认，原内容仍保留。</p>
    <router-link to="/oa/form-catalog">从当前表单目录发起</router-link>
    <el-button :loading="loading" @click="load">刷新</el-button>
    <el-alert v-if="error" :title="error" type="error" :closable="false" />
    <el-table v-else :data="rows" v-loading="loading" row-key="id">
      <el-table-column prop="title" label="标题" min-width="160" />
      <el-table-column prop="formKey" label="原表单" min-width="120" />
      <el-table-column label="记录版本" width="110"><template #default="{ row }">{{ row.recordedVersion ?? '无完整快照' }}</template></el-table-column>
      <el-table-column label="状态" width="150"><template #default="{ row }">{{ state(row.status) }}</template></el-table-column>
      <el-table-column label="来源" min-width="160"><template #default="{ row }">{{ row.source === 'LEGACY_VERSION_SNAPSHOT' ? '历史版本快照' : row.source === 'LEGACY_HEAD_BACKFILL' ? '历史补录，发布未核实' : '缺少版本快照' }}</template></el-table-column>
      <el-table-column prop="recordedUpdatedAt" label="原记录时间" min-width="190" />
    </el-table>
    <el-pagination v-if="!error && total > 20" v-model:current-page="page" :total="total" :page-size="20" layout="prev, pager, next" @current-change="load" />
  </section>
</template>

<style scoped>
.legacy-drafts { padding: 12px 0; } p { max-width: 850px; line-height: 1.7; } a { margin-right: 16px; }
</style>

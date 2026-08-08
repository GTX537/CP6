<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
import { Connection, Flag, Rank, Warning } from '@element-plus/icons-vue'

import { userApi } from '@/api/sys/user'
import { isFallbackEdge, type SchemaEdge } from './designerModel'

const props = defineProps<{ edge: SchemaEdge }>()
const emit = defineEmits<{ update: [patch: Partial<SchemaEdge>] }>()

function cloneEdge(edge: SchemaEdge): SchemaEdge {
  return { ...edge, ccUsers: edge.ccUsers ? [...edge.ccUsers] : [] }
}

const syncing = ref(false)
const local = ref<SchemaEdge>(cloneEdge(props.edge))
const conditionType = ref<'none' | 'condition'>(props.edge.condition ? 'condition' : 'none')
const isFallback = computed(() => isFallbackEdge(local.value))

watch(
  () => props.edge,
  async edge => {
    syncing.value = true
    local.value = cloneEdge(edge)
    conditionType.value = edge.condition ? 'condition' : 'none'
    await nextTick()
    syncing.value = false
  },
  { deep: true },
)

watch(conditionType, type => {
  if (type === 'none') local.value = { ...local.value, condition: undefined, isError: false }
  else if (!local.value.condition) local.value = { ...local.value, condition: 'false', isError: false }
})

watch(
  () => local.value.isError,
  enabled => {
    if (!enabled) return
    conditionType.value = 'none'
    local.value = { ...local.value, condition: undefined }
  },
)

watch(
  local,
  () => {
    if (syncing.value) return
    emit('update', { ...local.value, ccUsers: [...(local.value.ccUsers ?? [])] })
  },
  { deep: true },
)

interface UserOption { label: string; value: string }
const ccUserOptions = ref<UserOption[]>([])
const ccUserSearchLoading = ref(false)

async function searchCcUsers(keyword: string) {
  if (!keyword) {
    ccUserOptions.value = []
    return
  }
  ccUserSearchLoading.value = true
  try {
    const response = await userApi.getList({ page: 1, pageSize: 20, keyword }) as any
    ccUserOptions.value = (response.rows ?? []).map((user: any) => ({
      label: user.nickName || user.userName,
      value: String(user.id),
    }))
  } catch {
    // HTTP interceptor displays the request error.
  } finally {
    ccUserSearchLoading.value = false
  }
}
</script>

<template>
  <div class="edge-prop-panel">
    <section class="route-summary" :class="{ fallback: isFallback, error: local.isError }">
      <span><el-icon><Warning v-if="local.isError" /><Flag v-else-if="isFallback" /><Connection v-else /></el-icon></span>
      <div>
        <strong>{{ local.isError ? '失败路径' : (isFallback ? '无条件兜底路径' : '条件路径') }}</strong>
        <small>{{ local.from }} → {{ local.to }}</small>
      </div>
    </section>

    <el-form label-position="top" class="prop-form">
      <div class="section-title"><span><el-icon><Connection /></el-icon></span><div><strong>基础信息</strong><small>路径名称与画布连接方向</small></div></div>
      <el-form-item label="路径名称">
        <el-input v-model="local.name" clearable placeholder="例如：金额超过 10 万" />
      </el-form-item>
      <div class="route-metadata">
        <span><small>优先级</small><strong><el-icon><Rank /></el-icon>{{ local.priority ?? '—' }}</strong></span>
        <span><small>起点</small><strong>{{ local.sourceHandle || '自动' }}</strong></span>
        <span><small>终点</small><strong>{{ local.targetHandle || '自动' }}</strong></span>
      </div>

      <div class="section-title"><span class="amber"><el-icon><Flag /></el-icon></span><div><strong>匹配规则</strong><small>从上到下判断，无条件路径最后兜底</small></div></div>
      <el-form-item v-if="!local.isError" label="路径类型">
        <el-radio-group v-model="conditionType" class="route-type">
          <el-radio-button value="condition">条件路径</el-radio-button>
          <el-radio-button value="none">无条件兜底</el-radio-button>
        </el-radio-group>
      </el-form-item>
      <el-form-item v-if="conditionType === 'condition' && !local.isError" label="条件表达式">
        <el-input v-model="local.condition" type="textarea" :rows="3" placeholder="例如：amount >= 100000" />
      </el-form-item>
      <div v-if="isFallback && !local.isError" class="fallback-note">
        <el-icon><Flag /></el-icon><span>此路径不设置条件，运行时仅在前面的条件均未命中后执行。</span>
      </div>

      <div class="section-title"><span class="red"><el-icon><Warning /></el-icon></span><div><strong>异常与知会</strong><small>服务失败分流及路径抄送</small></div></div>
      <el-checkbox v-model="local.isError" class="error-toggle">服务任务失败时走此路径</el-checkbox>
      <p class="field-hint">失败路径不参与普通条件优先级判断，每个来源节点最多一条。</p>
      <el-form-item label="知会人员">
        <el-select
          v-model="local.ccUsers"
          style="width: 100%"
          multiple
          filterable
          remote
          :remote-method="searchCcUsers"
          :loading="ccUserSearchLoading"
          placeholder="搜索并选择人员"
          clearable
        >
          <el-option v-for="user in ccUserOptions" :key="user.value" :label="user.label" :value="user.value" />
        </el-select>
      </el-form-item>
    </el-form>
  </div>
</template>

<style scoped>
.edge-prop-panel { height: 100%; padding: 10px; overflow-y: auto; box-sizing: border-box; }
.route-summary { min-height: 58px; padding: 8px 10px; display: grid; grid-template-columns: 34px minmax(0, 1fr); align-items: center; gap: 9px; border: 1px solid #d8e3e5; border-radius: 6px; background: #f7fafb; }
.route-summary > span { width: 34px; height: 34px; display: grid; place-items: center; border-radius: 5px; background: #e6f1f2; color: #4c7c83; }
.route-summary strong, .route-summary small { display: block; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.route-summary strong { color: #3b555c; font-size: 11px; }
.route-summary small { margin-top: 4px; color: #819399; font-size: 9px; }
.route-summary.fallback { border-color: #cce2d8; background: #f3faf6; }
.route-summary.fallback > span { background: #dff1e8; color: #27815f; }
.route-summary.error { border-color: #ead0ce; background: #fff7f6; }
.route-summary.error > span { background: #f8e5e3; color: #c75a54; }
.prop-form { padding-top: 4px; }
.section-title { margin: 14px -10px 10px; padding: 10px; display: grid; grid-template-columns: 30px minmax(0, 1fr); align-items: center; gap: 8px; border-top: 1px solid var(--cp-line); border-bottom: 1px solid #edf1f2; background: #fbfcfc; }
.section-title > span { width: 30px; height: 30px; display: grid; place-items: center; border-radius: 5px; background: var(--cp-brand-bg); color: var(--cp-brand); }
.section-title > span.amber { background: #fff1dc; color: #b87618; }
.section-title > span.red { background: #fae9e7; color: #be5650; }
.section-title strong, .section-title small { display: block; }
.section-title strong { color: var(--cp-ink); font-size: 11px; }
.section-title small { margin-top: 2px; color: var(--cp-muted); font-size: 9px; }
.prop-form :deep(.el-form-item) { margin-bottom: 11px; }
.prop-form :deep(.el-form-item__label) { padding-bottom: 4px; color: var(--cp-text); font-size: 10px; line-height: 1.3; }
.route-metadata { display: grid; grid-template-columns: repeat(3, 1fr); overflow: hidden; border: 1px solid var(--cp-line); border-radius: 5px; }
.route-metadata > span { min-width: 0; padding: 7px 8px; border-right: 1px solid var(--cp-line); }
.route-metadata > span:last-child { border-right: 0; }
.route-metadata small, .route-metadata strong { display: block; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.route-metadata small { color: var(--cp-muted); font-size: 8px; }
.route-metadata strong { margin-top: 4px; color: #4a666d; font-size: 10px; }
.route-metadata .el-icon { margin-right: 3px; vertical-align: -2px; }
.route-type { display: flex; width: 100%; }
.route-type :deep(.el-radio-button) { flex: 1; }
.route-type :deep(.el-radio-button__inner) { width: 100%; }
.fallback-note { margin: -2px 0 8px; padding: 8px; display: flex; gap: 7px; border: 1px solid #d4e7dd; border-radius: 5px; background: #f3faf6; color: #3b7d65; font-size: 9px; line-height: 1.5; }
.fallback-note .el-icon { margin-top: 2px; flex-shrink: 0; }
.error-toggle { color: var(--cp-text); }
.field-hint { margin: 3px 0 12px; color: var(--cp-muted); font-size: 9px; line-height: 1.45; }
</style>

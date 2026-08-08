<template>
  <div class="inbox-draft">
    <div class="table-toolbar">
      <CpTag>{{ t('共 {n} 条', { n: total }) }}</CpTag>
      <el-button :icon="Refresh" circle size="small" :loading="loading" @click="load" />
    </div>

    <el-table :data="rows" border stripe size="small" max-height="560" v-loading="loading">
      <el-table-column prop="title" :label="t('标题')" min-width="150" />
      <el-table-column prop="formName" :label="t('表单')" min-width="150" />
      <el-table-column :label="t('版本')" width="90">
        <template #default="{ row }">v{{ row.formVersion }}</template>
      </el-table-column>
      <el-table-column :label="t('状态')" width="100">
        <template #default="{ row }">
          <CpTag :tone="row.stale ? 'warn' : 'info'">{{ row.stale ? t('需升级') : t('可提交') }}</CpTag>
        </template>
      </el-table-column>
      <el-table-column :label="t('更新时间')" width="170">
        <template #default="{ row }">{{ formatTime(row.updatedAtUtc) }}</template>
      </el-table-column>
      <el-table-column :label="t('oa.col.actions')" width="220" fixed="right">
        <template #default="{ row }">
          <el-button v-permission="'oa-form-catalog:edit'" type="primary" link size="small" @click.stop="openEdit(row)">
            {{ t('oa.draft.edit') }}
          </el-button>
          <el-button v-permission="'oa-form-catalog:submit'" type="success" link size="small"
            :disabled="row.stale" :loading="row._submitting" @click.stop="submitDraft(row)">
            {{ t('oa.draft.submit') }}
          </el-button>
          <el-button v-permission="'oa-form-catalog:del'" type="danger" link size="small" @click.stop="removeDraft(row)">
            {{ t('oa.draft.delete') }}
          </el-button>
        </template>
      </el-table-column>
    </el-table>
    <CpEmpty v-if="!loading && !rows.length" :text="t('oa.draft.empty')" />

    <el-dialog v-model="editor.visible" :title="editor.title || t('oa.draft.editTitle')" width="680px">
      <el-alert v-if="editor.stale" type="warning" :closable="false" show-icon
        :title="t('表单已有新版本，请升级后提交')" class="stale-alert" />
      <el-input v-model="editor.title" :placeholder="t('标题')" maxlength="200" class="title-input" />
      <DynamicForm v-if="editor.schema.fields.length" :schema="editor.schema" v-model="editor.data" />
      <CpEmpty v-else :text="t('oa.initiate.noFields')" />
      <template #footer>
        <el-button v-if="editor.stale" :loading="editor.rebasing" @click="rebaseDraft(false)">
          {{ t('升级草稿') }}
        </el-button>
        <el-button @click="editor.visible = false">{{ t('oa.draft.cancel') }}</el-button>
        <el-button type="primary" :loading="editor.saving" @click="saveEdit">{{ t('oa.draft.save') }}</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { reactive, ref, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Refresh } from '@element-plus/icons-vue'
import { draftApi, type DraftDetail, type DraftListItem } from '@/api/oa/draft'
import type { FormSchema } from '@/types/wf/wf'
import DynamicForm from '@/views/wf/DynamicForm.vue'
import CpTag from '@/components/base/CpTag.vue'
import CpEmpty from '@/components/base/CpEmpty.vue'

type Row = DraftListItem & { _submitting?: boolean }
const { t } = useI18n()
const rows = ref<Row[]>([])
const total = ref(0)
const loading = ref(false)
const editor = reactive({
  visible: false, id: '', title: '', stale: false, rowVersion: undefined as string | undefined,
  latestVersion: 0,
  schema: { fields: [] } as FormSchema, data: {} as Record<string, unknown>,
  saving: false, rebasing: false,
})

function payload<T>(response: any): T {
  return (response?.data ?? response) as T
}

async function load() {
  loading.value = true
  try {
    const page = payload<{ items: DraftListItem[]; total: number }>(await draftApi.list())
    rows.value = page.items ?? []
    total.value = page.total ?? rows.value.length
  } finally { loading.value = false }
}

async function openEdit(row: Row) {
  const detail = payload<DraftDetail>(await draftApi.get(row.id))
  editor.id = detail.id
  editor.title = detail.title ?? ''
  editor.stale = detail.stale
  editor.rowVersion = detail.rowVersion
  editor.latestVersion = detail.latestPublishedVersion
  editor.schema = JSON.parse(detail.schemaJson) as FormSchema
  editor.data = JSON.parse(detail.dataJson) as Record<string, unknown>
  editor.visible = true
}

async function saveEdit() {
  editor.saving = true
  try {
    const detail = payload<DraftDetail>(await draftApi.update(
      editor.id, editor.data, editor.title || undefined, editor.rowVersion))
    editor.rowVersion = detail.rowVersion
    editor.stale = detail.stale
    ElMessage.success(t('已保存'))
    await load()
  } catch (error: any) {
    if (error?.response?.data?.code === 'E-WF-041') ElMessage.error(t('草稿已被更新'))
  } finally { editor.saving = false }
}

async function rebaseDraft(confirmRemovedValues: boolean) {
  editor.rebasing = true
  try {
    const result = payload<{ dataJson: string; rowVersion?: string }>(
      await draftApi.rebase(editor.id, editor.latestVersion, confirmRemovedValues, editor.rowVersion))
    editor.data = JSON.parse(result.dataJson)
    editor.rowVersion = result.rowVersion
    editor.stale = false
    await openEdit(rows.value.find(x => x.id === editor.id)!)
    await load()
  } catch (error: any) {
    if (error?.response?.data?.code === 'E-WF-048' && !confirmRemovedValues) {
      const fields = error.response.data.removedFields?.join(', ') ?? ''
      await ElMessageBox.confirm(t('升级将删除已有字段值：{fields}。是否继续？', { fields }), t('升级草稿'), { type: 'warning' })
      await rebaseDraft(true)
    } else if (error?.response?.data?.code === 'E-WF-041') {
      ElMessage.error(t('草稿已被更新'))
    }
  } finally { editor.rebasing = false }
}

async function submitDraft(row: Row) {
  row._submitting = true
  try {
    const key = globalThis.crypto?.randomUUID?.() ?? `${Date.now()}-${Math.random().toString(16).slice(2)}`
    await draftApi.submit(row.id, row.rowVersion, key)
    ElMessage.success(t('已提交'))
    await load()
  } finally { row._submitting = false }
}

async function removeDraft(row: Row) {
  try { await ElMessageBox.confirm(t('确认删除该暂存草稿？'), t('删除'), { type: 'warning' }) }
  catch { return }
  await draftApi.remove(row.id)
  ElMessage.success(t('已删除'))
  await load()
}

function formatTime(value: string) {
  return value ? value.replace('T', ' ').slice(0, 19) : ''
}

onMounted(load)
</script>

<style scoped>
.table-toolbar { display: flex; align-items: center; gap: 10px; margin-bottom: 8px; }
.stale-alert { margin-bottom: 12px; }
.title-input { margin-bottom: 12px; }
</style>

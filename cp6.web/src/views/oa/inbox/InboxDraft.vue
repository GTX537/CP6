<template>
  <div class="inbox-draft">
    <div class="table-toolbar">
      <CpTag>{{ t('共 {n} 条', { n: rows.length }) }}</CpTag>
      <el-button :icon="Refresh" circle size="small" :loading="loading" @click="load" />
    </div>

    <el-table :data="rows" border stripe size="small" max-height="560" v-loading="loading">
      <el-table-column prop="flowName" :label="t('oa.col.flowName')" min-width="160" />
      <el-table-column :label="t('oa.col.createDate')" width="170">
        <template #default="{ row }">{{ formatTime(row.createDate) }}</template>
      </el-table-column>
      <el-table-column :label="t('oa.col.actions')" width="200" fixed="right">
        <template #default="{ row }">
          <el-button type="primary" link size="small" @click.stop="openEdit(row)">
            {{ t('oa.draft.edit') }}
          </el-button>
          <el-button type="success" link size="small" :loading="row._submitting" @click.stop="submitDraft(row)">
            {{ t('oa.draft.submit') }}
          </el-button>
          <el-button type="danger" link size="small" @click.stop="removeDraft(row)">
            {{ t('oa.draft.delete') }}
          </el-button>
        </template>
      </el-table-column>
    </el-table>
    <CpEmpty v-if="!loading && !rows.length" :text="t('oa.draft.empty')" />

    <!-- Edit dialog -->
    <el-dialog v-model="editDialog.visible" :title="t('oa.draft.editTitle')" width="520px">
      <el-input
        v-model="editDialog.varsJson"
        type="textarea"
        :rows="8"
        :placeholder="t('oa.draft.varsHint')"
        maxlength="10000"
        show-word-limit
      />
      <template #footer>
        <el-button @click="editDialog.visible = false">{{ t('oa.draft.cancel') }}</el-button>
        <el-button type="primary" :loading="editDialog.saving" @click="saveEdit">
          {{ t('oa.draft.save') }}
        </el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Refresh } from '@element-plus/icons-vue'
import { draftApi } from '@/api/oa/draft'
import CpTag from '@/components/base/CpTag.vue'
import CpEmpty from '@/components/base/CpEmpty.vue'

const { t } = useI18n()

interface DraftItem {
  instanceId: string
  flowKey: string
  flowName?: string
  createDate: string
  varsJson?: string
  _submitting?: boolean
}

const rows = ref<DraftItem[]>([])
const loading = ref(false)

const editDialog = reactive({
  visible: false,
  instanceId: '',
  varsJson: '',
  saving: false,
})

async function load() {
  loading.value = true
  try {
    const res = await draftApi.list()
    rows.value = ((res as any).data as DraftItem[]) || []
  } finally {
    loading.value = false
  }
}

function openEdit(row: DraftItem) {
  editDialog.instanceId = row.instanceId
  editDialog.varsJson = row.varsJson ?? ''
  editDialog.visible = true
}

async function saveEdit() {
  editDialog.saving = true
  try {
    await draftApi.update(editDialog.instanceId, editDialog.varsJson)
    ElMessage.success('已保存')
    editDialog.visible = false
    await load()
  } finally {
    editDialog.saving = false
  }
}

async function submitDraft(row: DraftItem) {
  row._submitting = true
  try {
    await draftApi.submit(row.instanceId)
    ElMessage.success('已提交')
    await load()
  } finally {
    row._submitting = false
  }
}

async function removeDraft(row: DraftItem) {
  try {
    await ElMessageBox.confirm('确认删除该暂存草稿？', '删除', { type: 'warning' })
  } catch {
    return
  }
  await draftApi.remove(row.instanceId)
  ElMessage.success('已删除')
  await load()
}

function formatTime(s: string): string {
  return s ? s.replace('T', ' ').slice(0, 19) : ''
}

onMounted(load)
</script>

<style scoped>
.inbox-draft {
  padding: 0;
}
.table-toolbar {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-bottom: 8px;
}
</style>

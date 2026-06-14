<template>
  <div class="todo-center">
    <div class="page-header"><h2>{{ t('待办中心') }}</h2></div>

    <el-card shadow="never" class="table-card">
      <div class="table-toolbar">
        <el-tag size="small">{{ t('共 {n} 条待办', { n: rows.length }) }}</el-tag>
        <el-button :icon="Refresh" circle size="small" :loading="loading" @click="load" />
      </div>

      <el-table :data="rows" border stripe size="small" max-height="620" v-loading="loading">
        <el-table-column prop="flowKey" :label="t('流程')" width="160" />
        <el-table-column prop="nodeId" :label="t('当前节点')" width="140" />
        <el-table-column :label="t('发起人')" min-width="180" show-overflow-tooltip>
          <template #default="{ row }">{{ row.starterId }}</template>
        </el-table-column>
        <el-table-column :label="t('提交时间')" width="170">
          <template #default="{ row }">{{ formatTime(row.createDate) }}</template>
        </el-table-column>
        <el-table-column :label="t('操作')" width="120" fixed="right">
          <template #default="{ row }">
            <el-button type="primary" link size="small" @click="openTodo(row)">{{ t('办理') }}</el-button>
          </template>
        </el-table-column>
      </el-table>
      <el-empty v-if="!rows.length && !loading" :description="t('暂无待办')" :image-size="80" />
    </el-card>

    <el-dialog v-model="dialog.visible" :title="t('办理待办')" width="560px">
      <div v-loading="dialog.loading">
        <DynamicForm
          v-if="dialog.schema"
          :schema="dialog.schema"
          v-model="dialog.model"
          :mask="dialog.mask"
        />
        <el-divider>{{ t('处理意见') }}</el-divider>
        <el-input v-model="dialog.comment" type="textarea" :rows="3" maxlength="500" show-word-limit :placeholder="t('可填写审批意见')" />
      </div>
      <template #footer>
        <el-button @click="dialog.visible = false">{{ t('取消') }}</el-button>
        <el-button type="danger" :loading="dialog.acting" @click="act(false)">{{ t('驳回') }}</el-button>
        <el-button type="primary" :loading="dialog.acting" @click="act(true)">{{ t('同意') }}</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import { Refresh } from '@element-plus/icons-vue'
import DynamicForm from './DynamicForm.vue'
import { buildFieldMask, safeParseObject } from './fieldMask'
import { flowApi } from '@/api/wf/flow'
import { formApi } from '@/api/wf/form'
import type { FieldMask, FormSchema, TodoItem } from '@/types/wf/wf'

const { t } = useI18n()

const rows = ref<TodoItem[]>([])
const loading = ref(false)

const dialog = reactive<{
  visible: boolean
  loading: boolean
  acting: boolean
  taskId: string
  schema: FormSchema | null
  model: Record<string, any>
  mask: FieldMask
  comment: string
}>({
  visible: false,
  loading: false,
  acting: false,
  taskId: '',
  schema: null,
  model: {},
  mask: {},
  comment: '',
})

async function load() {
  loading.value = true
  try {
    const res = await flowApi.myTodos()
    rows.value = res.data || []
  } finally {
    loading.value = false
  }
}

async function openTodo(row: TodoItem) {
  dialog.visible = true
  dialog.loading = true
  dialog.taskId = row.taskId
  dialog.comment = ''
  dialog.schema = null
  dialog.model = {}
  dialog.mask = {}
  try {
    // 1) 实例（取 varsJson 当前值）
    const instRes = await flowApi.instance(row.instanceId)
    const instance = instRes.data?.instance
    dialog.model = safeParseObject(instance?.varsJson)

    // 2) 流程定义（取当前节点 fieldPerms）
    const flowRes = await flowApi.getDef(row.flowKey)
    const flowDef = flowRes.data
    const flowSchema = safeParseObject(flowDef?.schemaJson)
    const node = (flowSchema.nodes || []).find((n: any) => n.id === row.nodeId)

    // 3) 表单定义（取字段）
    if (flowDef?.formKey) {
      const formRes = await formApi.getDef(flowDef.formKey)
      const formSchema = safeParseObject(formRes.data?.schemaJson) as FormSchema
      dialog.schema = formSchema?.fields ? formSchema : { fields: [] }
      const names = (dialog.schema.fields || []).map((f) => f.name)
      dialog.mask = buildFieldMask(names, node?.fieldPerms, false) // 审批节点默认 readonly
    }
  } finally {
    dialog.loading = false
  }
}

async function act(approve: boolean) {
  dialog.acting = true
  try {
    await flowApi.act(dialog.taskId, { approve, comment: dialog.comment })
    ElMessage.success(approve ? t('已同意') : t('已驳回'))
    dialog.visible = false
    await load()
  } finally {
    dialog.acting = false
  }
}

function formatTime(t: string): string {
  return t ? t.replace('T', ' ').slice(0, 19) : ''
}

onMounted(load)
</script>

<style scoped>
.todo-center {
  padding: 16px;
}
.page-header {
  margin-bottom: 12px;
}
.page-header h2 {
  margin: 0;
  font-size: 20px;
  font-weight: 650;
  color: #303133;
}
.table-toolbar {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-bottom: 8px;
}
</style>

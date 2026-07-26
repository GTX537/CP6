<template>
  <div class="form-designer">
    <!-- 工具栏 -->
    <el-card shadow="never" class="toolbar">
      <el-space wrap>
        <el-input v-model="formKey" :placeholder="t('表单标识')" style="width: 160px" size="small" />
        <el-input v-model="formName" :placeholder="t('表单名称')" style="width: 160px" size="small" />
        <el-button size="small" @click="load">{{ t('加载') }}</el-button>
        <el-button size="small" :disabled="!canUndo" @click="undo">{{ t('撤销') }}</el-button>
        <el-button size="small" :disabled="!canRedo" @click="redo">{{ t('重做') }}</el-button>
        <el-button v-permission="'oa-designer:form-save'" size="small" @click="save">{{ t('保存草稿') }}</el-button>
        <el-button v-permission="'oa-designer:form-publish'" type="primary" size="small" @click="publish">{{ t('发布') }}</el-button>
      </el-space>
    </el-card>

    <div class="panes">
      <!-- 左：控件库 -->
      <el-card shadow="never" class="pane lib">
        <template #header><span class="pane-title">{{ t('控件库') }}</span></template>
        <div v-for="c in CONTROL_LIBRARY" :key="c.type" class="lib-item" @click="addField(c.type)">
          <el-icon><component :is="c.icon" /></el-icon>
          <span>{{ t(c.label) }}</span>
        </div>
      </el-card>

      <!-- 中：画布（字段列表 + 实时预览） -->
      <el-card shadow="never" class="pane canvas">
        <template #header><span class="pane-title">{{ t('画布') }}</span></template>
        <el-empty v-if="!schema.fields.length" :description="t('暂无字段，点击左侧控件添加')" :image-size="60" />
        <div
          v-for="(f, i) in schema.fields"
          :key="f.name"
          class="field-row"
          :class="{ active: i === selectedIdx }"
          @click="selectedIdx = i"
        >
          <div class="field-meta">
            <span class="field-label">{{ f.label || f.name }}</span>
            <el-tag size="small" type="info">{{ f.type }}</el-tag>
            <el-tag v-if="f.required" size="small" type="danger">{{ t('必填') }}</el-tag>
          </div>
          <div class="field-ops" @click.stop>
            <el-button link size="small" :disabled="i === 0" @click="move(i, -1)">{{ t('上移') }}</el-button>
            <el-button link size="small" :disabled="i === schema.fields.length - 1" @click="move(i, 1)">{{ t('下移') }}</el-button>
            <el-button link size="small" type="danger" @click="remove(i)">{{ t('删除') }}</el-button>
          </div>
        </div>
        <el-divider v-if="schema.fields.length">{{ t('预览') }}</el-divider>
        <DynamicForm v-if="schema.fields.length" :schema="schema" v-model="previewModel" />
      </el-card>

      <!-- 右：属性面板 -->
      <el-card shadow="never" class="pane prop">
        <template #header><span class="pane-title">{{ t('属性') }}</span></template>
        <el-empty v-if="!selected" :description="t('请先选择字段')" :image-size="60" />
        <el-form v-else label-width="84px" size="small">
          <el-form-item :label="t('字段标识')">
            <el-input v-model="selected.name" />
          </el-form-item>
          <el-form-item :label="t('标签')">
            <el-input v-model="selected.label" />
          </el-form-item>
          <el-form-item :label="t('类型')">
            <el-select v-model="selected.type" style="width: 100%" @change="onFieldTypeChange">
              <el-option v-for="c in CONTROL_LIBRARY" :key="c.type" :label="t(c.label)" :value="c.type" />
            </el-select>
          </el-form-item>
          <el-form-item :label="t('必填')">
            <el-switch v-model="selected.required" />
          </el-form-item>
          <template v-if="selected.type !== 'table'">
            <el-form-item :label="t('最大长度')">
              <el-input-number v-model="selected.maxLength" :min="1" :max="10000" :controls="false" style="width: 100%" />
            </el-form-item>
            <el-form-item :label="t('占位提示')">
              <el-input v-model="selected.placeholder" />
            </el-form-item>
            <el-form-item :label="t('校验正则')">
              <el-input v-model="selected.pattern" />
            </el-form-item>
          </template>
          <template v-if="isOptionType(selected.type)">
            <el-divider>{{ t('选项') }}</el-divider>
            <div v-for="(o, oi) in selected.options || []" :key="oi" class="opt-row">
              <el-input v-model="o.label" :placeholder="t('标签')" size="small" style="width: 96px" />
              <el-input v-model="o.value" :placeholder="t('值')" size="small" style="width: 96px" />
              <el-button link type="danger" size="small" @click="removeOption(oi)">{{ t('删除') }}</el-button>
            </div>
            <el-button link size="small" @click="addOption">{{ t('添加选项') }}</el-button>
          </template>
          <template v-if="selected.type === 'table'">
            <el-form-item :label="t('最少行数')">
              <el-input-number v-model="selected.minRows" :min="0" :max="200" :controls="false" style="width: 100%" />
            </el-form-item>
            <el-form-item :label="t('最多行数')">
              <el-input-number v-model="selected.maxRows" :min="1" :max="200" :controls="false" style="width: 100%" />
            </el-form-item>
            <el-divider>{{ t('子表列') }}</el-divider>
            <div
              v-for="(column, ci) in selected.columns || []"
              :key="ci"
              class="column-card"
              :data-test="`table-column-${ci}`"
            >
              <div class="column-card__header">
                <span>{{ t('列') }} {{ ci + 1 }}</span>
                <el-button link type="danger" size="small" @click="removeTableColumn(ci)">{{ t('删除') }}</el-button>
              </div>
              <el-input v-model="column.name" :placeholder="t('列标识')" size="small" />
              <el-input v-model="column.label" :placeholder="t('列标题')" size="small" />
              <el-select
                v-model="column.type"
                :placeholder="t('列类型')"
                size="small"
                style="width: 100%"
                @change="onColumnTypeChange(column)"
              >
                <el-option
                  v-for="type in TABLE_COLUMN_TYPES"
                  :key="type.type"
                  :label="t(type.label)"
                  :value="type.type"
                />
              </el-select>
              <div class="column-required">
                <span>{{ t('必填') }}</span>
                <el-switch v-model="column.required" />
              </div>
              <template v-if="column.type === 'input' || column.type === 'textarea'">
                <el-input-number
                  v-model="column.maxLength"
                  :min="1"
                  :max="10000"
                  :controls="false"
                  :placeholder="t('最大长度')"
                  style="width: 100%"
                />
                <el-input v-model="column.pattern" :placeholder="t('校验正则')" size="small" />
              </template>
              <template v-if="column.type === 'select'">
                <div v-for="(option, oi) in column.options || []" :key="oi" class="opt-row">
                  <el-input v-model="option.label" :placeholder="t('标签')" size="small" />
                  <el-input v-model="option.value" :placeholder="t('值')" size="small" />
                  <el-button link type="danger" size="small" @click="removeColumnOption(column, oi)">
                    {{ t('删除') }}
                  </el-button>
                </div>
                <el-button link size="small" @click="addColumnOption(column)">{{ t('添加选项') }}</el-button>
              </template>
            </div>
            <el-button link size="small" data-test="add-table-column" @click="addTableColumn">
              {{ t('添加列') }}
            </el-button>
          </template>
        </el-form>
      </el-card>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import DynamicForm from '../DynamicForm.vue'
import {
  CONTROL_LIBRARY,
  TABLE_COLUMN_TYPES,
  defaultFieldOf,
  isOptionType,
} from './controlLibrary'
import { SchemaHistory } from './schemaHistory'
import { validateFormSchema } from './designValidate'
import { formApi } from '@/api/wf/form'
import type { FormSchema, FormFieldDef, FormTableColumnDef } from '@/types/wf/wf'

const { t } = useI18n()

/**
 * 表单设计器（OA 章09 §2/§5）。三栏：控件库 / 画布(复用 DynamicForm 预览) / 属性面板。
 * 直接读写运行时 schema（FormDef.SchemaJson，同构 OA4-D6），保存即 Version+1，无中间格式。
 */
const formKey = ref('')
const formName = ref('')
const schema = reactive<FormSchema>({ fields: [], rules: [] })
const selectedIdx = ref(-1)
const previewModel = ref<Record<string, any>>({})

const selected = computed<FormFieldDef | null>(() =>
  selectedIdx.value >= 0 && selectedIdx.value < schema.fields.length ? schema.fields[selectedIdx.value]! : null,
)

// ── 撤销/重做（快照栈，章09 §5）──
const history = new SchemaHistory(snapshot())
const canUndo = ref(false)
const canRedo = ref(false)
let restoring = false

function snapshot(): FormSchema {
  return JSON.parse(JSON.stringify({ fields: schema.fields, rules: schema.rules || [] }))
}
function applySnapshot(s: FormSchema) {
  restoring = true
  schema.fields = s.fields || []
  schema.rules = s.rules || []
  if (selectedIdx.value >= schema.fields.length) selectedIdx.value = -1
  restoring = false
}
// schema 变化即压栈（恢复态不压，避免回环）
watch(
  () => snapshot(),
  () => {
    if (restoring) return
    history.push(snapshot())
    syncHistoryFlags()
  },
  { deep: true },
)
function syncHistoryFlags() {
  canUndo.value = history.canUndo
  canRedo.value = history.canRedo
}
function undo() {
  const s = history.undo()
  if (s) applySnapshot(s)
  syncHistoryFlags()
}
function redo() {
  const s = history.redo()
  if (s) applySnapshot(s)
  syncHistoryFlags()
}

// ── 编辑动作 ──
function addField(type: string) {
  schema.fields.push(defaultFieldOf(type, schema.fields))
  selectedIdx.value = schema.fields.length - 1
}
function move(i: number, dir: number) {
  const j = i + dir
  if (j < 0 || j >= schema.fields.length) return
  const arr = schema.fields
  ;[arr[i], arr[j]] = [arr[j]!, arr[i]!]
  selectedIdx.value = j
}
function remove(i: number) {
  schema.fields.splice(i, 1)
  if (selectedIdx.value === i) selectedIdx.value = -1
  else if (selectedIdx.value > i) selectedIdx.value--
}
function addOption() {
  if (!selected.value) return
  selected.value.options = selected.value.options || []
  selected.value.options.push({ label: '', value: '' })
}
function removeOption(oi: number) {
  selected.value?.options?.splice(oi, 1)
}
function onFieldTypeChange(type: string) {
  if (!selected.value) return
  if (type === 'table') {
    selected.value.minRows ??= 0
    selected.value.maxRows ??= 20
    selected.value.columns ??= [
      { name: 'column1', label: '列1', type: 'input', required: false },
    ]
  } else if (isOptionType(type) && !selected.value.options?.length) {
    selected.value.options = [
      { label: '选项1', value: '1' },
      { label: '选项2', value: '2' },
    ]
  }
}
function addTableColumn() {
  if (!selected.value) return
  selected.value.columns = selected.value.columns || []
  const used = new Set(selected.value.columns.map((column) => column.name))
  let index = 1
  while (used.has(`column${index}`)) index++
  selected.value.columns.push({
    name: `column${index}`,
    label: `列${index}`,
    type: 'input',
    required: false,
  })
}
function removeTableColumn(index: number) {
  selected.value?.columns?.splice(index, 1)
}
function onColumnTypeChange(column: FormTableColumnDef) {
  if (column.type === 'select' && !column.options?.length) {
    column.options = [
      { label: '选项1', value: '1' },
      { label: '选项2', value: '2' },
    ]
  }
}
function addColumnOption(column: FormTableColumnDef) {
  column.options = column.options || []
  column.options.push({ label: '', value: '' })
}
function removeColumnOption(column: FormTableColumnDef, index: number) {
  column.options?.splice(index, 1)
}

// ── 加载 / 保存（接运行时 FormDef，章09 §5）──
const draftRowVersion = ref<string>()
async function load() {
  if (!formKey.value.trim()) {
    ElMessage.warning(t('请输入表单标识'))
    return
  }
  const res = await formApi.getDraft(formKey.value.trim())
  if (!res.data) {
    ElMessage.info(t('该表单尚未定义'))
    return
  }
  formName.value = res.data.name || ''
  draftRowVersion.value = res.data.rowVersion
  const parsed: FormSchema = JSON.parse(res.data.schemaJson || '{"fields":[]}')
  applySnapshot({ fields: parsed.fields || [], rules: parsed.rules || [] })
  history.reset(snapshot())
  syncHistoryFlags()
}

async function save() {
  if (!formKey.value.trim() || !formName.value.trim()) {
    ElMessage.warning(t('请填写表单标识与名称'))
    return
  }
  const errors = validateFormSchema(schema)
  if (errors.length) {
    ElMessage.error(errors[0])
    return
  }
  const res = await formApi.saveDraft(formKey.value.trim(), {
    name: formName.value.trim(),
    schemaJson: JSON.stringify({ fields: schema.fields, rules: schema.rules || [] }),
    rowVersion: draftRowVersion.value,
  })
  draftRowVersion.value = res.data.rowVersion
  ElMessage.success(t('保存成功'))
}
async function publish() {
  if (!draftRowVersion.value) { ElMessage.warning(t('请先保存草稿')); return }
  await formApi.publish(formKey.value.trim(), draftRowVersion.value)
  ElMessage.success(t('发布成功'))
  await load()
}
</script>

<style scoped>
.form-designer { display: flex; flex-direction: column; gap: 8px; height: 100%; }
.toolbar { flex: none; }
.panes { display: flex; gap: 8px; flex: 1; min-height: 0; }
.pane { overflow: auto; }
.pane.lib { width: 180px; flex: none; }
.pane.canvas { flex: 1; }
.pane.prop { width: 360px; flex: none; }
.pane-title { font-weight: 600; }
.lib-item { display: flex; align-items: center; gap: 6px; padding: 8px; cursor: pointer; border-radius: 4px; }
.lib-item:hover { background: var(--el-fill-color-light); }
.field-row { display: flex; justify-content: space-between; align-items: center; padding: 8px; border: 1px solid var(--el-border-color-lighter); border-radius: 4px; margin-bottom: 6px; cursor: pointer; }
.field-row.active { border-color: var(--el-color-primary); background: var(--el-color-primary-light-9); }
.field-meta { display: flex; align-items: center; gap: 6px; }
.field-label { font-size: 13px; }
.opt-row { display: flex; align-items: center; gap: 6px; margin-bottom: 6px; }
.column-card { display: flex; flex-direction: column; gap: 8px; padding: 10px; margin-bottom: 10px; border: 1px solid var(--el-border-color-lighter); border-radius: 6px; }
.column-card__header, .column-required { display: flex; align-items: center; justify-content: space-between; }
.column-card__header { font-size: 12px; font-weight: 600; color: var(--el-text-color-regular); }
</style>

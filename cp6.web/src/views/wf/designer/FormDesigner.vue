<template>
  <DefinitionDesignerEntryV2 v-if="!managedSource" kind="FORM" />
  <div v-else ref="designerRoot" class="form-designer">
    <!-- 工具栏 -->
    <el-card shadow="never" class="toolbar">
      <el-space wrap>
        <el-button size="small" :disabled="readOnly || !canUndo" @click="undo">{{ t('撤销') }}</el-button>
        <el-button size="small" :disabled="readOnly || !canRedo" @click="redo">{{ t('重做') }}</el-button>
      </el-space>
    </el-card>

    <fieldset class="panes" :disabled="readOnly">
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
          :data-field-index="i"
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
        <DynamicForm v-if="schema.fields.length && previewEnabled !== false" :schema="schema" v-model="previewModel" />
        <p v-else-if="schema.fields.length">保存并通过校验后可预览控件。</p>
        <p v-if="schema.fields.some(f => f.optionSourceV1 || f.columns?.some(c => c.optionSourceV1))">外部字典选项在实际填写时读取；此处预览控件结构。</p>
      </el-card>

      <!-- 右：属性面板 -->
      <el-card shadow="never" class="pane prop">
        <template #header><span class="pane-title">{{ t('属性') }}</span></template>
        <el-empty v-if="!selected" :description="t('请先选择字段')" :image-size="60" />
        <el-form v-else :disabled="readOnly" label-width="84px" size="small">
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
            <FormOptionSourcePicker :model-value="selected.optionSourceV1" :disabled="readOnly" @update:model-value="setOptionSource(selected!, $event)" />
            <template v-if="!selected.optionSourceV1">
            <div v-for="(o, oi) in selected.options || []" :key="oi" class="opt-row">
              <el-input v-model="o.label" :placeholder="t('标签')" size="small" style="width: 96px" />
              <el-input v-model="o.value" :placeholder="t('值')" size="small" style="width: 96px" />
              <el-button link type="danger" size="small" @click="removeOption(oi)">{{ t('删除') }}</el-button>
            </div>
            <el-button link size="small" @click="addOption">{{ t('添加选项') }}</el-button>
            </template>
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
                <FormOptionSourcePicker :model-value="column.optionSourceV1" :disabled="readOnly" @update:model-value="setOptionSource(column, $event)" />
                <template v-if="!column.optionSourceV1">
                <div v-for="(option, oi) in column.options || []" :key="oi" class="opt-row">
                  <el-input v-model="option.label" :placeholder="t('标签')" size="small" />
                  <el-input v-model="option.value" :placeholder="t('值')" size="small" />
                  <el-button link type="danger" size="small" @click="removeColumnOption(column, oi)">
                    {{ t('删除') }}
                  </el-button>
                </div>
                <el-button link size="small" @click="addColumnOption(column)">{{ t('添加选项') }}</el-button>
                </template>
              </template>
            </div>
            <el-button link size="small" data-test="add-table-column" @click="addTableColumn">
              {{ t('添加列') }}
            </el-button>
          </template>
        </el-form>
      </el-card>
    </fieldset>
  </div>
</template>

<script setup lang="ts">
import { computed, defineAsyncComponent, nextTick, reactive, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import DynamicForm from '../DynamicForm.vue'
import FormOptionSourcePicker from './FormOptionSourcePicker.vue'
import type { FormOptionSource } from '@/api/oa/formOptions'
import {
  CONTROL_LIBRARY,
  TABLE_COLUMN_TYPES,
  defaultFieldOf,
  isOptionType,
} from './controlLibrary'
import { SchemaHistory } from './schemaHistory'
import { preserveSchema } from '@/views/oa/designer/preserveSchema'
import type { FormSchema, FormFieldDef, FormTableColumnDef } from '@/types/wf/wf'

const props = defineProps<{ managedSource?: { schemaJson: string; version: string }; readOnly?: boolean; previewEnabled?: boolean }>()
const emit = defineEmits<{ schemaChange: [raw: string] }>()
const DefinitionDesignerEntryV2 = defineAsyncComponent(() => import('@/views/oa/designer/DefinitionDesignerEntryV2.vue'))
const { t } = useI18n()

/**
 * 表单设计器（OA 章09 §2/§5）。三栏：控件库 / 画布(复用 DynamicForm 预览) / 属性面板。
 * 受控模式由 FORM Owner 工作区加载和保存完整 schema；独立入口也采用同一工作区。
 */
const schema = reactive<FormSchema>(Object.assign(Object.create(null), { fields: [], rules: [] }))
let originalSchema = '{"fields":[],"rules":[]}'
const selectedIdx = ref(-1)
const previewModel = ref<Record<string, any>>({})
const designerRoot = ref<HTMLElement>()
async function locate(path: string) {
  const match = /^\$\.fields\[(\d+)\]/.exec(path)
  if (!match || !schema.fields[Number(match[1])]) return
  selectedIdx.value = Number(match[1]); await nextTick()
  const column = /\.columns\[(\d+)\]/.exec(path)
  const target = designerRoot.value?.querySelector(column ? `[data-test="table-column-${Number(column[1])}"]` : `[data-field-index="${selectedIdx.value}"]`)
  target?.scrollIntoView({ block: 'center', behavior: 'smooth' })
  target?.querySelector('input')?.focus()
}
defineExpose({ locate })

const selected = computed<FormFieldDef | null>(() =>
  selectedIdx.value >= 0 && selectedIdx.value < schema.fields.length ? schema.fields[selectedIdx.value]! : null,
)

// ── 撤销/重做（快照栈，章09 §5）──
const history = new SchemaHistory(originalSchema)
const canUndo = ref(false)
const canRedo = ref(false)
let restoring = false

function snapshot(): FormSchema {
  return JSON.parse(JSON.stringify(schema))
}
function applySnapshot(s: FormSchema, raw: string) {
  restoring = true
  originalSchema = raw
  // Remove properties from the previous form as well as copying the complete new schema.
  for (const key of Object.keys(schema)) delete schema[key]
  Object.assign(schema, s)
  if (selectedIdx.value >= schema.fields.length) selectedIdx.value = -1
  restoring = false
}
// schema 变化即压栈（恢复态不压，避免回环）
watch(
  () => snapshot(),
  () => {
    if (restoring) return
    // Record exact fragments after each atomic edit. A later rename/reorder or undo cannot
    // lose precision by rebasing several edits onto the originally loaded positional array.
    originalSchema = preserveSchema(originalSchema, snapshot(), 'name')
    emit('schemaChange', originalSchema)
    history.push(originalSchema)
    syncHistoryFlags()
  },
  { deep: true, flush: 'sync' },
)
function syncHistoryFlags() {
  canUndo.value = history.canUndo
  canRedo.value = history.canRedo
}
function undo() {
  if (props.readOnly) return
  const s = history.undo()
  if (s) { applySnapshot(JSON.parse(s), s); emit('schemaChange', s) }
  syncHistoryFlags()
}
function redo() {
  if (props.readOnly) return
  const s = history.redo()
  if (s) { applySnapshot(JSON.parse(s), s); emit('schemaChange', s) }
  syncHistoryFlags()
}

// ── 编辑动作 ──
function addField(type: string) {
  if (props.readOnly) return
  schema.fields.push(defaultFieldOf(type, schema.fields))
  selectedIdx.value = schema.fields.length - 1
}
function move(i: number, dir: number) {
  if (props.readOnly) return
  const j = i + dir
  if (j < 0 || j >= schema.fields.length) return
  const arr = [...schema.fields]
  ;[arr[i], arr[j]] = [arr[j]!, arr[i]!]
  schema.fields = arr
  selectedIdx.value = j
}
function remove(i: number) {
  if (props.readOnly) return
  schema.fields.splice(i, 1)
  if (selectedIdx.value === i) selectedIdx.value = -1
  else if (selectedIdx.value > i) selectedIdx.value--
}
function setOptionSource(field: FormFieldDef | FormTableColumnDef, source?: FormOptionSource) {
  if (props.readOnly) return
  const next = { ...field }
  if (source) { next.optionSourceV1 = source; delete next.options } else delete next.optionSourceV1
  const index = schema.fields.indexOf(field as FormFieldDef)
  if (index >= 0) schema.fields[index] = next as FormFieldDef
  else if (selected.value?.columns) { const col = selected.value.columns.indexOf(field as FormTableColumnDef); if (col >= 0) selected.value.columns[col] = next as FormTableColumnDef }
}
function addOption() {
  if (props.readOnly) return
  if (!selected.value) return
  selected.value.options = selected.value.options || []
  selected.value.options.push({ label: '', value: '' })
}
function removeOption(oi: number) {
  if (props.readOnly) return
  selected.value?.options?.splice(oi, 1)
}
function onFieldTypeChange(type: string) {
  if (props.readOnly) return
  if (!selected.value) return
  if (!isOptionType(type)) delete selected.value.optionSourceV1
  if (type === 'table') {
    selected.value.minRows ??= 0
    selected.value.maxRows ??= 20
    selected.value.columns ??= [
      { name: 'column1', label: '列1', type: 'input', required: false },
    ]
  } else if (isOptionType(type) && !selected.value.optionSourceV1 && !selected.value.options?.length) {
    selected.value.options = [
      { label: '选项1', value: '1' },
      { label: '选项2', value: '2' },
    ]
  }
}
function addTableColumn() {
  if (props.readOnly) return
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
  if (props.readOnly) return
  selected.value?.columns?.splice(index, 1)
}
function onColumnTypeChange(column: FormTableColumnDef) {
  if (props.readOnly) return
  if (column.type !== 'select') delete column.optionSourceV1
  if (column.type === 'select' && !column.optionSourceV1 && !column.options?.length) {
    column.options = [
      { label: '选项1', value: '1' },
      { label: '选项2', value: '2' },
    ]
  }
}
function addColumnOption(column: FormTableColumnDef) {
  if (props.readOnly) return
  column.options = column.options || []
  column.options.push({ label: '', value: '' })
}
function removeColumnOption(column: FormTableColumnDef, index: number) {
  if (props.readOnly) return
  column.options?.splice(index, 1)
}

watch(() => props.managedSource, value => {
  if (!value) return
  applySnapshot(JSON.parse(value.schemaJson), value.schemaJson)
  history.reset(value.schemaJson); syncHistoryFlags(); selectedIdx.value = -1
}, { immediate: true })

</script>

<style scoped>
.form-designer { display: flex; flex-direction: column; gap: 8px; height: 100%; }
.toolbar { flex: none; }
.panes { border: 0; margin: 0; padding: 0; display: flex; gap: 8px; flex: 1; min-height: 0; }
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

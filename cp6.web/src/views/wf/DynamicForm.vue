<template>
  <el-form ref="formRef" :model="model" :rules="rules" label-width="110px" size="small">
    <el-form-item
      v-for="f in visibleFields"
      :key="f.name"
      :label="f.label || f.name"
      :prop="f.name"
    >
      <el-input
        v-if="isText(f)"
        v-model="model[f.name]"
        :disabled="readonly(f)"
        :maxlength="f.maxLength"
        :placeholder="f.placeholder"
        clearable
      />
      <el-input
        v-else-if="f.type === 'textarea'"
        v-model="model[f.name]"
        type="textarea"
        :rows="3"
        :disabled="readonly(f)"
        :maxlength="f.maxLength"
      />
      <el-input-number
        v-else-if="f.type === 'number'"
        v-model="model[f.name]"
        :disabled="readonly(f)"
        :controls="false"
        style="width: 100%"
      />
      <el-select
        v-else-if="f.type === 'select'"
        v-model="model[f.name]"
        :disabled="readonly(f)"
        clearable
        style="width: 100%"
      >
        <el-option v-for="o in optionsOf(f)" :key="String(o.value)" :label="o.label" :value="o.value" />
      </el-select>
      <el-radio-group v-else-if="f.type === 'radio'" v-model="model[f.name]" :disabled="readonly(f)">
        <el-radio v-for="o in optionsOf(f)" :key="String(o.value)" :value="o.value">{{ o.label }}</el-radio>
      </el-radio-group>
      <el-checkbox-group v-else-if="f.type === 'checkbox'" v-model="model[f.name]" :disabled="readonly(f)">
        <el-checkbox v-for="o in optionsOf(f)" :key="String(o.value)" :value="o.value">{{ o.label }}</el-checkbox>
      </el-checkbox-group>
      <el-date-picker
        v-else-if="f.type === 'date'"
        v-model="model[f.name]"
        type="date"
        value-format="YYYY-MM-DD"
        :disabled="readonly(f)"
        style="width: 100%"
      />
      <el-date-picker
        v-else-if="f.type === 'datetime'"
        v-model="model[f.name]"
        type="datetime"
        value-format="YYYY-MM-DD HH:mm:ss"
        :disabled="readonly(f)"
        style="width: 100%"
      />
      <div v-else-if="f.type === 'table'" class="subtable" :data-test="`subtable-${f.name}`">
        <el-table :data="tableRows(f)" border size="small" empty-text="暂无明细">
          <el-table-column type="index" label="#" width="48" align="center" />
          <el-table-column
            v-for="column in f.columns || []"
            :key="column.name"
            :label="column.label || column.name"
            min-width="150"
          >
            <template #header>
              <span>{{ column.label || column.name }}</span>
              <span v-if="column.required" class="required-mark">*</span>
            </template>
            <template #default="{ row }">
              <el-input
                v-if="column.type === 'input'"
                v-model="row[column.name]"
                :disabled="readonly(f)"
                :maxlength="column.maxLength"
                :placeholder="column.placeholder"
                clearable
              />
              <el-input
                v-else-if="column.type === 'textarea'"
                v-model="row[column.name]"
                type="textarea"
                :autosize="{ minRows: 1, maxRows: 3 }"
                :disabled="readonly(f)"
                :maxlength="column.maxLength"
                :placeholder="column.placeholder"
              />
              <el-input-number
                v-else-if="column.type === 'number'"
                v-model="row[column.name]"
                :disabled="readonly(f)"
                :controls="false"
                style="width: 100%"
              />
              <el-select
                v-else-if="column.type === 'select'"
                v-model="row[column.name]"
                :disabled="readonly(f)"
                clearable
                style="width: 100%"
              >
                <el-option
                  v-for="option in column.options || []"
                  :key="String(option.value)"
                  :label="option.label"
                  :value="option.value"
                />
              </el-select>
              <el-date-picker
                v-else-if="column.type === 'date'"
                v-model="row[column.name]"
                type="date"
                value-format="YYYY-MM-DD"
                :disabled="readonly(f)"
                style="width: 100%"
              />
              <el-date-picker
                v-else-if="column.type === 'datetime'"
                v-model="row[column.name]"
                type="datetime"
                value-format="YYYY-MM-DD HH:mm:ss"
                :disabled="readonly(f)"
                style="width: 100%"
              />
            </template>
          </el-table-column>
          <el-table-column v-if="!readonly(f)" label="操作" width="72" fixed="right">
            <template #default="{ $index }">
              <el-button
                link
                type="danger"
                :disabled="tableRows(f).length <= (f.minRows ?? 0)"
                :data-test="`remove-${f.name}-${$index}`"
                @click="removeTableRow(f, $index)"
              >
                删除
              </el-button>
            </template>
          </el-table-column>
        </el-table>
        <div v-if="!readonly(f)" class="subtable-actions">
          <el-button
            size="small"
            :disabled="tableRows(f).length >= maxRowsOf(f)"
            :data-test="`add-${f.name}`"
            @click="addTableRow(f)"
          >
            添加明细
          </el-button>
          <span class="row-limit">{{ tableRows(f).length }} / {{ maxRowsOf(f) }}</span>
        </div>
      </div>
      <el-select
        v-else-if="f.type === 'user'"
        v-model="model[f.name]"
        :multiple="!!f.multiple"
        filterable remote
        :remote-method="(kw: string) => searchUsers(f.name, kw)"
        :disabled="readonly(f)"
        :placeholder="f.placeholder"
        clearable
        style="width: 100%"
      >
        <el-option v-for="o in (userOpts[f.name] ?? [])" :key="o.value" :label="o.label" :value="o.value" />
      </el-select>
      <el-input v-else v-model="model[f.name]" :disabled="readonly(f)" :placeholder="f.placeholder" clearable />
    </el-form-item>
  </el-form>
</template>

<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import type { FormInstance, FormRules } from 'element-plus'
import type { FormSchema, FormFieldDef, FieldMask } from '@/types/wf/wf'
import { applyRules, type FieldEffect } from './ruleEngine'
import { createSubtableRow, maxRowsOf, rowsOf, validateSubtable } from './subtable'
import { userApi } from '@/api/sys/user'

const { t } = useI18n()

/**
 * Schema 驱动动态表单（OA 章02 B-2 + 章06 规则引擎）。按 field.type 映射 element-plus 控件；
 * buildRules 翻 required/maxLength/pattern；规则引擎(章06)驱动显隐/必填/禁用/联动选项/计算回写；
 * mask 入口(D-1)：PUB 字段权限与规则取交集——更严赢（mask hidden/readonly 优先于规则 show/enable）。
 */
const props = defineProps<{ schema: FormSchema; mask?: FieldMask }>()
const model = defineModel<Record<string, any>>({ default: () => ({}) })

const formRef = ref<FormInstance>()

// user 字段远程搜索选项（按字段名分组）
const userOpts = ref<Record<string, { label: string; value: string }[]>>({})
async function searchUsers(field: string, kw: string) {
  if (!kw) { userOpts.value[field] = []; return }
  const res = await userApi.getList({ page: 1, pageSize: 20, keyword: kw }) as any
  userOpts.value[field] = (res.rows ?? []).map((u: any) => ({ label: u.nickName || u.userName, value: String(u.id) }))
}

// 规则生效效果（逐字段 visible/required/disabled/options）。watch(model) 单轮前向重算，
// compute 写回 model；稳定 compute 下 Vue 同值不再触发 → 收敛（循环依赖一轮不级联）。
const effects = ref<Record<string, FieldEffect>>({})
watch(
  [model, () => props.schema],
  () => { effects.value = applyRules(props.schema, model.value) },
  { deep: true, immediate: true },
)

function eff(name: string): FieldEffect | undefined {
  return effects.value[name]
}

const visibleFields = computed<FormFieldDef[]>(() =>
  (props.schema?.fields || []).filter(
    (f) => props.mask?.[f.name] !== 'hidden' && eff(f.name)?.visible !== false, // 规则隐藏或 mask 隐藏 → 不渲染
  ),
)

function readonly(f: FormFieldDef): boolean {
  return props.mask?.[f.name] === 'readonly' || eff(f.name)?.disabled === true // 更严赢
}
function isText(f: FormFieldDef): boolean {
  return ['input', 'dept'].includes(f.type)
}
function optionsOf(f: FormFieldDef): { label: string; value: string | number }[] {
  return eff(f.name)?.options ?? f.options ?? [] // 规则 setOptions 优先（联动）
}

function tableRows(f: FormFieldDef): Record<string, unknown>[] {
  return rowsOf(model.value[f.name])
}
function addTableRow(f: FormFieldDef) {
  if (!Array.isArray(model.value[f.name])) model.value[f.name] = []
  const rows = tableRows(f)
  if (rows.length >= maxRowsOf(f)) return
  rows.push(createSubtableRow(f))
}
function removeTableRow(f: FormFieldDef, index: number) {
  const rows = tableRows(f)
  if (rows.length <= (f.minRows ?? 0)) return
  rows.splice(index, 1)
}

const rules = computed<FormRules>(() => {
  const r: FormRules = {}
  for (const f of props.schema?.fields || []) {
    const perm = props.mask?.[f.name]
    if (perm === 'hidden' || perm === 'readonly') continue // 不可编辑字段不校验
    if (eff(f.name)?.visible === false) continue // 规则隐藏字段不校验
    const list: FormRules[string] = []
    const label = f.label || f.name
    const required = eff(f.name)?.required ?? f.required
    if (f.type === 'table') {
      list.push({
        validator: (_rule: unknown, value: unknown, callback: (error?: Error) => void) => {
          const errors = validateSubtable(f, value, !!required)
          callback(errors.length ? new Error(errors[0]) : undefined)
        },
        trigger: 'change',
      })
    } else {
      if (required) list.push({ required: true, message: t('{label}必填', { label }), trigger: 'blur' }) // 生效必填(规则可改)
      if (f.maxLength) list.push({ max: f.maxLength, message: t('{label}最多 {n} 字', { label, n: f.maxLength }), trigger: 'blur' })
      if (f.pattern) list.push({ pattern: new RegExp(f.pattern), message: t('{label}格式不符', { label }), trigger: 'blur' })
    }
    if (list.length) r[f.name] = list
  }
  return r
})

/** 供父组件提交前校验 */
async function validate(): Promise<boolean> {
  if (!formRef.value) return true
  try {
    await formRef.value.validate()
    return true
  } catch {
    return false
  }
}

defineExpose({ validate })
</script>

<style scoped>
.subtable { width: 100%; min-width: 0; }
.subtable-actions { display: flex; align-items: center; gap: 10px; margin-top: 8px; }
.row-limit { color: var(--el-text-color-secondary); font-size: 12px; }
.required-mark { color: var(--el-color-danger); margin-left: 3px; }
</style>

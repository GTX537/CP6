<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import type { FormMask } from '@/api/oa/personalForm'
import PersonalReferenceInput from './PersonalReferenceInput.vue'
import PersonalFileInput from './PersonalFileInput.vue'
import type { FormFileReadContext } from '@/api/oa/formFiles'
import { FormNumber, formObject, readFormJson, writeFormJson, schemaProperty, schemaText, schemaNumber, type FormValue, type FormObject } from './personalFormJson'
const props = defineProps<{ fields: FormObject[]; modelValue: FormObject; mask?: FormMask; disabled?: boolean; hidden?: string[]; formKey?: string; draftId?: string; allowUnset?: boolean; fileContext?: FormFileReadContext }>()
const emit = defineEmits<{ 'update:modelValue': [value: FormObject]; validity: [value: boolean]; forbidden: [] }>()
const errors = ref<Record<string, string>>({}), edits = ref<Record<string, string>>({}), children = ref<Record<string, boolean>>({})
const name = (field: FormObject) => schemaText(field, 'name')
const type = (field: FormObject) => schemaText(field, 'type', 'input').toLowerCase()
const locked = (field: FormObject) => !!props.disabled || !!props.mask && props.mask[name(field)] !== 'edit'
const fields = computed(() => props.fields.filter(f => props.mask?.[name(f)] !== 'hidden' && !props.hidden?.includes(name(f))))
const valid = computed(() => !Object.values(errors.value).some(Boolean) && !Object.values(children.value).some(x => !x))
watch(valid, value => emit('validity', value), { immediate: true })
watch(() => writeFormJson(props.fields), () => { errors.value = {}; edits.value = {}; children.value = {} })
function assign(field: FormObject, value: FormValue | undefined) {
  if (locked(field)) return
  const result = { ...props.modelValue }
  if (value === undefined) delete result[name(field)]; else result[name(field)] = value
  delete errors.value[name(field)]; delete edits.value[name(field)]
  emit('update:modelValue', result)
}
function text(field: FormObject): string {
  if (edits.value[name(field)] !== undefined) return edits.value[name(field)]!
  const value = props.modelValue[name(field)]
  return value instanceof FormNumber ? value.raw : typeof value === 'string' ? value : value === undefined || value === null ? '' : writeFormJson(value)
}
function input(field: FormObject, value: string) {
  try {
    if (type(field) === 'number') assign(field, value === '' ? undefined : new FormNumber(value))
    else if ((type(field) === 'user' || type(field) === 'dept') && schemaProperty(field, 'multiple') === true)
      assign(field, value.split(',').map(x => x.trim()).filter(Boolean))
    else assign(field, value)
  } catch (e: any) { errors.value[name(field)] = e.message; edits.value[name(field)] = value }
}
function choices(field: FormObject) {
  const options = schemaProperty(field, 'options')
  return Array.isArray(options) ? options.map(formObject).map(o => ({ label: schemaText(o, 'label'), value: schemaProperty(o, 'value')! })) : []
}
function selected(field: FormObject) {
  const value = props.modelValue[name(field)]
  return value === undefined ? '' : writeFormJson(value)
}
function choose(field: FormObject, raw: string) { assign(field, raw === '' ? undefined : readFormJson(raw)) }
function checked(field: FormObject, value: FormValue) {
  const all = props.modelValue[name(field)]
  return Array.isArray(all) && all.some(x => writeFormJson(x) === writeFormJson(value))
}
function toggle(field: FormObject, value: FormValue, on: boolean) {
  const all = props.modelValue[name(field)]
  const next = Array.isArray(all) ? all.filter(x => writeFormJson(x) !== writeFormJson(value)) : []
  if (on) next.push(value)
  assign(field, next)
}
function columns(field: FormObject) { return (schemaProperty(field, 'columns') as FormValue[]).map(formObject) }
function rows(field: FormObject): FormObject[] { const value = props.modelValue[name(field)]; return Array.isArray(value) ? value.map(formObject) : [] }
function changeRow(field: FormObject, index: number, value: FormObject) { const next = [...rows(field)]; next[index] = value; assign(field, next) }
function removeRow(field: FormObject, index: number) { const next = [...rows(field)]; next.splice(index, 1); assign(field, next); children.value = {} }
function addRow(field: FormObject) {
  const row: FormObject = Object.create(null)
  for (const column of columns(field)) { const value = schemaProperty(column, 'default'); if (value !== undefined) row[name(column)] = value }
  assign(field, [...rows(field), row])
}
defineExpose({ valid })
</script>
<template>
  <div class="personal-fields">
    <div v-for="field in fields" :key="name(field)" class="field" :data-field="name(field)">
      <label>{{ schemaText(field, 'label', name(field)) }}<span v-if="schemaProperty(field, 'required') === true"> *</span></label>
      <template v-if="type(field) === 'table'">
        <fieldset v-for="(row, index) in rows(field)" :key="index">
          <legend>第 {{ index + 1 }} 行</legend>
          <PersonalFormFields :fields="columns(field)" :model-value="row" :disabled="locked(field)" :allow-unset="allowUnset"
            @update:model-value="changeRow(field, index, $event)" @validity="children[`${name(field)}:${index}`] = $event" />
          <el-button v-if="!locked(field)" size="small" @click="removeRow(field, index)">删除此行</el-button>
        </fieldset>
        <el-button v-if="!locked(field)" :disabled="rows(field).length >= schemaNumber(field, 'maxRows', 100)" @click="addRow(field)">添加明细</el-button>
        <span class="hint">{{ rows(field).length }} / {{ schemaNumber(field, 'maxRows', 100) }} 行</span>
        <p v-if="rows(field).length > schemaNumber(field, 'maxRows', 100)" role="alert">当前明细超过目标版本行数上限，已保留全部内容，请人工调整。</p>
      </template>
      <template v-else-if="['select', 'radio'].includes(type(field))">
        <select :value="selected(field)" :disabled="locked(field)" @change="choose(field, ($event.target as HTMLSelectElement).value)">
          <option value="">请选择</option>
          <option v-for="option in choices(field)" :key="writeFormJson(option.value)" :value="writeFormJson(option.value)">{{ option.label }}</option>
        </select>
      </template>
      <template v-else-if="type(field) === 'checkbox' && choices(field).length">
        <label v-for="option in choices(field)" :key="writeFormJson(option.value)" class="choice">
          <input type="checkbox" :checked="checked(field, option.value)" :disabled="locked(field)" @change="toggle(field, option.value, ($event.target as HTMLInputElement).checked)">{{ option.label }}
        </label>
      </template>
      <input v-else-if="type(field) === 'checkbox'" type="checkbox" :checked="modelValue[name(field)] === true" :disabled="locked(field)" @change="assign(field, ($event.target as HTMLInputElement).checked)">
      <textarea v-else-if="type(field) === 'textarea'" :value="text(field)" :disabled="locked(field)" :maxlength="schemaNumber(field, 'maxLength', 10000)" @input="input(field, ($event.target as HTMLTextAreaElement).value)" />
      <PersonalReferenceInput v-else-if="['user', 'dept'].includes(type(field)) && formKey" :form-key="formKey" :draft-id="draftId" :field="name(field)" :model-value="modelValue[name(field)]" :multiple="schemaProperty(field, 'multiple') === true" :disabled="locked(field)" @update:model-value="assign(field, $event)" @forbidden="emit('forbidden')" />
      <PersonalFileInput v-else-if="type(field) === 'upload'" :context="fileContext" :field="name(field)" :model-value="modelValue[name(field)]" :disabled="locked(field)" @update:model-value="assign(field, $event)" @validity="children[`file:${name(field)}`] = $event" @forbidden="emit('forbidden')" />
      <input v-else :value="text(field)" :disabled="locked(field)" :inputmode="type(field) === 'number' ? 'decimal' : undefined" :placeholder="schemaText(field, 'placeholder')" @input="input(field, ($event.target as HTMLInputElement).value)">
      <small v-if="errors[name(field)]" role="alert">{{ errors[name(field)] }}</small>
      <div v-if="!locked(field)" class="value-state">
        <span>{{ modelValue[name(field)] === undefined ? '未填写' : modelValue[name(field)] === null ? '空值' : '' }}</span>
        <button v-if="allowUnset !== false" type="button" :disabled="type(field) === 'upload' && children[`file:${name(field)}`] === false" @click="assign(field, undefined)">移除填写</button>
        <button type="button" :disabled="type(field) === 'upload' && children[`file:${name(field)}`] === false" @click="assign(field, null)">设为空值</button>
      </div>
    </div>
  </div>
</template>
<style scoped>
.field { margin: 12px 0; } .field > label { display: block; font-weight: 500; margin-bottom: 6px; }
input:not([type=checkbox]), select, textarea { box-sizing: border-box; width: 100%; padding: 8px; border: 1px solid var(--el-border-color); border-radius: 4px; background: var(--el-fill-color-blank); color: var(--el-text-color-primary); }
input:disabled, select:disabled, textarea:disabled { background: var(--el-fill-color-light); }
textarea { min-height: 80px; } fieldset { border: 1px solid var(--el-border-color); margin-bottom: 10px; }
.hint, .value-state { color: var(--el-text-color-secondary); font-size: 12px; margin: 6px; } .choice { display: inline-flex; margin-right: 12px; }
.value-state button { background: none; border: none; color: var(--el-color-primary); cursor: pointer; }
[role=alert] { display: block; color: var(--el-color-danger); }
</style>

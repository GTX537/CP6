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
        <el-option v-for="o in f.options || []" :key="String(o.value)" :label="o.label" :value="o.value" />
      </el-select>
      <el-radio-group v-else-if="f.type === 'radio'" v-model="model[f.name]" :disabled="readonly(f)">
        <el-radio v-for="o in f.options || []" :key="String(o.value)" :value="o.value">{{ o.label }}</el-radio>
      </el-radio-group>
      <el-checkbox-group v-else-if="f.type === 'checkbox'" v-model="model[f.name]" :disabled="readonly(f)">
        <el-checkbox v-for="o in f.options || []" :key="String(o.value)" :value="o.value">{{ o.label }}</el-checkbox>
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
      <el-input v-else v-model="model[f.name]" :disabled="readonly(f)" :placeholder="f.placeholder" clearable />
    </el-form-item>
  </el-form>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import type { FormInstance, FormRules } from 'element-plus'
import type { FormSchema, FormFieldDef, FieldMask } from '@/types/wf/wf'

/**
 * Schema 驱动动态表单（OA 章02 B-2）。按 field.type 映射 element-plus 控件；
 * buildRules 翻 required/maxLength/pattern；mask 入口（D-1）：hidden 不渲染、readonly disabled。
 */
const props = defineProps<{ schema: FormSchema; mask?: FieldMask }>()
const model = defineModel<Record<string, any>>({ default: () => ({}) })

const formRef = ref<FormInstance>()

const visibleFields = computed<FormFieldDef[]>(() =>
  (props.schema?.fields || []).filter((f) => props.mask?.[f.name] !== 'hidden'),
)

function readonly(f: FormFieldDef): boolean {
  return props.mask?.[f.name] === 'readonly'
}
function isText(f: FormFieldDef): boolean {
  return ['input', 'user', 'dept'].includes(f.type)
}

const rules = computed<FormRules>(() => {
  const r: FormRules = {}
  for (const f of props.schema?.fields || []) {
    const perm = props.mask?.[f.name]
    if (perm === 'hidden' || perm === 'readonly') continue // 不可编辑字段不校验
    const list: FormRules[string] = []
    const label = f.label || f.name
    if (f.required) list.push({ required: true, message: `${label}必填`, trigger: 'blur' })
    if (f.maxLength) list.push({ max: f.maxLength, message: `${label}最多 ${f.maxLength} 字`, trigger: 'blur' })
    if (f.pattern) list.push({ pattern: new RegExp(f.pattern), message: `${label}格式不符`, trigger: 'blur' })
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

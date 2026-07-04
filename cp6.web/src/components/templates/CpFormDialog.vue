<!--
  CpFormDialog —— 表单弹窗（设计系统 §9.2；新建/编辑类操作的目标模板）。
  组合结构：el-dialog + el-form（观感由 Element Plus overrides 保证），footer = ghost 取消 + primary 确认。
  按 fields 声明自组表单；或用默认 slot 自定义复杂表单（与 fields 互斥，slot 优先）。
  行为：点确认先 elForm.validate()；校验失败不提交、不关闭、不弹错（el-form 内联提示）；
  submit resolve → emit saved + update:modelValue(false)；reject → ElMessage.error 且保持打开；提交期间确认钮 loading。

  Props:
    - modelValue: boolean               弹窗开合（v-model）。
    - title: string                     标题。
    - fields?: FormField[]              字段声明；默认 slot 存在时被忽略。
    - form: Record<string,unknown>      表单对象（父级持有并负责重置）。
    - rules?: FormRules                 校验规则；同 key 覆盖 required 自动规则。
    - submit: (form) => Promise<void>   提交回调；resolve 视为成功。
    - width?: string                    弹窗宽度，透传 el-dialog。
    - labels?: { cancel?; confirm? }    footer 按钮文案覆盖；缺省中文，供业务侧接 i18n。
    - requiredMessage?: (label) => string  自动必填规则文案生成器；缺省 `${label}为必填项`。
  Slots: default（自定义表单体，替代 fields；仍在 el-form 内，parent 提供 el-form-item 规则同样生效）。
  Emits: update:modelValue(open) ｜ saved()

  使用示例：
    <CpFormDialog v-model="open" title="新建物料" :form="form" :submit="save"
      :fields="[{ key:'name', label:'名称', type:'text', required:true },
                { key:'qty', label:'数量', type:'number' }]"
      @saved="reload" />
-->
<script lang="ts">
export interface FormField {
  key: string
  label: string
  type: 'text' | 'number' | 'select' | 'date' | 'textarea'
  options?: { label: string; value: unknown }[]
  required?: boolean
}
</script>

<script setup lang="ts">
import { computed, ref } from 'vue'
import type { FormInstance, FormRules } from 'element-plus'
import {
  ElDialog, ElForm, ElFormItem, ElInput, ElInputNumber,
  ElSelect, ElOption, ElDatePicker, ElButton, ElMessage
} from 'element-plus'

const props = defineProps<{
  modelValue: boolean
  title: string
  fields?: FormField[]
  form: Record<string, unknown>
  rules?: FormRules
  submit: (form: Record<string, unknown>) => Promise<void>
  width?: string
  labels?: { cancel?: string; confirm?: string }
  requiredMessage?: (label: string) => string
}>()

// 自动必填规则文案：缺省中文，业务侧可传 requiredMessage 覆盖（接 i18n）
const defaultRequiredMessage = (label: string) => `${label}为必填项`

const emit = defineEmits<{
  (e: 'update:modelValue', open: boolean): void
  (e: 'saved'): void
}>()

const formRef = ref<FormInstance>()
const submitting = ref(false)

// required 字段自动生成必填规则；显式 rules 同 key 覆盖（explicit wins）
const mergedRules = computed<FormRules>(() => {
  const out: FormRules = {}
  for (const f of props.fields ?? []) {
    if (f.required) {
      const trigger = f.type === 'select' || f.type === 'date' ? 'change' : 'blur'
      out[f.key] = [{ required: true, message: (props.requiredMessage ?? defaultRequiredMessage)(f.label), trigger }]
    }
  }
  for (const [k, v] of Object.entries(props.rules ?? {})) out[k] = v
  return out
})

// 就地写回表单字段（父级持有 form 对象，表单编辑即改其属性）
function setVal(key: string, value: unknown) {
  props.form[key] = value
}

function close() {
  emit('update:modelValue', false)
}

async function onConfirm() {
  if (submitting.value) return // 防双提交：校验/提交在途时二次点击直接忽略
  submitting.value = true // 提前置位——覆盖 validate 在途窗口，杜绝并发进入
  try {
    const valid = await formRef.value?.validate().catch(() => false)
    if (!valid) return // 校验失败：不提交、不关闭、el-form 已内联提示（finally 复位 submitting）
    await props.submit(props.form)
    emit('saved')
    emit('update:modelValue', false)
  } catch (err) {
    ElMessage.error((err as Error)?.message ?? String(err))
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <el-dialog
    :model-value="modelValue"
    :title="title"
    :width="width"
    @update:model-value="emit('update:modelValue', $event)"
  >
    <el-form ref="formRef" :model="form" :rules="mergedRules" label-position="top">
      <slot v-if="$slots.default" />
      <template v-else>
        <el-form-item v-for="f in fields" :key="f.key" :label="f.label" :prop="f.key">
          <el-input
            v-if="f.type === 'text'"
            :model-value="(form[f.key] as string | undefined)"
            @update:model-value="setVal(f.key, $event)"
          />
          <el-input
            v-else-if="f.type === 'textarea'"
            type="textarea"
            :model-value="(form[f.key] as string | undefined)"
            @update:model-value="setVal(f.key, $event)"
          />
          <el-input-number
            v-else-if="f.type === 'number'"
            :model-value="(form[f.key] as number | undefined)"
            @update:model-value="setVal(f.key, $event)"
          />
          <el-select
            v-else-if="f.type === 'select'"
            :model-value="(form[f.key] as any)"
            @update:model-value="setVal(f.key, $event)"
          >
            <el-option
              v-for="o in f.options"
              :key="String(o.value)"
              :label="o.label"
              :value="(o.value as any)"
            />
          </el-select>
          <el-date-picker
            v-else-if="f.type === 'date'"
            type="date"
            :model-value="(form[f.key] as any)"
            @update:model-value="setVal(f.key, $event)"
          />
        </el-form-item>
      </template>
    </el-form>

    <template #footer>
      <div class="cp-fd-footer">
        <el-button @click="close">{{ labels?.cancel ?? '取消' }}</el-button>
        <el-button type="primary" :loading="submitting" @click="onConfirm">{{ labels?.confirm ?? '确认' }}</el-button>
      </div>
    </template>
  </el-dialog>
</template>

<style scoped>
.cp-fd-footer { display:flex; justify-content:flex-end; gap:8px; }
</style>

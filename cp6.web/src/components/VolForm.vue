<template>
  <el-dialog v-model="visible" :title="title" width="600px" @close="handleClose">
    <el-form ref="formRef" :model="localData" label-width="80px">
      <el-form-item
        v-for="col in visibleColumns"
        :key="col.prop"
        :label="col.label"
        :prop="col.prop"
        :rules="col.required ? [{ required: true, message: `${col.label}` }] : []"
      >
        <!-- 文本域 -->
        <el-input
          v-if="col.formType === 'textarea'"
          v-model="localData[col.prop]"
          type="textarea"
          :rows="4"
        />
        <!-- 开关 -->
        <el-switch
          v-else-if="col.formType === 'switch'"
          v-model="localData[col.prop]"
        />
        <!-- 下拉框 -->
        <el-select
          v-else-if="col.formType === 'select'"
          v-model="localData[col.prop]"
          clearable
          :placeholder="$t('table.select')"
          style="width: 100%"
        >
          <el-option
            v-for="opt in col.options"
            :key="opt.value"
            :label="opt.label"
            :value="opt.value"
          />
        </el-select>
        <!-- 密码框 -->
        <el-input
          v-else-if="col.formType === 'password'"
          v-model="localData[col.prop]"
          type="password"
          show-password
        />
        <!-- 默认输入框 -->
        <el-input v-else v-model="localData[col.prop]" />
      </el-form-item>
    </el-form>
    <template #footer>
      <el-button @click="visible = false">{{ $t('table.cancel') }}</el-button>
      <el-button type="primary" @click="handleSubmit">{{ $t('table.confirm') }}</el-button>
    </template>
  </el-dialog>
</template>

<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import type { FormInstance } from 'element-plus'
import type { ColumnConfig } from './VolTable.vue'

const props = defineProps<{
  title: string
  columns: ColumnConfig[]
  formData: any
}>()

const visible = defineModel<boolean>('visible', { default: false })
const emit = defineEmits<{ submit: [data: any] }>()

const formRef = ref<FormInstance>()
const localData = ref<any>({})

// 过滤掉隐藏字段
const visibleColumns = computed(() =>
  props.columns.filter((c) => !c.hidden && c.formType !== 'none')
)

// 当弹窗打开时，复制表单数据
watch(
  () => props.formData,
  (val) => {
    localData.value = { ...val }
  },
  { deep: true }
)

async function handleSubmit() {
  if (!formRef.value) return
  await formRef.value.validate()
  emit('submit', { ...localData.value })
}

function handleClose() {
  formRef.value?.resetFields()
}
</script>

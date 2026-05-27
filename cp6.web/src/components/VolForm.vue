<template>
  <el-dialog
    v-model="visible"
    :title="title"
    :width="isMobile ? '100vw' : '600px'"
    :top="isMobile ? '0' : '15vh'"
    :class="{ 'mobile-sheet': isMobile }"
    :show-close="true"
    :close-on-click-modal="!isMobile"
    @close="handleClose"
  >
    <el-form
      ref="formRef"
      :model="localData"
      :label-width="isMobile ? 'auto' : '80px'"
      :label-position="isMobile ? 'top' : 'right'"
      size="default"
    >
      <el-form-item
        v-for="col in visibleColumns"
        :key="col.prop"
        :label="col.label"
        :prop="col.prop"
        :rules="col.required ? [{ required: true, message: `${col.label}` }] : []"
      >
        <el-input
          v-if="col.formType === 'textarea'"
          v-model="localData[col.prop]"
          type="textarea"
          :rows="4"
        />
        <el-switch
          v-else-if="col.formType === 'switch'"
          v-model="localData[col.prop]"
          :inline-prompt="false"
        />
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
        <el-input
          v-else-if="col.formType === 'password'"
          v-model="localData[col.prop]"
          type="password"
          show-password
        />
        <el-input v-else v-model="localData[col.prop]" />
      </el-form-item>
    </el-form>
    <template #footer>
      <div class="vol-form-footer" :class="{ 'is-mobile': isMobile }">
        <el-button
          :size="isMobile ? 'large' : 'default'"
          :style="isMobile ? 'flex:1' : ''"
          @click="visible = false"
        >{{ $t('table.cancel') }}</el-button>
        <el-button
          :size="isMobile ? 'large' : 'default'"
          :style="isMobile ? 'flex:1' : ''"
          type="primary"
          @click="handleSubmit"
        >{{ $t('table.confirm') }}</el-button>
      </div>
    </template>
  </el-dialog>
</template>

<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import type { FormInstance } from 'element-plus'
import type { ColumnConfig } from './VolTable.vue'
import { useBreakpoint } from '@/composables/useBreakpoint'

const props = defineProps<{
  title: string
  columns: ColumnConfig[]
  formData: any
}>()

const visible = defineModel<boolean>('visible', { default: false })
const emit = defineEmits<{ submit: [data: any] }>()
const { isMobile } = useBreakpoint()

const formRef = ref<FormInstance>()
const localData = ref<any>({})

const visibleColumns = computed(() =>
  props.columns.filter((c) => !c.hidden && c.formType !== 'none')
)

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

<style scoped>
.vol-form-footer {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
}
.vol-form-footer.is-mobile {
  width: 100%;
  gap: 12px;
}
</style>

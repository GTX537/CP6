<template>
  <el-form :model="model" label-width="120px" size="small">
    <el-row :gutter="16">
      <el-col :span="8">
        <el-form-item :label="t('指図NO')">
          <el-input v-model="model.workOrderNo" disabled :placeholder="t('保存後に自動採番')" />
        </el-form-item>
      </el-col>
      <el-col :span="8">
        <el-form-item :label="t('拠点CD')" required>
          <el-input v-model="model.baseCd" :disabled="!isEditable" />
        </el-form-item>
      </el-col>
      <el-col :span="8">
        <el-form-item :label="t('優先度')">
          <el-select v-model="model.priority" :disabled="!isEditable">
            <el-option v-for="o in PRIORITY_OPTIONS" :key="o.value" :label="o.label" :value="o.value" />
          </el-select>
        </el-form-item>
      </el-col>
    </el-row>

    <el-divider content-position="left">{{ t('受注情報') }}</el-divider>
    <el-row :gutter="16">
      <el-col :span="6">
        <el-form-item :label="t('手配NO1')">
          <el-input v-model="model.orderNo1" :disabled="!isEditable" />
        </el-form-item>
      </el-col>
      <el-col :span="6">
        <el-form-item :label="t('手配NO2')">
          <el-input v-model="model.orderNo2" :disabled="!isEditable" />
        </el-form-item>
      </el-col>
      <el-col :span="6">
        <el-form-item :label="t('手配NO3')">
          <el-input v-model="model.orderNo3" :disabled="!isEditable" />
        </el-form-item>
      </el-col>
      <el-col :span="6">
        <el-form-item :label="t('受注Web NO')">
          <el-input v-model="model.webOrderNo" :disabled="!isEditable">
            <template #append>
              <el-button :icon="Search" @click="onExpand" :disabled="!isEditable || !model.webOrderNo" />
            </template>
          </el-input>
        </el-form-item>
      </el-col>
    </el-row>

    <el-divider content-position="left">{{ t('製品情報') }}</el-divider>
    <el-row :gutter="16">
      <el-col :span="6">
        <el-form-item :label="t('製品CD')" required>
          <el-input v-model="model.productCd" :disabled="!isEditable" />
        </el-form-item>
      </el-col>
      <el-col :span="12">
        <el-form-item :label="t('製品名')">
          <el-input v-model="model.productName" :disabled="!isEditable" />
        </el-form-item>
      </el-col>
      <el-col :span="6">
        <el-form-item :label="t('得意先CD')">
          <el-input v-model="model.customerCd" :disabled="!isEditable" />
        </el-form-item>
      </el-col>
    </el-row>

    <el-divider content-position="left">{{ t('数量・納期') }}</el-divider>
    <el-row :gutter="16">
      <el-col :span="6">
        <el-form-item :label="t('生産数量')" required>
          <el-input-number v-model="model.productionQty" :min="0" :precision="0" :disabled="!isEditable" style="width: 100%;" />
        </el-form-item>
      </el-col>
      <el-col :span="6">
        <el-form-item :label="t('ロットNO')">
          <el-input v-model="model.lotNo" :disabled="!isEditable" />
        </el-form-item>
      </el-col>
      <el-col :span="6">
        <el-form-item :label="t('客先納期')" required>
          <el-date-picker v-model="model.deliveryDate" type="date" value-format="YYYY-MM-DD" style="width: 100%;" :disabled="!isEditable" />
        </el-form-item>
      </el-col>
      <el-col :span="6"></el-col>
      <el-col :span="6">
        <el-form-item :label="t('計画開始日')" required>
          <el-date-picker v-model="model.planStartDate" type="date" value-format="YYYY-MM-DD" style="width: 100%;" :disabled="!isEditable" />
        </el-form-item>
      </el-col>
      <el-col :span="6">
        <el-form-item :label="t('計画完了日')" required>
          <el-date-picker v-model="model.planEndDate" type="date" value-format="YYYY-MM-DD" style="width: 100%;" :disabled="!isEditable" />
        </el-form-item>
      </el-col>
      <el-col :span="6">
        <el-form-item :label="t('完了数量')">
          <el-input-number v-model="model.completedQty" :min="0" :precision="0" disabled style="width: 100%;" />
        </el-form-item>
      </el-col>
      <el-col :span="6">
        <el-form-item :label="t('不良数量')">
          <el-input-number v-model="model.defectQty" :min="0" :precision="0" disabled style="width: 100%;" />
        </el-form-item>
      </el-col>
    </el-row>

    <el-form-item :label="t('備考')">
      <el-input v-model="model.remarks" type="textarea" :rows="2" :disabled="!isEditable" />
    </el-form-item>
  </el-form>
</template>

<script setup lang="ts">
import { useI18n } from 'vue-i18n'
const { t } = useI18n()
import { computed } from 'vue'
import { Search } from '@element-plus/icons-vue'
import { PRIORITY_OPTIONS, type WorkOrderDto } from '@/types/mes'

const props = defineProps<{
  modelValue: WorkOrderDto
  isEdit: boolean
}>()

const emit = defineEmits<{
  (e: 'update:modelValue', v: WorkOrderDto): void
  (e: 'expandFromOrder', webOrderNo: string): void
}>()

const model = computed({
  get: () => props.modelValue,
  set: v => emit('update:modelValue', v),
})

// 訂正可：Status=0(下書き) or 1(確定済)
const isEditable = computed(() => model.value.status === 0 || model.value.status === 1)

function onExpand() {
  if (model.value.webOrderNo) emit('expandFromOrder', model.value.webOrderNo)
}
</script>

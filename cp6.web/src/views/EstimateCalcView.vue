<template>
  <div class="estimate-calc">
    <!-- 顶部：操作种别 + 採番号 + 步骤条 -->
    <el-card shadow="never" class="header-card">
      <div class="header-row">
        <div class="header-left">
          <el-tag :type="opTagType" size="large" effect="dark">
            {{ opLabel }}
          </el-tag>
          <span v-if="store.basicInfo.qtnCalcNo" class="qtn-no">
            No. {{ store.basicInfo.qtnCalcNo }}
          </span>
        </div>
        <div class="header-right">
          <el-radio-group v-model="opModel" size="small" :disabled="hasNoNumber">
            <el-radio-button :value="OperationType.New">新建</el-radio-button>
            <el-radio-button :value="OperationType.Edit" :disabled="hasNoNumber">编辑</el-radio-button>
            <el-radio-button :value="OperationType.Copy" :disabled="hasNoNumber">流用</el-radio-button>
            <el-radio-button :value="OperationType.View" :disabled="hasNoNumber">查看</el-radio-button>
            <el-radio-button :value="OperationType.Delete" :disabled="hasNoNumber">删除</el-radio-button>
          </el-radio-group>
        </div>
      </div>

      <el-steps :active="store.currentStep - 1" finish-status="success" class="step-bar">
        <el-step title="基本信息" description="Step 1" />
        <el-step title="工程明细" description="Step 2" />
        <el-step title="计算结果" description="Step 3" />
      </el-steps>
    </el-card>

    <!-- 查询入力：按 No 加载 -->
    <el-card shadow="never" class="search-card" v-if="!store.isNew">
      <el-form inline>
        <el-form-item label="見積計算書No">
          <el-input v-model="searchNo" placeholder="例: 00000001-01" clearable style="width: 200px" />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" @click="loadByNo" :loading="loading">読込</el-button>
          <el-button @click="onNewClick">新規</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <!-- 步骤内容 -->
    <div class="step-content">
      <Step1BasicInfo v-if="store.currentStep === 1" ref="step1Ref" />
      <Step2Processes v-else-if="store.currentStep === 2" />
      <Step3Result v-else />
    </div>

    <!-- 底部操作按钮 -->
    <el-card shadow="never" class="footer-card">
      <div class="btn-row">
        <el-button v-if="store.currentStep > 1" @click="onPrev">上一步</el-button>
        <el-button
          v-if="btn.next && store.currentStep < 3"
          type="primary"
          @click="onNext"
        >
          下一步
        </el-button>
        <el-button
          v-if="btn.save"
          type="success"
          :loading="saving"
          @click="onSave"
        >
          保存
        </el-button>
        <el-button v-if="btn.del" type="danger" :loading="saving" @click="onDelete">删除</el-button>
        <el-button v-if="btn.close" @click="onReset">关闭</el-button>
        <el-button v-if="btn.cancel && !btn.close" @click="onReset">取消</el-button>
      </div>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onBeforeUnmount } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useEstimateStore } from '@/stores/estimate'
import { useFieldControl } from '@/composables/useFieldControl'
import { useConflictHandler } from '@/composables/useConflictHandler'
import { OperationType } from '@/types/estimateCalc'
import { estimateCalcApi } from '@/api/estimateCalc'
import Step1BasicInfo from './estimate/Step1BasicInfo.vue'
import Step2Processes from './estimate/Step2Processes.vue'
import Step3Result from './estimate/Step3Result.vue'

const store = useEstimateStore()
const { buttonVisibility } = useFieldControl()
const { handle: handleConflict } = useConflictHandler()

const step1Ref = ref<InstanceType<typeof Step1BasicInfo> | null>(null)
const searchNo = ref('')
const loading = ref(false)
const saving = ref(false)

// 操作种别 v-model 双向
const opModel = computed({
  get: () => store.operationType,
  set: (v: OperationType) => onOpChange(v),
})

const btn = computed(() => buttonVisibility.value)

const opLabel = computed(() => {
  switch (store.operationType) {
    case OperationType.New: return '新規'
    case OperationType.Edit: return '編集'
    case OperationType.Copy: return '流用'
    case OperationType.View: return '照会'
    case OperationType.Delete: return '削除'
    default: return ''
  }
})
const opTagType = computed<'primary' | 'success' | 'warning' | 'info' | 'danger'>(() => {
  switch (store.operationType) {
    case OperationType.New: return 'primary'
    case OperationType.Edit: return 'warning'
    case OperationType.Copy: return 'success'
    case OperationType.View: return 'info'
    case OperationType.Delete: return 'danger'
    default: return 'info'
  }
})

const hasNoNumber = computed(() => !store.basicInfo.qtnCalcNo)

// ============== 操作种别切换（5×4 矩阵） ==============
async function onOpChange(target: OperationType) {
  // 有未保存修改，确认
  if (store.isDirty) {
    try {
      await ElMessageBox.confirm('当前修改尚未保存，继续切换将丢弃？', '确认', {
        type: 'warning',
      })
    } catch {
      return
    }
  }

  const from = store.operationType
  store.setOperationType(target)

  // 新建：清空数据
  if (target === OperationType.New) {
    store.reset()
    store.setOperationType(OperationType.New)
    return
  }

  // 流用：从已有 No 复制
  if (target === OperationType.Copy && store.basicInfo.qtnCalcNo) {
    try {
      loading.value = true
      const res = await estimateCalcApi.copy(store.basicInfo.qtnCalcNo)
      if (res.code === 0) {
        store.loadBasicInfo(res.data)
        ElMessage.success('已生成流用副本')
      }
    } finally {
      loading.value = false
    }
    return
  }

  // 删除：无需重新拉数据（同一条记录改写 RO）
  if (target === OperationType.Delete || target === OperationType.View || target === OperationType.Edit) {
    // 若页面没数据但切到编辑/删除/查看，提示输入 No
    if (!store.basicInfo.qtnCalcNo) {
      ElMessage.info('请先输入見積計算書No並读取')
      store.setOperationType(from)
    }
  }
}

async function loadByNo() {
  if (!searchNo.value) {
    ElMessage.warning('请输入見積計算書No')
    return
  }
  try {
    loading.value = true
    const res = await estimateCalcApi.getByNo(searchNo.value)
    if (res.code === 0) {
      store.loadBasicInfo(res.data)
      ElMessage.success('读取成功')
    } else {
      ElMessage.warning(res.message)
    }
  } finally {
    loading.value = false
  }
}

function onNewClick() {
  store.reset()
  store.setOperationType(OperationType.New)
  searchNo.value = ''
}

async function onPrev() {
  if (store.currentStep > 1) store.setStep(store.currentStep - 1)
}

async function onNext() {
  if (store.currentStep === 1) {
    const ok = await step1Ref.value?.validate()
    if (!ok) return
  }
  store.setStep(store.currentStep + 1)
}

async function onSave() {
  if (store.currentStep === 1) {
    const ok = await step1Ref.value?.validate()
    if (!ok) return
  }
  try {
    saving.value = true
    if (store.isNew || store.isCopy) {
      const res = await estimateCalcApi.create(store.basicInfo)
      if (res.code === 0) {
        store.loadBasicInfo(res.data)
        store.setOperationType(OperationType.Edit)
        ElMessage.success('保存成功')
      }
    } else if (store.isEdit) {
      const no = store.basicInfo.qtnCalcNo!
      const res = await estimateCalcApi.update(no, store.basicInfo)
      if (res.code === 0) {
        store.loadBasicInfo(res.data)
        ElMessage.success('保存成功')
      }
    }
  } catch (e) {
    const handled = await handleConflict(e)
    if (!handled) throw e // 非 409，继续走默认错误 toast
  } finally {
    saving.value = false
  }
}

async function onDelete() {
  try {
    await ElMessageBox.confirm(`删除 ${store.basicInfo.qtnCalcNo} 后不可恢复（软删除），确定？`, '确认删除', {
      type: 'warning',
    })
  } catch {
    return
  }
  try {
    saving.value = true
    const res = await estimateCalcApi.remove(store.basicInfo.qtnCalcNo!, store.basicInfo.rowVersion)
    if (res.code === 0) {
      ElMessage.success('删除成功')
      store.reset()
    }
  } catch (e) {
    const handled = await handleConflict(e)
    if (!handled) throw e
  } finally {
    saving.value = false
  }
}

function onReset() {
  store.reset()
  searchNo.value = ''
}

// 页面卸载时 reset
onBeforeUnmount(() => {
  // 防止离开后下次再进带脏数据
  store.$reset?.()
})
</script>

<style scoped>
.estimate-calc {
  padding: 12px;
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.header-card :deep(.el-card__body),
.search-card :deep(.el-card__body),
.footer-card :deep(.el-card__body) {
  padding: 12px 16px;
}
.header-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 12px;
}
.header-left {
  display: flex;
  align-items: center;
  gap: 12px;
}
.qtn-no {
  font-size: 16px;
  font-weight: 600;
  color: #409eff;
}
.step-bar {
  margin-top: 8px;
}
.step-content {
  flex: 1;
  min-height: 400px;
}
.btn-row {
  display: flex;
  gap: 8px;
  justify-content: flex-end;
}
</style>

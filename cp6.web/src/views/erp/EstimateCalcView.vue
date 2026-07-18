<template>
  <div class="estimate-calc" :class="{ 'is-mobile': isMobile }">
    <!-- 顶部：操作种别 + 採番号 + 步骤条 -->
    <el-card shadow="never" class="header-card">
      <div class="header-row">
        <div class="header-left">
          <el-tag :type="opTagType" :size="isMobile ? 'default' : 'large'" effect="dark">
            {{ opLabel }}
          </el-tag>
          <span v-if="store.basicInfo.qtnCalcNo" class="qtn-no">
            No. {{ store.basicInfo.qtnCalcNo }}
          </span>
        </div>
        <div class="header-right">
          <el-radio-group v-model="opModel" size="small" :disabled="hasNoNumber">
            <el-radio-button :value="OperationType.New">{{ t('sales.btn.new') }}</el-radio-button>
            <el-radio-button :value="OperationType.Edit" :disabled="hasNoNumber">{{ t('sales.op.edit') }}</el-radio-button>
            <el-radio-button :value="OperationType.Copy" :disabled="hasNoNumber">{{ t('sales.op.copy') }}</el-radio-button>
            <el-radio-button :value="OperationType.View" :disabled="hasNoNumber">{{ t('sales.op.view') }}</el-radio-button>
            <el-radio-button :value="OperationType.Delete" :disabled="hasNoNumber">{{ t('sales.op.delete') }}</el-radio-button>
          </el-radio-group>
        </div>
      </div>

      <el-steps
        :active="store.currentStep - 1"
        finish-status="success"
        class="step-bar"
        :simple="isMobile"
      >
        <el-step :title="isMobile ? t('基本') : t('sales.section.basicInfo')" :description="isMobile ? '' : 'Step 1'" />
        <el-step :title="isMobile ? t('工程') : t('sales.section.process')" :description="isMobile ? '' : 'Step 2'" />
        <el-step :title="isMobile ? t('結果') : t('sales.list.detail')" :description="isMobile ? '' : 'Step 3'" />
      </el-steps>
    </el-card>

    <!-- 查询入力：按 No 加载 -->
    <el-card shadow="never" class="search-card" v-if="!store.isNew">
      <el-form inline>
        <el-form-item :label="t('sales.term.calcNo')">
          <el-input v-model="searchNo" :placeholder="t('例: 00000001-01')" clearable style="width: 200px" />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" @click="loadByNo" :loading="loading">{{ t('sales.btn.load') }}</el-button>
          <el-button @click="onNewClick">{{ t('sales.btn.new') }}</el-button>
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
        <el-button v-if="store.currentStep > 1" @click="onPrev">{{ t('sales.btn.prev') }}</el-button>
        <el-button
          v-if="btn.next && store.currentStep < 3"
          type="primary"
          @click="onNext"
        >
          {{ t('sales.btn.next') }}
        </el-button>
        <el-button
          v-if="btn.save"
          v-permission="'erp-estimate-calc:add'"
          type="success"
          :loading="saving"
          @click="onSave"
        >
          {{ t('sales.btn.save') }}
        </el-button>
        <el-button v-if="btn.del" v-permission="'erp-estimate-calc:del'" type="danger" :loading="saving" @click="onDelete">{{ t('sales.op.delete') }}</el-button>
        <el-button v-if="btn.close" @click="onReset">{{ t('sales.btn.cancel') }}</el-button>
        <el-button v-if="btn.cancel && !btn.close" @click="onReset">{{ t('sales.btn.cancel') }}</el-button>
      </div>
    </el-card>
    <div v-if="isMobile" class="mobile-footer-spacer" />
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onBeforeUnmount, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'

const { t } = useI18n()
import { useEstimateStore } from '@/stores/estimate'
import { useFieldControl } from '@/composables/useFieldControl'
import { useConflictHandler } from '@/composables/useConflictHandler'
import { OperationType } from '@/types/erp/estimateCalc'
import { estimateCalcApi } from '@/api/erp/estimateCalc'
import Step1BasicInfo from './estimate/Step1BasicInfo.vue'
import Step2Processes from './estimate/Step2Processes.vue'
import Step3Result from './estimate/Step3Result.vue'
import { useBreakpoint } from '@/composables/useBreakpoint'

const { isMobile } = useBreakpoint()
const store = useEstimateStore()
const route = useRoute()
const { buttonVisibility } = useFieldControl()
const { handle: handleConflict } = useConflictHandler()

// 独立窗口模式（popup 打开时 standalone=true）
const isStandalone = computed(() => !!route.meta?.standalone)

// 向父窗口广播保存/删除成功，便于列表自动刷新
function notifyOpener(type: 'saved' | 'deleted') {
  try {
    if (window.opener && !window.opener.closed) {
      window.opener.postMessage({ source: 'cp6-estimate', type }, window.location.origin)
    }
  } catch {
    // 跨源或已关闭则忽略
  }
}

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
    case OperationType.New: return t('sales.op.register')
    case OperationType.Edit: return t('sales.op.edit')
    case OperationType.Copy: return t('sales.op.copy')
    case OperationType.View: return t('sales.op.view')
    case OperationType.Delete: return t('sales.op.delete')
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
      await ElMessageBox.confirm(t('sales.msg.unsavedChanges'), t('sales.msg.confirmTitle'), {
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
        ElMessage.success(t('sales.msg.loadSuccess'))
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
      ElMessage.info(t('sales.err.E10022'))
      store.setOperationType(from)
    }
  }
}

async function loadByNo() {
  if (!searchNo.value) {
    ElMessage.warning(t('sales.err.E10022'))
    return
  }
  try {
    loading.value = true
    const res = await estimateCalcApi.getByNo(searchNo.value)
    if (res.code === 0) {
      store.loadBasicInfo(res.data)
      ElMessage.success(t('sales.msg.loadSuccess'))
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
        ElMessage.success(t('sales.msg.saveSuccess'))
        notifyOpener('saved')
      }
    } else if (store.isEdit) {
      const no = store.basicInfo.qtnCalcNo!
      const res = await estimateCalcApi.update(no, store.basicInfo)
      if (res.code === 0) {
        store.loadBasicInfo(res.data)
        ElMessage.success(t('sales.msg.saveSuccess'))
        notifyOpener('saved')
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
    await ElMessageBox.confirm(`${t('sales.msg.deleteConfirm')} (${store.basicInfo.qtnCalcNo})`, t('sales.msg.confirmTitle'), {
      type: 'warning',
    })
  } catch {
    return
  }
  try {
    saving.value = true
    const res = await estimateCalcApi.remove(store.basicInfo.qtnCalcNo!, store.basicInfo.rowVersion)
    if (res.code === 0) {
      ElMessage.success(t('sales.msg.deleteSuccess'))
      store.reset()
      notifyOpener('deleted')
    }
  } catch (e) {
    const handled = await handleConflict(e)
    if (!handled) throw e
  } finally {
    saving.value = false
  }
}

function onReset() {
  // 独立窗口：直接关闭窗口
  if (isStandalone.value) {
    if (store.isDirty) {
      ElMessageBox.confirm(t('sales.msg.unsavedChanges'), t('sales.msg.confirmTitle'), { type: 'warning' })
        .then(() => window.close())
        .catch(() => {})
    } else {
      window.close()
    }
    return
  }
  store.reset()
  searchNo.value = ''
}

// 页面卸载时 reset（setup-style store なので $reset は無し → 自前の reset() を呼ぶ）
onBeforeUnmount(() => {
  // 防止离开后下次再进带脏数据
  if (typeof store.reset === 'function') store.reset()
})

// ============== URL 参数驱动（独立窗口时用）==============
// URL 示例：
//   /estimate-calc/window?op=new
//   /estimate-calc/window?op=view&no=00000003-01
//   /estimate-calc/window?op=edit&no=00000003-01
//   /estimate-calc/window?op=copy&no=00000003-01
onMounted(async () => {
  const opParam = String(route.query.op || '').toLowerCase()
  const noParam = route.query.no ? String(route.query.no) : ''

  const opMap: Record<string, OperationType> = {
    new: OperationType.New,
    edit: OperationType.Edit,
    view: OperationType.View,
    delete: OperationType.Delete,
    copy: OperationType.Copy,
  }
  const op = opMap[opParam]
  if (op == null) return // 无 query → 走默认（store 初始值）

  store.reset()

  if (op === OperationType.New) {
    store.setOperationType(OperationType.New)
  } else if (noParam) {
    try {
      loading.value = true
      const res = await estimateCalcApi.getByNo(noParam, op === OperationType.View)
      if (res.code === 0) {
        store.loadBasicInfo(res.data)
        store.setOperationType(op)
      } else {
        ElMessage.warning(res.message || `No=${noParam} not found`)
      }
    } finally {
      loading.value = false
    }
  }

  // 标题：独立窗口时改为更有区分度的标题
  if (isStandalone.value) {
    const labels: Record<number, string> = {
      [OperationType.New]: t('sales.op.register'),
      [OperationType.Edit]: t('sales.op.edit'),
      [OperationType.View]: t('sales.op.view'),
      [OperationType.Delete]: t('sales.op.delete'),
      [OperationType.Copy]: t('sales.op.copy'),
    }
    const title = `${t('sales.term.calcNo')} - ${labels[op] ?? ''}${noParam ? ` - ${noParam}` : ''}`
    try { document.title = title } catch {}
  }
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
  color: var(--cp-brand);
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
  flex-wrap: wrap;
}

/* ============ 手机端优化 ============ */
.estimate-calc.is-mobile {
  padding: 10px;
  padding-bottom: 80px; /* sticky footer 占位 */
  gap: 10px;
}
.estimate-calc.is-mobile .header-card :deep(.el-card__body),
.estimate-calc.is-mobile .search-card :deep(.el-card__body) {
  padding: 10px 12px;
}
.estimate-calc.is-mobile .header-row {
  flex-direction: column;
  align-items: stretch;
  gap: 10px;
  margin-bottom: 10px;
}
.estimate-calc.is-mobile .header-left {
  font-size: 13px;
  gap: 8px;
}
.estimate-calc.is-mobile .qtn-no {
  font-size: 14px;
}
.estimate-calc.is-mobile .header-right :deep(.el-radio-group) {
  display: flex;
  width: 100%;
}
.estimate-calc.is-mobile .header-right :deep(.el-radio-button) {
  flex: 1;
}
.estimate-calc.is-mobile .header-right :deep(.el-radio-button__inner) {
  width: 100%;
  padding: 8px 2px;
  font-size: 12px;
}
.estimate-calc.is-mobile .step-bar {
  margin-top: 4px;
}
.estimate-calc.is-mobile .step-content {
  min-height: 200px;
}
/* sticky footer */
.estimate-calc.is-mobile .footer-card {
  position: fixed;
  bottom: 0;
  left: 0;
  right: 0;
  margin: 0;
  border-radius: 0;
  z-index: 50;
  box-shadow: var(--cp-shadow-up);
  padding-bottom: env(safe-area-inset-bottom, 0);
}
.estimate-calc.is-mobile .footer-card :deep(.el-card__body) {
  padding: 10px 12px;
}
.estimate-calc.is-mobile .btn-row {
  justify-content: stretch;
  gap: 6px;
}
.estimate-calc.is-mobile .btn-row :deep(.el-button) {
  flex: 1;
  margin-left: 0 !important;
  min-width: 0;
  padding: 8px 4px;
}
.mobile-footer-spacer { height: 20px; }
</style>

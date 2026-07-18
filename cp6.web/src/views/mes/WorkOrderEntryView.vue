<template>
  <div class="work-order-entry">
    <el-card shadow="never">
      <template #header>
        <div style="display: flex; justify-content: space-between; align-items: center;">
          <div>
            <el-tag size="large">{{ isEdit ? t('訂正') : t('新規') }}</el-tag>
            <span style="margin-left: 12px; font-weight: 600;">
              {{ t('製造指図入力') }} (MSBBME020)
              <span v-if="form.workOrderNo" style="margin-left: 12px; color: #409EFF;">
                {{ form.workOrderNo }}
              </span>
            </span>
            <el-tag v-if="form.workOrderNo" style="margin-left: 12px;"
              :color="getStatusColor(form.status)" effect="dark" size="small">
              {{ getStatusLabel(form.status) }}
            </el-tag>
          </div>
          <div>
            <el-button @click="$router.back()">{{ t('戻る') }}</el-button>
          </div>
        </div>
      </template>

      <!-- ステップバー -->
      <el-steps :active="step" finish-status="success" align-center style="margin-bottom: 20px;">
        <el-step :title="t('指図基本情報')" :description="t('第1页：基本情報')" />
        <el-step :title="t('工程計画')" :description="t('第2页：工程一覧')" />
        <el-step :title="t('材料手配 + 保存')" :description="t('第3页：材料一覧')" />
      </el-steps>

      <!-- 第1页 -->
      <WoStep1BasicInfo
        v-show="step === 0"
        v-model="form"
        :is-edit="isEdit"
        @expand-from-order="onExpandFromOrder"
      />
      <!-- 第2页 -->
      <WoStep2ProcessPlan v-show="step === 1" v-model="form.processes" :production-qty="form.productionQty" />
      <!-- 第3页 -->
      <WoStep3MaterialConfirm
        v-show="step === 2"
        v-model="form.materials"
        :production-qty="form.productionQty"
      />

      <div style="margin-top: 24px; text-align: right;">
        <el-button v-if="step > 0" @click="step--">← {{ t('戻る') }}</el-button>
        <el-button v-if="step < 2" type="primary" @click="next">{{ t('次画面') }} →</el-button>
        <template v-if="step === 2">
          <el-button v-if="isEdit" v-permission="'mes-work-order:edit'" type="primary" :loading="saving" @click="onSave">{{ t('保存') }}</el-button>
          <el-button v-else v-permission="'mes-work-order:add'" type="primary" :loading="saving" @click="onSave">{{ t('保存') }}</el-button>
          <el-button v-if="canIssue" v-permission="'mes-work-order:issue'" type="success" :loading="saving" @click="onIssue">{{ t('指図発行') }}</el-button>
        </template>
      </div>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { useI18n } from 'vue-i18n'
const { t } = useI18n()
import { ref, reactive, computed, onMounted, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { workOrderApi } from '@/api/mes/mes'
import { WORK_ORDER_STATUS_OPTIONS, type WorkOrderDto } from '@/types/mes/mes'
import WoStep1BasicInfo from './work-order/steps/WoStep1BasicInfo.vue'
import WoStep2ProcessPlan from './work-order/steps/WoStep2ProcessPlan.vue'
import WoStep3MaterialConfirm from './work-order/steps/WoStep3MaterialConfirm.vue'

const route = useRoute()
const router = useRouter()
const step = ref(0)
const saving = ref(false)

const emptyForm = (): WorkOrderDto => ({
  workOrderNo: '',
  status: 0,
  orderNo1: '',
  orderNo2: '',
  orderNo3: '',
  webOrderNo: '',
  customerCd: '',
  productCd: '',
  productName: '',
  productionQty: 0,
  completedQty: 0,
  defectQty: 0,
  deliveryDate: null,
  planStartDate: null,
  planEndDate: null,
  priority: 1,
  lotNo: '',
  baseCd: '',
  remarks: '',
  processCount: 0,
  completedProcessCount: 0,
  progressRate: 0,
  delayDays: 0,
  processes: [],
  materials: [],
})

const form = reactive<WorkOrderDto>(emptyForm())

const isEdit = computed(() => !!form.workOrderNo)
const canIssue = computed(() => isEdit.value && (form.status === 0 || form.status === 1))

function getStatusLabel(v: number) {
  return t(WORK_ORDER_STATUS_OPTIONS.find(s => s.value === v)?.label ?? `${v}`)
}
function getStatusColor(v: number) {
  return WORK_ORDER_STATUS_OPTIONS.find(s => s.value === v)?.color ?? '#909399'
}

async function loadByNo(no: string) {
  const res = await workOrderApi.get(no)
  if (res.code === 0 && res.data) {
    Object.assign(form, emptyForm())
    Object.assign(form, res.data)
  }
}

async function onExpandFromOrder(webOrderNo: string) {
  try {
    await ElMessageBox.confirm(
      t('受注 {webOrderNo} から製造指図を自動展開します。よろしいですか？', { webOrderNo }),
      t('確認'),
      { type: 'info' }
    )
    saving.value = true
    const res = await workOrderApi.expandFromOrder({ webOrderNo, priority: 1 })
    const nos = res.data?.workOrderNos || []
    const first = nos[0]
    if (first) {
      ElMessage.success(t('{count}件の指図を作成しました：{nos}', { count: nos.length, nos: nos.join(', ') }))
      await loadByNo(first)
    }
  } catch (e) {
    // cancelled
  } finally {
    saving.value = false
  }
}

function validateStep1(): boolean {
  if (!isEdit.value && !form.orderNo1 && !form.orderNo2 && !form.orderNo3 && !form.webOrderNo && !form.productCd) {
    ElMessage.error(t('ME-MSG-001: 手配NO or 製品CD が未入力です'))
    return false
  }
  if (form.productionQty <= 0) {
    ElMessage.error(t('ME-MSG-002: 生産数量は0より大きい値を入力してください'))
    return false
  }
  if (form.planStartDate && form.planEndDate && form.planStartDate > form.planEndDate) {
    ElMessage.error(t('ME-MSG-003: 計画開始日は計画完了日以前を指定してください'))
    return false
  }
  if (form.planEndDate && form.deliveryDate && form.planEndDate > form.deliveryDate) {
    ElMessageBox.confirm(t('ME-MSG-004: 計画完了日が客先納期を超えています。よろしいですか？'), t('警告'), {
      type: 'warning',
    }).then(() => { step.value = 1 }).catch(() => {})
    return false
  }
  return true
}

function validateStep2(): boolean {
  if (!form.processes || form.processes.length === 0) {
    ElMessage.error(t('ME-MSG-006: 登録する工程がありません'))
    return false
  }
  // 重複チェック
  const keys = new Set<string>()
  for (const p of form.processes) {
    const k = `${p.processCd}|${p.taskCd}`
    if (keys.has(k)) {
      ElMessage.error(t('ME-MSG-007: 同一の工程コード・作業CDが含まれます'))
      return false
    }
    keys.add(k)
  }
  return true
}

function next() {
  if (step.value === 0 && !validateStep1()) return
  if (step.value === 1 && !validateStep2()) return
  step.value += 1
}

async function onSave() {
  if (!validateStep1()) { step.value = 0; return }
  if (!validateStep2()) { step.value = 1; return }
  saving.value = true
  try {
    if (isEdit.value) {
      await workOrderApi.update(form.workOrderNo, form)
      ElMessage.success(t('ME-MSG-041: 保存が完了しました'))
      await loadByNo(form.workOrderNo)
    } else {
      const res = await workOrderApi.create(form)
      const no = res.data?.workOrderNo
      ElMessage.success(t('ME-MSG-041: 保存が完了しました（{no}）', { no }))
      if (no) await loadByNo(no)
    }
  } finally {
    saving.value = false
  }
}

async function onIssue() {
  try {
    await ElMessageBox.confirm(t('指図を発行します。発行後は基本情報・工程・材料の編集ができません。よろしいですか？'), t('確認'), {
      type: 'warning',
    })
    saving.value = true
    await workOrderApi.issue(form.workOrderNo)
    ElMessage.success(t('指図を発行しました'))
    await loadByNo(form.workOrderNo)
  } catch (e) {
    // cancelled
  } finally {
    saving.value = false
  }
}

onMounted(async () => {
  const no = route.query.no as string | undefined
  if (no) await loadByNo(no)
})

watch(() => route.query.no, async (no) => {
  if (no) await loadByNo(no as string)
  else Object.assign(form, emptyForm())
})
</script>

<style scoped>
.work-order-entry {
  padding: 16px;
}
</style>

<template>
  <div class="retention-controls">
    <el-button @click="open">审计保留与清理</el-button>
    <el-dialog v-model="visible" title="审计保留与清理" width="min(670px, 95vw)" destroy-on-close>
      <p>按记录类别使用已批准的保留政策。执行时会重新核对权限、保留限制和记录版本。</p>
      <el-form label-position="top">
        <el-form-item label="记录类别">
          <el-select v-model="kind" :disabled="busy || !!planId">
            <el-option label="技术遥测" value="TECH_TELEMETRY" />
            <el-option label="安全事件" value="SECURITY_EVENT" />
            <el-option label="字段历史" value="FIELD_HISTORY" />
          </el-select>
        </el-form-item>
        <el-form-item label="原计划编号（新建预览会自动填写）">
          <el-input :model-value="planId" :disabled="busy || !!plan" maxlength="36" @update:model-value="changeId" />
        </el-form-item>
      </el-form>
      <el-alert v-if="message" :title="message" type="warning" :closable="false" />
      <el-descriptions v-if="plan" :column="1" border>
        <el-descriptions-item label="政策版本">{{ plan.policyVersion }}</el-descriptions-item>
        <el-descriptions-item label="处置方式">{{ plan.method === 'ANONYMIZE' ? '匿名化' : '删除' }}</el-descriptions-item>
        <el-descriptions-item label="保留截止时间（UTC）">{{ plan.cutoff }}</el-descriptions-item>
        <el-descriptions-item label="预览有效期至">{{ plan.expiresAt }}</el-descriptions-item>
        <el-descriptions-item label="计划内符合条件记录">{{ plan.eligible }}</el-descriptions-item>
        <el-descriptions-item v-if="plan.state !== 'READY'" label="处置回执">
          {{ plan.state === 'COMPLETED' ? '已完成' : plan.state === 'EXPIRED' ? '已过期' : '已阻止' }} · {{ plan.affected }} 条 · {{ plan.completedAt }}
          <span v-if="plan.reason && plan.reason !== 'APPLIED'">（{{ plan.reason }}）</span>
        </el-descriptions-item>
      </el-descriptions>
      <template #footer>
        <el-button v-if="!plan" :disabled="!!planId && !auditGuid.test(planId)" :loading="busy" @click="preview">预览计划</el-button>
        <el-button v-if="planId" :disabled="!auditGuid.test(planId)" :loading="busy" @click="read">查询原计划</el-button>
        <el-popconfirm v-if="plan?.state === 'READY'" title="确认按当前计划执行处置？" @confirm="execute">
          <template #reference><el-button type="danger" :disabled="busy || readRequired">确认执行</el-button></template>
        </el-popconfirm>
        <el-button v-if="plan && plan.state !== 'READY'" :disabled="busy" @click="clear">开始新计划</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'
import { auditAccess, auditGuid, type AuditClass, type AuditDisposal } from '@/api/sys/auditAccess'
import { AUTH_EVENT, identityStamp } from '@/auth/identity'

const emit = defineEmits<{ completed: [] }>()
const visible = ref(false), busy = ref(false), readRequired = ref(false), message = ref('')
const kind = ref<AuditClass>('TECH_TELEMETRY'), planId = ref(''), plan = ref<AuditDisposal>()
const resumeKey = 'cp6-audit-retention-plan-v1'
let generation = 0

function open() {
  visible.value = true
  const saved = sessionStorage.getItem(resumeKey)
  const [savedKind, id, ...stamp] = saved?.split('|') ?? []
  if (!plan.value && !planId.value && savedKind && id && auditGuid.test(id) && stamp.join('|') === identityStamp()) {
    kind.value = savedKind as AuditClass
    planId.value = id
    void read()
  }
}

function changeId(id: string) {
  planId.value = id.trim()
  message.value = ''
}

function remember() {
  sessionStorage.setItem(resumeKey, `${kind.value}|${planId.value}|${identityStamp()}`)
}

async function run(request: () => Promise<AuditDisposal>, failed: string) {
  if (busy.value) return
  const id = ++generation, stamp = identityStamp()
  const current = () => id === generation && stamp === identityStamp()
  busy.value = true
  message.value = ''
  try {
    const result = await request()
    if (!current()) return
    plan.value = result
    kind.value = result.retentionClass
    readRequired.value = false
    remember()
    if (result.state === 'COMPLETED') emit('completed')
  } catch {
    if (current()) message.value = failed
  } finally {
    if (current()) busy.value = false
  }
}

function preview() {
  if (!planId.value) planId.value = crypto.randomUUID()
  if (!auditGuid.test(planId.value)) return
  remember()
  return run(() => auditAccess.preview(kind.value, planId.value), '预览尚未确认。先查询原编号，或用同一编号重试预览。')
}
function read() {
  if (!auditGuid.test(planId.value)) return
  return run(() => auditAccess.readPlan(planId.value), '未能读取原计划。请核对权限、租户及编号。')
}
function execute() {
  if (readRequired.value || plan.value?.state !== 'READY') return
  readRequired.value = true
  return run(() => auditAccess.executePlan(planId.value), '执行结果待核对。请先查询原计划和回执。')
}

function clear() {
  ++generation
  plan.value = undefined
  planId.value = ''
  readRequired.value = false
  message.value = ''
  busy.value = false
  sessionStorage.removeItem(resumeKey)
}
function identityChanged() { visible.value = false; clear() }
onMounted(() => window.addEventListener(AUTH_EVENT, identityChanged))
onBeforeUnmount(() => { ++generation; window.removeEventListener(AUTH_EVENT, identityChanged) })
</script>

<style scoped>
.retention-controls { margin-top: 16px; }
</style>

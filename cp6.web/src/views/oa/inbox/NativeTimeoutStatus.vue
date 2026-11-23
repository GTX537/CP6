<script setup lang="ts">
import type { NativeDetail } from '@/api/oa/nativeInbox'
defineProps<{ value: NonNullable<NativeDetail['timeout']> }>()
const state = (s: string) => ({ READY: '等待截止时间', CLAIMED: '执行器已领取', COMPLETED: '已执行', BLOCKED_POLICY: '政策不允许自动决定', BLOCKED_TARGET: '升级目标不可用', INVALID_POLICY: '截止政策无效', SKIPPED_ALREADY_HANDLED: '已由其他操作办理', ALREADY_HANDLED: '已办理，等待扫描记录跳过', SUSPENDED: '暂时挂起，恢复后沿用原截止时间' }[s] ?? s)
const action = (s: string | null) => ({ remind: '提醒', approve: '系统同意', reject: '系统拒绝', escalate: '升级', errorEdge: '走错误分支' }[s ?? ''] ?? '无效动作')
const stageLabel: Record<string, string> = {
  CODE_PRESENT: '代码实现', HOST_REGISTERED: '宿主登记', CONFIG_ENABLED: '当前配置启用',
  OBSERVED_RUNNING: '最近扫描观察', LAST_EFFECT_VERIFIED: '最近核实的业务效果'
}
function stageValue(layer: string, confirmed: boolean | null) {
  if (confirmed === null) return '暂无证据'
  if (layer === 'CONFIG_ENABLED') return confirmed ? '已启用' : '未启用'
  return confirmed ? '已核实' : '未观察到'
}
const runLabel: Record<string, string> = {
  COMPLETED_NO_CHANGES: '确认零变更', COMPLETED_WITH_VERIFIED_RESULTS: '完成并核实回执',
  PARTIALLY_COMPLETED: '部分处理', BLOCKED: '已阻断', UNKNOWN: '结果待核实', RUNNING: '已领取，结果待回报',
  APPLIED: '已提交', ALREADY_TERMINAL: '原任务已有终态', FAILED_BEFORE_EFFECT: '未产生效果即失败',
  OWNER_VERIFIED: '已核实原 Owner 回执', RETRYING: '可重试的临时失败', ISOLATED: '已隔离'
}
const missedLabel: Record<string, string> = {
  SKIP: '跳过错过周期', CATCH_UP_BOUNDED: '有限补跑', CONTINUE_ORIGINAL: '沿原操作继续'
}
</script>
<template>
  <section aria-label="超时处理状态" class="timeout-status">
    <h4>截止时间与超时处理</h4>
    <p v-if="value.missingEvidenceTaskIds.length" role="alert">以下历史任务缺少可信截止事实（MISSING_EVIDENCE），不会按当前新版政策猜测执行：{{ value.missingEvidenceTaskIds.join('、') }}。</p>
    <p v-if="value.runtimeState === 'UNPROVEN'">尚无执行器实际扫描记录，不能确认超时处理正在运行。</p>
    <p v-else>最近实际扫描：{{ value.lastScan }}。这不代表每项超时动作已成功。</p>
    <p>当前可见流程中的逾期项：{{ value.overdueCount }}；最早逾期：{{ value.oldestDue || '无' }}；最近已确认动作：{{ value.lastEffect || '无' }}。</p>
    <dl class="runtime-stages">
      <template v-for="stage in value.stages" :key="stage.layer">
        <dt>{{ stageLabel[stage.layer] ?? stage.layer }}</dt>
        <dd :data-layer="stage.layer">{{ stage.observedAt ?? stageValue(stage.layer, stage.confirmed) }}</dd>
      </template>
    </dl>
    <details v-if="value.runs?.length">
      <summary>查看作业运行与原回执</summary>
      <ol aria-label="可见作业运行">
        <li v-for="run in value.runs" :key="run.runId" :data-run-id="run.runId">
          <p>{{ run.evaluatedAt }} · {{ runLabel[run.state] ?? run.state }}</p>
          <p>候选 {{ run.candidateCount }} · 已提交 {{ run.appliedCount }} · 原已终态 {{ run.alreadyTerminalCount }} · 阻断 {{ run.blockedCount }} · 未知 {{ run.unknownCount }} · 失败 {{ run.failedCount }} · 在途 {{ run.inFlightCount }}</p>
          <details>
            <summary>查看执行明细</summary>
            <p>运行编号 {{ run.runId }}<span v-if="run.isRecovery"> · 原操作恢复</span> · {{ missedLabel[run.missedRun] ?? run.missedRun }}<span v-if="run.slotAt"> · 原周期 {{ run.slotAt }}</span></p>
            <p>调度版本 {{ run.scheduleVersion }} · 日历版本 {{ run.calendarVersion }} · 时区 {{ run.timeZoneId }}（{{ run.timeZoneVersion }}）</p>
            <ul>
              <li v-for="unit in run.units" :key="unit.attemptId">
                <p>任务 {{ unit.taskId }} · {{ runLabel[unit.state] ?? unit.state }}</p>
                <p>原业务操作 {{ unit.operationId }} · 领取尝试 {{ unit.attemptId }} · 代次 {{ unit.epoch }}</p>
                <p v-if="unit.commandId">Owner 命令 {{ unit.commandId }}<span v-if="unit.messageIds.length"> · 消息 {{ unit.messageIds.join('、') }}</span></p>
                <ol v-if="unit.observations.length" aria-label="原作业结果时间线">
                  <li v-for="observation in unit.observations" :key="observation.sequence">
                    {{ observation.observedAt }} · {{ runLabel[observation.state] ?? observation.state }}<span v-if="observation.reason">（{{ observation.reason }}）</span>
                  </li>
                </ol>
              </li>
            </ul>
          </details>
        </li>
      </ol>
    </details>
    <ul><li v-for="deadline in value.deadlines" :key="`${deadline.taskId}:${deadline.revision}`">
      <p>任务 {{ deadline.taskId }}：{{ state(deadline.state) }}。截止 {{ deadline.dueAt || '未能计算' }}；动作：{{ action(deadline.policy.action) }}。</p>
      <p v-if="deadline.policy.workday">从起始日之后计算 {{ deadline.policy.workday.workdays }} 个工作日，目标当地时间 {{ deadline.policy.workday.localTime }}。</p>
      <p v-else>固定小时数 {{ deadline.policy.hours }}，按 UTC 时长计算。</p>
      <p>原租户时区 {{ deadline.policy.timeZoneId || '无可信配置' }}。当前次数 {{ deadline.occurrence }}；下次截止 {{ deadline.nextDue || '无' }}。</p>
      <p v-if="deadline.policy.failureCode" role="status">计算受阻（{{ deadline.policy.failureCode }}）。</p>
      <p v-if="deadline.lastResult">原执行结果：{{ state(deadline.lastResult.state) }}<span v-if="deadline.lastResult.reason">（{{ deadline.lastResult.reason }}）</span>，{{ deadline.lastResult.occurredAt }}。系统执行记录不计作人工审批票。</p>
      <details>
        <summary>查看采用的版本与执行记录</summary>
        <p>流程版本 {{ deadline.policy.flowVersionId }}；节点 {{ deadline.policy.nodeId }}；截止版本 {{ deadline.revision }}；租约版本 {{ deadline.claimEpoch ?? '无活动领取' }}；租约期限 {{ deadline.leaseUntil || '无' }}。</p>
        <p v-if="deadline.policy.calendarId">工作日历 {{ deadline.policy.calendarId }}；日历版本 {{ deadline.policy.calendarRevision }}。</p>
        <p>时区规则版本 {{ deadline.policy.timeZoneRulesVersion || '缺少原始记录' }}<span v-if="deadline.policy.localDueAt">；当地截止 {{ deadline.policy.localDueAt }}；UTC 偏移 {{ deadline.policy.offsetMinutes }} 分钟</span>。</p>
        <p v-if="deadline.lastResult">执行编号 {{ deadline.lastResult.executionId }}；执行前任务版本 {{ deadline.lastResult.originalTaskVersion || '缺少原始记录' }}；执行前实例版本 {{ deadline.lastResult.originalInstanceVersion || '缺少原始记录' }}；服务身份 {{ deadline.lastResult.serviceId }}；原命令 {{ deadline.lastResult.commandId || '无业务写入' }}。</p>
      </details>
    </li></ul>
  </section>
</template>
<style scoped>.timeout-status { border: 1px solid var(--el-border-color); padding: 12px; margin-block: 16px; }p, dd { overflow-wrap: anywhere; }.runtime-stages { display: grid; grid-template-columns: max-content 1fr; gap: 4px 12px; }.runtime-stages dd { margin: 0; }</style>

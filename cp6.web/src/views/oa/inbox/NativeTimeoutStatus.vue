<script setup lang="ts">
import type { NativeDetail } from '@/api/oa/nativeInbox'
defineProps<{ value: NonNullable<NativeDetail['timeout']> }>()
const state = (s: string) => ({ READY: '等待截止时间', CLAIMED: '执行器已领取', COMPLETED: '已执行', BLOCKED_POLICY: '政策不允许自动决定', BLOCKED_TARGET: '升级目标不可用', INVALID_POLICY: '截止政策无效', SKIPPED_ALREADY_HANDLED: '已由其他操作办理', ALREADY_HANDLED: '已办理，等待扫描记录跳过', SUSPENDED: '暂时挂起，恢复后沿用原截止时间' }[s] ?? s)
const action = (s: string | null) => ({ remind: '提醒', approve: '系统同意', reject: '系统拒绝', escalate: '升级', errorEdge: '走错误分支' }[s ?? ''] ?? '无效动作')
</script>
<template>
  <section aria-label="超时处理状态" class="timeout-status">
    <h4>截止时间与超时处理</h4>
    <p v-if="value.missingEvidenceTaskIds.length" role="alert">以下历史任务缺少可信截止事实（MISSING_EVIDENCE），不会按当前新版政策猜测执行：{{ value.missingEvidenceTaskIds.join('、') }}。</p>
    <p v-if="value.runtimeState === 'UNPROVEN'">尚无执行器实际扫描记录，不能确认超时处理正在运行。</p>
    <p v-else>最近实际扫描：{{ value.lastScan }}。这不代表每项超时动作已成功。</p>
    <p>当前可见流程中的逾期项：{{ value.overdueCount }}；最早逾期：{{ value.oldestDue || '无' }}；最近已确认动作：{{ value.lastEffect || '无' }}。</p>
    <ul><li v-for="deadline in value.deadlines" :key="`${deadline.taskId}:${deadline.revision}`">
      <p>任务 {{ deadline.taskId }}：{{ state(deadline.state) }}。截止 {{ deadline.dueAt || '未能计算' }}；动作：{{ action(deadline.policy.action) }}。</p>
      <p>固定小时数 {{ deadline.policy.hours }}，按 UTC 时长计算；原租户时区 {{ deadline.policy.timeZoneId }}。当前次数 {{ deadline.occurrence }}；下次截止 {{ deadline.nextDue || '无' }}。</p>
      <p v-if="deadline.lastResult">原执行结果：{{ state(deadline.lastResult.state) }}<span v-if="deadline.lastResult.reason">（{{ deadline.lastResult.reason }}）</span>，{{ deadline.lastResult.occurredAt }}。系统执行记录不计作人工审批票。</p>
      <details><summary>查看采用的版本与执行记录</summary><p>流程版本 {{ deadline.policy.flowVersionId }}；截止版本 {{ deadline.revision }}；租约版本 {{ deadline.claimEpoch ?? '无活动领取' }}；租约期限 {{ deadline.leaseUntil || '无' }}。</p><p v-if="deadline.lastResult">执行编号 {{ deadline.lastResult.executionId }}；执行前任务版本 {{ deadline.lastResult.originalTaskVersion || '缺少原始记录' }}；执行前实例版本 {{ deadline.lastResult.originalInstanceVersion || '缺少原始记录' }}；服务身份 {{ deadline.lastResult.serviceId }}；原命令 {{ deadline.lastResult.commandId || '无业务写入' }}。</p></details>
    </li></ul>
  </section>
</template>
<style scoped>.timeout-status { border: 1px solid var(--el-border-color); padding: 12px; margin-block: 16px; }p { overflow-wrap: anywhere; }</style>

<script setup lang="ts">
import type { DefinitionImpact } from '@/api/oa/definitionImpact'
defineProps<{ impact?: DefinitionImpact | null; form?: boolean }>()
</script>
<template>
  <div v-if="impact" aria-label="当前定义引用统计">
    <p>查询时点 {{ impact.observedAtUtc }}：运行中 {{ impact.runningInstances }}，挂起 {{ impact.suspendedInstances }}；其中根实例 {{ impact.rootInstances }}，子实例 {{ impact.childInstances }}。</p>
    <p v-if="form">此表单仍有 {{ impact.activeDrafts }} 份未提交草稿。发布新版本不自动改写草稿或已提交内容。</p>
    <p>表单入口绑定 {{ impact.personalEntryBindings }}；当前或已安排的业务绑定 {{ impact.relatedAppBindings }}；固定引用此定义的已发布上游版本 {{ impact.publishedParentVersions }}。</p>
    <p>统计仅包含当前租户与环境已接入的流程，不含旧历史流程；启停不会改写在途实例和固定版本。绑定数量表示关联范围，不表示全部入口会立即切换版本。</p>
  </div>
  <p v-else>当前没有可用的引用统计，不能据此判断为零。</p>
</template>

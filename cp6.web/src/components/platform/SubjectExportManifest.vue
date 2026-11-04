<template>
  <section class="subject-manifest" aria-label="主体导出覆盖说明">
    <h3>本次导出为部分资料</h3>
    <p>主体：{{ manifest.subjectId }}；所属租户：{{ manifest.tenantId }}</p>
    <p>以下来源分别读取，并非同一时点快照。未覆盖或未分类的内容不能解释为没有数据。</p>
    <table>
      <thead><tr><th>资料来源</th><th>本次结果</th><th>读取时间（UTC）</th></tr></thead>
      <tbody>
        <tr v-for="source in included" :key="source.source">
          <td>{{ sourceName(source.source) }}</td>
          <td>{{ source.recordCount }} 条</td>
          <td>{{ source.readStartedAtUtc }} — {{ source.readCompletedAtUtc }}</td>
        </tr>
      </tbody>
    </table>
    <p>仍有 {{ unknown.length }} 个来源尚未确认主体归属或尚未接入导出，包括{{ missingNames.join('、') }}。</p>
    <p>密码、令牌与认证材料不导出；未分类的嵌套内容和其他字段已排除。完整来源及字段说明随文件提供。</p>
    <details>
      <summary>查看已读取来源的字段排除</summary>
      <ul><li v-for="row in exclusions" :key="row.key">{{ row.label }}</li></ul>
    </details>
  </section>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import type { SubjectExportManifest } from '@/api/platform/gdpr'
const props = defineProps<{ manifest: SubjectExportManifest }>()
const included = computed(() => props.manifest.sources.filter(x => x.status === 'INCLUDED'))
const unknown = computed(() => props.manifest.sources.filter(x => x.status === 'UNKNOWN_SCOPE'))
const ownerNames: Record<string, string> = {
  'MAIN.AUDIT': '旧操作日志', 'MAIN.UNCLASSIFIED_OWNER': '其他业务资料', 'MAIN.OA': '表单与审批历史',
  'MAIN.PLM': '工程资料', 'MAIN.OPERATIONS': '业务操作原件', CRM: '客户关系资料',
  FILE: '文件及外部副本', BACKUP: '备份', UNREGISTERED_OWNERS: '目录外来源',
}
const missingNames = computed(() => [...new Set(unknown.value.map(x => ownerNames[x.owner] ?? '未接入来源'))])
const sourceName = (source: string) => ({ Sys_User: '用户资料', Sys_SecurityLog: '安全事件', Sys_UserRole: '角色关联' }[source] ?? '业务资料')
const exclusions = computed(() => included.value.flatMap(source => {
  const fields = source.fields.filter(x => x.status === 'EXCLUDED_WITH_REASON')
  return fields.length ? [{ key: source.source, label: `${sourceName(source.source)}：${fields.length} 项字段未纳入；详情见文件中的字段说明。` }] : []
}))
</script>

<style scoped>
.subject-manifest { margin-top: 20px; padding: 16px; border: 1px solid #dcdfe6; border-radius: 6px; }
table { width: 100%; border-collapse: collapse; }
th, td { padding: 8px; text-align: left; border-bottom: 1px solid #e4e7ed; overflow-wrap: anywhere; }
p { overflow-wrap: anywhere; }
summary { cursor: pointer; }
</style>

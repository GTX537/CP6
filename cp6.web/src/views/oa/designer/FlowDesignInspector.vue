<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import {
  CircleCheck,
  Connection,
  DocumentChecked,
  MoreFilled,
  Operation,
  Setting,
  Timer,
  User,
  Warning,
} from '@element-plus/icons-vue'

import type { FlowPreviewNode } from './flowDesignConcept'

const props = withDefaults(defineProps<{
  node: FlowPreviewNode
  mode?: 'professional' | 'focus' | 'control'
}>(), {
  mode: 'professional',
})

const activeTab = ref<'base' | 'assignee' | 'advanced'>('base')
const localTitle = ref('')
const localAssignee = ref('')
const enabled = ref(true)
const approvalMode = ref('按角色解析')
const timeoutHours = ref(8)
const timeoutAction = ref('发送提醒')
const allowReject = ref(true)
const allowTransfer = ref(false)

watch(
  () => props.node,
  (node) => {
    localTitle.value = node.title
    localAssignee.value = node.assignee
    timeoutHours.value = Number.parseInt(node.sla, 10) || 8
  },
  { immediate: true },
)

const tabs = computed(() => props.mode === 'control'
  ? [
      { key: 'base' as const, label: '节点属性' },
      { key: 'assignee' as const, label: '流转规则' },
      { key: 'advanced' as const, label: '执行检查' },
    ]
  : [
      { key: 'base' as const, label: '基本设置' },
      { key: 'assignee' as const, label: '处理人' },
      { key: 'advanced' as const, label: '高级' },
    ])

const kindName = computed(() => ({
  start: '开始节点',
  approval: '审批节点',
  gateway: '条件分支',
  finance: '财务审批',
  compliance: '合规会签',
  join: '并行汇聚',
  service: '服务任务',
  timer: '定时器节点',
  subflow: '子流程节点',
  end: '结束节点',
  reject: '终止节点',
}[props.node.kind]))
</script>

<template>
  <div class="design-inspector" :class="`mode-${mode}`">
    <header class="inspector-head">
      <span class="inspector-symbol"><el-icon><Setting /></el-icon></span>
      <div>
        <small>{{ node.code }} · {{ kindName }}</small>
        <strong>{{ node.title }}</strong>
      </div>
      <button type="button" title="更多节点操作"><el-icon><MoreFilled /></el-icon></button>
    </header>

    <nav class="inspector-tabs" aria-label="节点属性分类">
      <button
        v-for="tab in tabs"
        :key="tab.key"
        type="button"
        :class="{ active: activeTab === tab.key }"
        @click="activeTab = tab.key"
      >
        {{ tab.label }}
      </button>
    </nav>

    <div class="inspector-scroll">
      <template v-if="activeTab === 'base'">
        <section class="property-section">
          <div class="property-title">
            <el-icon><DocumentChecked /></el-icon>
            <span><strong>节点信息</strong><small>流程图中的显示与识别</small></span>
          </div>
          <label>
            <span>节点名称 <b>*</b></span>
            <el-input v-model="localTitle" />
          </label>
          <label>
            <span>状态代码</span>
            <el-input :model-value="node.code" readonly>
              <template #prefix>STATE</template>
            </el-input>
          </label>
          <label>
            <span>节点类型</span>
            <el-input :model-value="kindName" readonly />
          </label>
        </section>

        <section class="property-section compact-settings">
          <div class="setting-row">
            <span><strong>启用节点</strong><small>发布时纳入流程执行</small></span>
            <el-switch v-model="enabled" />
          </div>
          <div class="setting-row">
            <span><strong>允许退回</strong><small>处理人可退回前一节点</small></span>
            <el-switch v-model="allowReject" />
          </div>
          <div class="setting-row">
            <span><strong>允许转交</strong><small>处理人可转交其他人员</small></span>
            <el-switch v-model="allowTransfer" />
          </div>
        </section>
      </template>

      <template v-else-if="activeTab === 'assignee'">
        <section class="property-section">
          <div class="property-title blue">
            <el-icon><User /></el-icon>
            <span><strong>处理人规则</strong><small>运行时动态解析人员</small></span>
          </div>
          <label>
            <span>解析方式</span>
            <el-select v-model="approvalMode">
              <el-option label="按角色解析" value="按角色解析" />
              <el-option label="按部门主管" value="按部门主管" />
              <el-option label="表单字段指定" value="表单字段指定" />
              <el-option label="固定人员" value="固定人员" />
            </el-select>
          </label>
          <label>
            <span>处理人 / 角色 <b>*</b></span>
            <el-input v-model="localAssignee" />
          </label>
          <div class="resolver-result">
            <el-icon><CircleCheck /></el-icon>
            <span><strong>规则可正常解析</strong><small>测试数据命中 3 位候选处理人</small></span>
          </div>
        </section>

        <section class="property-section">
          <div class="property-title amber">
            <el-icon><Connection /></el-icon>
            <span><strong>审批方式</strong><small>多人处理时的完成条件</small></span>
          </div>
          <el-radio-group model-value="任一通过" class="approval-choice">
            <el-radio-button value="任一通过">任一通过</el-radio-button>
            <el-radio-button value="全部通过">全部通过</el-radio-button>
          </el-radio-group>
        </section>
      </template>

      <template v-else>
        <section class="property-section">
          <div class="property-title amber">
            <el-icon><Timer /></el-icon>
            <span><strong>时限与升级</strong><small>节点逾期后的自动处理</small></span>
          </div>
          <label>
            <span>处理时限（小时）</span>
            <el-input-number v-model="timeoutHours" :min="1" :max="720" controls-position="right" />
          </label>
          <label>
            <span>超时动作</span>
            <el-select v-model="timeoutAction">
              <el-option label="发送提醒" value="发送提醒" />
              <el-option label="升级至上级" value="升级至上级" />
              <el-option label="自动通过" value="自动通过" />
              <el-option label="进入异常分支" value="进入异常分支" />
            </el-select>
          </label>
        </section>

        <section class="property-section execution-checks">
          <div class="property-title blue">
            <el-icon><Operation /></el-icon>
            <span><strong>执行检查</strong><small>保存前即时校验</small></span>
          </div>
          <div class="check-row ok"><el-icon><CircleCheck /></el-icon><span>处理人规则有效</span></div>
          <div class="check-row ok"><el-icon><CircleCheck /></el-icon><span>流出路径已配置</span></div>
          <div class="check-row warn"><el-icon><Warning /></el-icon><span>建议配置代理处理规则</span></div>
        </section>
      </template>
    </div>

    <footer class="inspector-footer">
      <span><el-icon><CircleCheck /></el-icon>配置完整 8 / 9</span>
      <el-button size="small" type="primary">应用</el-button>
    </footer>
  </div>
</template>

<style scoped>
.design-inspector {
  width: 100%;
  height: 100%;
  min-width: 0;
  min-height: 0;
  display: flex;
  flex-direction: column;
  background: #fff;
  color: #314a52;
}

.inspector-head {
  min-height: 72px;
  padding: 12px 14px;
  display: grid;
  grid-template-columns: 38px minmax(0, 1fr) 30px;
  align-items: center;
  gap: 10px;
  border-bottom: 1px solid #e0e7e9;
}

.inspector-symbol {
  width: 38px;
  height: 38px;
  display: grid;
  place-items: center;
  border-radius: 6px;
  background: #e2f3f3;
  color: #118b90;
  font-size: 18px;
}

.inspector-head small,
.inspector-head strong {
  display: block;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.inspector-head small {
  margin-bottom: 4px;
  color: #839499;
  font-size: 9px;
  font-weight: 700;
}

.inspector-head strong { font-size: 13px; }

.inspector-head button {
  width: 30px;
  height: 30px;
  display: grid;
  place-items: center;
  border: 1px solid #dbe4e6;
  border-radius: 5px;
  background: #fff;
  color: #667d84;
  cursor: pointer;
}

.inspector-tabs {
  min-height: 42px;
  padding: 0 12px;
  display: flex;
  gap: 18px;
  border-bottom: 1px solid #e1e8ea;
}

.inspector-tabs button {
  position: relative;
  padding: 0;
  border: 0;
  background: transparent;
  color: #71858a;
  font-size: 10px;
  font-weight: 700;
  cursor: pointer;
}

.inspector-tabs button.active { color: #0f898e; }
.inspector-tabs button.active::after {
  content: '';
  position: absolute;
  right: 0;
  bottom: -1px;
  left: 0;
  height: 2px;
  background: #169ba0;
}

.inspector-scroll {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  scrollbar-gutter: stable;
}

.property-section {
  padding: 16px 15px 18px;
  border-bottom: 1px solid #e5ebed;
}

.property-title {
  margin-bottom: 15px;
  display: flex;
  align-items: center;
  gap: 9px;
}

.property-title > .el-icon {
  width: 30px;
  height: 30px;
  display: grid;
  place-items: center;
  flex: 0 0 30px;
  border-radius: 5px;
  background: #e5f4ef;
  color: #258864;
  font-size: 15px;
}

.property-title.blue > .el-icon { background: #e8f0fb; color: #4779c1; }
.property-title.amber > .el-icon { background: #fff1dc; color: #b97819; }

.property-title strong,
.property-title small { display: block; }
.property-title strong { font-size: 11px; }
.property-title small { margin-top: 3px; color: #85969b; font-size: 9px; }

.property-section label {
  display: block;
  margin-top: 13px;
}

.property-section label > span {
  display: block;
  margin-bottom: 6px;
  color: #566c72;
  font-size: 10px;
  font-weight: 700;
}

.property-section label > span b { color: #d85954; }
.property-section :deep(.el-select),
.property-section :deep(.el-input-number) { width: 100%; }

.compact-settings { padding-top: 4px; padding-bottom: 4px; }
.setting-row {
  min-height: 60px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 14px;
  border-bottom: 1px solid #e8edef;
}
.setting-row:last-child { border-bottom: 0; }
.setting-row strong,
.setting-row small { display: block; }
.setting-row strong { font-size: 10px; }
.setting-row small { margin-top: 3px; color: #87979b; font-size: 8px; }

.resolver-result {
  min-height: 50px;
  margin-top: 14px;
  padding: 9px 10px;
  display: flex;
  align-items: center;
  gap: 9px;
  border: 1px solid #cbe3d9;
  border-radius: 5px;
  background: #f1f9f5;
  color: #2a7e60;
}
.resolver-result strong,
.resolver-result small { display: block; }
.resolver-result strong { font-size: 9px; }
.resolver-result small { margin-top: 3px; color: #709084; font-size: 8px; }

.approval-choice { width: 100%; display: flex; }
.approval-choice :deep(.el-radio-button) { flex: 1; }
.approval-choice :deep(.el-radio-button__inner) { width: 100%; }

.execution-checks { display: grid; gap: 0; }
.check-row {
  min-height: 42px;
  display: flex;
  align-items: center;
  gap: 8px;
  border-top: 1px solid #e7edef;
  font-size: 9px;
}
.check-row.ok { color: #367d63; }
.check-row.warn { color: #a66c18; }

.inspector-footer {
  min-height: 54px;
  padding: 9px 13px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  border-top: 1px solid #dfe7e9;
  background: #f9fbfb;
}

.inspector-footer > span {
  display: flex;
  align-items: center;
  gap: 5px;
  color: #2b8062;
  font-size: 9px;
}

.mode-focus .inspector-head { min-height: 64px; }
.mode-focus .property-section { padding-right: 13px; padding-left: 13px; }
.mode-control .inspector-symbol { background: #e9eef9; color: #4d70ad; }
.mode-control .inspector-tabs button.active { color: #476ba7; }
.mode-control .inspector-tabs button.active::after { background: #5276b4; }
</style>

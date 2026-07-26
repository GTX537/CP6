<script setup lang="ts">
import { ref, watch } from 'vue'
import {
  CircleCheck,
  Connection,
  Cpu,
  Link,
  Operation,
  Setting,
  UserFilled,
} from '@element-plus/icons-vue'

import type { FlowPreviewEditableEdge } from './flowDesignConcept'

const props = withDefaults(defineProps<{
  edge: FlowPreviewEditableEdge
  mode?: 'professional' | 'focus' | 'control'
}>(), {
  mode: 'professional',
})

const emit = defineEmits<{
  update: [patch: Partial<FlowPreviewEditableEdge>]
}>()

const activeTab = ref<'base' | 'sign' | 'api' | 'job'>('base')
const label = ref('')
const condition = ref('')
const routeType = ref<FlowPreviewEditableEdge['routeType']>('normal')
const isDefault = ref(false)
const addSignMode = ref('不加签')
const apiMethod = ref('POST')
const apiPath = ref('')
const apiTimeout = ref(30)
const apiRetry = ref(3)
const jobCode = ref('')
const jobQueue = ref('default')
const jobParameters = ref('{}')

watch(
  () => props.edge,
  (edge) => {
    label.value = edge.label
    condition.value = edge.condition
    routeType.value = edge.routeType
    isDefault.value = edge.isDefault
    addSignMode.value = edge.addSignMode
    apiMethod.value = edge.apiMethod
    apiPath.value = edge.apiPath
    apiTimeout.value = edge.apiTimeout
    apiRetry.value = edge.apiRetry
    jobCode.value = edge.jobCode
    jobQueue.value = edge.jobQueue
    jobParameters.value = edge.jobParameters
  },
  { immediate: true },
)

function applyChanges() {
  emit('update', {
    label: label.value,
    condition: condition.value,
    routeType: routeType.value,
    isDefault: isDefault.value,
    addSignMode: addSignMode.value,
    apiMethod: apiMethod.value,
    apiPath: apiPath.value,
    apiTimeout: apiTimeout.value,
    apiRetry: apiRetry.value,
    jobCode: jobCode.value,
    jobQueue: jobQueue.value,
    jobParameters: jobParameters.value,
  })
}
</script>

<template>
  <div class="edge-inspector" :class="`mode-${mode}`">
    <header class="edge-head">
      <span><el-icon><Connection /></el-icon></span>
      <div>
        <small>路径 {{ edge.id }}</small>
        <strong>{{ edge.sourceName }} → {{ edge.targetName }}</strong>
      </div>
      <el-icon><Setting /></el-icon>
    </header>

    <nav class="edge-tabs" aria-label="路径配置分类">
      <button type="button" :class="{ active: activeTab === 'base' }" @click="activeTab = 'base'">路径设置</button>
      <button type="button" :class="{ active: activeTab === 'sign' }" @click="activeTab = 'sign'">加签人员</button>
      <button type="button" :class="{ active: activeTab === 'api' }" @click="activeTab = 'api'">WebAPI</button>
      <button type="button" :class="{ active: activeTab === 'job' }" @click="activeTab = 'job'">JOB</button>
    </nav>

    <div class="edge-scroll">
      <template v-if="activeTab === 'base'">
        <section>
          <div class="section-title green">
            <el-icon><Link /></el-icon>
            <span><strong>路径基本设定</strong><small>状态流转与触发条件</small></span>
          </div>
          <label>
            <span>开始节点</span>
            <el-input :model-value="edge.sourceName" readonly />
          </label>
          <label>
            <span>目标节点</span>
            <el-input :model-value="edge.targetName" readonly />
          </label>
          <label>
            <span>路径名称 <b>*</b></span>
            <el-input v-model="label" />
          </label>
          <label>
            <span>审核线优先级</span>
            <el-input :model-value="edge.isDefault ? `${edge.priority}（无条件兜底）` : String(edge.priority)" readonly />
          </label>
          <label>
            <span>路径类型</span>
            <el-select v-model="routeType">
              <el-option label="普通路径" value="normal" />
              <el-option label="条件路径" value="condition" />
              <el-option label="异常路径" value="exception" />
            </el-select>
          </label>
          <label v-if="routeType === 'condition'">
            <span>条件表达式 <b>*</b></span>
            <el-input v-model="condition" type="textarea" :rows="3" placeholder="${amount >= 5000}" />
          </label>
        </section>

        <section class="compact-section">
          <div class="switch-row">
            <span><strong>默认流出路径</strong><small>其他条件不命中时使用</small></span>
            <el-switch v-model="isDefault" />
          </div>
          <div class="anchor-row">
            <span><strong>连接锚点</strong><small>{{ edge.sourceHandle }} → {{ edge.targetHandle }}</small></span>
            <el-icon><Operation /></el-icon>
          </div>
        </section>
      </template>

      <template v-else-if="activeTab === 'sign'">
        <section>
          <div class="section-title blue">
            <el-icon><UserFilled /></el-icon>
            <span><strong>加签人员设定</strong><small>路径执行前追加审批人员</small></span>
          </div>
          <label>
            <span>加签方式</span>
            <el-select v-model="addSignMode">
              <el-option label="不加签" value="不加签" />
              <el-option label="前加签" value="前加签" />
              <el-option label="后加签" value="后加签" />
              <el-option label="并行加签" value="并行加签" />
            </el-select>
          </label>
          <label v-if="addSignMode !== '不加签'">
            <span>人员来源</span>
            <el-select model-value="按角色解析">
              <el-option label="按角色解析" value="按角色解析" />
              <el-option label="按部门解析" value="按部门解析" />
              <el-option label="表单字段指定" value="表单字段指定" />
            </el-select>
          </label>
          <label v-if="addSignMode !== '不加签'">
            <span>角色或人员</span>
            <el-input placeholder="请选择审批角色" />
          </label>
        </section>
      </template>

      <template v-else-if="activeTab === 'api'">
        <section>
          <div class="section-title teal">
            <el-icon><Link /></el-icon>
            <span><strong>执行 WebAPI 设定</strong><small>路径触发时调用业务接口</small></span>
          </div>
          <div class="inline-fields">
            <label class="method-field">
              <span>方法</span>
              <el-select v-model="apiMethod">
                <el-option label="POST" value="POST" />
                <el-option label="PUT" value="PUT" />
                <el-option label="GET" value="GET" />
                <el-option label="DELETE" value="DELETE" />
              </el-select>
            </label>
            <label>
              <span>接口路径</span>
              <el-input v-model="apiPath" placeholder="/api/workflow/callback" />
            </label>
          </div>
          <div class="inline-fields equal">
            <label>
              <span>超时（秒）</span>
              <el-input-number v-model="apiTimeout" :min="1" :max="600" controls-position="right" />
            </label>
            <label>
              <span>重试次数</span>
              <el-input-number v-model="apiRetry" :min="0" :max="10" controls-position="right" />
            </label>
          </div>
          <label>
            <span>请求内容</span>
            <el-input type="textarea" :rows="5" model-value="{&#10;  &quot;instanceId&quot;: &quot;${instanceId}&quot;&#10;}" />
          </label>
        </section>
      </template>

      <template v-else>
        <section>
          <div class="section-title amber">
            <el-icon><Cpu /></el-icon>
            <span><strong>执行 JOB 设定</strong><small>提交后台任务并跟踪结果</small></span>
          </div>
          <label>
            <span>JOB 编号</span>
            <el-input v-model="jobCode" placeholder="例如 PUR_SYNC_001" />
          </label>
          <label>
            <span>执行队列</span>
            <el-select v-model="jobQueue">
              <el-option label="默认队列" value="default" />
              <el-option label="高优先级" value="high" />
              <el-option label="批处理" value="batch" />
            </el-select>
          </label>
          <label>
            <span>执行参数</span>
            <el-input v-model="jobParameters" type="textarea" :rows="6" />
          </label>
        </section>
      </template>
    </div>

    <footer class="edge-footer">
      <span><el-icon><CircleCheck /></el-icon>路径配置可编辑</span>
      <el-button size="small" type="primary" @click="applyChanges">应用</el-button>
    </footer>
  </div>
</template>

<style scoped>
.edge-inspector {
  width: 100%;
  height: 100%;
  min-width: 0;
  min-height: 0;
  display: flex;
  flex-direction: column;
  background: #fff;
  color: #314a52;
}

.edge-head {
  min-height: 72px;
  padding: 12px 14px;
  display: grid;
  grid-template-columns: 38px minmax(0, 1fr) 20px;
  align-items: center;
  gap: 10px;
  border-bottom: 1px solid #e0e7e9;
}

.edge-head > span {
  width: 38px;
  height: 38px;
  display: grid;
  place-items: center;
  border-radius: 6px;
  background: #fff0dc;
  color: #b87417;
  font-size: 18px;
}

.edge-head small,
.edge-head strong {
  display: block;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.edge-head small { margin-bottom: 4px; color: #839499; font-size: 9px; font-weight: 700; }
.edge-head strong { font-size: 12px; }
.edge-head > .el-icon { color: #829399; }

.edge-tabs {
  min-height: 42px;
  padding: 0 10px;
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  border-bottom: 1px solid #e1e8ea;
}

.edge-tabs button {
  position: relative;
  padding: 0 3px;
  border: 0;
  background: transparent;
  color: #71858a;
  font-size: 9px;
  font-weight: 700;
  white-space: nowrap;
  cursor: pointer;
}

.edge-tabs button.active { color: #b87517; }
.edge-tabs button.active::after { content: ''; position: absolute; right: 5px; bottom: -1px; left: 5px; height: 2px; background: #d58a22; }

.edge-scroll { flex: 1; min-height: 0; overflow-y: auto; scrollbar-gutter: stable; }
.edge-scroll section { padding: 16px 15px 18px; border-bottom: 1px solid #e5ebed; }
.edge-scroll section.compact-section { padding-top: 4px; padding-bottom: 4px; }

.section-title { margin-bottom: 15px; display: flex; align-items: center; gap: 9px; }
.section-title > .el-icon {
  width: 30px;
  height: 30px;
  display: grid;
  place-items: center;
  flex: 0 0 30px;
  border-radius: 5px;
  background: #e6f4ef;
  color: #278765;
}
.section-title.blue > .el-icon { background: #e9f0fa; color: #4777bd; }
.section-title.teal > .el-icon { background: #e2f3f3; color: #14878c; }
.section-title.amber > .el-icon { background: #fff1dc; color: #b87517; }
.section-title strong,
.section-title small { display: block; }
.section-title strong { font-size: 11px; }
.section-title small { margin-top: 3px; color: #85969b; font-size: 9px; }

.edge-scroll label { display: block; min-width: 0; margin-top: 13px; }
.edge-scroll label > span { display: block; margin-bottom: 6px; color: #566c72; font-size: 10px; font-weight: 700; }
.edge-scroll label > span b { color: #d85954; }
.edge-scroll :deep(.el-select),
.edge-scroll :deep(.el-input-number) { width: 100%; }

.switch-row,
.anchor-row {
  min-height: 60px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  border-bottom: 1px solid #e8edef;
}
.anchor-row { border-bottom: 0; }
.switch-row strong,
.switch-row small,
.anchor-row strong,
.anchor-row small { display: block; }
.switch-row strong,
.anchor-row strong { font-size: 10px; }
.switch-row small,
.anchor-row small { margin-top: 3px; color: #87979b; font-size: 8px; }
.anchor-row > .el-icon { color: #b87517; }

.inline-fields { display: grid; grid-template-columns: 84px minmax(0, 1fr); gap: 9px; }
.inline-fields.equal { grid-template-columns: 1fr 1fr; }

.edge-footer {
  min-height: 54px;
  padding: 9px 13px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  border-top: 1px solid #dfe7e9;
  background: #f9fbfb;
}
.edge-footer > span { display: flex; align-items: center; gap: 5px; color: #2b8062; font-size: 9px; }

.mode-focus .edge-head { min-height: 64px; }
.mode-control .edge-head > span { background: #e9eef9; color: #4d70ad; }
.mode-control .edge-tabs button.active { color: #476ba7; }
.mode-control .edge-tabs button.active::after { background: #5276b4; }

@media (max-width: 1280px) {
  .edge-tabs button { font-size: 8px; }
  .inline-fields,
  .inline-fields.equal { grid-template-columns: 1fr; gap: 0; }
}
</style>

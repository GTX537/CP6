<template>
  <div class="flow-designer">
    <!-- 工具栏 -->
    <el-card shadow="never" class="toolbar">
      <el-space wrap>
        <el-input v-model="flowKey" :placeholder="t('流程标识')" style="width: 140px" size="small" />
        <el-input v-model="flowName" :placeholder="t('流程名称')" style="width: 140px" size="small" />
        <el-input v-model="formKey" :placeholder="t('表单标识')" style="width: 130px" size="small" />
        <el-button size="small" @click="load">{{ t('加载') }}</el-button>
        <el-divider direction="vertical" />
        <el-button size="small" @click="addNode('start')">{{ t('加开始节点') }}</el-button>
        <el-button size="small" @click="addNode('approval')">{{ t('加审批节点') }}</el-button>
        <el-button size="small" @click="addNode('end')">{{ t('加结束节点') }}</el-button>
        <el-button size="small" :type="connecting ? 'warning' : 'default'" @click="toggleConnect">
          {{ connecting ? t('退出连线') : t('连线模式') }}
        </el-button>
        <el-divider direction="vertical" />
        <el-button size="small" :disabled="!canUndo" @click="undo">{{ t('撤销') }}</el-button>
        <el-button size="small" :disabled="!canRedo" @click="redo">{{ t('重做') }}</el-button>
        <el-button v-permission="'oa-designer:edit'" type="primary" size="small" @click="save">{{ t('保存') }}</el-button>
      </el-space>
    </el-card>

    <div class="panes">
      <!-- 画布 -->
      <el-card shadow="never" class="pane canvas">
        <svg
          ref="svgRef"
          class="graph"
          @mousemove="onMove"
          @mouseup="onUp"
          @mouseleave="onUp"
        >
          <defs>
            <marker id="arrow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">
              <path d="M0,0 L10,5 L0,10 z" fill="var(--el-color-info)" />
            </marker>
          </defs>
          <!-- 边 -->
          <g v-for="(e, i) in schema.edges" :key="'e' + i">
            <line
              :x1="edgePoint(e).x1" :y1="edgePoint(e).y1" :x2="edgePoint(e).x2" :y2="edgePoint(e).y2"
              :stroke="selKind === 'edge' && selIdx === i ? 'var(--el-color-primary)' : 'var(--el-color-info)'"
              stroke-width="2" marker-end="url(#arrow)" class="edge" @click.stop="selectEdge(i)"
            />
            <text v-if="e.condition" :x="(edgePoint(e).x1 + edgePoint(e).x2) / 2" :y="(edgePoint(e).y1 + edgePoint(e).y2) / 2 - 4" text-anchor="middle" class="edge-label">{{ e.condition }}</text>
          </g>
          <!-- 节点 -->
          <g
            v-for="(n, i) in schema.nodes"
            :key="n.id"
            :transform="`translate(${n.x},${n.y})`"
            class="node"
            @mousedown="onNodeDown($event, i)"
            @click.stop="onNodeClick(i)"
          >
            <rect
              :width="NODE_W" :height="NODE_H" rx="6"
              :class="['node-rect', n.type, { sel: selKind === 'node' && selIdx === i, connectsrc: connectFrom === i }]"
            />
            <text :x="NODE_W / 2" :y="18" text-anchor="middle" class="node-name">{{ n.name || n.id }}</text>
            <text :x="NODE_W / 2" :y="34" text-anchor="middle" class="node-type">{{ typeLabel(n.type) }}</text>
          </g>
        </svg>
        <div class="hint">{{ connecting ? (connectFrom == null ? t('请点击起始节点') : t('请点击目标节点')) : t('拖动节点移动；连线模式下点两节点连线') }}</div>
      </el-card>

      <!-- 属性面板 -->
      <el-card shadow="never" class="pane prop">
        <template #header>
          <span class="pane-title">{{ selKind === 'node' ? t('节点属性') : selKind === 'edge' ? t('连线属性') : t('属性') }}</span>
          <el-button v-if="selKind" link size="small" type="danger" style="float: right" @click="deleteSel">{{ t('删除选中') }}</el-button>
        </template>

        <el-empty v-if="!selKind" :description="t('请先选择节点或连线')" :image-size="60" />

        <!-- 节点属性 -->
        <el-form v-else-if="selKind === 'node' && curNode" label-width="92px" size="small">
          <el-form-item :label="t('名称')"><el-input v-model="curNode.name" /></el-form-item>
          <el-form-item :label="t('类型')">
            <el-select v-model="curNode.type" style="width: 100%">
              <el-option :label="t('开始')" value="start" />
              <el-option :label="t('审批')" value="approval" />
              <el-option :label="t('结束')" value="end" />
            </el-select>
          </el-form-item>
          <template v-if="curNode.type === 'approval'">
            <el-form-item :label="t('审批人策略')">
              <el-select v-model="curNode.approverStrategy" style="width: 100%">
                <el-option :label="t('指定人员')" value="Specified" />
                <el-option :label="t('直属上级')" value="DirectManager" />
                <el-option :label="t('部门负责人')" value="DeptLeader" />
                <el-option :label="t('角色')" value="Role" />
                <el-option :label="t('发起人')" value="Starter" />
              </el-select>
            </el-form-item>
            <el-form-item v-if="curNode.approverStrategy === 'Specified'" :label="t('指定审批人')">
              <el-input v-model="curNode.approverUserId" :placeholder="t('用户ID')" />
            </el-form-item>
            <el-form-item v-if="curNode.approverStrategy === 'Role'" :label="t('审批角色')">
              <el-input-number v-model="curNode.approverRoleId" :controls="false" style="width: 100%" />
            </el-form-item>
            <el-form-item v-if="curNode.approverStrategy === 'DirectManager'" :label="t('上级层数')">
              <el-input-number v-model="curNode.approverLevels" :min="1" :controls="false" style="width: 100%" />
            </el-form-item>
            <el-form-item :label="t('会签规则')">
              <el-select v-model="curNode.countersign" style="width: 100%">
                <el-option :label="t('全部同意')" value="all" />
                <el-option :label="t('任一同意')" value="any" />
                <el-option :label="t('一票否决')" value="veto" />
              </el-select>
            </el-form-item>
            <el-form-item :label="t('超时(小时)')">
              <el-input-number v-model="curNode.timeoutHours" :min="0" :controls="false" style="width: 100%" />
            </el-form-item>
            <el-form-item :label="t('超时动作')">
              <el-select v-model="curNode.timeoutAction" clearable style="width: 100%">
                <el-option :label="t('催办')" value="remind" />
                <el-option :label="t('自动通过')" value="approve" />
                <el-option :label="t('自动驳回')" value="reject" />
                <el-option :label="t('升级')" value="escalate" />
              </el-select>
            </el-form-item>
            <el-form-item v-if="curNode.timeoutAction === 'escalate'" :label="t('升级对象')">
              <el-input v-model="curNode.escalateTo" :placeholder="t('用户ID')" />
            </el-form-item>
          </template>
        </el-form>

        <!-- 连线属性 -->
        <el-form v-else-if="selKind === 'edge' && curEdge" label-width="92px" size="small">
          <el-form-item :label="t('起点')"><el-tag>{{ curEdge.from }}</el-tag></el-form-item>
          <el-form-item :label="t('终点')"><el-tag>{{ curEdge.to }}</el-tag></el-form-item>
          <el-form-item :label="t('流转条件')">
            <el-input v-model="curEdge.condition" type="textarea" :rows="2" :placeholder="t('空=无条件；如 days <= 3')" />
          </el-form-item>
        </el-form>
      </el-card>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import { SchemaHistory } from './schemaHistory'
import { defaultNode, addEdge, removeNodeCascade } from './flowGraph'
import { validateFlowSchema } from './designValidate'
import { flowApi } from '@/api/wf/flow'
import type { FlowDesignSchema, FlowDesignNode, FlowDesignEdge } from '@/types/wf/wf'

const { t } = useI18n()

const NODE_W = 120
const NODE_H = 44

/**
 * 流程设计器（OA 章09 §3/§5）。SVG 有向图：拖节点 / 连线模式连边 / 节点属性配齐审批人·会签·
 * 超时·条件。图与语义分离（坐标 x/y 写 schema、运行时忽略，OA4-D5）。保存即 FlowDef Version+1。
 */
const flowKey = ref('')
const flowName = ref('')
const formKey = ref('')
const schema = reactive<FlowDesignSchema>({ start: '', nodes: [], edges: [] })

const selKind = ref<'' | 'node' | 'edge'>('')
const selIdx = ref(-1)
const connecting = ref(false)
const connectFrom = ref<number | null>(null)

const svgRef = ref<SVGSVGElement>()
const dragIdx = ref<number | null>(null)
const dragOff = reactive({ x: 0, y: 0 })
let moved = false

const curNode = computed<FlowDesignNode | null>(() =>
  selKind.value === 'node' && selIdx.value >= 0 ? schema.nodes[selIdx.value] || null : null,
)
const curEdge = computed<FlowDesignEdge | null>(() =>
  selKind.value === 'edge' && selIdx.value >= 0 ? schema.edges[selIdx.value] || null : null,
)

function typeLabel(type: string): string {
  return type === 'start' ? t('开始') : type === 'end' ? t('结束') : t('审批')
}

// ── 边端点（节点中心连线）──
function edgePoint(e: FlowDesignEdge) {
  const a = schema.nodes.find((n) => n.id === e.from)
  const b = schema.nodes.find((n) => n.id === e.to)
  return {
    x1: (a?.x ?? 0) + NODE_W / 2, y1: (a?.y ?? 0) + NODE_H / 2,
    x2: (b?.x ?? 0) + NODE_W / 2, y2: (b?.y ?? 0) + NODE_H / 2,
  }
}

// ── 撤销/重做 ──
function snapshot(): FlowDesignSchema {
  return JSON.parse(JSON.stringify({ start: schema.start, nodes: schema.nodes, edges: schema.edges }))
}
const history = new SchemaHistory<FlowDesignSchema>(snapshot())
const canUndo = ref(false)
const canRedo = ref(false)
let restoring = false
function applySnapshot(s: FlowDesignSchema) {
  restoring = true
  schema.start = s.start || ''
  schema.nodes = s.nodes || []
  schema.edges = s.edges || []
  selKind.value = ''
  selIdx.value = -1
  restoring = false
}
watch(
  () => snapshot(),
  () => {
    if (restoring) return
    history.push(snapshot())
    canUndo.value = history.canUndo
    canRedo.value = history.canRedo
  },
  { deep: true },
)
function undo() { const s = history.undo(); if (s) applySnapshot(s); canUndo.value = history.canUndo; canRedo.value = history.canRedo }
function redo() { const s = history.redo(); if (s) applySnapshot(s); canUndo.value = history.canUndo; canRedo.value = history.canRedo }

// ── 节点/边编辑 ──
let addCount = 0
function addNode(type: string) {
  const n = defaultNode(type, 40 + (addCount % 5) * 30, 40 + (addCount % 5) * 30, schema.nodes)
  addCount++
  schema.nodes.push(n)
  if (type === 'start') schema.start = n.id
  selectNode(schema.nodes.length - 1)
}
function toggleConnect() {
  connecting.value = !connecting.value
  connectFrom.value = null
}
function selectNode(i: number) { selKind.value = 'node'; selIdx.value = i }
function selectEdge(i: number) { if (connecting.value) return; selKind.value = 'edge'; selIdx.value = i }

function onNodeClick(i: number) {
  if (moved) return // 刚拖动过，不当作点选
  if (connecting.value) {
    if (connectFrom.value == null) connectFrom.value = i
    else {
      addEdge(schema.edges, schema.nodes[connectFrom.value]!.id, schema.nodes[i]!.id)
      connectFrom.value = null
    }
    return
  }
  selectNode(i)
}
function deleteSel() {
  if (selKind.value === 'node' && selIdx.value >= 0) {
    removeNodeCascade(schema.nodes, schema.edges, schema.nodes[selIdx.value]!.id)
  } else if (selKind.value === 'edge' && selIdx.value >= 0) {
    schema.edges.splice(selIdx.value, 1)
  }
  selKind.value = ''
  selIdx.value = -1
}

// ── 拖拽 ──
function onNodeDown(ev: MouseEvent, i: number) {
  if (connecting.value) return
  const rect = svgRef.value?.getBoundingClientRect()
  if (!rect) return
  dragIdx.value = i
  moved = false
  dragOff.x = ev.clientX - rect.left - (schema.nodes[i]!.x)
  dragOff.y = ev.clientY - rect.top - (schema.nodes[i]!.y)
}
function onMove(ev: MouseEvent) {
  if (dragIdx.value == null) return
  const rect = svgRef.value?.getBoundingClientRect()
  if (!rect) return
  const n = schema.nodes[dragIdx.value]!
  n.x = Math.max(0, ev.clientX - rect.left - dragOff.x)
  n.y = Math.max(0, ev.clientY - rect.top - dragOff.y)
  moved = true
}
function onUp() { dragIdx.value = null }

// ── 加载 / 保存 ──
async function load() {
  if (!flowKey.value.trim()) { ElMessage.warning(t('请输入流程标识')); return }
  const res = await flowApi.getDef(flowKey.value.trim())
  if (!res.data) { ElMessage.info(t('该流程尚未定义')); return }
  flowName.value = res.data.flowName || ''
  formKey.value = res.data.formKey || ''
  const parsed: FlowDesignSchema = JSON.parse(res.data.schemaJson || '{"nodes":[],"edges":[]}')
  // 无坐标的旧 schema：给个网格布局兜底
  ;(parsed.nodes || []).forEach((n, i) => { if (typeof n.x !== 'number') n.x = 40 + (i % 4) * 150; if (typeof n.y !== 'number') n.y = 40 + Math.floor(i / 4) * 90 })
  applySnapshot({ start: parsed.start || '', nodes: parsed.nodes || [], edges: parsed.edges || [] })
  history.reset(snapshot())
  canUndo.value = false
  canRedo.value = false
}
async function save() {
  if (!flowKey.value.trim() || !flowName.value.trim()) { ElMessage.warning(t('请填写流程标识与名称')); return }
  const errors = validateFlowSchema(schema)
  if (errors.length) { ElMessage.error(errors[0]); return }
  await flowApi.saveDef({
    flowKey: flowKey.value.trim(),
    flowName: flowName.value.trim(),
    formKey: formKey.value.trim(),
    schemaJson: JSON.stringify({ start: schema.start, nodes: schema.nodes, edges: schema.edges }),
  })
  ElMessage.success(t('保存成功'))
}
</script>

<style scoped>
.flow-designer { display: flex; flex-direction: column; gap: 8px; height: 100%; }
.toolbar { flex: none; }
.panes { display: flex; gap: 8px; flex: 1; min-height: 0; }
.pane { overflow: auto; }
.pane.canvas { flex: 1; position: relative; }
.pane.prop { width: 300px; flex: none; }
.pane-title { font-weight: 600; }
.graph { width: 100%; height: 520px; background: var(--el-fill-color-lighter); border-radius: 4px; user-select: none; }
.hint { position: absolute; bottom: 12px; left: 16px; font-size: 12px; color: var(--el-text-color-secondary); }
.node { cursor: move; }
.node-rect { fill: #fff; stroke: var(--el-color-info); stroke-width: 1.5; }
.node-rect.start { fill: var(--el-color-success-light-9); stroke: var(--el-color-success); }
.node-rect.end { fill: var(--el-color-danger-light-9); stroke: var(--el-color-danger); }
.node-rect.sel { stroke: var(--el-color-primary); stroke-width: 2.5; }
.node-rect.connectsrc { stroke: var(--el-color-warning); stroke-dasharray: 4; }
.node-name { font-size: 13px; font-weight: 600; fill: var(--el-text-color-primary); }
.node-type { font-size: 11px; fill: var(--el-text-color-secondary); }
.edge { cursor: pointer; }
.edge-label { font-size: 11px; fill: var(--el-color-primary); }
</style>

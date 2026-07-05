<script setup lang="ts">
import { ref, computed, watch, nextTick, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { userApi } from '@/api/sys/user'
import { roleApi } from '@/api/sys/role'
import { designerApi } from '@/api/oa/designer'
import type { ServiceCatalog } from '@/api/oa/designer'
import CpTag from '@/components/base/CpTag.vue'
import type { SchemaNode } from './designerModel'

const props = defineProps<{ node: SchemaNode }>()
const emit = defineEmits<{ update: [patch: Partial<SchemaNode>] }>()
const { t } = useI18n()

// ── Local copy (mirrors props.node; changes emit 'update') ────────
function cloneNode(n: SchemaNode): SchemaNode {
  return {
    ...n,
    ccUsers: n.ccUsers ? [...n.ccUsers] : [],
    stages: n.stages ? n.stages.map(s => ({ ...s })) : undefined,
    approverMembers: n.approverMembers ? n.approverMembers.map(m => ({ ...m })) : undefined,
  }
}

const syncing = ref(false)
const local = ref<SchemaNode>(cloneNode(props.node))

watch(
  () => props.node,
  async (n) => {
    syncing.value = true
    local.value = cloneNode(n)
    await nextTick()
    syncing.value = false
  },
  { deep: true },
)

watch(
  local,
  () => {
    if (syncing.value) return
    emit('update', { ...local.value, ccUsers: [...(local.value.ccUsers ?? [])] })
  },
  { deep: true },
)

// ── Timeout: stored as hours, UI shows days ───────────────────────
const timeoutDays = computed({
  get: () => (local.value.timeoutHours != null ? local.value.timeoutHours / 24 : 0),
  set: (v: number | null) => {
    local.value.timeoutHours = v != null && v > 0 ? Math.round(v * 24) : undefined
  },
})

const isApproval = computed(() => local.value.type === 'approval')
const isServiceTask = computed(() => local.value.type === 'serviceTask')

// ── 服务目录（C-T3）：serviceTask 节点的动作/连接器下拉数据源 ────────
const catalog = ref<ServiceCatalog>({ actions: [], connectors: [] })

onMounted(async () => {
  if (!isServiceTask.value) return
  try {
    catalog.value = await designerApi.getServiceCatalog()
  } catch { /* HTTP interceptor toasts */ }
})

// ── 串簽启用状态 ──────────────────────────────────────────────────
const stageEnabled = computed(
  () => Array.isArray(local.value.stages) && local.value.stages.length > 0,
)

function toggleStages(v: boolean) {
  if (v) {
    local.value.stages = [{ kind: 'fixed', countersign: 'all' }]
  } else {
    local.value.stages = undefined
  }
}

function addStage() {
  if (!local.value.stages) local.value.stages = []
  local.value.stages.push({ kind: 'fixed', countersign: 'all' })
}

function removeStage(idx: number) {
  local.value.stages?.splice(idx, 1)
  if (!local.value.stages?.length) local.value.stages = undefined
}

function addMember() {
  if (!local.value.approverMembers) local.value.approverMembers = []
  local.value.approverMembers.push({ strategy: 'Starter' })
}

function removeMember(i: number) {
  local.value.approverMembers?.splice(i, 1)
  if (!local.value.approverMembers?.length) local.value.approverMembers = undefined
}

function moveStageUp(idx: number) {
  const arr = local.value.stages
  if (!arr || idx === 0) return
  const tmp = arr[idx - 1]!
  arr[idx - 1] = arr[idx]!
  arr[idx] = tmp
}

function moveStageDown(idx: number) {
  const arr = local.value.stages
  if (!arr || idx >= arr.length - 1) return
  const tmp = arr[idx + 1]!
  arr[idx + 1] = arr[idx]!
  arr[idx] = tmp
}

// ── Collapse ──────────────────────────────────────────────────────
const collapseActive = ref<string[]>(['basic', 'stages', 'service', 'advanced', 'cc'])

// ── User search (Specified approver, reused for stages) ──────────
interface UserOpt { label: string; value: string }
const userOptions = ref<UserOpt[]>([])
const userSearchLoading = ref(false)

async function searchUsers(kw: string) {
  if (!kw) { userOptions.value = []; return }
  userSearchLoading.value = true
  try {
    const res = await userApi.getList({ page: 1, pageSize: 20, keyword: kw }) as any
    userOptions.value = (res.rows ?? []).map((u: any) => ({
      label: u.nickName || u.userName,
      value: String(u.id),
    }))
  } catch { /* HTTP interceptor toasts */ } finally {
    userSearchLoading.value = false
  }
}

// ── Role list (Role strategy, reused for stages) ─────────────────
const roleOptions = ref<{ label: string; value: number }[]>([])
const roleLoading = ref(false)

async function loadRoles() {
  if (roleOptions.value.length) return
  roleLoading.value = true
  try {
    const res = await roleApi.getAll() as any
    const list: any[] = Array.isArray(res) ? res : (res.rows ?? [])
    roleOptions.value = list.map((r: any) => ({
      label: r.roleName ?? r.name ?? '',
      value: Number(r.roleId ?? r.id ?? 0),
    }))
  } catch { /* HTTP interceptor toasts */ } finally {
    roleLoading.value = false
  }
}

// ── CC user search ────────────────────────────────────────────────
const ccUserOptions = ref<UserOpt[]>([])
const ccUserSearchLoading = ref(false)

async function searchCcUsers(kw: string) {
  if (!kw) { ccUserOptions.value = []; return }
  ccUserSearchLoading.value = true
  try {
    const res = await userApi.getList({ page: 1, pageSize: 20, keyword: kw }) as any
    ccUserOptions.value = (res.rows ?? []).map((u: any) => ({
      label: u.nickName || u.userName,
      value: String(u.id),
    }))
  } catch { /* HTTP interceptor toasts */ } finally {
    ccUserSearchLoading.value = false
  }
}
</script>

<template>
  <div class="node-prop-panel">
    <div class="panel-title">{{ t('oa.designer.nodeProps') }}</div>

    <el-collapse v-model="collapseActive">
      <!-- ── 基本參數 ──────────────────────────────────── -->
      <el-collapse-item :title="t('oa.designer.basicParams')" name="basic">
        <el-form label-position="top" size="small" class="prop-form">
          <el-form-item :label="t('oa.designer.stateName')">
            <el-input v-model="local.name" />
          </el-form-item>

          <el-form-item :label="t('oa.designer.stateCode')">
            <el-input v-model="local.code" />
          </el-form-item>

          <el-form-item :label="t('oa.designer.nodeType')">
            <CpTag tone="muted">{{ local.type }}</CpTag>
          </el-form-item>

          <!-- approval-specific fields (single-stage; hidden when 串簽 enabled) -->
          <template v-if="isApproval && !stageEnabled">
            <el-form-item :label="t('oa.designer.approverType')">
              <el-select v-model="local.approverStrategy" style="width: 100%" clearable>
                <el-option value="DirectManager" :label="t('oa.designer.strategy.directManager')" />
                <el-option value="DeptLeader"   :label="t('oa.designer.strategy.deptLeader')" />
                <el-option value="Role"         :label="t('oa.designer.strategy.role')" />
                <el-option value="Specified"    :label="t('oa.designer.strategy.specified')" />
                <el-option value="Starter"      :label="t('oa.designer.strategy.starter')" />
                <el-option value="FormField" :label="t('oa.designer.strategy.formField')" />
                <el-option value="DataMap"   :label="t('oa.designer.strategy.dataMap')" />
                <el-option value="Group"     :label="t('oa.designer.strategy.group')" />
              </el-select>
            </el-form-item>

            <!-- DirectManager → upper level count -->
            <el-form-item
              v-if="local.approverStrategy === 'DirectManager'"
              :label="t('oa.designer.approverLevels')"
            >
              <el-input-number
                v-model="local.approverLevels"
                :min="1"
                :max="10"
                style="width: 100%"
              />
            </el-form-item>

            <!-- Role → role select -->
            <el-form-item
              v-if="local.approverStrategy === 'Role'"
              :label="t('oa.designer.approverRole')"
            >
              <el-select
                v-model="local.approverRoleId"
                style="width: 100%"
                filterable
                :loading="roleLoading"
                @focus="loadRoles"
                clearable
              >
                <el-option
                  v-for="r in roleOptions"
                  :key="r.value"
                  :label="r.label"
                  :value="r.value"
                />
              </el-select>
            </el-form-item>

            <!-- Specified → user remote search -->
            <el-form-item
              v-if="local.approverStrategy === 'Specified'"
              :label="t('oa.designer.approverUser')"
            >
              <el-select
                v-model="local.approverUserId"
                style="width: 100%"
                filterable
                remote
                :remote-method="searchUsers"
                :loading="userSearchLoading"
                :placeholder="t('oa.designer.userHint')"
                clearable
              >
                <el-option
                  v-for="u in userOptions"
                  :key="u.value"
                  :label="u.label"
                  :value="u.value"
                />
              </el-select>
            </el-form-item>

            <!-- FormField → 选 user 型表单字段(降级文本) -->
            <el-form-item v-if="local.approverStrategy === 'FormField'" :label="t('oa.designer.approverField')">
              <el-input v-model="local.approverFieldName" :placeholder="t('oa.designer.approverFieldHint')" clearable />
            </el-form-item>
            <!-- DataMap → 映射键 + 匹配字段 -->
            <template v-if="local.approverStrategy === 'DataMap'">
              <el-form-item :label="t('oa.designer.approverMapKey')">
                <el-input v-model="local.approverMapKey" clearable />
              </el-form-item>
              <el-form-item :label="t('oa.designer.approverField')">
                <el-input v-model="local.approverFieldName" clearable />
              </el-form-item>
            </template>
            <!-- Group → 成员行增删 -->
            <template v-if="local.approverStrategy === 'Group'">
              <div v-for="(m, i) in (local.approverMembers ?? [])" :key="i" class="stage-card">
                <div class="stage-card-header">
                  <span class="stage-index-label">{{ t('oa.designer.member') }} {{ i + 1 }}</span>
                  <el-button link type="danger" size="small" @click="removeMember(i)">{{ t('oa.designer.stage.remove') }}</el-button>
                </div>
                <el-select v-model="m.strategy" style="width:100%" clearable>
                  <el-option value="DirectManager" :label="t('oa.designer.strategy.directManager')" />
                  <el-option value="DeptLeader" :label="t('oa.designer.strategy.deptLeader')" />
                  <el-option value="Role" :label="t('oa.designer.strategy.role')" />
                  <el-option value="Specified" :label="t('oa.designer.strategy.specified')" />
                  <el-option value="Starter" :label="t('oa.designer.strategy.starter')" />
                  <el-option value="FormField" :label="t('oa.designer.strategy.formField')" />
                  <el-option value="DataMap" :label="t('oa.designer.strategy.dataMap')" />
                </el-select>
                <el-input v-if="m.strategy === 'Specified'" v-model="m.approverUserId" :placeholder="t('oa.designer.approverUser')" style="margin-top:4px" clearable />
                <el-input-number v-if="m.strategy === 'DirectManager'" v-model="m.approverLevels" :min="1" :max="10" style="width:100%;margin-top:4px" />
                <el-input v-if="m.strategy === 'Role'" v-model.number="m.approverRoleId" :placeholder="t('oa.designer.approverRole')" style="margin-top:4px" clearable />
                <template v-if="m.strategy === 'FormField'"><el-input v-model="m.fieldName" :placeholder="t('oa.designer.approverField')" style="margin-top:4px" clearable /></template>
                <template v-if="m.strategy === 'DataMap'">
                  <el-input v-model="m.mapKey" :placeholder="t('oa.designer.approverMapKey')" style="margin-top:4px" clearable />
                  <el-input v-model="m.fieldName" :placeholder="t('oa.designer.approverField')" style="margin-top:4px" clearable />
                </template>
              </div>
              <el-button style="width:100%;margin-top:4px" @click="addMember">{{ t('oa.designer.addMember') }}</el-button>
            </template>

            <!-- Countersign type -->
            <el-form-item :label="t('oa.designer.countersign')">
              <el-select v-model="local.countersign" style="width: 100%" clearable>
                <el-option value="all"  :label="t('oa.designer.countersign.all')" />
                <el-option value="any"  :label="t('oa.designer.countersign.any')" />
                <el-option value="veto" :label="t('oa.designer.countersign.veto')" />
              </el-select>
            </el-form-item>
          </template>
        </el-form>
      </el-collapse-item>

      <!-- ── 服务任务配置（serviceTask 专属，按 serviceKind 切换）────── -->
      <el-collapse-item
        v-if="isServiceTask"
        :title="t('oa.designer.svc.title')"
        name="service"
      >
        <el-form label-position="top" size="small" class="prop-form">
          <!-- 服务类型 -->
          <el-form-item :label="t('oa.designer.svc.kind')">
            <el-select v-model="local.serviceKind" style="width: 100%">
              <el-option value="dataWriteback" :label="t('oa.designer.svc.kind.dataWriteback')" />
              <el-option value="webApi"        :label="t('oa.designer.svc.kind.webApi')" />
              <el-option value="timer"         :label="t('oa.designer.svc.kind.timer')" />
            </el-select>
          </el-form-item>

          <!-- 数据回写：动作 / 模式 / 参数模板 -->
          <template v-if="local.serviceKind === 'dataWriteback'">
            <el-form-item :label="t('oa.designer.svc.action')">
              <el-select v-model="local.serviceActionName" style="width: 100%" clearable>
                <el-option
                  v-for="a in catalog.actions"
                  :key="a.name"
                  :value="a.name"
                  :label="a.label || a.name"
                />
              </el-select>
            </el-form-item>

            <el-form-item :label="t('oa.designer.svc.mode')">
              <el-select v-model="local.serviceMode" style="width: 100%" clearable>
                <el-option value="sync"  :label="t('oa.designer.svc.mode.sync')" />
                <el-option value="async" :label="t('oa.designer.svc.mode.async')" />
              </el-select>
            </el-form-item>

            <el-form-item :label="t('oa.designer.svc.params')">
              <el-input
                v-model="local.serviceParamsJson"
                type="textarea"
                :rows="3"
                :placeholder="t('oa.designer.svc.paramsHint')"
              />
            </el-form-item>
          </template>

          <!-- 接口调用：连接器 / 路径 / 参数 / 模式 -->
          <template v-else-if="local.serviceKind === 'webApi'">
            <el-form-item :label="t('oa.designer.svc.connector')">
              <el-select v-model="local.serviceConnectorName" style="width: 100%" clearable>
                <el-option
                  v-for="c in catalog.connectors"
                  :key="c.name"
                  :value="c.name"
                  :label="c.label || c.name"
                />
              </el-select>
            </el-form-item>

            <el-form-item :label="t('oa.designer.svc.path')">
              <el-input
                v-model="local.servicePath"
                :placeholder="t('oa.designer.svc.pathHint')"
                clearable
              />
            </el-form-item>

            <el-form-item :label="t('oa.designer.svc.params')">
              <el-input
                v-model="local.serviceParamsJson"
                type="textarea"
                :rows="3"
                :placeholder="t('oa.designer.svc.paramsHint')"
              />
            </el-form-item>

            <el-form-item :label="t('oa.designer.svc.mode')">
              <el-select v-model="local.serviceMode" style="width: 100%" clearable>
                <el-option value="sync"  :label="t('oa.designer.svc.mode.sync')" />
                <el-option value="async" :label="t('oa.designer.svc.mode.async')" />
              </el-select>
            </el-form-item>
          </template>

          <!-- 定时器：延时模式 / 延时值 / 可选到点动作 -->
          <template v-else-if="local.serviceKind === 'timer'">
            <el-form-item :label="t('oa.designer.svc.delayMode')">
              <el-radio-group v-model="local.serviceDelayMode">
                <el-radio value="duration">{{ t('oa.designer.svc.delayMode.duration') }}</el-radio>
                <el-radio value="untilDate">{{ t('oa.designer.svc.delayMode.untilDate') }}</el-radio>
                <el-radio value="untilExpr">{{ t('oa.designer.svc.delayMode.untilExpr') }}</el-radio>
              </el-radio-group>
            </el-form-item>

            <el-form-item :label="t('oa.designer.svc.delayValue')">
              <el-input
                v-model="local.serviceDelayValue"
                :placeholder="t('oa.designer.svc.delayValueHint')"
                clearable
              />
            </el-form-item>

            <el-form-item :label="t('oa.designer.svc.timerAction')">
              <el-select v-model="local.serviceActionName" style="width: 100%" clearable>
                <el-option
                  v-for="a in catalog.actions"
                  :key="a.name"
                  :value="a.name"
                  :label="a.label || a.name"
                />
              </el-select>
            </el-form-item>
          </template>

          <!-- 重试（三 kind 共用）-->
          <el-form-item :label="t('oa.designer.svc.maxRetries')">
            <el-input-number
              v-model="local.serviceMaxRetries"
              :min="0"
              :max="10"
              style="width: 100%"
            />
          </el-form-item>

          <el-form-item :label="t('oa.designer.svc.backoff')">
            <el-input-number
              v-model="local.serviceRetryBackoffSec"
              :min="0"
              :step="5"
              style="width: 100%"
            />
          </el-form-item>
        </el-form>
      </el-collapse-item>

      <!-- ── 串簽档位（审批节点专属）──────────────────── -->
      <el-collapse-item
        v-if="isApproval"
        :title="t('oa.designer.stagesSection')"
        name="stages"
      >
        <el-form label-position="top" size="small" class="prop-form">
          <!-- 启用开关 -->
          <el-form-item>
            <el-switch
              :model-value="stageEnabled"
              @change="(v: boolean) => toggleStages(v)"
            />
            <span class="stage-switch-label">{{ t('oa.designer.stage.enable') }}</span>
          </el-form-item>

          <!-- 档位列表 -->
          <template v-if="stageEnabled">
            <div
              v-for="(stage, idx) in local.stages"
              :key="idx"
              class="stage-card"
            >
              <!-- 档头：序号 + 操作按钮 -->
              <div class="stage-card-header">
                <span class="stage-index-label">档 {{ idx + 1 }}</span>
                <div class="stage-card-actions">
                  <el-button
                    link
                    size="small"
                    :disabled="idx === 0"
                    @click="moveStageUp(idx)"
                  >{{ t('oa.designer.stage.moveUp') }}</el-button>
                  <el-button
                    link
                    size="small"
                    :disabled="idx === (local.stages?.length ?? 1) - 1"
                    @click="moveStageDown(idx)"
                  >{{ t('oa.designer.stage.moveDown') }}</el-button>
                  <el-button
                    link
                    size="small"
                    type="danger"
                    @click="removeStage(idx)"
                  >{{ t('oa.designer.stage.remove') }}</el-button>
                </div>
              </div>

              <!-- 档名（可选）-->
              <el-form-item :label="t('oa.designer.stage.name')">
                <el-input v-model="stage.name" :placeholder="`档 ${idx + 1}`" />
              </el-form-item>

              <!-- 档型：固定 / 逐级 -->
              <el-form-item :label="t('oa.designer.approverType')">
                <el-radio-group v-model="stage.kind">
                  <el-radio value="fixed">{{ t('oa.designer.stage.kind.fixed') }}</el-radio>
                  <el-radio value="managerChain">{{ t('oa.designer.stage.kind.managerChain') }}</el-radio>
                </el-radio-group>
              </el-form-item>

              <!-- fixed: approverStrategy + pickers (reuse same searchUsers/loadRoles) -->
              <template v-if="stage.kind === 'fixed'">
                <el-form-item :label="t('oa.designer.approverType')">
                  <el-select v-model="stage.approverStrategy" style="width: 100%" clearable>
                    <el-option value="DirectManager" :label="t('oa.designer.strategy.directManager')" />
                    <el-option value="DeptLeader"    :label="t('oa.designer.strategy.deptLeader')" />
                    <el-option value="Role"          :label="t('oa.designer.strategy.role')" />
                    <el-option value="Specified"     :label="t('oa.designer.strategy.specified')" />
                    <el-option value="Starter"       :label="t('oa.designer.strategy.starter')" />
                    <el-option value="FormField" :label="t('oa.designer.strategy.formField')" />
                    <el-option value="DataMap"   :label="t('oa.designer.strategy.dataMap')" />
                  </el-select>
                </el-form-item>

                <!-- DirectManager → approverLevels with tooltip (spec R7) -->
                <el-form-item
                  v-if="stage.approverStrategy === 'DirectManager'"
                  :label="t('oa.designer.approverLevels')"
                >
                  <el-tooltip
                    :content="t('oa.designer.stage.approverLevelsTip')"
                    placement="top"
                  >
                    <el-input-number
                      v-model="stage.approverLevels"
                      :min="1"
                      :max="10"
                      style="width: 100%"
                    />
                  </el-tooltip>
                </el-form-item>

                <!-- Role → role select -->
                <el-form-item
                  v-if="stage.approverStrategy === 'Role'"
                  :label="t('oa.designer.approverRole')"
                >
                  <el-select
                    v-model="stage.approverRoleId"
                    style="width: 100%"
                    filterable
                    :loading="roleLoading"
                    @focus="loadRoles"
                    clearable
                  >
                    <el-option
                      v-for="r in roleOptions"
                      :key="r.value"
                      :label="r.label"
                      :value="r.value"
                    />
                  </el-select>
                </el-form-item>

                <!-- Specified → user remote search (reuse searchUsers / userOptions) -->
                <el-form-item
                  v-if="stage.approverStrategy === 'Specified'"
                  :label="t('oa.designer.approverUser')"
                >
                  <el-select
                    v-model="stage.approverUserId"
                    style="width: 100%"
                    filterable
                    remote
                    :remote-method="searchUsers"
                    :loading="userSearchLoading"
                    :placeholder="t('oa.designer.userHint')"
                    clearable
                  >
                    <el-option
                      v-for="u in userOptions"
                      :key="u.value"
                      :label="u.label"
                      :value="u.value"
                    />
                  </el-select>
                </el-form-item>

                <!-- FormField → 表单字段名 -->
                <el-form-item v-if="stage.approverStrategy === 'FormField'" :label="t('oa.designer.approverField')">
                  <el-input v-model="stage.approverFieldName" :placeholder="t('oa.designer.approverFieldHint')" clearable />
                </el-form-item>
                <!-- DataMap → 映射键 + 匹配字段 -->
                <template v-if="stage.approverStrategy === 'DataMap'">
                  <el-form-item :label="t('oa.designer.approverMapKey')">
                    <el-input v-model="stage.approverMapKey" clearable />
                  </el-form-item>
                  <el-form-item :label="t('oa.designer.approverField')">
                    <el-input v-model="stage.approverFieldName" clearable />
                  </el-form-item>
                </template>
              </template>

              <!-- managerChain: maxLevels with tooltip (spec R7) -->
              <el-form-item
                v-if="stage.kind === 'managerChain'"
                :label="t('oa.designer.stage.maxLevels')"
              >
                <el-tooltip
                  :content="t('oa.designer.stage.maxLevelsTip')"
                  placement="top"
                >
                  <el-input-number
                    v-model="stage.maxLevels"
                    :min="1"
                    :max="20"
                    style="width: 100%"
                  />
                </el-tooltip>
              </el-form-item>

              <!-- 会签模式 -->
              <el-form-item :label="t('oa.designer.stage.countersign')">
                <el-select v-model="stage.countersign" style="width: 100%" clearable>
                  <el-option value="all"  :label="t('oa.designer.countersign.all')" />
                  <el-option value="any"  :label="t('oa.designer.countersign.any')" />
                  <el-option value="veto" :label="t('oa.designer.countersign.veto')" />
                </el-select>
              </el-form-item>
            </div>

            <!-- 加档按钮 -->
            <el-button
              style="width: 100%; margin-top: 4px"
              @click="addStage"
            >{{ t('oa.designer.stage.add') }}</el-button>
          </template>
        </el-form>
      </el-collapse-item>

      <!-- ── 進階參數 ──────────────────────────────────── -->
      <el-collapse-item :title="t('oa.designer.advancedParams')" name="advanced">
        <el-form label-position="top" size="small" class="prop-form">
          <el-form-item :label="t('oa.designer.timeoutDays')">
            <el-input-number
              v-model="timeoutDays"
              :min="0"
              :step="1"
              style="width: 100%"
            />
          </el-form-item>

          <el-form-item :label="t('oa.designer.allowReject')">
            <el-switch
              :model-value="!!local.allowReject"
              @change="(v: boolean) => { local.allowReject = v }"
            />
          </el-form-item>

          <el-form-item :label="t('oa.designer.timeoutAction')">
            <el-select v-model="local.timeoutAction" style="width: 100%" clearable>
              <el-option value="remind"   :label="t('oa.designer.timeoutAction.remind')" />
              <el-option value="approve"  :label="t('oa.designer.timeoutAction.approve')" />
              <el-option value="reject"   :label="t('oa.designer.timeoutAction.reject')" />
              <el-option value="escalate" :label="t('oa.designer.timeoutAction.escalate')" />
            </el-select>
          </el-form-item>

          <el-form-item v-if="isApproval" :label="t('oa.designer.approverWhen')">
            <el-input v-model="local.approverWhen" type="textarea" :rows="2" :placeholder="t('oa.designer.approverWhenHint')" />
          </el-form-item>
          <el-form-item v-if="isApproval" :label="t('oa.designer.approverFilter')">
            <el-input v-model="local.approverFilter" type="textarea" :rows="2" :placeholder="t('oa.designer.approverFilterHint')" />
          </el-form-item>
        </el-form>
      </el-collapse-item>

      <!-- ── 知會人員 ──────────────────────────────────── -->
      <el-collapse-item :title="t('oa.designer.ccSection')" name="cc">
        <el-form label-position="top" size="small" class="prop-form">
          <el-form-item :label="t('oa.designer.ccUsers')">
            <el-select
              v-model="local.ccUsers"
              style="width: 100%"
              multiple
              filterable
              remote
              :remote-method="searchCcUsers"
              :loading="ccUserSearchLoading"
              :placeholder="t('oa.designer.userHint')"
              clearable
            >
              <el-option
                v-for="u in ccUserOptions"
                :key="u.value"
                :label="u.label"
                :value="u.value"
              />
            </el-select>
          </el-form-item>
        </el-form>
      </el-collapse-item>
    </el-collapse>
  </div>
</template>

<style scoped>
.node-prop-panel {
  padding: 8px;
  overflow-y: auto;
  height: 100%;
  box-sizing: border-box;
}

.panel-title {
  font-size: 13px;
  font-weight: 600;
  color: var(--cp-ink);
  padding: 4px 0 8px;
  border-bottom: 1px solid var(--cp-line);
  margin-bottom: 8px;
}

.prop-form {
  padding: 4px 2px 0;
}

/* Compact form items inside the panel */
.prop-form :deep(.el-form-item) {
  margin-bottom: 10px;
}

.prop-form :deep(.el-form-item__label) {
  font-size: 12px;
  color: var(--cp-text);
  padding-bottom: 2px;
  line-height: 1.4;
}

/* 串簽档位卡片 */
.stage-card {
  border: 1px solid var(--cp-line);
  border-radius: var(--cp-r-sm);
  padding: 8px 10px 4px;
  margin-bottom: 8px;
  background: var(--cp-bg-th);
}

.stage-card-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 6px;
}

.stage-index-label {
  font-size: 12px;
  font-weight: 600;
  color: var(--cp-brand);
}

.stage-card-actions {
  display: flex;
  gap: 2px;
}

.stage-switch-label {
  margin-left: 8px;
  font-size: 12px;
  color: var(--cp-text);
}
</style>

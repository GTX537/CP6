<script setup lang="ts">
import { ref, computed, watch, nextTick } from 'vue'
import { useI18n } from 'vue-i18n'
import { userApi } from '@/api/sys/user'
import { roleApi } from '@/api/sys/role'
import type { SchemaNode } from './designerModel'

const props = defineProps<{ node: SchemaNode }>()
const emit = defineEmits<{ update: [patch: Partial<SchemaNode>] }>()
const { t } = useI18n()

// ── Local copy (mirrors props.node; changes emit 'update') ────────
function cloneNode(n: SchemaNode): SchemaNode {
  return { ...n, ccUsers: n.ccUsers ? [...n.ccUsers] : [] }
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

// ── Collapse ──────────────────────────────────────────────────────
const collapseActive = ref<string[]>(['basic', 'advanced', 'cc'])

// ── User search (Specified approver) ─────────────────────────────
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

// ── Role list (Role strategy) ─────────────────────────────────────
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
            <el-tag type="info" size="small">{{ local.type }}</el-tag>
          </el-form-item>

          <!-- approval-specific fields -->
          <template v-if="isApproval">
            <el-form-item :label="t('oa.designer.approverType')">
              <el-select v-model="local.approverStrategy" style="width: 100%" clearable>
                <el-option value="DirectManager" :label="t('oa.designer.strategy.directManager')" />
                <el-option value="DeptLeader"   :label="t('oa.designer.strategy.deptLeader')" />
                <el-option value="Role"         :label="t('oa.designer.strategy.role')" />
                <el-option value="Specified"    :label="t('oa.designer.strategy.specified')" />
                <el-option value="Starter"      :label="t('oa.designer.strategy.starter')" />
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
  color: #303133;
  padding: 4px 0 8px;
  border-bottom: 1px solid #e4e7ed;
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
  color: #606266;
  padding-bottom: 2px;
  line-height: 1.4;
}
</style>

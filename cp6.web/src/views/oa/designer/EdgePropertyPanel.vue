<script setup lang="ts">
import { ref, watch, nextTick } from 'vue'
import { useI18n } from 'vue-i18n'
import { userApi } from '@/api/sys/user'
import type { SchemaEdge } from './designerModel'

const props = defineProps<{ edge: SchemaEdge }>()
const emit = defineEmits<{ update: [patch: Partial<SchemaEdge>] }>()
const { t } = useI18n()

// ── Local copy ────────────────────────────────────────────────────
function cloneEdge(e: SchemaEdge): SchemaEdge {
  return { ...e, ccUsers: e.ccUsers ? [...e.ccUsers] : [] }
}

const syncing = ref(false)
const local = ref<SchemaEdge>(cloneEdge(props.edge))

// conditionType: UI concept — derived from whether condition is set
const conditionType = ref<'none' | 'condition'>(props.edge.condition ? 'condition' : 'none')

watch(
  () => props.edge,
  async (e) => {
    syncing.value = true
    local.value = cloneEdge(e)
    conditionType.value = e.condition ? 'condition' : 'none'
    await nextTick()
    syncing.value = false
  },
  { deep: true },
)

// When switching to 'none', clear the condition expression
watch(conditionType, (ct) => {
  if (ct === 'none') {
    local.value = { ...local.value, condition: undefined }
  }
})

watch(
  local,
  () => {
    if (syncing.value) return
    emit('update', { ...local.value, ccUsers: [...(local.value.ccUsers ?? [])] })
  },
  { deep: true },
)

// ── CC user search ────────────────────────────────────────────────
interface UserOpt { label: string; value: string }
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
  <div class="edge-prop-panel">
    <div class="panel-title">{{ t('oa.designer.edgeProps') }}</div>

    <el-form label-position="top" size="small" class="prop-form">
      <!-- 路徑類型 -->
      <el-form-item :label="t('oa.designer.pathType')">
        <el-radio-group v-model="conditionType">
          <el-radio value="none">{{ t('oa.designer.conditionNone') }}</el-radio>
          <el-radio value="condition">{{ t('oa.designer.conditionCond') }}</el-radio>
        </el-radio-group>
      </el-form-item>

      <!-- 條件表達式（條件路徑時顯示）-->
      <el-form-item
        v-if="conditionType === 'condition'"
        :label="t('oa.designer.conditionExpr')"
      >
        <el-input
          v-model="local.condition"
          type="textarea"
          :rows="2"
          :placeholder="t('oa.designer.conditionPlaceholder')"
        />
      </el-form-item>

      <!-- 知會人員 -->
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
  </div>
</template>

<style scoped>
.edge-prop-panel {
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

.prop-form :deep(.el-form-item) {
  margin-bottom: 10px;
}

.prop-form :deep(.el-form-item__label) {
  font-size: 12px;
  color: var(--cp-text);
  padding-bottom: 2px;
  line-height: 1.4;
}
</style>

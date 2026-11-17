<template>
  <section class="manufacturing-workspace" aria-label="制造成本维护">
    <el-card shadow="never">
      <el-alert v-if="error" role="alert" type="error" :title="error" :closable="false" />
      <div class="context-bar">
        <label for="manufacturing-ledger">财务账套</label>
        <el-select id="manufacturing-ledger" v-model="ledger" aria-label="财务账套"
          placeholder="请选择法人和账套" :loading="loading" @change="selectLedger">
          <el-option v-for="choice in choices" :key="choiceId(choice)" :value="choiceId(choice)"
            :label="choice.legalEntityLabel + ' / ' + choice.ledgerLabel" />
        </el-select>
        <el-button :disabled="loading" @click="loadChoices">刷新账套</el-button>
        <span v-if="finance" class="context-data">会计日 {{ finance.businessDate }} · {{ finance.currency }}</span>
      </div>
      <el-empty v-if="!loading && !choices.length && !error" description="当前账号没有可用制造账套" :image-size="60" />
      <el-tabs v-if="finance" v-model="active">
        <el-tab-pane v-if="canSeeStandard" label="标准成本" name="standard" lazy>
          <ManufacturingStandardCostPanel :key="finance.token" :finance="finance" />
        </el-tab-pane>
        <el-tab-pane label="工序费率" name="rates" lazy>
          <ManufacturingRatePanel :key="finance.token" :finance="finance" />
        </el-tab-pane>
      </el-tabs>
    </el-card>
  </section>
</template>

<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { manufacturingCostApi, maintenanceMessage } from '@/api/fin/manufacturingCost'
import { useOperationsIntent } from '@/composables/useOperationsIntent'
import { usePermissionStore } from '@/stores/permission'
import type { FinanceChoice, FinanceContext } from '@/types/fin/manufacturingCost'
import ManufacturingRatePanel from './ManufacturingRatePanel.vue'
import ManufacturingStandardCostPanel from './ManufacturingStandardCostPanel.vue'

const permissions = usePermissionStore()
const mayRead = computed(() => permissions.loaded && permissions.has('fin-cost:view'))
const canSeeStandard = computed(() => permissions.loaded && permissions.has('fin-cost:basis-view'))
const choices = ref<FinanceChoice[]>([])
const ledger = ref('')
const active = ref('standard')
const finance = ref<FinanceContext | null>(null)
const error = ref('')
const loading = ref(false)
const choiceId = (choice: FinanceChoice) => JSON.stringify([choice.legalEntityLabel, choice.ledgerLabel])
const intent = useOperationsIntent(clear)

function clear() {
  choices.value = []
  ledger.value = ''
  finance.value = null
  error.value = ''
  loading.value = false
}

async function loadChoices() {
  intent.invalidate()
  clear()
  if (!mayRead.value) return
  const token = intent.capture()
  loading.value = true
  try {
    const result = await manufacturingCostApi.options()
    if (!intent.current(token) || !mayRead.value) return
    choices.value = result.contexts
  } catch (failure) {
    if (intent.current(token)) error.value = maintenanceMessage(failure, '无法读取财务账套。')
  } finally {
    if (intent.current(token)) loading.value = false
  }
}

async function selectLedger(value: string) {
  intent.invalidate()
  finance.value = null
  error.value = ''
  const choice = choices.value.find(item => choiceId(item) === value)
  if (!choice || !mayRead.value) return
  active.value = canSeeStandard.value ? 'standard' : 'rates'
  const token = intent.capture()
  loading.value = true
  try {
    const result = await manufacturingCostApi.options(choice.legalEntityLabel, choice.ledgerLabel)
    if (!intent.current(token) || !mayRead.value) return
    const currency = result.currencies[0]
    if (result.availability !== 'AVAILABLE' || !result.scope || !result.contextToken || !result.businessDate || !currency) {
      error.value = '所选账套当前不可用。'
      return
    }
    const scope = result.scope
    finance.value = { token: result.contextToken, businessDate: result.businessDate, currency,
      scopeKey: JSON.stringify([scope.environmentId, scope.tenantId, scope.legalEntityKey, scope.ledgerKey, scope.mode]) }
  } catch (failure) {
    if (intent.current(token)) error.value = maintenanceMessage(failure, '无法读取所选账套。')
  } finally {
    if (intent.current(token)) loading.value = false
  }
}

watch(mayRead, allowed => { if (allowed) void loadChoices(); else { intent.invalidate(); clear() } })
watch(canSeeStandard, allowed => { if (!allowed) active.value = 'rates' })
onMounted(() => { if (mayRead.value) void loadChoices() })
</script>

<style scoped>
.context-bar { display: flex; flex-wrap: wrap; align-items: center; gap: 12px; margin-block: 8px 16px; }
.context-bar .el-select { width: min(320px, 100%); }
.context-data { color: var(--el-text-color-secondary); }
</style>

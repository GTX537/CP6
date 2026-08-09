<template>
  <el-dialog v-model="serialVisible" title="Post serial lifecycle" width="660px" append-to-body>
    <el-form :model="serialForm" label-width="150px">
      <el-form-item label="Transaction">
        <el-select v-model="serialForm.txnType">
          <el-option v-for="type in serialTypes" :key="type" :label="type" :value="type" />
        </el-select>
      </el-form-item>
      <el-form-item label="Product"><el-input v-model="serialForm.productCd" maxlength="128" /></el-form-item>
      <el-form-item label="Warehouse"><el-input v-model="serialForm.warehouseCd" maxlength="128" /></el-form-item>
      <el-form-item label="Lot"><el-input v-model="serialForm.lotNo" maxlength="128" /></el-form-item>
      <el-form-item v-if="serialNeedsSource" label="Source location">
        <el-input v-model="serialForm.fromLocationCd" maxlength="128" />
      </el-form-item>
      <el-form-item v-if="serialNeedsTarget" label="Target location">
        <el-input v-model="serialForm.toLocationCd" maxlength="128" />
      </el-form-item>
      <el-form-item label="LPN"><el-input v-model="serialForm.lpnNo" maxlength="64" /></el-form-item>
      <el-form-item label="Device"><el-input v-model="serialForm.deviceId" maxlength="128" /></el-form-item>
      <el-form-item label="Serial numbers">
        <el-input
          v-model="serialForm.serialNosText"
          type="textarea"
          :rows="7"
          placeholder="One serial number per line"
        />
      </el-form-item>
    </el-form>
    <template #footer>
      <el-button @click="serialVisible = false">Cancel</el-button>
      <el-button type="primary" :loading="serialSaving" @click="submitSerialLifecycle">
        Commit transaction
      </el-button>
    </template>
  </el-dialog>

  <el-dialog v-model="trackingVisible" title="Controlled serial tracking conversion" width="720px" append-to-body>
    <el-alert
      :closable="false"
      type="warning"
      show-icon
      title="Conversion is irreversible after serial transactions exist. Every physical unit in every stock bucket must be listed exactly once."
    />
    <el-form :model="trackingForm" label-width="150px" class="dialog-form">
      <el-form-item label="Product"><el-input v-model="trackingForm.productCd" maxlength="128" /></el-form-item>
      <el-form-item label="Tracking mode">
        <el-select v-model="trackingForm.trackingMode">
          <el-option label="Serial" :value="2" />
          <el-option label="Lot and serial" :value="3" />
        </el-select>
      </el-form-item>
      <el-form-item label="Existing serials">
        <div class="field-stack">
          <el-input
            v-model="trackingForm.existingSerialsText"
            type="textarea"
            :rows="10"
            placeholder="serial,warehouse,location,lot — one physical unit per line"
          />
          <small>Leave empty only when the product has no physical stock.</small>
        </div>
      </el-form-item>
    </el-form>
    <template #footer>
      <el-button @click="trackingVisible = false">Cancel</el-button>
      <el-button type="danger" :loading="trackingSaving" @click="submitSerialTracking">
        Validate and enable
      </el-button>
    </template>
  </el-dialog>

  <el-dialog v-model="lpnVisible" :title="lpnTitle" width="720px" append-to-body>
    <el-alert
      v-if="selectedLpn"
      :closable="false"
      type="info"
      :title="`${selectedLpn.lpnNo} · ${selectedLpn.warehouseCd} / ${selectedLpn.locationCd}`"
    />
    <el-form :model="lpnForm" label-width="160px" class="dialog-form">
      <template v-if="lpnAction === 'create'">
        <el-form-item label="LPN"><el-input v-model="lpnForm.lpnNo" maxlength="64" /></el-form-item>
        <el-form-item label="Container type"><el-input v-model="lpnForm.containerType" maxlength="128" /></el-form-item>
        <el-form-item label="Warehouse"><el-input v-model="lpnForm.warehouseCd" maxlength="128" /></el-form-item>
        <el-form-item label="Location"><el-input v-model="lpnForm.locationCd" maxlength="128" /></el-form-item>
      </template>
      <el-form-item v-if="lpnAction === 'move'" label="Target location">
        <el-input v-model="lpnForm.toLocationCd" maxlength="128" />
      </el-form-item>
      <template v-if="lpnAction === 'split'">
        <el-form-item label="Target LPN"><el-input v-model="lpnForm.targetLpnNo" maxlength="64" /></el-form-item>
        <el-form-item label="Target container">
          <el-input v-model="lpnForm.targetContainerType" maxlength="128" />
        </el-form-item>
      </template>
      <el-form-item v-if="lpnAction === 'merge'" label="Source LPN">
        <el-input v-model="lpnForm.sourceLpnNo" maxlength="64" />
      </el-form-item>
      <el-form-item v-if="['pack', 'unpack', 'split'].includes(lpnAction)" label="Child LPNs">
        <el-input
          v-model="lpnForm.childLpnsText"
          type="textarea"
          :rows="4"
          placeholder="One child LPN per line"
        />
      </el-form-item>
      <el-form-item v-if="lpnAction === 'pack'" label="Contents">
        <div class="field-stack">
          <el-input
            v-model="lpnForm.contentsText"
            type="textarea"
            :rows="7"
            placeholder="product,lot,serial,qty — one content line per row"
          />
          <small>Serialized rows require quantity 1. Leave serial blank for aggregate quantity.</small>
        </div>
      </el-form-item>
      <el-form-item v-if="['unpack', 'split'].includes(lpnAction)" label="Serial numbers">
        <el-input
          v-model="lpnForm.serialNosText"
          type="textarea"
          :rows="5"
          placeholder="One serial number per line"
        />
      </el-form-item>
      <el-form-item label="Device"><el-input v-model="lpnForm.deviceId" maxlength="128" /></el-form-item>
    </el-form>
    <template #footer>
      <el-button @click="lpnVisible = false">Cancel</el-button>
      <el-button
        :type="lpnAction === 'merge' ? 'danger' : 'primary'"
        :loading="lpnSaving"
        @click="submitLpn"
      >
        {{ lpnAction === 'create' ? 'Create LPN' : `Confirm ${lpnAction}` }}
      </el-button>
    </template>
  </el-dialog>
</template>

<script setup lang="ts">
import { computed, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { newProductionOperationId, productionApi } from '@/api/wms/production'
import type {
  LogisticsUnit,
  LpnLifecycleAction,
  SerialLifecycleType,
} from '@/api/wms/production'
import {
  buildCreateLpnCommand,
  buildLpnLifecycleCommand,
  buildSerialLifecycleCommand,
  buildSerialTrackingCommand,
  ProductionFormValidationError,
  type LpnLifecycleForm,
  type SerialLifecycleForm,
  type SerialTrackingForm,
} from '@/utils/wmsProductionForms'

const emit = defineEmits<{
  serialsChanged: []
  lpnsChanged: []
}>()

const serialTypes: SerialLifecycleType[] = [
  'RECEIVE', 'PUTAWAY', 'MOVE', 'PICK', 'SHIP', 'COUNT', 'RETURN',
]
const serialSourceTypes: SerialLifecycleType[] = ['PUTAWAY', 'MOVE', 'PICK', 'SHIP', 'COUNT']
const serialTargetTypes: SerialLifecycleType[] = ['RECEIVE', 'PUTAWAY', 'MOVE', 'RETURN']

const newSerialForm = (): SerialLifecycleForm => ({
  txnType: 'MOVE',
  productCd: '',
  serialNosText: '',
  warehouseCd: '',
  lotNo: '',
  fromLocationCd: '',
  toLocationCd: '',
  lpnNo: '',
  deviceId: '',
})
const newTrackingForm = (): SerialTrackingForm => ({
  productCd: '',
  trackingMode: 2,
  existingSerialsText: '',
})
const newLpnForm = (): LpnLifecycleForm => ({
  lpnNo: '',
  containerType: '',
  warehouseCd: '',
  locationCd: '',
  deviceId: '',
  toLocationCd: '',
  childLpnsText: '',
  contentsText: '',
  serialNosText: '',
  targetLpnNo: '',
  targetContainerType: '',
  sourceLpnNo: '',
})

const serialVisible = ref(false)
const serialSaving = ref(false)
const serialOperationId = ref('')
const serialForm = reactive(newSerialForm())
const serialNeedsSource = computed(() => serialSourceTypes.includes(serialForm.txnType))
const serialNeedsTarget = computed(() => serialTargetTypes.includes(serialForm.txnType))

const trackingVisible = ref(false)
const trackingSaving = ref(false)
const trackingOperationId = ref('')
const trackingForm = reactive(newTrackingForm())

type LpnDialogAction = 'create' | LpnLifecycleAction
const lpnVisible = ref(false)
const lpnSaving = ref(false)
const lpnOperationId = ref('')
const lpnAction = ref<LpnDialogAction>('create')
const selectedLpn = ref<LogisticsUnit>()
const lpnForm = reactive(newLpnForm())
const lpnTitle = computed(() =>
  lpnAction.value === 'create'
    ? 'Create logistics unit'
    : `${lpnAction.value.toUpperCase()} ${selectedLpn.value?.lpnNo ?? ''}`,
)

function showValidation(error: unknown) {
  if (!(error instanceof ProductionFormValidationError)) return false
  ElMessage.warning(error.message)
  return true
}

function showCommandFailure(error: unknown) {
  const status = (error as { response?: { status?: number } })?.response?.status
  if (status === 401) return
  if (status === 409) {
    ElMessage.warning('The current serial or LPN state changed. Refresh the table before retrying.')
    return
  }
  if (status && status < 500) {
    ElMessage.warning('The command was rejected. Correct the form or refresh current state before retrying.')
    return
  }
  ElMessage.warning(
    'Command result was not confirmed. Refresh current state before retrying; this form will reuse the same operation ID.',
  )
}

function openSerialLifecycle() {
  Object.assign(serialForm, newSerialForm())
  serialOperationId.value = newProductionOperationId()
  serialVisible.value = true
}

function openSerialTracking() {
  Object.assign(trackingForm, newTrackingForm())
  trackingOperationId.value = newProductionOperationId()
  trackingVisible.value = true
}

function openLpn(action: LpnDialogAction, lpn?: LogisticsUnit) {
  Object.assign(lpnForm, newLpnForm())
  lpnAction.value = action
  selectedLpn.value = lpn
  lpnOperationId.value = newProductionOperationId()
  lpnVisible.value = true
}

async function submitSerialLifecycle() {
  let request
  try {
    request = buildSerialLifecycleCommand(serialForm)
  } catch (error) {
    if (showValidation(error)) return
    throw error
  }
  serialSaving.value = true
  try {
    await productionApi.postSerial({
      ...request,
      operationId: serialOperationId.value,
    })
    serialVisible.value = false
    ElMessage.success('Serial transaction committed')
    emit('serialsChanged')
  } catch (error) {
    showCommandFailure(error)
  } finally {
    serialSaving.value = false
  }
}

async function submitSerialTracking() {
  let request
  try {
    request = buildSerialTrackingCommand(trackingForm)
  } catch (error) {
    if (showValidation(error)) return
    throw error
  }
  try {
    await ElMessageBox.confirm(
      `Enable serial tracking for ${request.productCd}? This conversion cannot be downgraded after serial activity.`,
      'Confirm controlled conversion',
      {
        type: 'warning',
        confirmButtonText: 'Enable tracking',
      },
    )
  } catch {
    return
  }
  trackingSaving.value = true
  try {
    await productionApi.enableSerialTracking({
      ...request,
      operationId: trackingOperationId.value,
    })
    trackingVisible.value = false
    ElMessage.success('Serial tracking enabled')
    emit('serialsChanged')
  } catch (error) {
    showCommandFailure(error)
  } finally {
    trackingSaving.value = false
  }
}

async function submitLpn() {
  const action = lpnAction.value
  if (action === 'create') {
    let request
    try {
      request = buildCreateLpnCommand(lpnForm)
    } catch (error) {
      if (showValidation(error)) return
      throw error
    }
    lpnSaving.value = true
    try {
      await productionApi.createLpn({
        ...request,
        operationId: lpnOperationId.value,
      })
      lpnVisible.value = false
      ElMessage.success('LPN created')
      emit('lpnsChanged')
    } catch (error) {
      showCommandFailure(error)
    } finally {
      lpnSaving.value = false
    }
    return
  }

  let request
  try {
    request = buildLpnLifecycleCommand(action, selectedLpn.value?.rowVersion ?? '', lpnForm)
  } catch (error) {
    if (showValidation(error)) return
    throw error
  }
  if (action === 'merge' && 'sourceLpnNo' in request) {
    try {
      await ElMessageBox.confirm(
        `Merge ${request.sourceLpnNo} into ${selectedLpn.value?.lpnNo}?`,
        'Confirm LPN merge',
        { type: 'warning', confirmButtonText: 'Merge' },
      )
    } catch {
      return
    }
  }
  lpnSaving.value = true
  try {
    await productionApi.lpnCommand(selectedLpn.value!.lpnNo, action, {
      ...request,
      operationId: lpnOperationId.value,
    })
    lpnVisible.value = false
    ElMessage.success(`LPN ${action} committed`)
    emit('lpnsChanged')
  } catch (error) {
    showCommandFailure(error)
  } finally {
    lpnSaving.value = false
  }
}

defineExpose({
  openSerialLifecycle,
  openSerialTracking,
  openLpn,
})
</script>

<style scoped>
.dialog-form { margin-top: 16px; }
.field-stack { width: 100%; display: grid; gap: 6px; }
.field-stack small { color: var(--el-text-color-secondary); line-height: 1.4; }
</style>

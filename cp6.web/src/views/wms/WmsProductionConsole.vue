<template>
  <div>
    <el-tabs v-model="tab" @tab-change="loadTab">
      <el-tab-pane label="Rollout" name="rollout" v-permission="'wms-mobile:device-manage'">
        <div class="tab-toolbar">
          <el-button type="primary" @click="openFeatureChange()">Request new warehouse</el-button>
          <el-button @click="loadFeatureFlags">Refresh</el-button>
        </div>
        <el-alert
          :closable="false"
          type="warning"
          title="Production feature flags are read-only here. Every change requires OA approval by a different person."
          class="rollout-alert"
        />
        <el-table :data="featureFlags">
          <el-table-column prop="warehouseCd" label="Warehouse" />
          <el-table-column label="Production MOVE">
            <template #default="{ row }">
              <el-tag :type="row.productionMoveEnabled ? 'success' : 'info'">
                {{ row.productionMoveEnabled ? 'Enabled' : 'Disabled' }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column label="Serial / LPN">
            <template #default="{ row }">
              <el-tag :type="row.serialLpnEnabled ? 'success' : 'info'">
                {{ row.serialLpnEnabled ? 'Enabled' : 'Disabled' }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column prop="scanRetentionDays" label="Scan retention (days)" min-width="180" />
          <el-table-column label="Approval" min-width="160">
            <template #default="{ row }">
              <el-tag v-if="hasPendingFeatureChange(row.warehouseCd)" type="warning">Pending</el-tag>
              <el-button
                v-else
                text
                type="primary"
                @click="openFeatureChange(row)"
              >
                Request change
              </el-button>
            </template>
          </el-table-column>
        </el-table>

        <h4>Approval history</h4>
        <el-table :data="featureChanges">
          <el-table-column prop="warehouseCd" label="Warehouse" />
          <el-table-column prop="changeTicket" label="Change ticket" min-width="140" />
          <el-table-column label="Target" min-width="240">
            <template #default="{ row }">
              MOVE {{ row.targetProductionMoveEnabled ? 'on' : 'off' }};
              Serial/LPN {{ row.targetSerialLpnEnabled ? 'on' : 'off' }};
              {{ row.targetScanRetentionDays }} days
            </template>
          </el-table-column>
          <el-table-column label="Status" min-width="150">
            <template #default="{ row }">
              <el-tag :type="featureStatusType(row.status)">{{ row.status }}</el-tag>
              <div v-if="row.failureCode" class="failure-code">{{ row.failureCode }}</div>
            </template>
          </el-table-column>
          <el-table-column prop="requestedAtUtc" label="Requested at" min-width="190" />
          <el-table-column label="Action">
            <template #default="{ row }">
              <el-button
                v-if="row.status === 'PENDING'"
                text
                type="danger"
                @click="cancelFeatureChange(row)"
              >
                Cancel
              </el-button>
            </template>
          </el-table-column>
        </el-table>
      </el-tab-pane>

      <el-tab-pane label="Task scopes" name="scopes" v-permission="'pub-data-scope:query'">
        <div class="tab-toolbar">
          <el-input-number v-model="scopeRoleId" :min="2" :max="999999" />
          <el-button @click="loadRoleScopes">Load role</el-button>
          <el-button v-permission="'pub-data-scope:edit'" type="primary" @click="addRoleScope">Add scope</el-button>
          <el-button v-permission="'pub-data-scope:edit'" type="success" @click="saveRoleScopes">Save scopes</el-button>
        </div>
        <el-alert
          :closable="false"
          type="info"
          title="Blank area grants the whole warehouse. A role with no scope is denied all WMS task data. Administrator role 1 is always unrestricted."
        />
        <el-table :data="roleScopes" class="scope-table">
          <el-table-column label="Warehouse" min-width="160">
            <template #default="{ row }">
              <el-input v-model="row.warehouseCd" placeholder="W01 or *" />
            </template>
          </el-table-column>
          <el-table-column label="Area" min-width="160">
            <template #default="{ row }">
              <el-input v-model="row.areaCd" placeholder="Blank = whole warehouse" />
            </template>
          </el-table-column>
          <el-table-column label="Action" width="100">
            <template #default="{ $index }">
              <el-button v-permission="'pub-data-scope:edit'" text type="danger" @click="removeRoleScope($index)">Remove</el-button>
            </template>
          </el-table-column>
        </el-table>
      </el-tab-pane>

      <el-tab-pane label="Analytics" name="analytics" v-permission="'wms-mobile:analytics'">
        <el-row :gutter="12">
          <el-col v-for="metric in metrics" :key="metric.label" :span="4">
            <el-statistic :title="metric.label" :value="metric.value" />
          </el-col>
        </el-row>
      </el-tab-pane>

      <el-tab-pane label="Devices" name="devices" v-permission="'wms-mobile:device-manage'">
        <div class="tab-toolbar">
          <el-button type="primary" @click="activationVisible = true">Create activation QR</el-button>
          <el-button @click="loadDevices">Refresh</el-button>
        </div>
        <el-table :data="devices">
          <el-table-column prop="deviceId" label="Device" min-width="210" />
          <el-table-column prop="deviceMode" label="Mode" />
          <el-table-column prop="platform" label="Platform" />
          <el-table-column prop="warehouseCd" label="Warehouse" />
          <el-table-column prop="areaCd" label="Area" />
          <el-table-column prop="currentUser" label="Current user" />
          <el-table-column prop="lastSeenAt" label="Last heartbeat" min-width="165" />
          <el-table-column prop="status" label="Status" />
          <el-table-column label="Action">
            <template #default="{ row }">
              <el-button text :type="row.status === 'Active' ? 'danger' : 'primary'"
                         @click="toggleDevice(row)">
                {{ row.status === 'Active' ? 'Disable' : 'Enable' }}
              </el-button>
            </template>
          </el-table-column>
        </el-table>
      </el-tab-pane>

      <el-tab-pane label="Barcodes" name="barcodes" v-permission="'wms-mobile:barcode-manage'">
        <div class="tab-toolbar">
          <el-button type="primary" @click="upsertBarcode">Add barcode</el-button>
          <el-button @click="upsertBarcodeProfile">Add parser profile</el-button>
          <el-button @click="testBarcodeParser">Test parser</el-button>
          <el-upload :auto-upload="false" :show-file-list="false" accept=".xlsx"
                     :on-change="handleBarcodeFile">
            <el-button>Excel preflight</el-button>
          </el-upload>
          <el-button v-if="pendingBarcodeFile" type="primary" @click="commitBarcodeFile">Commit valid rows</el-button>
          <el-button @click="loadBarcodes">Refresh</el-button>
        </div>
        <el-alert v-if="barcodeImport" :closable="false"
                  :type="barcodeImport.invalidCount ? 'warning' : 'success'"
                  :title="`${barcodeImport.validCount} valid / ${barcodeImport.invalidCount} invalid`" />
        <el-table :data="barcodes">
          <el-table-column prop="barcode" label="Barcode" min-width="180" />
          <el-table-column prop="barcodeType" label="Type" />
          <el-table-column prop="targetKey" label="Target" />
          <el-table-column prop="productCd" label="Product" />
          <el-table-column prop="lotNo" label="Lot" />
          <el-table-column prop="locationCd" label="Location" />
          <el-table-column prop="packageUnitCd" label="Pack unit" />
          <el-table-column prop="conversionRate" label="Rate" />
        </el-table>
        <el-divider content-position="left">GS1 and custom compound parser profiles</el-divider>
        <el-table :data="barcodeProfiles">
          <el-table-column prop="profileName" label="Profile" />
          <el-table-column prop="format" label="Format" />
          <el-table-column prop="pattern" label="Pattern" min-width="220" show-overflow-tooltip />
          <el-table-column prop="mappingJson" label="Mapping" min-width="220" show-overflow-tooltip />
          <el-table-column prop="priority" label="Priority" />
          <el-table-column prop="isEnabled" label="Enabled" />
        </el-table>
      </el-tab-pane>

      <el-tab-pane label="Serials" name="serials" v-permission="'wms-mobile:view'">
        <div class="tab-toolbar">
          <el-button type="primary" v-permission="'wms-mobile:serial-manage'" @click="postSerialLifecycle">Post lifecycle</el-button>
          <el-button v-permission="'wms-mobile:serial-manage'" @click="enableSerialTracking">Enable tracking</el-button>
          <el-button @click="loadSerials">Refresh</el-button>
        </div>
        <el-table :data="serials">
          <el-table-column prop="serialNo" label="Serial" min-width="170" />
          <el-table-column prop="productCd" label="Product" />
          <el-table-column prop="warehouseCd" label="Warehouse" />
          <el-table-column prop="locationCd" label="Location" />
          <el-table-column prop="lotNo" label="Lot" />
          <el-table-column prop="lpnNo" label="LPN" />
          <el-table-column prop="status" label="Status" />
        </el-table>
      </el-tab-pane>

      <el-tab-pane label="LPNs" name="lpns">
        <div class="tab-toolbar">
          <el-button type="primary" v-permission="'wms-mobile:lpn-manage'" @click="createLpn">Create LPN</el-button>
          <el-button @click="loadLpns">Refresh</el-button>
        </div>
        <el-table :data="lpns">
          <el-table-column prop="lpnNo" label="LPN" min-width="160" />
          <el-table-column prop="containerType" label="Container" />
          <el-table-column prop="warehouseCd" label="Warehouse" />
          <el-table-column prop="locationCd" label="Location" />
          <el-table-column prop="parentLpnNo" label="Parent" />
          <el-table-column label="Contents">
            <template #default="{ row }">{{ row.contents.length }} lines / {{ row.childLpns.length }} child LPNs</template>
          </el-table-column>
          <el-table-column prop="status" label="Status" />
          <el-table-column label="Action">
            <template #default="{ row }">
              <el-button text @click="moveLpn(row)">Move tree</el-button>
              <el-button text @click="packLpn(row)">Pack</el-button>
              <el-button text @click="unpackLpn(row)">Unpack</el-button>
              <el-button text @click="splitLpn(row)">Split</el-button>
              <el-button text @click="mergeLpn(row)">Merge</el-button>
            </template>
          </el-table-column>
        </el-table>
      </el-tab-pane>

      <el-tab-pane label="Labels" name="labels" v-permission="'wms-mobile:label-print'">
        <div class="tab-toolbar">
          <el-button type="primary" @click="createLabelJob">Create print job</el-button>
          <el-button v-permission="'wms-mobile:label-manage'" @click="upsertLabelTemplate">Add template</el-button>
          <el-button @click="loadLabels">Refresh</el-button>
        </div>
        <el-table :data="labelJobs">
          <el-table-column prop="jobNo" label="Job" min-width="150" />
          <el-table-column prop="warehouseCd" label="Warehouse" />
          <el-table-column prop="templateName" label="Template" />
          <el-table-column prop="format" label="Format" />
          <el-table-column prop="printerName" label="Printer" />
          <el-table-column prop="status" label="Status" />
          <el-table-column prop="attemptCount" label="Attempts" />
          <el-table-column prop="resultMessage" label="Result" min-width="180" />
        </el-table>
        <el-divider content-position="left">Templates</el-divider>
        <el-table :data="labelTemplates">
          <el-table-column prop="templateName" label="Template" />
          <el-table-column prop="format" label="Format" />
          <el-table-column prop="language" label="Language" />
          <el-table-column prop="templateBody" label="Body" min-width="260" show-overflow-tooltip />
          <el-table-column prop="isEnabled" label="Enabled" />
          <el-table-column label="Action">
            <template #default="{ row }">
              <el-button text v-permission="'wms-mobile:label-manage'" @click="upsertLabelTemplate(row)">Edit</el-button>
            </template>
          </el-table-column>
        </el-table>
      </el-tab-pane>
    </el-tabs>

    <el-dialog v-model="activationVisible" title="Device activation" width="560px" append-to-body>
      <el-form :model="activationForm" label-width="140px">
        <el-form-item label="Server URL"><el-input v-model="activationForm.server" /></el-form-item>
        <el-form-item label="Tenant"><el-input v-model="activationForm.tenant" /></el-form-item>
        <el-form-item label="Platform">
          <el-select v-model="activationForm.platform"><el-option label="Android" value="Android" /><el-option label="Windows" value="Windows" /></el-select>
        </el-form-item>
        <el-form-item label="Mode">
          <el-select v-model="activationForm.deviceMode"><el-option label="Shared" value="Shared" /><el-option label="Personal" value="Personal" /></el-select>
        </el-form-item>
        <el-form-item label="Warehouse"><el-input v-model="activationForm.warehouseCd" /></el-form-item>
        <el-form-item label="Area"><el-input v-model="activationForm.areaCd" /></el-form-item>
        <template v-if="activationForm.platform === 'Android'">
          <el-divider content-position="left">Scanner provisioning</el-divider>
          <el-form-item label="HID prefix">
            <el-input v-model="activationForm.scanPrefix" maxlength="32" placeholder="Optional, e.g. ]C1" />
          </el-form-item>
          <el-form-item label="HID suffix">
            <el-input v-model="activationForm.scanSuffix" maxlength="32" placeholder="Optional framing suffix" />
          </el-form-item>
          <el-form-item label="HID terminator">
            <el-select v-model="activationForm.scanTerminator">
              <el-option label="Enter / CR" value="Enter" />
              <el-option label="Tab" value="Tab" />
              <el-option label="Manual submit" value="None" />
            </el-select>
          </el-form-item>
          <el-form-item label="Duplicate window">
            <el-input-number
              v-model="activationForm.scanDuplicateMs"
              :min="100"
              :max="5000"
              :step="50"
              controls-position="right"
            />
            <span class="field-suffix">ms</span>
          </el-form-item>
        </template>
      </el-form>
      <div v-if="activationQr" class="activation-qr">
        <img :src="activationQr" alt="Device activation QR" />
        <code>{{ activationPayload }}</code>
      </div>
      <template #footer><el-button type="primary" @click="createActivation">Generate one-time QR</el-button></template>
    </el-dialog>
    <el-dialog v-model="featureChangeVisible" title="Request production feature change" width="620px">
      <el-form label-width="180px">
        <el-form-item label="Warehouse">
          <el-input v-model="featureChangeForm.warehouseCd" :disabled="Boolean(featureChangeBase)" />
        </el-form-item>
        <el-form-item label="Production MOVE">
          <el-switch v-model="featureChangeForm.productionMoveEnabled" />
        </el-form-item>
        <el-form-item label="Serial / LPN">
          <el-switch v-model="featureChangeForm.serialLpnEnabled" />
        </el-form-item>
        <el-form-item label="Scan retention (days)">
          <el-input-number v-model="featureChangeForm.scanRetentionDays" :min="30" :max="3650" />
        </el-form-item>
        <el-form-item label="Reason">
          <el-input v-model="featureChangeForm.reason" type="textarea" :rows="3" />
        </el-form-item>
        <el-form-item label="External change ticket">
          <el-input v-model="featureChangeForm.changeTicket" placeholder="CHG-..." />
        </el-form-item>
        <el-form-item label="R2A evidence URI">
          <el-input v-model="featureChangeForm.evidenceUri" placeholder="s3://... or https://..." />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="featureChangeVisible = false">Close</el-button>
        <el-button type="primary" @click="submitFeatureChange">Submit for OA approval</el-button>
      </template>
    </el-dialog>

    <WmsSerialLpnDialogs
      ref="serialLpnDialogs"
      @serials-changed="loadSerials"
      @lpns-changed="loadLpns"
    />
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox, type UploadFile } from 'element-plus'
import QRCode from 'qrcode'
import { productionApi } from '@/api/wms/production'
import { buildDeviceActivationPayload, type ScannerHidTerminator } from '@/utils/deviceActivation'
import WmsSerialLpnDialogs from './WmsSerialLpnDialogs.vue'
import type {
  BarcodeAlias, BarcodeImportResult, BarcodeProfile, ClientDevice, LabelJob,
  LabelTemplate, LogisticsUnit, StockSerial, TaskAnalytics, WmsFeatureFlag,
  WmsFeatureFlagChange, WmsFeatureFlagChangeStatus, WmsRoleScope,
} from '@/api/wms/production'

const tab = ref('rollout')
const analytics = ref<TaskAnalytics>()
const featureFlags = ref<WmsFeatureFlag[]>([])
const featureChanges = ref<WmsFeatureFlagChange[]>([])
const featureChangeVisible = ref(false)
const featureChangeBase = ref<WmsFeatureFlag>()
const featureChangeForm = reactive({
  warehouseCd: '',
  productionMoveEnabled: false,
  serialLpnEnabled: false,
  scanRetentionDays: 180,
  reason: '',
  changeTicket: '',
  evidenceUri: '',
})
const scopeRoleId = ref(20)
const roleScopes = ref<WmsRoleScope[]>([])
const devices = ref<ClientDevice[]>([])
const barcodes = ref<BarcodeAlias[]>([])
const barcodeProfiles = ref<BarcodeProfile[]>([])
const serials = ref<StockSerial[]>([])
const lpns = ref<LogisticsUnit[]>([])
const labelJobs = ref<LabelJob[]>([])
const labelTemplates = ref<LabelTemplate[]>([])
const barcodeImport = ref<BarcodeImportResult>()
const pendingBarcodeFile = ref<File>()
const activationVisible = ref(false)
const activationPayload = ref('')
const activationQr = ref('')
const serialLpnDialogs = ref<InstanceType<typeof WmsSerialLpnDialogs>>()
const activationForm = reactive({
  server: window.location.origin, tenant: '', platform: 'Android' as 'Android' | 'Windows',
  deviceMode: 'Shared' as 'Shared' | 'Personal', warehouseCd: '', areaCd: '',
  scanPrefix: '', scanSuffix: '',
  scanTerminator: 'Enter' as ScannerHidTerminator, scanDuplicateMs: 750,
})
const metrics = computed(() => [
  { label: 'Created', value: analytics.value?.created ?? 0 },
  { label: 'Completed', value: analytics.value?.completed ?? 0 },
  { label: 'Partial', value: analytics.value?.partiallyCompleted ?? 0 },
  { label: 'Exceptions', value: analytics.value?.exceptions ?? 0 },
  { label: 'Overdue', value: analytics.value?.overdue ?? 0 },
  { label: 'Avg minutes', value: analytics.value?.averageMinutes ?? 0 },
])

async function loadTab(name: string | number) {
  if (name === 'rollout') await loadFeatureFlags()
  if (name === 'scopes') await loadRoleScopes()
  if (name === 'analytics') analytics.value = await productionApi.analytics()
  if (name === 'devices') await loadDevices()
  if (name === 'barcodes') await loadBarcodes()
  if (name === 'serials') await loadSerials()
  if (name === 'lpns') await loadLpns()
  if (name === 'labels') await loadLabels()
}
async function loadDevices() { devices.value = (await productionApi.devices({ pageSize: 100 })).items }
async function loadFeatureFlags() {
  [featureFlags.value, featureChanges.value] = await Promise.all([
    productionApi.featureFlags(),
    productionApi.featureChanges(),
  ])
}
async function loadRoleScopes() {
  roleScopes.value = await productionApi.roleScopes(scopeRoleId.value)
}
function addRoleScope() {
  roleScopes.value.push({
    roleId: scopeRoleId.value,
    warehouseCd: '',
    areaCd: '',
  })
}
function removeRoleScope(index: number) {
  roleScopes.value.splice(index, 1)
}
async function saveRoleScopes() {
  roleScopes.value = await productionApi.replaceRoleScopes(
    scopeRoleId.value,
    roleScopes.value.map(scope => ({
      warehouseCd: scope.warehouseCd.trim(),
      areaCd: scope.areaCd?.trim() || undefined,
    })),
  )
  ElMessage.success('Role task scope saved')
}
function hasPendingFeatureChange(warehouseCd: string) {
  return featureChanges.value.some(change =>
    change.warehouseCd === warehouseCd && change.status === 'PENDING')
}
function featureStatusType(status: WmsFeatureFlagChangeStatus) {
  if (status === 'APPLIED') return 'success'
  if (status === 'PENDING') return 'warning'
  if (status === 'REJECTED' || status === 'FAILED') return 'danger'
  return 'info'
}
function openFeatureChange(flag?: WmsFeatureFlag) {
  featureChangeBase.value = flag
  Object.assign(featureChangeForm, {
    warehouseCd: flag?.warehouseCd ?? '',
    productionMoveEnabled: flag?.productionMoveEnabled ?? false,
    serialLpnEnabled: flag?.serialLpnEnabled ?? false,
    scanRetentionDays: flag?.scanRetentionDays ?? 180,
    reason: '',
    changeTicket: '',
    evidenceUri: '',
  })
  featureChangeVisible.value = true
}
async function submitFeatureChange() {
  if (!featureChangeForm.warehouseCd.trim()) {
    ElMessage.error('Warehouse is required')
    return
  }
  if (!featureChangeForm.reason.trim()) {
    ElMessage.error('Reason is required')
    return
  }
  if (!featureChangeForm.changeTicket.trim()) {
    ElMessage.error('External change ticket is required')
    return
  }
  if (featureChangeForm.serialLpnEnabled && !featureChangeForm.productionMoveEnabled) {
    ElMessage.error('Serial / LPN requires Production MOVE')
    return
  }
  if (!featureChangeBase.value?.serialLpnEnabled
      && featureChangeForm.serialLpnEnabled
      && !featureChangeForm.evidenceUri.trim()) {
    ElMessage.error('R2A exit evidence URI is required before enabling Serial / LPN')
    return
  }
  await productionApi.requestFeatureChange({
    warehouseCd: featureChangeForm.warehouseCd.trim(),
    productionMoveEnabled: featureChangeForm.productionMoveEnabled,
    serialLpnEnabled: featureChangeForm.serialLpnEnabled,
    scanRetentionDays: featureChangeForm.scanRetentionDays,
    rowVersion: featureChangeBase.value?.rowVersion ?? '',
    reason: featureChangeForm.reason.trim(),
    changeTicket: featureChangeForm.changeTicket.trim(),
    evidenceUri: featureChangeForm.evidenceUri.trim() || undefined,
  })
  featureChangeVisible.value = false
  ElMessage.success('Feature change submitted to OA approval')
  await loadFeatureFlags()
}
async function cancelFeatureChange(change: WmsFeatureFlagChange) {
  await productionApi.cancelFeatureChange(change.id)
  ElMessage.success('Feature change cancelled')
  await loadFeatureFlags()
}
async function loadBarcodes() {
  [barcodes.value, barcodeProfiles.value] = await Promise.all([
    productionApi.barcodes({ pageSize: 100 }).then(result => result.items),
    productionApi.barcodeProfiles(),
  ])
}
async function loadSerials() { serials.value = (await productionApi.serials({ pageSize: 100 })).items }
async function loadLpns() { lpns.value = (await productionApi.lpns({ pageSize: 100 })).items }
async function loadLabels() {
  [labelJobs.value, labelTemplates.value] = await Promise.all([
    productionApi.labelJobs({ pageSize: 100 }).then(result => result.items),
    productionApi.labelTemplates(),
  ])
}
async function toggleDevice(device: ClientDevice) {
  await productionApi.updateDevice(device.deviceId, {
    rowVersion: device.rowVersion,
    status: device.status === 'Active' ? 'Disabled' : 'Active',
    deviceMode: device.deviceMode,
    warehouseCd: device.warehouseCd,
    areaCd: device.areaCd,
  })
  await loadDevices()
}
async function createActivation() {
  const ticket = await productionApi.createActivation({
    platform: activationForm.platform, deviceMode: activationForm.deviceMode,
    warehouseCd: activationForm.warehouseCd || undefined, areaCd: activationForm.areaCd || undefined,
  })
  activationPayload.value = buildDeviceActivationPayload({
    server: activationForm.server,
    tenant: activationForm.tenant,
    token: ticket.activationToken,
    platform: activationForm.platform,
    scanPrefix: activationForm.scanPrefix,
    scanSuffix: activationForm.scanSuffix,
    scanTerminator: activationForm.scanTerminator,
    scanDuplicateMs: activationForm.scanDuplicateMs,
  })
  activationQr.value = await QRCode.toDataURL(activationPayload.value, { width: 300, errorCorrectionLevel: 'M' })
}
async function previewBarcodeFile(file: File) {
  pendingBarcodeFile.value = file
  barcodeImport.value = await productionApi.importBarcodes(file, false)
}
function handleBarcodeFile(file: UploadFile) {
  if (file.raw) return previewBarcodeFile(file.raw)
}
async function commitBarcodeFile() {
  if (!pendingBarcodeFile.value) return
  barcodeImport.value = await productionApi.importBarcodes(pendingBarcodeFile.value, true)
  ElMessage.success('Valid barcode rows committed')
  await loadBarcodes()
}
async function promptJson(
  title: string,
  value: Record<string, unknown>,
): Promise<Record<string, unknown>> {
  const result = await ElMessageBox.prompt('Review and submit the command payload', title, {
    inputType: 'textarea',
    inputValue: JSON.stringify(value, null, 2),
    inputValidator: input => {
      try {
        const parsed = JSON.parse(input)
        return (parsed && typeof parsed === 'object' && !Array.isArray(parsed))
          || 'A JSON object is required'
      } catch {
        return 'Valid JSON is required'
      }
    },
  })
  return JSON.parse(result.value) as Record<string, unknown>
}
async function upsertBarcode() {
  const request = await promptJson('Add or correct barcode mapping', {
    barcode: '', barcodeType: 'Product', targetKey: '', productCd: '',
    lotNo: '', locationCd: '', packageUnitCd: '', conversionRate: 1,
    isEnabled: true,
  })
  await productionApi.upsertBarcode(request as Partial<BarcodeAlias> & Pick<BarcodeAlias, 'barcode' | 'barcodeType' | 'targetKey'>)
  await loadBarcodes()
}
async function upsertBarcodeProfile() {
  const request = await promptJson('Add GS1 or custom parser profile', {
    profileName: '', format: 'CUSTOM', pattern: '',
    mappingJson: '{"product":"productCd","serial":"serialNo"}',
    priority: 100, isEnabled: true,
  })
  await productionApi.upsertBarcodeProfile(request)
  await loadBarcodes()
}
async function testBarcodeParser() {
  const result = await ElMessageBox.prompt('Raw compound barcode', 'Test barcode parser', {
    inputValidator: value => Boolean(value?.trim()) || 'Barcode is required',
  })
  const parsed = await productionApi.parseBarcode(result.value.trim())
  await ElMessageBox.alert(JSON.stringify(parsed, null, 2), 'Parser result')
}
function postSerialLifecycle() {
  serialLpnDialogs.value?.openSerialLifecycle()
}
function enableSerialTracking() {
  serialLpnDialogs.value?.openSerialTracking()
}
function createLpn() {
  serialLpnDialogs.value?.openLpn('create')
}
function moveLpn(lpn: LogisticsUnit) {
  serialLpnDialogs.value?.openLpn('move', lpn)
}
function packLpn(lpn: LogisticsUnit) {
  serialLpnDialogs.value?.openLpn('pack', lpn)
}
function unpackLpn(lpn: LogisticsUnit) {
  serialLpnDialogs.value?.openLpn('unpack', lpn)
}
function splitLpn(lpn: LogisticsUnit) {
  serialLpnDialogs.value?.openLpn('split', lpn)
}
function mergeLpn(lpn: LogisticsUnit) {
  serialLpnDialogs.value?.openLpn('merge', lpn)
}
async function createLabelJob() {
  const request = await promptJson('Create idempotent print job', {
    warehouseCd: '', templateName: '', payloadJson: '{}',
    printerName: null, deviceId: null,
  })
  await productionApi.createLabelJob(request)
  await loadLabels()
}
async function upsertLabelTemplate(template?: LabelTemplate) {
  const request = await promptJson(template ? `Edit ${template.templateName}` : 'Add label template', {
    id: template?.id, templateName: template?.templateName ?? '',
    format: template?.format ?? 'ZPL', templateBody: template?.templateBody ?? '',
    language: template?.language ?? null, isEnabled: template?.isEnabled ?? true,
    rowVersion: template?.rowVersion,
  })
  await productionApi.upsertLabelTemplate(request)
  ElMessage.success('Label template saved')
  await loadLabels()
}
onMounted(() => loadTab(tab.value))
</script>

<style scoped>
.tab-toolbar { display: flex; gap: 8px; margin-bottom: 12px; }
.rollout-alert { margin-bottom: 12px; }
.failure-code { margin-top: 4px; color: var(--el-color-danger); font-size: 12px; }
.scope-table { margin-top: 12px; }
.field-suffix { margin-left: 8px; color: var(--el-text-color-secondary); }
.activation-qr { display: grid; justify-items: center; gap: 12px; }
.activation-qr img { width: 300px; height: 300px; }
.activation-qr code { max-width: 100%; overflow-wrap: anywhere; }
</style>

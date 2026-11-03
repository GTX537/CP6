<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import CpPageShell from '@/components/templates/CpPageShell.vue'
import FlowTriggerPanel from './FlowTriggerPanel.vue'
import FlowActivationPanelV2 from './FlowActivationPanelV2.vue'
import FormActivationPanelV2 from './FormActivationPanelV2.vue'
import ApprovalBindingPanelV2 from './ApprovalBindingPanelV2.vue'
import WfConnectorPanel from './WfConnectorPanel.vue'
import BatchTransferDialog from './BatchTransferDialog.vue'
import LegacyDefinitionsViewV2 from '../designer/LegacyDefinitionsViewV2.vue'
const { t } = useI18n()
const activeTab = ref('activation'), batchTransferVisible = ref(false)
</script>
<template>
  <CpPageShell :title="t('oa.flowadmin.title')">
    <template #actions><el-button v-permission="'oa-inbox:batch-transfer'" type="warning" plain @click="batchTransferVisible = true">{{ t('oa.bt.entry') }}</el-button></template>
    <el-tabs v-model="activeTab">
      <el-tab-pane label="准确版本启停" name="activation" lazy><FlowActivationPanelV2 /></el-tab-pane>
      <el-tab-pane label="审批业务绑定" name="binding" lazy><ApprovalBindingPanelV2 /></el-tab-pane>
      <el-tab-pane label="表单版本启停" name="form-activation" lazy><FormActivationPanelV2 /></el-tab-pane>
      <el-tab-pane label="历史来源（只读）" name="legacy" lazy><LegacyDefinitionsViewV2 kind="FLOW" @close="activeTab = 'activation'" /></el-tab-pane>
      <el-tab-pane :label="t('oa.flowtrigger.tab')" name="triggers" lazy><FlowTriggerPanel /></el-tab-pane>
      <el-tab-pane :label="t('oa.connector.tab')" name="connectors" lazy><WfConnectorPanel /></el-tab-pane>
    </el-tabs>
    <BatchTransferDialog v-model="batchTransferVisible" />
  </CpPageShell>
</template>

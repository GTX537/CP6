<template>
  <el-card shadow="never">
    <el-form
      ref="formRef"
      :model="form"
      :rules="rules"
      label-width="110px"
      label-position="right"
      :disabled="isPageReadOnly"
      size="small"
    >
      <!-- ============ 区块 1：基本信息 ============ -->
      <el-divider content-position="left">{{ t('基本信息') }}</el-divider>
      <el-row :gutter="12">
        <el-col :span="6">
          <el-form-item :label="t('見積計算書No')">
            <el-input v-model="form.qtnCalcNo" disabled :placeholder="t('保存时系统采番')" />
          </el-form-item>
        </el-col>
        <el-col :span="6">
          <el-form-item :label="t('商品コード')" prop="proCd" :required="isRequired('proCd')">
            <el-input v-model="form.proCd" :disabled="isDisabled('proCd')" />
          </el-form-item>
        </el-col>
        <el-col :span="6">
          <el-form-item :label="t('見積日')" prop="qtnDate" :required="isRequired('qtnDate')">
            <el-date-picker
              v-model="form.qtnDate"
              type="date"
              value-format="YYYY-MM-DD"
              style="width: 100%"
              :disabled="isDisabled('qtnDate')"
            />
          </el-form-item>
        </el-col>
        <el-col :span="6">
          <el-form-item :label="t('参照元No')">
            <el-input v-model="form.refQtnCalcNo" :disabled="isDisabled('refQtnCalcNo')" />
          </el-form-item>
        </el-col>

        <el-col :span="6">
          <el-form-item :label="t('見積拠点')" prop="qtnBaseCd" :required="isRequired('qtnBaseCd')">
            <el-select v-model="form.qtnBaseCd" :disabled="isDisabled('qtnBaseCd')" clearable>
              <el-option v-for="b in bases" :key="b.baseCd" :value="b.baseCd" :label="`${b.baseCd} ${b.baseName}`" />
            </el-select>
          </el-form-item>
        </el-col>
        <el-col :span="6">
          <el-form-item :label="t('受注拠点')" prop="orderBaseCd" :required="isRequired('orderBaseCd')">
            <el-select v-model="form.orderBaseCd" :disabled="isDisabled('orderBaseCd')" clearable>
              <el-option v-for="b in bases" :key="b.baseCd" :value="b.baseCd" :label="`${b.baseCd} ${b.baseName}`" />
            </el-select>
          </el-form-item>
        </el-col>
        <el-col :span="6">
          <el-form-item :label="t('担当者')" prop="staffCd" :required="isRequired('staffCd')">
            <el-select v-model="form.staffCd" :disabled="isDisabled('staffCd') || !form.orderBaseCd" clearable>
              <el-option v-for="s in staffs" :key="s.staffCd" :value="s.staffCd" :label="`${s.staffCd} ${s.staffName}`" />
            </el-select>
          </el-form-item>
        </el-col>
        <el-col :span="6">
          <el-form-item :label="t('受注形態')" prop="orderType" :required="isRequired('orderType')">
            <el-select v-model="form.orderType" :disabled="isDisabled('orderType')" clearable>
              <el-option v-for="o in codes.orderType" :key="o.code" :value="o.code" :label="o.name" />
            </el-select>
          </el-form-item>
        </el-col>
      </el-row>

      <el-row :gutter="12">
        <el-col :span="6">
          <el-form-item :label="t('顧客コード')" prop="customerCd" :required="isRequired('customerCd')">
            <el-input v-model="form.customerCd" :disabled="isDisabled('customerCd')" />
          </el-form-item>
        </el-col>
        <el-col :span="6">
          <el-form-item :label="t('親案件No')">
            <el-input v-model="form.projectNoParent" :disabled="isDisabled('projectNoParent')" />
          </el-form-item>
        </el-col>
        <el-col :span="6">
          <el-form-item :label="t('子案件No')">
            <el-input v-model="form.projectNoChild" :disabled="isDisabled('projectNoChild')" />
          </el-form-item>
        </el-col>
        <el-col :span="6">
          <el-form-item :label="t('材質')">
            <el-input v-model="form.projectNoMaterial" :disabled="isDisabled('projectNoMaterial')" />
          </el-form-item>
        </el-col>
      </el-row>

      <!-- ============ 区块 2：商品分类 & 顧客品名 ============ -->
      <el-divider content-position="left">{{ t('商品分類・顧客品名') }}</el-divider>
      <el-row :gutter="12">
        <el-col :span="6">
          <el-form-item :label="t('大分類')" prop="productCategoryBig" :required="isRequired('productCategoryBig')">
            <el-select v-model="form.productCategoryBig" :disabled="isDisabled('productCategoryBig')" clearable>
              <el-option v-for="o in codes.categoryBig" :key="o.code" :value="o.code" :label="o.name" />
            </el-select>
          </el-form-item>
        </el-col>
        <el-col :span="6">
          <el-form-item :label="t('中分類')" prop="productCategoryMid" :required="isRequired('productCategoryMid')">
            <el-select
              v-model="form.productCategoryMid"
              :disabled="isDisabled('productCategoryMid') || !form.productCategoryBig"
              :placeholder="!form.productCategoryBig ? t('先に大分類を選択') : ''"
              clearable
            >
              <el-option v-for="o in filteredCategoryMid" :key="o.code" :value="o.code" :label="o.name" />
            </el-select>
          </el-form-item>
        </el-col>
        <el-col :span="6">
          <el-form-item :label="t('小分類')">
            <el-select
              v-model="form.productCategorySml"
              :disabled="isDisabled('productCategorySml') || !form.productCategoryMid"
              :placeholder="!form.productCategoryMid ? t('先に中分類を選択') : ''"
              clearable
            >
              <el-option v-for="o in filteredCategorySml" :key="o.code" :value="o.code" :label="o.name" />
            </el-select>
          </el-form-item>
        </el-col>
        <el-col :span="6">
          <el-form-item :label="t('顧客品名1')" prop="customerProductName1" :required="isRequired('customerProductName1')">
            <el-input v-model="form.customerProductName1" :disabled="isDisabled('customerProductName1')" />
          </el-form-item>
        </el-col>
        <el-col :span="12">
          <el-form-item :label="t('顧客品名2')">
            <el-input v-model="form.customerProductName2" :disabled="isDisabled('customerProductName2')" />
          </el-form-item>
        </el-col>
      </el-row>

      <!-- ============ 区块 3：受注情報 ============ -->
      <el-divider content-position="left">{{ t('受注情報') }}</el-divider>
      <el-row :gutter="12">
        <el-col :span="6">
          <el-form-item :label="t('受注数量')" prop="orderQty" :required="isRequired('orderQty')">
            <el-input-number v-model="form.orderQty" :min="0" :controls="false" style="width: 100%" :disabled="isDisabled('orderQty')" />
          </el-form-item>
        </el-col>
        <el-col :span="6">
          <el-form-item :label="t('受注年月')">
            <el-input v-model="form.orderYm" placeholder="YYYYMM" :disabled="isDisabled('orderYm')" />
          </el-form-item>
        </el-col>
        <el-col :span="6">
          <el-form-item :label="t('親子区分')" prop="parentChildDiv" :required="isRequired('parentChildDiv')">
            <el-select v-model="form.parentChildDiv" :disabled="isDisabled('parentChildDiv')" clearable>
              <el-option v-for="o in codes.parentChild" :key="o.code" :value="o.code" :label="o.name" />
            </el-select>
          </el-form-item>
        </el-col>
        <el-col :span="6">
          <el-form-item :label="t('FSC商品')">
            <el-select v-model="form.fscProductDiv" :disabled="isDisabled('fscProductDiv')" clearable>
              <el-option v-for="o in codes.fscDiv" :key="o.code" :value="o.code" :label="o.name" />
            </el-select>
          </el-form-item>
        </el-col>
        <el-col :span="6">
          <el-form-item :label="t('FSC原紙')">
            <el-select v-model="form.fscMaterialDiv" :disabled="isDisabled('fscMaterialDiv')" clearable>
              <el-option v-for="o in codes.fscDiv" :key="o.code" :value="o.code" :label="o.name" />
            </el-select>
          </el-form-item>
        </el-col>
      </el-row>

      <!-- ============ 区块 4：材質・印刷・成型 ============ -->
      <el-divider content-position="left">{{ t('材質・印刷・成型') }}</el-divider>
      <el-row :gutter="12">
        <el-col :span="6">
          <el-form-item :label="t('シート・フルート')" prop="sheetFlute" :required="isRequired('sheetFlute')">
            <el-select v-model="form.sheetFlute" :disabled="isDisabled('sheetFlute')" clearable>
              <el-option v-for="o in codes.sheetFlute" :key="o.code" :value="o.code" :label="o.name" />
            </el-select>
          </el-form-item>
        </el-col>
        <el-col :span="6">
          <el-form-item :label="t('原紙(F)')" prop="paperCdF" :required="isRequired('paperCdF')">
            <el-select v-model="form.paperCdF" :disabled="isDisabled('paperCdF')" clearable>
              <el-option v-for="o in codes.paper" :key="o.code" :value="o.code" :label="o.name" />
            </el-select>
          </el-form-item>
        </el-col>
        <el-col :span="6">
          <el-form-item :label="t('原紙(C)')">
            <el-select v-model="form.paperCdC" :disabled="isDisabled('paperCdC')" clearable>
              <el-option v-for="o in codes.paper" :key="o.code" :value="o.code" :label="o.name" />
            </el-select>
          </el-form-item>
        </el-col>
        <el-col :span="6">
          <el-form-item :label="t('原紙(B)')">
            <el-select v-model="form.paperCdB" :disabled="isDisabled('paperCdB')" clearable>
              <el-option v-for="o in codes.paper" :key="o.code" :value="o.code" :label="o.name" />
            </el-select>
          </el-form-item>
        </el-col>

        <el-col :span="6">
          <el-form-item :label="t('印刷(F)')" prop="printCdF" :required="isRequired('printCdF')">
            <el-select v-model="form.printCdF" :disabled="isDisabled('printCdF')" clearable>
              <el-option v-for="o in codes.print" :key="o.code" :value="o.code" :label="o.name" />
            </el-select>
          </el-form-item>
        </el-col>
        <el-col :span="6">
          <el-form-item :label="t('印刷(C)')">
            <el-select v-model="form.printCdC" :disabled="isDisabled('printCdC')" clearable>
              <el-option v-for="o in codes.print" :key="o.code" :value="o.code" :label="o.name" />
            </el-select>
          </el-form-item>
        </el-col>
        <el-col :span="6">
          <el-form-item :label="t('印刷(B)')">
            <el-select v-model="form.printCdB" :disabled="isDisabled('printCdB')" clearable>
              <el-option v-for="o in codes.print" :key="o.code" :value="o.code" :label="o.name" />
            </el-select>
          </el-form-item>
        </el-col>
        <el-col :span="6">
          <el-form-item :label="t('シート印刷')">
            <el-input v-model="form.sheetPrint" :disabled="isDisabled('sheetPrint')" />
          </el-form-item>
        </el-col>

        <el-col :span="6">
          <el-form-item :label="t('エンボス(F)')">
            <el-input v-model="form.embossCdF" :disabled="isDisabled('embossCdF')" />
          </el-form-item>
        </el-col>
        <el-col :span="6">
          <el-form-item :label="t('エンボス(C)')">
            <el-input v-model="form.embossCdC" :disabled="isDisabled('embossCdC')" />
          </el-form-item>
        </el-col>
        <el-col :span="6">
          <el-form-item :label="t('エンボス(B)')">
            <el-input v-model="form.embossCdB" :disabled="isDisabled('embossCdB')" />
          </el-form-item>
        </el-col>
        <el-col :span="6">
          <el-form-item :label="t('型数(F/B)')">
            <el-input-number v-model="form.patternCntF" :min="0" :controls="false" style="width: 45%" :disabled="isDisabled('patternCntF')" />
            <span style="margin: 0 4px">/</span>
            <el-input-number v-model="form.patternCntB" :min="0" :controls="false" style="width: 45%" :disabled="isDisabled('patternCntB')" />
          </el-form-item>
        </el-col>
      </el-row>

      <!-- ============ 区块 5：刃・シート寸法 ============ -->
      <el-divider content-position="left">{{ t('刃・シート寸法') }}</el-divider>
      <el-row :gutter="12">
        <el-col :span="6">
          <el-form-item :label="t('刃渡り(mm)')">
            <el-input-number
              v-model="form.bladeWidth"
              :min="0"
              :precision="1"
              :controls="false"
              style="width: 100%"
              :disabled="isDisabled('bladeWidth')"
              @blur="onBladeBlur"
            />
          </el-form-item>
        </el-col>
        <el-col :span="6">
          <el-form-item :label="t('流れ(mm)')">
            <el-input-number
              v-model="form.bladeFlow"
              :min="0"
              :precision="1"
              :controls="false"
              style="width: 100%"
              :disabled="isDisabled('bladeFlow')"
              @blur="onBladeBlur"
            />
          </el-form-item>
        </el-col>
        <el-col :span="6">
          <el-form-item :label="t('のりしろFB')">
            <el-input-number v-model="form.gutterFb" :min="0" :precision="1" :controls="false" style="width: 100%" :disabled="isDisabled('gutterFb')" />
          </el-form-item>
        </el-col>
        <el-col :span="6">
          <el-form-item :label="t('のりしろLR')">
            <el-input-number v-model="form.gutterLr" :min="0" :precision="1" :controls="false" style="width: 100%" :disabled="isDisabled('gutterLr')" />
          </el-form-item>
        </el-col>

        <el-col :span="6">
          <el-form-item :label="t('シート寸法W')">
            <el-input-number v-model="form.sheetDimW" :min="0" :precision="1" :controls="false" style="width: 100%" :disabled="isDisabled('sheetDimW')" />
          </el-form-item>
        </el-col>
        <el-col :span="6">
          <el-form-item :label="t('シート寸法F')">
            <el-input-number v-model="form.sheetDimF" :min="0" :precision="1" :controls="false" style="width: 100%" :disabled="isDisabled('sheetDimF')" />
          </el-form-item>
        </el-col>
      </el-row>

      <!-- ============ 区块 6：最終工程・形状 ============ -->
      <el-divider content-position="left">{{ t('最終工程・形状') }}</el-divider>
      <el-row :gutter="12">
        <el-col :span="6">
          <el-form-item :label="t('最終工程')" prop="finalMachineProc" :required="isRequired('finalMachineProc')">
            <el-select v-model="form.finalMachineProc" :disabled="isDisabled('finalMachineProc')" clearable>
              <el-option v-for="o in codes.process" :key="o.code" :value="o.code" :label="o.name" />
            </el-select>
          </el-form-item>
        </el-col>
        <el-col :span="6">
          <el-form-item :label="t('形状1')" prop="productShape1" :required="isRequired('productShape1')">
            <el-select v-model="form.productShape1" :disabled="isDisabled('productShape1')" clearable>
              <el-option v-for="o in codes.shape1" :key="o.code" :value="o.code" :label="o.name" />
            </el-select>
          </el-form-item>
        </el-col>
        <el-col :span="6">
          <el-form-item :label="t('形状2')">
            <el-select v-model="form.productShape2" :disabled="isDisabled('productShape2')" clearable>
              <el-option v-for="o in codes.shape2" :key="o.code" :value="o.code" :label="o.name" />
            </el-select>
          </el-form-item>
        </el-col>
        <el-col :span="6">
          <el-form-item :label="t('流通区分')" prop="distDiv" :required="isRequired('distDiv')">
            <el-select v-model="form.distDiv" :disabled="isDisabled('distDiv')" clearable>
              <el-option v-for="o in codes.distDiv" :key="o.code" :value="o.code" :label="o.name" />
            </el-select>
          </el-form-item>
        </el-col>

        <el-col :span="6">
          <el-form-item :label="t('再利用払込')">
            <el-input v-model="form.recyclePayment" :disabled="isDisabled('recyclePayment')" />
          </el-form-item>
        </el-col>
        <el-col :span="6">
          <el-form-item :label="t('ID マーク')">
            <el-input v-model="form.idMark" :disabled="isDisabled('idMark')" />
          </el-form-item>
        </el-col>
        <el-col :span="6">
          <el-form-item :label="t('AD 形状')">
            <el-input v-model="form.adShape" :disabled="isDisabled('adShape')" />
          </el-form-item>
        </el-col>
      </el-row>

      <!-- ============ 区块 7：戦略区分（1..10 bool） ============ -->
      <el-divider content-position="left">{{ t('戦略区分') }}</el-divider>
      <el-row :gutter="12">
        <el-col v-for="i in 10" :key="i" :span="4">
          <el-checkbox v-model="form.strategicDivs![i - 1]" :disabled="isDisabled('strategicDivs')">
            {{ t('戦略 {n}', { n: String(i).padStart(2, '0') }) }}
          </el-checkbox>
        </el-col>
      </el-row>

      <!-- ============ 区块 8：見積り数量・パレット（8 段） ============ -->
      <el-divider content-position="left">{{ t('見積り数量・パレット') }}</el-divider>
      <el-row :gutter="12">
        <el-col v-for="i in 8" :key="i" :span="6">
          <el-form-item :label="t('数量{n}', { n: i })">
            <el-input-number
              v-model="form.estimateQtys![i - 1]"
              :min="0"
              :controls="false"
              style="width: 100%"
              :disabled="isDisabled('estimateQtys')"
              @blur="onEstimateQtyBlur(i - 1)"
            />
          </el-form-item>
        </el-col>
        <el-col v-for="i in 8" :key="`p${i}`" :span="6">
          <el-form-item :label="t('ﾊﾟﾚｯﾄ{n}', { n: i })">
            <el-input-number
              v-model="form.palletCnts![i - 1]"
              :min="0"
              :controls="false"
              style="width: 100%"
              :disabled="isDisabled('palletCnts')"
            />
          </el-form-item>
        </el-col>
      </el-row>

      <!-- ============ 区块 9：提案ロット・単位・見積区分 ============ -->
      <el-divider content-position="left">{{ t('提案ロット・単位') }}</el-divider>
      <el-row :gutter="12">
        <el-col :span="6">
          <el-form-item :label="t('提案ロット1')">
            <el-input-number v-model="form.proposalLot1" :min="0" :controls="false" style="width: 100%" :disabled="isDisabled('proposalLot1')" />
          </el-form-item>
        </el-col>
        <el-col :span="6">
          <el-form-item :label="t('提案ロット2')">
            <el-input-number v-model="form.proposalLot2" :min="0" :controls="false" style="width: 100%" :disabled="isDisabled('proposalLot2')" />
          </el-form-item>
        </el-col>
        <el-col :span="6">
          <el-form-item :label="t('単位')" prop="unit" :required="isRequired('unit')">
            <el-select v-model="form.unit" :disabled="isDisabled('unit')" clearable>
              <el-option v-for="o in codes.unit" :key="o.code" :value="o.code" :label="o.name" />
            </el-select>
          </el-form-item>
        </el-col>
        <el-col :span="6">
          <el-form-item :label="t('決定予定')">
            <el-input-number v-model="form.decidedQty" :min="0" :controls="false" style="width: 100%" :disabled="isDisabled('decidedQty')" />
          </el-form-item>
        </el-col>
      </el-row>

      <!-- ============ 区块 10：備考 ============ -->
      <el-divider content-position="left">{{ t('備考') }}</el-divider>
      <el-row :gutter="12">
        <el-col :span="12">
          <el-form-item :label="t('印刷備考')">
            <el-input v-model="form.printNote" type="textarea" :rows="2" :disabled="isDisabled('printNote')" />
          </el-form-item>
        </el-col>
        <el-col :span="12">
          <el-form-item :label="t('製造備考')">
            <el-input v-model="form.mfgNote" type="textarea" :rows="2" :disabled="isDisabled('mfgNote')" />
          </el-form-item>
        </el-col>
        <el-col :span="12">
          <el-form-item :label="t('伝票備考')">
            <el-input v-model="form.slipNote" type="textarea" :rows="2" :disabled="isDisabled('slipNote')" />
          </el-form-item>
        </el-col>
        <el-col :span="12">
          <el-form-item :label="t('納入備考')">
            <el-input v-model="form.deliveryNote" type="textarea" :rows="2" :disabled="isDisabled('deliveryNote')" />
          </el-form-item>
        </el-col>
        <el-col :span="12">
          <el-form-item :label="t('出荷備考1')">
            <el-input v-model="form.shipNote1" :disabled="isDisabled('shipNote1')" />
          </el-form-item>
        </el-col>
        <el-col :span="12">
          <el-form-item :label="t('出荷備考2')">
            <el-input v-model="form.shipNote2" :disabled="isDisabled('shipNote2')" />
          </el-form-item>
        </el-col>
      </el-row>
    </el-form>
  </el-card>
</template>

<script setup lang="ts">
import { useI18n } from 'vue-i18n'
const { t } = useI18n()
import { ref, computed, onMounted, watch } from 'vue'
import type { FormInstance } from 'element-plus'
import { storeToRefs } from 'pinia'
import { useEstimateStore } from '@/stores/estimate'
import { useFieldControl } from '@/composables/useFieldControl'
import { useStep1Validation } from '@/composables/useValidation'
import { useStep1Linkage } from '@/composables/useLinkage'
import { masterApi } from '@/api/erp/master'
import type { MasterBase, MasterStaff, MasterGenericCode } from '@/types/erp/estimateCalc'

const store = useEstimateStore()
const { basicInfo } = storeToRefs(store)
const { isDisabled, isRequired, isPageReadOnly } = useFieldControl()
const { rules, validateBusiness } = useStep1Validation()

// form 直接绑 store.basicInfo，修改即同步
const form = basicInfo

const formRef = ref<FormInstance | null>(null)

// 主数据
const bases = ref<MasterBase[]>([])
const staffs = ref<MasterStaff[]>([])

const codes = ref({
  orderType: [] as MasterGenericCode[],
  parentChild: [] as MasterGenericCode[],
  fscDiv: [] as MasterGenericCode[],
  categoryBig: [] as MasterGenericCode[],
  categoryMid: [] as MasterGenericCode[],
  categorySml: [] as MasterGenericCode[],
  sheetFlute: [] as MasterGenericCode[],
  paper: [] as MasterGenericCode[],
  print: [] as MasterGenericCode[],
  process: [] as MasterGenericCode[],
  shape1: [] as MasterGenericCode[],
  shape2: [] as MasterGenericCode[],
  distDiv: [] as MasterGenericCode[],
  unit: [] as MasterGenericCode[],
})

// 联动
const { onBladeBlur, onEstimateQtyBlur } = useStep1Linkage(form, staffs)

// 製品区分 大→中→小 级联过滤（基于 Attr1 = 親 Code）
const filteredCategoryMid = computed(() => {
  const parent = form.value.productCategoryBig
  if (!parent) return codes.value.categoryMid
  return codes.value.categoryMid.filter((c) => c.attr1 === parent)
})

const filteredCategorySml = computed(() => {
  const parent = form.value.productCategoryMid
  if (!parent) return codes.value.categorySml
  return codes.value.categorySml.filter((c) => c.attr1 === parent)
})

// 大分類变化时清空中/小；中分類变化时清空小
watch(
  () => form.value.productCategoryBig,
  (cur, prev) => {
    if (prev === undefined) return
    if (cur === prev) return
    // 如果当前中分類不再属于新大分類，则清空
    const mid = form.value.productCategoryMid
    if (mid) {
      const stillValid = codes.value.categoryMid.some((c) => c.code === mid && c.attr1 === cur)
      if (!stillValid) {
        form.value.productCategoryMid = ''
        form.value.productCategorySml = ''
      }
    }
  },
)

watch(
  () => form.value.productCategoryMid,
  (cur, prev) => {
    if (prev === undefined) return
    if (cur === prev) return
    const sml = form.value.productCategorySml
    if (sml) {
      const stillValid = codes.value.categorySml.some((c) => c.code === sml && c.attr1 === cur)
      if (!stillValid) {
        form.value.productCategorySml = ''
      }
    }
  },
)

// 脏值监听
watch(
  form,
  () => {
    store.markDirty()
  },
  { deep: true }
)

// 对外暴露校验方法
async function validate(): Promise<boolean> {
  if (!formRef.value) return true
  try {
    await formRef.value.validate()
  } catch {
    return false
  }
  const errs = validateBusiness(form.value)
  if (errs.length) {
    const { ElMessage } = await import('element-plus')
    ElMessage.warning(errs[0])
    return false
  }
  return true
}

defineExpose({ validate })

// 初始化主数据
onMounted(async () => {
  try {
    const [baseRes, ...codeResults] = await Promise.all([
      masterApi.getBases(),
      masterApi.getGenericCodes('OrderType'),
      masterApi.getGenericCodes('ParentChildDiv'),
      masterApi.getGenericCodes('FscDiv'),
      masterApi.getGenericCodes('ProductCategoryBig'),
      masterApi.getGenericCodes('ProductCategoryMid'),
      masterApi.getGenericCodes('ProductCategorySml'),
      masterApi.getGenericCodes('SheetFlute'),
      masterApi.getGenericCodes('Paper'),
      masterApi.getGenericCodes('M014'),
      masterApi.getGenericCodes('M038'),
      masterApi.getGenericCodes('ProductShape1'),
      masterApi.getGenericCodes('ProductShape2'),
      masterApi.getGenericCodes('DistDiv'),
      masterApi.getGenericCodes('Unit'),
    ])
    bases.value = baseRes.data ?? []
    const keys: Array<keyof typeof codes.value> = [
      'orderType', 'parentChild', 'fscDiv', 'categoryBig', 'categoryMid', 'categorySml',
      'sheetFlute', 'paper', 'print', 'process', 'shape1', 'shape2', 'distDiv', 'unit',
    ]
    codeResults.forEach((r, i) => {
      const k = keys[i]
      if (k) codes.value[k] = r.data ?? []
    })

    // 若已经有 orderBaseCd（编辑/查看）则预加载担当者
    if (form.value.orderBaseCd) {
      const sRes = await masterApi.getStaffs(form.value.orderBaseCd)
      staffs.value = sRes.data ?? []
    }
  } catch (e) {
    console.error('主数据加载失败', e)
  }
})
</script>

<style scoped>
:deep(.el-divider__text) {
  font-weight: 600;
  color: var(--cp-brand);
}
:deep(.el-form-item) {
  margin-bottom: 12px;
}
</style>

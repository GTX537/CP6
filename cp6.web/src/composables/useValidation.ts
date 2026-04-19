import type { FormRules } from 'element-plus'
import type { EstimateCalcDto } from '@/types/estimateCalc'

/**
 * 基本信息（Step 1）校验规则
 * 对应 MSBBPA010 详细需求规格 MSG-111 ~ MSG-129（19 条）
 *
 * 约定：rules 的 key 必须与 EstimateCalcDto 字段名一致
 */
export function useStep1Validation() {
  const rules: FormRules = {
    // MSG-111 商品コード必入
    proCd: [{ required: true, message: 'MSG-111 商品コードを入力してください', trigger: 'blur' }],

    // MSG-112 見積日必入
    qtnDate: [{ required: true, message: 'MSG-112 見積日を指定してください', trigger: 'change' }],

    // MSG-113 見積拠点必入
    qtnBaseCd: [
      { required: true, message: 'MSG-113 見積拠点を選択してください', trigger: 'change' },
    ],

    // MSG-114 受注拠点必入
    orderBaseCd: [
      { required: true, message: 'MSG-114 受注拠点を選択してください', trigger: 'change' },
    ],

    // MSG-115 担当者必入
    staffCd: [{ required: true, message: 'MSG-115 担当者を選択してください', trigger: 'change' }],

    // MSG-116 顧客コード必入
    customerCd: [
      { required: true, message: 'MSG-116 顧客コードを入力してください', trigger: 'blur' },
    ],

    // MSG-117 受注形態必入
    orderType: [
      { required: true, message: 'MSG-117 受注形態を選択してください', trigger: 'change' },
    ],

    // MSG-118 商品大分類必入
    productCategoryBig: [
      { required: true, message: 'MSG-118 商品大分類を選択してください', trigger: 'change' },
    ],

    // MSG-119 商品中分類必入
    productCategoryMid: [
      { required: true, message: 'MSG-119 商品中分類を選択してください', trigger: 'change' },
    ],

    // MSG-120 顧客品名 1 必入
    customerProductName1: [
      { required: true, message: 'MSG-120 顧客品名1を入力してください', trigger: 'blur' },
    ],

    // MSG-121 受注数量必入、>0
    orderQty: [
      { required: true, message: 'MSG-121 受注数量を入力してください', trigger: 'blur' },
      {
        validator: (_r, v, cb) => (typeof v === 'number' && v > 0 ? cb() : cb(new Error('MSG-121 受注数量は 0 より大きくしてください'))),
        trigger: 'blur',
      },
    ],

    // MSG-122 親子区分必入
    parentChildDiv: [
      { required: true, message: 'MSG-122 親子区分を選択してください', trigger: 'change' },
    ],

    // MSG-123 シート・フルート必入
    sheetFlute: [
      { required: true, message: 'MSG-123 シート・フルートを選択してください', trigger: 'change' },
    ],

    // MSG-124 原紙 F 必入
    paperCdF: [
      { required: true, message: 'MSG-124 原紙(F)を選択してください', trigger: 'change' },
    ],

    // MSG-125 印刷 F 必入
    printCdF: [
      { required: true, message: 'MSG-125 印刷(F)を選択してください', trigger: 'change' },
    ],

    // MSG-126 最終工程必入
    finalMachineProc: [
      { required: true, message: 'MSG-126 最終工程を選択してください', trigger: 'change' },
    ],

    // MSG-127 形状 1 必入
    productShape1: [
      { required: true, message: 'MSG-127 形状1を選択してください', trigger: 'change' },
    ],

    // MSG-128 流通区分必入
    distDiv: [{ required: true, message: 'MSG-128 流通区分を選択してください', trigger: 'change' }],

    // MSG-129 单位必入
    unit: [{ required: true, message: 'MSG-129 単位を選択してください', trigger: 'change' }],
  }

  /**
   * 业务级手动校验（无法通过 rules 表达的部分）
   * @returns 错误消息数组（空即通过）
   */
  function validateBusiness(dto: EstimateCalcDto): string[] {
    const errors: string[] = []

    // 商品大分類/中分類/小分類 联动合法性（若有小分類，必须有中分類）
    if (dto.productCategorySml && !dto.productCategoryMid) {
      errors.push('MSG-W10010 商品小分類が指定されたのに中分類が空です')
    }

    // 見積り数は 1 件以上必須
    const hasQty = (dto.estimateQtys ?? []).some((q) => (q ?? 0) > 0)
    if (!hasQty) {
      errors.push('MSG-W10011 見積り数量を 1 件以上入力してください')
    }

    // 刃渡り × 流れ 必須（若其中之一存在）
    if ((dto.bladeWidth ?? 0) > 0 && (dto.bladeFlow ?? 0) <= 0) {
      errors.push('MSG-W10012 刃渡りを入力した場合、流れも必須です')
    }

    return errors
  }

  return { rules, validateBusiness }
}

import { test, expect, type Page } from '@playwright/test'

// 2026-11-17 待确认骨架。这里的驱动尚未接线，必须报红，不能当作业务验收通过。
// 基础资料可模拟；业务单据须经真实应用入口写入 PostgreSQL，不拦截 API 伪造结果。
const sample = {
  currency: 'CNY', taxRate: '0.00000000',
  orderQty: '100.00000000', salePrice: '30.00000000', invoiceTotal: '3000.00000000',
  bomQtyPerUnit: '2.00000000', openingMaterialQty: '20.00000000',
  purchaseQty: '180.00000000', materialQty: '200.00000000', materialPrice: '10.00000000',
  standardLaborHours: '2.00000000', standardMachineHours: '1.00000000',
  actualLaborHours: '2.50000000', actualMachineHours: '1.25000000',
  laborRate: '30.00000000', overheadRate: '20.00000000',
  standardTotal: '2080.00000000', actualTotal: '2100.00000000',
  actualUnitCost: '21.00000000', variance: '20.00000000', zero: '0.00000000',
} as const

type Order = { orderNo: string; sourceInquiryId: string; quantity: string; total: string }
type Plan = { runId: string; productionPlanId: string; purchasePlanId: string; productionQty: string; purchaseQty: string }
type Converted = { prNo: string; workOrderNo: string; sourceOrderNo: string }
type Purchase = { poNo: string; sourcePrNo: string; quantity: string }
type Receipt = { grNo: string; poNo: string; receivedQty: string; qcState: string; availableMaterialQty: string }
type WorkOrder = { workOrderNo: string; sourceOrderNo: string; quantity: string; standardTotal: string }
type Report = { reportNo: string; workOrderNo: string; goodQty: string; laborHours: string; machineHours: string }
type Inbound = { inboundNo: string; workOrderNo: string; quantity: string }
type Shipment = { shipmentNo: string; orderNo: string; workOrderNo: string; quantity: string; remainingFg: string; remainingOrderQty: string }
type Invoice = { invoiceNo: string; shipmentNo: string; total: string }
type Chain = {
  order: Order; converted: Converted; purchase: Purchase; receipt: Receipt;
  workOrder: WorkOrder; report: Report; inbound: Inbound; shipment: Shipment; invoice: Invoice
}

// 每个方法的接线由对应目标完成；方法名是测试驱动接口，不是假定存在的服务端 API。
// page 用于未来真实页面操作；环境准备必须核对实际数据库提供者，不能只相信环境变量。
function pendingDriver(page: Page) {
  function missing<T>(cards: string, action: string): Promise<T> {
    throw new Error(`MAINLINE_NOT_CONNECTED: ${cards} ${action}；当前页面 ${page.url()}。骨架尚未接线，不能视为通过。`)
  }
  return {
    prepare: (_input: typeof sample) => missing<{ provider: string; isolated: boolean }>('集成道', '准备隔离 PostgreSQL 和模拟基础资料'),
    loginAndReadMasters: () => missing<{ currency: string; bomQtyPerUnit: string; openingMaterialQty: string }>('WP04-01 / WP04-33 / WP10-01', '真实登录并读取客户、产品、BOM 和仓库'),
    inquiryToOrder: () => missing<Order>('WP04-13 / WP04-14', '模拟询盘交接经真实订单入口生成草稿'),
    approveOrder: (_order: Order) => missing<{ orderNo: string; state: string }>('WP04-16 / WP06-02', '审批订单并发布可执行需求'),
    runMrp: (_order: Order) => missing<Plan>('WP06-05 / WP06-07', '按本订单运行 MRP 并读取净需求'),
    convertPlans: (_order: Order, _plan: Plan) => missing<Converted>('WP06-11', '计划建议转真实 PR 和工单'),
    approvePrAndConvert: (_docs: Converted) => missing<Purchase>('WP07-02', '采购申请审批并转 PO'),
    approvePoAndReceive: (_purchase: Purchase) => missing<Receipt>('WP07-04 / WP07-06', 'PO 审批后真实收货，保持待检'),
    inspectReceipt: (_receipt: Receipt) => missing<{ grNo: string; acceptedQty: string; availableMaterialQty: string }>('WP09-03 / WP09-04 / WP10-46', '检验合格并放行库存'),
    releaseWorkOrderAndStandard: (_docs: Converted) => missing<WorkOrder>('WP08-04 / WP11-39', '下达工单并生成基础标准成本'),
    issueMaterials: (_workOrder: WorkOrder) => missing<{ workOrderNo: string; actualMaterialQty: string; remainingMaterialQty: string }>('WP08-06', '领料并登记一次真实消耗'),
    reportProduction: (_workOrder: WorkOrder) => missing<Report>('WP08-07', '报工、工时登记及完成'),
    receiveOutput: (_report: Report) => missing<Inbound>('WP10-15', '本工单产出真实入库'),
    collectActualCost: (_workOrder: WorkOrder, _inbound: Inbound) => missing<{ workOrderNo: string; total: string; unitCost: string }>('WP11-40', '归集本次实耗、人工和制造费用'),
    shipOrder: (_order: Order, _inbound: Inbound) => missing<Shipment>('WP10-19', '按订单拣配并发出本工单成品'),
    invoiceShipment: (_shipment: Shipment) => missing<Invoice>('WP11-26', '从真实发货生成唯一发票'),
    readCostAndTrace: (_chain: Chain) => missing<{
      orderNo: string; prNo: string; poNo: string; grNo: string; workOrderNo: string;
      reportNo: string; inboundNo: string; shipmentNo: string; invoiceNo: string;
      standardTotal: string; actualTotal: string; unitCost: string; variance: string
    }>('WP11-26 / FIN-COST-01', '在成本页面核对整链来源和金额'),
  }
}

test('接单到算钱：同一笔模拟订单在 PostgreSQL 上走完 17 步', async ({ page }, testInfo) => {
  const flow = pendingDriver(page)
  let completed = 0
  let current = ''
  let lastCompleted = ''
  async function step<T>(name: string, body: () => Promise<T>): Promise<T> {
    current = `${String(completed + 1).padStart(2, '0')} ${name}`
    const result = await test.step(current, body)
    completed += 1
    lastCompleted = current
    return result
  }

  try {
    await step('隔离 PostgreSQL 与模拟基础资料', async () => {
      expect(await flow.prepare(sample)).toMatchObject({ provider: 'PostgreSQL', isolated: true })
    })
    await step('浏览器登录并检查客户、产品、仓库', async () => {
      expect(await flow.loginAndReadMasters()).toMatchObject({
        currency: sample.currency, bomQtyPerUnit: sample.bomQtyPerUnit, openingMaterialQty: sample.openingMaterialQty,
      })
    })
    const order = await step('询盘交接生成订单', async () => {
      const result = await flow.inquiryToOrder()
      expect(result.orderNo).toBeTruthy()
      expect(result.sourceInquiryId).toBeTruthy()
      expect(result).toMatchObject({ quantity: sample.orderQty, total: sample.invoiceTotal })
      return result
    })
    await step('订单审批', async () => {
      expect(await flow.approveOrder(order)).toMatchObject({ orderNo: order.orderNo, state: 'APPROVED' })
    })
    const plan = await step('MRP 算出生产 100 件、采购 180 kg', async () => {
      const result = await flow.runMrp(order)
      expect(result).toMatchObject({ productionQty: sample.orderQty, purchaseQty: sample.purchaseQty })
      expect(result.runId).toBeTruthy()
      expect(result.productionPlanId).toBeTruthy()
      expect(result.purchasePlanId).toBeTruthy()
      return result
    })
    const converted = await step('计划转真实 PR 和工单', async () => {
      const result = await flow.convertPlans(order, plan)
      expect(result.sourceOrderNo).toBe(order.orderNo)
      expect(result.prNo).toBeTruthy()
      expect(result.workOrderNo).toBeTruthy()
      expect(result.prNo).not.toContain('STUB')
      expect(result.workOrderNo).not.toContain('STUB')
      return result
    })
    const purchase = await step('PR 审批并转 PO', async () => {
      const result = await flow.approvePrAndConvert(converted)
      expect(result).toMatchObject({ sourcePrNo: converted.prNo, quantity: sample.purchaseQty })
      expect(result.poNo).toBeTruthy()
      return result
    })
    const receipt = await step('PO 审批后收货，待检量不可用', async () => {
      const result = await flow.approvePoAndReceive(purchase)
      expect(result).toMatchObject({ poNo: purchase.poNo, receivedQty: sample.purchaseQty,
        qcState: 'PENDING', availableMaterialQty: sample.openingMaterialQty })
      expect(result.grNo).toBeTruthy()
      return result
    })
    await step('收货检验合格并放行', async () => {
      expect(await flow.inspectReceipt(receipt)).toMatchObject({ grNo: receipt.grNo,
        acceptedQty: sample.purchaseQty, availableMaterialQty: sample.materialQty })
    })
    const workOrder = await step('工单下达及标准成本', async () => {
      const result = await flow.releaseWorkOrderAndStandard(converted)
      expect(result).toMatchObject({ workOrderNo: converted.workOrderNo, sourceOrderNo: order.orderNo,
        quantity: sample.orderQty, standardTotal: sample.standardTotal })
      return result
    })
    await step('领用并消耗 200 kg 材料', async () => {
      expect(await flow.issueMaterials(workOrder)).toMatchObject({ workOrderNo: workOrder.workOrderNo,
        actualMaterialQty: sample.materialQty, remainingMaterialQty: sample.zero })
    })
    const report = await step('报工 100 件并登记实际工时', async () => {
      const result = await flow.reportProduction(workOrder)
      expect(result).toMatchObject({ workOrderNo: workOrder.workOrderNo, goodQty: sample.orderQty,
        laborHours: sample.actualLaborHours, machineHours: sample.actualMachineHours })
      expect(result.reportNo).toBeTruthy()
      return result
    })
    const inbound = await step('完工 100 件入成品库', async () => {
      const result = await flow.receiveOutput(report)
      expect(result).toMatchObject({ workOrderNo: workOrder.workOrderNo, quantity: sample.orderQty })
      expect(result.inboundNo).toBeTruthy()
      return result
    })
    await step('按实际用量和工时归集成本', async () => {
      expect(await flow.collectActualCost(workOrder, inbound)).toMatchObject({ workOrderNo: workOrder.workOrderNo,
        total: sample.actualTotal, unitCost: sample.actualUnitCost })
    })
    const shipment = await step('发出本订单 100 件成品', async () => {
      const result = await flow.shipOrder(order, inbound)
      expect(result).toMatchObject({ orderNo: order.orderNo, workOrderNo: workOrder.workOrderNo,
        quantity: sample.orderQty, remainingFg: sample.zero, remainingOrderQty: sample.zero })
      expect(result.shipmentNo).toBeTruthy()
      return result
    })
    const invoice = await step('按发货开票 3000 元', async () => {
      const result = await flow.invoiceShipment(shipment)
      expect(result).toMatchObject({ shipmentNo: shipment.shipmentNo, total: sample.invoiceTotal })
      expect(result.invoiceNo).toBeTruthy()
      return result
    })
    await step('查看成本单并核对整链来源和金额', async () => {
      const result = await flow.readCostAndTrace({ order, converted, purchase, receipt, workOrder, report, inbound, shipment, invoice })
      expect(result).toMatchObject({ orderNo: order.orderNo, prNo: converted.prNo, poNo: purchase.poNo,
        grNo: receipt.grNo, workOrderNo: workOrder.workOrderNo, reportNo: report.reportNo,
        inboundNo: inbound.inboundNo, shipmentNo: shipment.shipmentNo, invoiceNo: invoice.invoiceNo,
        standardTotal: sample.standardTotal, actualTotal: sample.actualTotal,
        unitCost: sample.actualUnitCost, variance: sample.variance })
    })
  } finally {
    await testInfo.attach('mainline-progress.json', {
      body: Buffer.from(JSON.stringify({ totalSteps: 17, completedSteps: completed,
        lastCompletedStep: lastCompleted || null, firstUnfinishedStep: completed === 17 ? null : current,
        result: completed === 17 ? 'PASS' : 'INCOMPLETE', simulatedBusinessData: true }, null, 2)),
      contentType: 'application/json',
    })
  }
})

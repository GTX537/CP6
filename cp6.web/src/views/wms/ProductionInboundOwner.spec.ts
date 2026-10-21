import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, shallowMount } from '@vue/test-utils'
import { reactive } from 'vue'
const mes = vi.hoisted(() => ({ getWorkOrderSummary:vi.fn(),search:vi.fn(),start:vi.fn(),suspend:vi.fn(),resume:vi.fn(),complete:vi.fn(),report:vi.fn() }))
const plan = vi.hoisted(() => ({ get:vi.fn(),search:vi.fn(),create:vi.fn(),update:vi.fn(),delete:vi.fn(),confirm:vi.fn(),cancel:vi.fn() }))
const receipt = vi.hoisted(() => ({ confirm:vi.fn(),search:vi.fn() }))
const messages = vi.hoisted(() => ({ success:vi.fn(),warning:vi.fn(),error:vi.fn(),confirm:vi.fn() }))
vi.mock('@/api/mes/mes',()=>({productionResultApi:mes}))
vi.mock('@/api/wms/inboundOrder',()=>({inboundOrderApi:plan}))
vi.mock('@/api/wms/inboundReceipt',()=>({inboundReceiptApi:receipt}))
vi.mock('element-plus',()=>({ElMessage:messages,ElMessageBox:{confirm:messages.confirm}}))
vi.mock('vue-i18n',async original=>({...await original<typeof import('vue-i18n')>(),useI18n:()=>({t:(key:string)=>key})}))
vi.mock('vue-router',()=>({useRoute:()=>route,useRouter:()=>({push:vi.fn(),replace:vi.fn(),back:vi.fn()})}))
import Inbound from './InboundOrderView.vue'
import Receipt from './InboundReceiptView.vue'
import Finished from './ProductionInboundView.vue'
import Production from '../mes/ProductionResultEntryView.vue'
import Results from '../mes/ProductionResultListView.vue'
import Inbounds from './InboundOrderListView.vue'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
const route=reactive({query:{} as Record<string,string>})
let view:ReturnType<typeof shallowMount>|undefined
function render(index:number):any {
 view=shallowMount([Inbound,Receipt,Finished,Production,Results,Inbounds][index]!,{global:{directives:{permission:{},loading:{}},config:{warnHandler:()=>{}},stubs:{
 ElCard:{template:'<div><slot/><slot name="header"/></div>'},ElAlert:true,ElTable:true,ElTableColumn:true,
 CpPageShell:{template:'<div><slot/><slot name="actions"/></div>'},CpListPage:true,
 }}});return view.vm
}
const changeIdentity=()=>{localStorage.setItem(AUTH_EPOCH,crypto.randomUUID());window.dispatchEvent(new Event(AUTH_EVENT))}
const document={inboundNo:'IN1',status:1,warehouseCd:'W01',details:[{lineNo:1,productCd:'P',expectedQty:2,receivedQty:0,expectedLocationCd:'L'}]}
const workOrder={workOrderNo:'WO1',status:2,productCd:'P',processes:[{processCd:'P',taskCd:'T',processStatus:0}]}
beforeEach(()=>{
 localStorage.clear();route.query={};vi.clearAllMocks()
 for(const api of [mes,plan,receipt])Object.values(api).forEach(m=>m.mockReset())
 mes.search.mockResolvedValue({data:{items:[],total:0}});receipt.search.mockResolvedValue({data:[]})
 messages.confirm.mockReset().mockResolvedValue('confirm')
})
afterEach(()=>{view?.unmount();view=undefined})
describe('production and inbound owner intent',()=>{
 it('freezes plan writes, suppresses duplicate submits and preserves a newer draft with the committed number',async()=>{
  const vm=render(0);Object.assign(vm.form,document);delete vm.form.inboundNo
  let done!:(x:unknown)=>void;plan.create.mockImplementationOnce(()=>new Promise(r=>{done=r}))
  const first=vm.onSave();await vm.onSave();expect(plan.create).toHaveBeenCalledTimes(1)
  vm.form.remarks='new draft';expect(plan.create.mock.calls[0]![0].remarks).not.toBe('new draft')
  done({data:{inboundNo:'SAVED'}});await first;expect(vm.form.inboundNo).toBe('SAVED');expect(vm.form.remarks).toBe('new draft')
 })
 it('discards obsolete plan reads and a confirmation opened by a prior identity',async()=>{
  const vm=render(0);let done!:(x:unknown)=>void;plan.get.mockImplementationOnce(()=>new Promise(r=>{done=r}))
  const load=vm.loadByNo('IN1');changeIdentity();done({data:document});await load;expect(vm.form.details).toEqual([])
  Object.assign(vm.form,document);messages.confirm.mockImplementationOnce(()=>new Promise(r=>{done=r}))
  const write=vm.onCancel();await vm.onCancel();changeIdentity();done('ok');await write;expect(plan.cancel).not.toHaveBeenCalled()
 })
 it('freezes receipt stock lines, retains confirmed identity and never posts the same saved receipt again',async()=>{
  const vm=render(1);vm.form.warehouseCd='W01';vm.form.details=[{productCd:'P',locationCd:'L',receivedQty:2}]
  let done!:(x:unknown)=>void;receipt.confirm.mockImplementationOnce(()=>new Promise(r=>{done=r}))
  const first=vm.onConfirm();await vm.onConfirm();vm.form.details[0].receivedQty=9
  expect(receipt.confirm.mock.calls[0]![0].details[0].receivedQty).toBe(2)
  done({data:{receiptNo:'RC1'}});await first;await vm.onConfirm();expect(receipt.confirm).toHaveBeenCalledTimes(1);expect(vm.form.receiptNo).toBe('RC1')
 })
 it('keeps stock outcome unknown blocked and drops prior-identity source reads',async()=>{
  const vm=render(1);vm.form.warehouseCd='W01';vm.form.details=[{productCd:'P',locationCd:'L',receivedQty:2}]
  receipt.confirm.mockRejectedValueOnce({response:{status:503,data:{message:'OPERATIONS_OUTCOME_UNKNOWN'}}})
  await vm.onConfirm();await vm.onConfirm();expect(receipt.confirm).toHaveBeenCalledTimes(1);expect(vm.intent.uncertain.value).toBe(true)
  changeIdentity();vm.form.inboundNo='IN1';let done!:(x:unknown)=>void;plan.get.mockImplementationOnce(()=>new Promise(r=>{done=r}))
  const read=vm.loadOrder();changeIdentity();done({data:document});await read;expect(vm.form.details).toEqual([])
 })
 it('retains a known manual production receipt when its history refresh fails and clears identity-bound history',async()=>{
  const vm=render(2);await flushPromises();Object.assign(vm.form,{productCd:'FG',receivedQty:2,lotNo:'LOT',locationCd:'L'})
  let done!:(x:unknown)=>void;receipt.confirm.mockImplementationOnce(()=>new Promise(r=>{done=r}))
  const first=vm.onConfirm();await vm.onConfirm();vm.form.remarks='next';receipt.search.mockRejectedValueOnce({response:{status:403}})
  done({data:{receiptNo:'RC2'}});await first;expect(receipt.confirm).toHaveBeenCalledTimes(1);expect(vm.savedReceiptNo).toBe('RC2');expect(vm.form.remarks).toBe('next');expect(vm.recent).toEqual([])
  changeIdentity();expect(vm.form.locationCd).toBe('');expect(vm.savedReceiptNo).toBe('')
 })
 it('keeps a committed report visible when summary permission is revoked and does not replace newer edits',async()=>{
  const vm=render(3);vm.workOrderNo='WO1';mes.getWorkOrderSummary.mockResolvedValueOnce({code:0,data:workOrder});await vm.loadWorkOrder()
  vm.operatorCd='OP';vm.openDialog(workOrder.processes[0],1);let done!:(x:unknown)=>void;mes.start.mockImplementationOnce(()=>new Promise(r=>{done=r}))
  const first=vm.submit();await vm.submit();vm.form.resultNote='new draft';mes.getWorkOrderSummary.mockRejectedValueOnce({response:{status:403}})
  done({data:{resultNo:'PR1'}});await first;expect(mes.start).toHaveBeenCalledTimes(1);expect(vm.savedResultNo).toBe('PR1');expect(vm.form.resultNote).toBe('new draft');expect(vm.workOrder).toBeNull()
 })
 it('ignores obsolete manufacturing summaries and blocks unknown reports',async()=>{
  const vm=render(3);vm.workOrderNo='WO1';let done!:(x:unknown)=>void;mes.getWorkOrderSummary.mockImplementationOnce(()=>new Promise(r=>{done=r}))
  const load=vm.loadWorkOrder();vm.workOrderNo='WO2';done({code:0,data:workOrder});await load;expect(vm.workOrder).toBeNull()
  vm.workOrder=workOrder;vm.operatorCd='OP';vm.openDialog(workOrder.processes[0],1)
  mes.start.mockRejectedValueOnce({response:{status:503}});await vm.submit();await vm.submit();expect(mes.start).toHaveBeenCalledTimes(1)
 })
 it('clears denied result searches and ignores old-identity inbound lists',async()=>{
  const vm=render(4);await flushPromises();mes.search.mockResolvedValueOnce({data:{items:[{resultNo:'PR'}],total:1}})
  await vm.search();expect(vm.rows).toHaveLength(1);mes.search.mockRejectedValueOnce({response:{status:403}});await vm.search();expect(vm.rows).toEqual([])
  view?.unmount();const list=render(5);let done!:(x:unknown)=>void;plan.search.mockImplementationOnce(()=>new Promise(r=>{done=r}))
  const read=list.fetchList({page:1,size:20,filters:{}});changeIdentity();done({data:[document]});expect(await read).toEqual({rows:[],total:0})
 })
})

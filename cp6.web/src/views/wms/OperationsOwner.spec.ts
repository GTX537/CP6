import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, shallowMount } from '@vue/test-utils'
import { reactive } from 'vue'
const mes = vi.hoisted(() => ({get:vi.fn(),search:vi.fn(),create:vi.fn(),update:vi.fn(),delete:vi.fn(),issue:vi.fn(),expandFromOrder:vi.fn(),creationContext:vi.fn(),creationScope:vi.fn(),createOperation:vi.fn(),readCreation:vi.fn(),closeCreation:vi.fn()}))
const wms = vi.hoisted(() => ({get:vi.fn(),search:vi.fn(),create:vi.fn(),update:vi.fn(),delete:vi.fn(),confirm:vi.fn(),allocate:vi.fn(),ship:vi.fn(),cancel:vi.fn(),fromOrder:vi.fn(),fromWorkOrder:vi.fn()}))
const messages = vi.hoisted(() => ({success:vi.fn(),warning:vi.fn(),error:vi.fn(),confirm:vi.fn()}))
vi.mock('@/api/mes/mes',()=>({workOrderApi:mes}))
vi.mock('@/stores/permission',()=>({usePermissionStore:()=>({loaded:true,has:()=>true})}))
vi.mock('@/api/wms/outboundOrder',()=>({outboundOrderApi:wms}))
vi.mock('element-plus',()=>({ElMessage:messages,ElMessageBox:{confirm:messages.confirm}}))
vi.mock('vue-i18n',async original=>({...await original<typeof import('vue-i18n')>(),useI18n:()=>({t:(key:string)=>key})}))
vi.mock('vue-router',()=>({useRoute:()=>route,useRouter:()=>({push:vi.fn(),replace:vi.fn()})}))
import Manufacturing from '../mes/WorkOrderEntryView.vue'
import ManufacturingList from '../mes/WorkOrderListView.vue'
import Outbound from './OutboundOrderView.vue'
import OutboundList from './OutboundOrderListView.vue'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
const route=reactive({query:{} as Record<string,string>})
let view:ReturnType<typeof shallowMount>|undefined
const components=[Manufacturing,ManufacturingList,Outbound,OutboundList]
function render(index:number):any {
 view=shallowMount(components[index]!,{global:{directives:{permission:{},loading:{}},config:{warnHandler:()=>{}},stubs:{
 ElCard:{template:'<div><slot/><slot name="header"/></div>'},ElAlert:true,ElTable:true,ElTableColumn:true,
 CpPageShell:{template:'<div><slot/><slot name="actions"/></div>'},CpListPage:true,
 }}});return view.vm
}
function changeIdentity(){localStorage.setItem(AUTH_EPOCH,crypto.randomUUID());window.dispatchEvent(new Event(AUTH_EVENT))}
const manufacturing={workOrderNo:'WO1',status:0,productCd:'P',productionQty:2,processes:[{processCd:'P',taskCd:'T'}],materials:[]}
const outbound={outboundNo:'OUT1',status:0,warehouseCd:'W01',details:[{productCd:'P',requiredQty:2}]}
beforeEach(()=>{
 localStorage.clear();route.query={};vi.clearAllMocks()
 Object.values(mes).forEach(m=>m.mockReset());Object.values(wms).forEach(m=>m.mockReset())
 const scope={tenantId:'10000000-0000-0000-0000-000000000001',actorId:'20000000-0000-0000-0000-000000000002'}
 mes.creationContext.mockResolvedValue({code:0,data:{...scope,authorityVersion:'current',allowsUnbound:true,departments:[]}})
 mes.creationScope.mockResolvedValue({code:0,data:scope})
 Object.defineProperty(navigator,'locks',{configurable:true,value:{request:async (_key:string,_options:any,run:(x:any)=>any)=>run({})}})
 messages.confirm.mockReset().mockResolvedValue('confirm');mes.search.mockResolvedValue({data:{items:[],total:0}})
})
afterEach(()=>{view?.unmount();view=undefined})
describe('independent manufacturing and outbound intent',()=>{
 it('keeps the original outbound line identity in the save request after deleting another line',async()=>{
  const vm=render(2)
  const remaining={lineNo:2,productCd:'SAME',requiredQty:2,unitCd:'EA'}
  wms.get.mockResolvedValueOnce({data:{...outbound,details:[{...remaining,lineNo:1},{...remaining}]}})
  await vm.loadByNo('OUT1');vm.removeLine(0)
  wms.update.mockResolvedValueOnce({code:0})
  wms.get.mockResolvedValueOnce({data:{...outbound,details:[{...remaining}]}})
  await vm.onSave()
  expect(wms.update.mock.calls[0]![1].details).toEqual([remaining])
  vm.addLine();expect(vm.form.details.map((x:any)=>x.lineNo)).toEqual([2,3])
 })
 it.each([0,2])('discards prior-identity reads and preserves drafts edited while loading (%s)',async index=>{
  const vm=render(index),api=index===0?mes:wms,doc=index===0?manufacturing:outbound
  let done!:(x:unknown)=>void;api.get.mockImplementationOnce(()=>new Promise(r=>{done=r}))
  const read=vm.loadByNo('ONE');changeIdentity();done({code:0,data:doc});await read
  expect(vm.form.details??vm.form.processes).toEqual([])
  api.get.mockImplementationOnce(()=>new Promise(r=>{done=r}));const newer=vm.loadByNo('TWO')
  vm.form.remarks='unsaved';done({code:0,data:doc});await newer;expect(vm.form.remarks).toBe('unsaved')
 })
 it.each([0,2])('locks a write before awaiting, freezes the draft and keeps committed identity if read is denied (%s)',async index=>{
  const vm=render(index),api=index===0?mes:wms,doc=index===0?manufacturing:outbound
  await flushPromises();Object.assign(vm.form,doc);if(index===0)vm.form.workOrderNo='';else delete vm.form.outboundNo
  let done!:(x:unknown)=>void;const write=index===0?mes.createOperation:api.create;write.mockImplementationOnce(()=>new Promise(r=>{done=r}))
  const first=vm.onSave();await flushPromises();await vm.onSave();expect(write).toHaveBeenCalledTimes(1)
  vm.form.remarks='new draft';expect((index===0?write.mock.calls[0]![0].workOrder:write.mock.calls[0]![0]).remarks).not.toBe('new draft')
  api.get.mockRejectedValue({response:{status:403}})
  done({code:0,data:index===0?{operationId:write.mock.calls[0]![0].operationId,workOrderId:'40000000-0000-0000-0000-000000000004',workOrderNo:'NEW',departmentId:null}:{outboundNo:'NEW'}});await first
  expect(index===0?vm.form.workOrderNo:vm.form.outboundNo).toBe('NEW')
  expect(vm.form.remarks).toBe('new draft');expect(messages.success).toHaveBeenCalledTimes(1)
 })
 it.each([0,2])('does not replay unknown writes or navigate with an obsolete completion (%s)',async index=>{
  const vm=render(index),api=index===0?mes:wms
  Object.assign(vm.form,index===0?manufacturing:outbound)
  api.update.mockRejectedValueOnce({response:{status:503,data:{message:'OPERATIONS_OUTCOME_UNKNOWN'}}})
  await vm.onSave();await vm.onSave();expect(api.update).toHaveBeenCalledTimes(1);expect(vm.intent.uncertain.value).toBe(true)
  changeIdentity();expect(vm.form.details??vm.form.processes).toEqual([])
 })
 it('does not issue or cancel for a new identity after a confirmation dialog was opened',async()=>{
  const vm=render(2);Object.assign(vm.form,outbound);let confirm!:(x:unknown)=>void
  messages.confirm.mockImplementationOnce(()=>new Promise(r=>{confirm=r}));const first=vm.onCancel();await vm.onCancel()
  expect(messages.confirm).toHaveBeenCalledTimes(1);changeIdentity();confirm('ok');await first;expect(wms.cancel).not.toHaveBeenCalled()
 })
 it('clears manufacturing results on a rejected refresh or identity change',async()=>{
  const vm=render(1);await flushPromises();mes.search.mockResolvedValueOnce({data:{items:[manufacturing],total:1}})
  await vm.search();expect(vm.rows).toHaveLength(1)
  mes.search.mockRejectedValueOnce({response:{status:403}});await vm.search();expect(vm.rows).toEqual([])
  mes.search.mockResolvedValueOnce({data:{items:[manufacturing],total:1}});await vm.search();changeIdentity();expect(vm.rows).toEqual([])
 })
 it('guards outbound list bridge duplication and stale list responses',async()=>{
  const vm=render(3);let done!:(x:unknown)=>void;wms.fromOrder.mockImplementationOnce(()=>new Promise(r=>{done=r}))
  vm.bridgeOrderNo='ORDER';const first=vm.onBridgeOrder();await vm.onBridgeOrder();expect(wms.fromOrder).toHaveBeenCalledTimes(1)
  changeIdentity();done({data:{outboundNo:'OLD'}});await first;expect(messages.success).not.toHaveBeenCalled()
  wms.search.mockImplementationOnce(()=>new Promise(r=>{done=r}));const read=vm.fetchList({page:1,size:20,filters:{}})
  changeIdentity();done({data:[outbound]});expect(await read).toEqual({rows:[],total:0})
 })
})

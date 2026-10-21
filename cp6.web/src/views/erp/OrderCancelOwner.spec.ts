import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
const api=vi.hoisted(()=>({ cancel:vi.fn() }))
const messages=vi.hoisted(()=>({ error:vi.fn(), warning:vi.fn() }))
vi.mock('@/api/erp/order',()=>({orderApi:api}))
vi.mock('element-plus',()=>({ElMessage:messages}))
vi.mock('vue-i18n',()=>({useI18n:()=>({t:(key:string)=>key})}))
import OrderCancelDialog from './OrderCancelDialog.vue'
import { AUTH_EPOCH,AUTH_EVENT } from '@/auth/identity'
let view:ReturnType<typeof mount>|undefined
function render():any {
 view=mount(OrderCancelDialog,{props:{modelValue:true,webOrderNo:'ORDER-1'},global:{stubs:{
 ElDialog:{template:'<div><slot/><slot name="footer"/></div>'},ElAlert:true,ElForm:{template:'<div><slot/></div>'},ElFormItem:{template:'<div><slot/></div>'},
 ElInput:true,ElTable:true,ElTableColumn:true,ElResult:true,CpTag:true,ElButton:{template:'<button><slot/></button>'}
 }}});return view.vm
}
const response=(outcome='NeedsDecision')=>({code:0,data:{outcome,relatedWorkOrders:[],relatedOutbounds:[],correlationId:'C'}})
beforeEach(()=>{localStorage.clear();vi.clearAllMocks();api.cancel.mockReset()})
afterEach(()=>{view?.unmount();view=undefined})
describe('Order cancellation identity and intent',()=>{
 it('deduplicates the first call because force=false can already write',async()=>{
  let done!:(x:unknown)=>void;api.cancel.mockImplementation(()=>new Promise(r=>{done=r}))
  const vm=render();vm.reason='reason';const first=vm.onProbe();const second=vm.onProbe()
  expect(api.cancel).toHaveBeenCalledTimes(1);done(response('Cancelled'));await first;await second
 })
 it('discards old identity/target responses and never confirms another order with an old decision',async()=>{
  let done!:(x:unknown)=>void;api.cancel.mockImplementationOnce(()=>new Promise(r=>{done=r}))
  const vm=render();vm.reason='reason';const first=vm.onProbe()
  await view!.setProps({webOrderNo:'ORDER-2'});done(response());await first
  expect(vm.result).toBeNull();expect(vm.step).toBe('input');await vm.onForceConfirm();expect(api.cancel).toHaveBeenCalledTimes(1)
  api.cancel.mockImplementationOnce(()=>new Promise(r=>{done=r}));vm.reason='new';const next=vm.onProbe()
  localStorage.setItem(AUTH_EPOCH,crypto.randomUUID());window.dispatchEvent(new Event(AUTH_EVENT));done(response('Cancelled'));await next
  expect(vm.result).toBeNull();expect(view!.emitted('cancelled')).toBeUndefined()
 })
 it('binds second confirmation to the same frozen number and reason, and does not report Rejected as success',async()=>{
  api.cancel.mockResolvedValueOnce(response());const vm=render();vm.reason='original';await vm.onProbe();vm.reason='changed'
  api.cancel.mockResolvedValueOnce(response('Rejected'));await vm.onForceConfirm()
  expect(api.cancel).toHaveBeenLastCalledWith('ORDER-1','original',true);expect(view!.emitted('cancelled')).toBeUndefined()
 })
 it('does not replay an unknown commit outcome within the dialog',async()=>{
  api.cancel.mockRejectedValueOnce({response:{status:503,data:{message:'ORDER_CANCEL_OUTCOME_UNKNOWN'}}})
  const vm=render();vm.reason='reason';await vm.onProbe();await vm.onProbe();await vm.onForceConfirm();await flushPromises()
  expect(api.cancel).toHaveBeenCalledTimes(1);expect(view!.emitted('cancelled')).toBeUndefined()
 })
})

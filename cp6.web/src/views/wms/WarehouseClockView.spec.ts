import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils'
import { reactive } from 'vue'
import type { ClockContext, ClockCommand, ClockPolicy } from '@/api/wms/warehouseClock'
const mock=vi.hoisted(()=>({context:vi.fn(),history:vi.fn(),save:vi.fn(),operation:vi.fn(),permission:null as any,authenticated:true,route:null as any}))
vi.mock('@/api/wms/warehouseClock',()=>({warehouseClockApi:mock}))
vi.mock('@/api/wms/transferEnvironment',()=>({transferEnvironmentApi:{}}))
vi.mock('vue-router',()=>({useRoute:()=>mock.route}))
vi.mock('@/auth/browserAuthentication',()=>({authenticationCanRead:()=>mock.authenticated}))
vi.mock('@/stores/permission',()=>({usePermissionStore:()=>({loaded:true,has:(k:string)=>k.endsWith(':query')||mock.permission.allowed})}))
import View from './WarehouseClockView.vue'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
import { clockKey, pendingClock } from '@/auth/warehouseClockRecovery'
const id=(n:number)=>`30000000-0000-4000-8000-${String(n).padStart(12,'0')}`
const source={reference:{owner:'Main.PostgreSql.ClockAuthority.v1',id:id(7),version:'9007199254740993',digest:'a'.repeat(64)},authority:{environmentId:id(8),id:id(7),version:'9007199254740993',provider:'POSTGRESQL_CLOCK_TIMESTAMP_V1',evidenceMode:'DEMO' as const,attestationRef:'EXPLICIT SOURCE',validFrom:'2026-10-01T00:00:00.0000001+08:00',validTo:'2026-11-01T00:00:00.0000002+08:00',maxErrorMilliseconds:25,maxReadMilliseconds:1000}}
const context:ClockContext={tenantId:id(1),actorId:id(2),warehouseId:id(3),warehouseCode:'WH',version:'absent',policy:null,source,qualification:'CLOCK_CONFIGURATION_ONLY',admissionGrant:false}
const reference={owner:'Main.Wms.WarehouseClockPolicy.v1',id:id(4),version:'1',digest:'b'.repeat(64)}
const definition={enabled:true,source:source.reference,validFrom:source.authority.validFrom,validTo:source.authority.validTo}
const policy:ClockPolicy={reference,original:{tenantId:id(1),id:id(4),warehouseId:id(3),version:'1',actorId:id(2),definition,source,reason:'PRIVATE REASON'},originalJson:'PRIVATE ORIGINAL'}
const current={...context,version:'1',policy},ok=<T,>(data:T)=>({code:0,data})
let view:VueWrapper|undefined
const field=(label:string)=>view!.get(`[aria-label="${label}"]`)
async function click(label:string){await view!.findAll('button').find(x=>x.text()===label)!.trigger('click');await flushPromises()}
async function submit(){await view!.findAll('form')[0]!.trigger('submit');await flushPromises()}
async function render(){view=mount(View,{global:{stubs:{RouterLink:{template:'<a><slot /></a>'}}}});await flushPromises()}
async function fill(){await field('政策状态').setValue('enabled');await field('开始时刻').setValue(definition.validFrom);await field('结束时刻').setValue(definition.validTo);await field('操作原因').setValue('PRIVATE INTENT')}
beforeEach(()=>{
  vi.resetAllMocks();mock.permission=reactive({allowed:true});mock.authenticated=true;mock.route=reactive({params:{id:context.warehouseId}});localStorage.clear();localStorage.setItem(AUTH_EPOCH,'first')
  mock.context.mockResolvedValue(ok(structuredClone(context)));mock.history.mockResolvedValue(ok(structuredClone(policy)))
  mock.save.mockImplementation(async(_w:string,c:ClockCommand)=>ok({operationId:c.operationId,warehouseId:context.warehouseId,reference,changed:true}))
  Object.defineProperty(navigator,'locks',{configurable:true,value:{request:(_k:string,_o:unknown,f:(l:object)=>unknown)=>f({})}})
})
afterEach(()=>{view?.unmount();view=undefined})
it('requires explicit policy interval and saves exact source, long version and original offsets without local clock defaults',async()=>{
  await render();expect(field('政策状态').element).toHaveProperty('value','');expect(field('开始时刻').element).toHaveProperty('value','')
  await fill();await submit();const c=mock.save.mock.calls[0]![1] as ClockCommand;expect(c.definition).toEqual(definition);expect(c.expectedVersion).toBe('absent');expect(pendingClock(context)).toBeNull()
})
it('disables saved policy even with unavailable source and unsaved dates',async()=>{
  mock.context.mockResolvedValue(ok({...structuredClone(current),source:null}));await render();await field('开始时刻').setValue('unsaved');await field('政策状态').setValue('disabled');await field('操作原因').setValue('disable');await submit()
  expect(mock.save.mock.calls[0]![1].definition).toEqual({...definition,enabled:false})
})
it('retains only UUID after lost acknowledgement, queries UNKNOWN and recovers without repeating save',async()=>{
  mock.save.mockRejectedValue(new Error('lost'));await render();await fill();await submit();const operationId=pendingClock(context)!;expect(operationId).toBeTruthy();expect(JSON.stringify(localStorage)).not.toContain('PRIVATE')
  mock.operation.mockResolvedValue(ok({operationId,warehouseId:id(3),state:'UNKNOWN'}));await click('查询原操作');expect(mock.save).toHaveBeenCalledTimes(1);expect(pendingClock(context)).toBe(operationId)
  mock.permission.allowed=false;mock.operation.mockResolvedValue(ok({operationId,warehouseId:id(3),state:'COMMITTED',result:{operationId,warehouseId:id(3),reference,changed:true}}));await click('查询原操作');expect(pendingClock(context)).toBeNull();expect(mock.save).toHaveBeenCalledTimes(1)
})
it.each(['tenantId','actorId','warehouseId','version','source'])('refuses changed %s before allocating an original UUID',async key=>{
  await render();await fill();mock.context.mockResolvedValue(ok({...context,[key]:key==='version'?'2':key==='source'?null:id(99)}));await submit();expect(mock.save).not.toHaveBeenCalled();expect(pendingClock(context)).toBeNull()
})
it('requires actual source, dedicated permission and browser exclusion',async()=>{
  mock.context.mockResolvedValue(ok({...context,source:null}));await render();await fill();await submit();expect(mock.save).not.toHaveBeenCalled()
  mock.context.mockResolvedValue(ok(context));await click('重新载入');await fill();mock.permission.allowed=false;await submit();expect(mock.save).not.toHaveBeenCalled()
  mock.permission.allowed=true;Object.defineProperty(navigator,'locks',{configurable:true,value:undefined});await submit();expect(mock.save).not.toHaveBeenCalled()
})
it('keeps mismatched receipts and clears private history after denied read',async()=>{
  const operationId=id(9);localStorage.setItem(clockKey(current),operationId);mock.context.mockResolvedValue(ok(current));await render()
  mock.operation.mockResolvedValue(ok({operationId,warehouseId:id(99),state:'CLOSED'}));await click('查询原操作');expect(pendingClock(current)).toBe(operationId)
  await field('历史版本').setValue('1');await view!.findAll('form')[1]!.trigger('submit');await flushPromises();expect(view!.text()).toContain('PRIVATE ORIGINAL')
  mock.history.mockRejectedValue({response:{status:403}});await view!.findAll('form')[1]!.trigger('submit');await flushPromises();expect(view!.text()).not.toContain('PRIVATE ORIGINAL');expect(view!.find('[aria-label="操作原因"]').exists()).toBe(false)
})
it('drops late responses after identity change',async()=>{
  let done!:(r:unknown)=>void;mock.context.mockImplementationOnce(()=>new Promise(resolve=>{done=resolve}));await render()
  mock.authenticated=false;localStorage.setItem(AUTH_EPOCH,'changed');window.dispatchEvent(new Event(AUTH_EVENT));done(ok(current));await flushPromises();expect(view!.text()).not.toContain('PRIVATE');expect(view!.find('[aria-label="操作原因"]').exists()).toBe(false)
})

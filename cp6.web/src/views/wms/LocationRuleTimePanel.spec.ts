import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils'
import { reactive } from 'vue'
import type { LocationRuleReviewScope, LocationRuleReviewSource } from '@/api/wms/locationRuleReview'
const mock=vi.hoisted(()=>({evaluate:vi.fn(),permission:null as any,authenticated:true}))
vi.mock('@/api/wms/warehouseClock',()=>({warehouseClockApi:mock}))
vi.mock('@/api/wms/transferEnvironment',()=>({transferEnvironmentApi:{}}))
vi.mock('@/auth/browserAuthentication',()=>({authenticationCanRead:()=>mock.authenticated}))
vi.mock('@/stores/permission',()=>({usePermissionStore:()=>({loaded:true,has:()=>mock.permission.allowed})}))
import Panel from './LocationRuleTimePanel.vue'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
const id=(n:number)=>`30000000-0000-4000-8000-${String(n).padStart(12,'0')}`
const reference={owner:'Main.Wms.LocationRuleProposal.v1',id:id(4),version:'9007199254740993',digest:'b'.repeat(64)}
const scope:LocationRuleReviewScope={tenantId:id(1),actorId:id(2),environmentId:id(9),evidenceMode:'DEMO'}
const source={reference,body:{tenantId:id(1),id:id(4),locationId:id(5)},etag:'"native"'} as LocationRuleReviewSource
function response(request:any){return {code:0,data:{tenantId:id(1),actorId:id(2),locationId:id(5),warehouseId:id(6),proposal:reference,etag:source.etag,requested:request,outcome:'TIME_SATISFIED',reason:'TIME_SATISFIED',evaluatedAt:request.at||'2026-10-28T00:00:00Z',isPrediction:request.kind==='PLANNED',qualification:'RULE_TIME_ONLY',admissionGrant:false,requiresFinalEffectRecheck:true,remainingGates:['OCCUPANCY'],clock:{reference:{owner:'Main.Wms.WarehouseClockObservation.v1',id:id(7),version:'1',digest:'a'.repeat(64)},original:{tenantId:id(1),environmentId:id(9),warehouseId:id(6),id:id(7),policy:{version:'1'},evidenceMode:'DEMO',observedAt:'2026-10-28T00:00:00Z',earliest:'2026-10-27T23:59:59.975Z',latest:'2026-10-28T00:00:00.030Z'}}}}}
let view:VueWrapper|undefined
async function render(){view=mount(Panel,{props:{source,scope},global:{stubs:{RouterLink:{template:'<a><slot /></a>'}}}});await flushPromises()}
async function submit(){await view!.get('form').trigger('submit');await flushPromises()}
beforeEach(()=>{vi.resetAllMocks();mock.permission=reactive({allowed:true});mock.authenticated=true;localStorage.clear();localStorage.setItem(AUTH_EPOCH,'first');mock.evaluate.mockImplementation(async(_l:string,_id:string,_etag:string,c:any)=>response(c))})
afterEach(()=>{view?.unmount();view=undefined})
it('uses current exact candidate and shows a point-in-time interval without claiming rule activation',async()=>{
  await render();await submit();expect(mock.evaluate).toHaveBeenCalledWith(id(5),id(4),'"native"',{proposal:reference,kind:'CURRENT',at:null});expect(view!.text()).toContain('该时点满足有效期');expect(view!.text()).toContain('保守区间');expect(view!.text()).toContain('实际应用时必须重新检查')
})
it('passes exact offset/seven-digit planned time as prediction and discards old assessment on input change',async()=>{
  await render();await submit();await view!.get('[aria-label="评估时点"]').setValue('PLANNED');expect(view!.text()).not.toContain('该时点满足有效期')
  const at='2026-10-28T08:00:00.0000001+08:00';await view!.get('[aria-label="指定时刻"]').setValue(at);await submit();expect(mock.evaluate.mock.calls.at(-1)![3].at).toBe(at);expect(view!.text()).toContain('指定时刻预测，不代表已兑现效果')
})
it('displays unknown source without treating client planned time as authority',async()=>{
  mock.evaluate.mockImplementation(async(_l:string,_id:string,_etag:string,c:any)=>{const r=response(c);return {...r,data:{...r.data,outcome:'UNKNOWN',reason:'CLOCK_SOURCE_UNAVAILABLE',clock:null,evaluatedAt:null}}})
  await render();await submit();expect(view!.text()).toContain('认可的时钟来源不可用');expect(view!.text()).not.toContain('该时点满足有效期')
})
it.each(['tenantId','actorId','etag','admissionGrant','requested'])('rejects mismatched or granting %s result',async key=>{
  mock.evaluate.mockImplementation(async(_l:string,_id:string,_etag:string,c:any)=>{const r=response(c);return {...r,data:{...r.data,[key]:key==='admissionGrant'?true:key==='requested'?{...c,at:'forged'}:id(99)}}})
  await render();await submit();expect(view!.text()).toContain('返回评估与当前候选不一致');expect(view!.text()).not.toContain('该时点满足有效期')
})
it('rejects late identity responses and requires current read permission',async()=>{
  let done!:(v:any)=>void;mock.evaluate.mockImplementation(()=>new Promise(resolve=>{done=resolve}));await render();await submit();mock.authenticated=false;localStorage.setItem(AUTH_EPOCH,'changed');window.dispatchEvent(new Event(AUTH_EVENT));done(response({proposal:reference,kind:'CURRENT',at:null}));await flushPromises();expect(view!.text()).not.toContain('该时点满足有效期')
  mock.authenticated=true;mock.permission.allowed=false;await submit();expect(mock.evaluate).toHaveBeenCalledTimes(1)
})

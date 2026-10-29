import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils'
import { reactive } from 'vue'
import type { ControlView, ControlResult, ControlEntry } from '@/api/wms/locationControl'
const mock=vi.hoisted(()=>({read:vi.fn(),command:vi.fn(),operation:vi.fn(),permissions:null as any,route:null as any,authenticated:true}))
vi.mock('@/api/wms/locationControl',()=>({locationControlApi:mock}))
vi.mock('@/api/wms/transferEnvironment',()=>({transferEnvironmentApi:{}}))
vi.mock('vue-router',()=>({useRoute:()=>mock.route}))
vi.mock('@/stores/permission',()=>({usePermissionStore:()=>mock.permissions}))
vi.mock('@/auth/browserAuthentication',()=>({authenticationCanRead:()=>mock.authenticated}))
import View from './LocationControlView.vue'
import { AUTH_EPOCH, AUTH_EVENT } from '@/auth/identity'
import { pendingControl } from '@/auth/locationControlRecovery'
const id=(n:number)=>`50000000-0000-4000-8000-${String(n).padStart(12,'0')}`
const owner='Main.Wms.LocationOperationalControl.v1'
const scope={tenantId:id(1),actorId:id(2),locationId:id(3)}
let current:ControlView,view:VueWrapper|undefined,serial:number
const ok=(data:any)=>({code:0,data})
const tag=(v:string)=>`"lc1.AAAAAAAAAAE=.${v}"`
const click=async(text:string)=>{await view!.findAll('button').find(b=>b.text()===text)!.trigger('click');await flushPromises()}
const reason=()=>view!.get('[aria-label="操作原因"]').setValue('现场限制已核对')
async function render(){view=mount(View);await flushPromises()}
beforeEach(()=>{
 vi.resetAllMocks();localStorage.clear();localStorage.setItem(AUTH_EPOCH,'one');mock.authenticated=true;serial=10
 mock.route=reactive({params:{id:scope.locationId}});mock.permissions=reactive({loaded:true,read:true,write:true,has(k:string){return k.endsWith(':query')?this.read:this.write}})
 current={...scope,etag:tag('1'),controlRevision:'1',isBlocked:false,initializationQuarantined:false,otherRestrictionRetained:false,openBlocks:0,entries:[],page:1,qualification:'NATIVE_LOCAL_CONTROL_ONLY',admissionGrant:false}
 mock.read.mockImplementation(()=>Promise.resolve(ok(structuredClone(current))))
 mock.command.mockImplementation((_l:string,route:string,_etag:string,body:any)=>{
  const kind=route==='blocks'?'BLOCK':route==='release-decisions'?'RELEASE_DECISION':'RELEASE',ref={owner,id:id(serial++),version:'1',digest:'a'.repeat(64)}
  const blockId=kind==='BLOCK'?ref.id:body.blockId
  const e:ControlEntry={reference:ref,state:kind==='BLOCK'?'OPEN':'RECORDED',original:{id:ref.id,blockId,kind,actorId:scope.actorId,reason:body.reason,controlBefore:current.controlRevision,nativeEtag:current.etag}}
  current.entries.push(e)
  if(kind!=='RELEASE_DECISION'){current.controlRevision=String(Number(current.controlRevision)+1);current.etag=tag(current.controlRevision);current.openBlocks+=kind==='BLOCK'?1:-1;current.isBlocked=current.openBlocks>0||current.initializationQuarantined||current.otherRestrictionRetained}
  if(kind==='RELEASE')current.entries.find(x=>x.reference.id===blockId)!.state='RELEASED'
  const r:ControlResult={operationId:body.operationId,locationId:scope.locationId,reference:ref,blockId,effect:kind,etag:current.etag,controlRevision:current.controlRevision,isBlocked:current.isBlocked}
  return Promise.resolve(ok(r))
 })
 Object.defineProperty(navigator,'locks',{configurable:true,value:{request:(_k:string,_o:any,f:(lock:object)=>unknown)=>f({})}})
})
afterEach(()=>{view?.unmount();view=undefined})
it('completes a block, current release decision and exact application without claiming activation',async()=>{
 await render();await reason();await click('添加本域封锁');expect(mock.command.mock.calls[0]![1]).toBe('blocks');expect(pendingControl(scope)).toBeNull()
 await reason();await click('记录此项解除决定');expect(current.isBlocked).toBe(true)
 const decision=current.entries.find(x=>x.original.kind==='RELEASE_DECISION')!
 await reason();await click('按此决定解除');expect(mock.command.mock.calls[2]![3]).toMatchObject({blockId:decision.original.blockId,decisionRef:decision.reference})
 expect(current.openBlocks).toBe(0);expect(view!.text()).toContain('作业资格仍须另行核定');expect(view!.text()).toContain('已解除')
})
it('displays quarantine and independent restrictions after the local release',async()=>{
 current.initializationQuarantined=true;current.otherRestrictionRetained=true;current.isBlocked=true
 await render();await reason();await click('添加本域封锁');await reason();await click('记录此项解除决定');await reason();await click('按此决定解除')
 expect(view!.text()).toContain('初始化隔离');expect(view!.text()).toContain('已有或其他来源的限制仍保留');expect(view!.text()).toContain('仍有封锁标记')
})
it('retains unknown original keys across reload and recovers before permitting another command',async()=>{
 mock.command.mockRejectedValue(new Error('连接中断'));await render();await reason();await click('添加本域封锁');const operation=pendingControl(scope);expect(operation).toBeTruthy()
 await click('重新载入');await reason();expect(view!.findAll('button').find(b=>b.text()==='添加本域封锁')!.attributes('disabled')).toBeDefined()
 mock.operation.mockResolvedValue(ok({operationId:operation,locationId:scope.locationId,state:'UNKNOWN'}));await click('查询原操作');expect(pendingControl(scope)).toBe(operation)
 mock.operation.mockResolvedValue(ok({operationId:operation,locationId:scope.locationId,state:'CLOSED'}));await click('关闭未提交原操作');expect(pendingControl(scope)).toBeNull();expect(mock.command).toHaveBeenCalledTimes(1)
})
it('rejects changed control predecessors before allocating an operation key',async()=>{
 await render();current.etag=tag('2');current.controlRevision='2';await reason();await click('添加本域封锁');expect(mock.command).not.toHaveBeenCalled();expect(pendingControl(scope)).toBeNull();expect(view!.text()).toContain('已变化')
})
it('rejects changed response identity and preserves recovery on mismatched results',async()=>{
 await render();mock.command.mockImplementation((_l,_r,_e,b)=>Promise.resolve(ok({operationId:b.operationId,locationId:id(99),effect:'BLOCK'})))
 await reason();await click('添加本域封锁');expect(pendingControl(scope)).toBeTruthy();expect(view!.text()).toContain('不一致')
 mock.operation.mockResolvedValue(ok({operationId:pendingControl(scope),locationId:id(99),state:'CLOSED'}));await click('查询原操作');expect(pendingControl(scope)).toBeTruthy()
})
it('clears private reasons on permission or authentication change and discards late reads',async()=>{
 await render();await reason();await click('添加本域封锁');expect(view!.text()).toContain('现场限制已核对')
 mock.permissions.read=false;await flushPromises();expect(view!.find('section').exists()).toBe(false)
 mock.permissions.read=true;await flushPromises();let resolve!:(v:any)=>void;mock.read.mockImplementation(()=>new Promise(r=>{resolve=r}))
 await view!.findAll('button').find(b=>b.text()==='重新载入')!.trigger('click');await flushPromises()
 localStorage.setItem(AUTH_EPOCH,'two');window.dispatchEvent(new Event(AUTH_EVENT));resolve(ok(structuredClone(current)));await flushPromises();expect(view!.find('section').exists()).toBe(false)
})
it('requires a browser lock before allocating a command',async()=>{
 await render();Object.defineProperty(navigator,'locks',{configurable:true,value:undefined});await reason();await click('添加本域封锁');expect(mock.command).not.toHaveBeenCalled();expect(pendingControl(scope)).toBeNull()
})

it('disables stale release decisions after another control change',async()=>{
 await render();await reason();await click('添加本域封锁');await reason();await click('记录此项解除决定');await reason();await click('添加本域封锁')
 expect(view!.findAll('button').find(b=>b.text()==='按此决定解除')!.attributes('disabled')).toBeDefined()
})
it('keeps the newly saved decision usable when history pagination omits it',async()=>{
 await render();await reason();await click('添加本域封锁')
 mock.read.mockImplementation(()=>Promise.resolve(ok({...structuredClone(current),entries:current.entries.filter(e=>e.original.kind!=='RELEASE_DECISION')})))
 await reason();await click('记录此项解除决定');await reason();await click('应用本次解除决定');expect(current.openBlocks).toBe(0)
})

"""Run the actual built CP6.WebApi with an owned PG authorization fixture.

No substitute host, ALC, package publication, application migrations or deployment.
Private settings/tokens stay under the explicit owner's ACL-protected directory.
"""
import argparse
import base64
import copy
import datetime
import hashlib
import hmac
import json
import os
from pathlib import Path
import re
import secrets
import socket
import subprocess
import time
import urllib.error
import urllib.request
import uuid
import zipfile
import pg_resource as pg

ROOT = Path(__file__).resolve().parents[2]
DOTNET = Path('C:/Users/tt/.dotnet/dotnet.exe')
DLL = ROOT/'CP6.WebApi/bin/Release/net8.0/CP6.WebApi.dll'
PACKAGE_IDS = ['CP6.Platform.'+n for n in ('Abstractions','AspNetCore','Contracts','EntityFramework','Messaging')]
ENDPOINT = '/health/platform-identity'


def digest(raw): return hashlib.sha256(raw).hexdigest()
def write(path, value): path.write_text(json.dumps(value, indent=2)+'\n', encoding='utf-8')


def expectations():
    publication = json.loads((ROOT/'docs/development/evidence/authorized-adoption-release-20261015/publication/formal-package-publication.v1.json').read_bytes())
    assets = json.loads((ROOT/'CP6.WebApi/obj/project.assets.json').read_bytes())
    cache = Path(next(iter(assets['packageFolders'])))
    expected = []
    for name in PACKAGE_IDS:
        published = next(p for p in publication['packages'] if p['packageId'] == name)
        folder = cache/name.lower()/'0.10.3'
        archive = folder/(name.lower()+'.0.10.3.nupkg')
        pg.require(digest(archive.read_bytes()) == published['publishedPackageSha256'], 'Archive differs from formal publication')
        with zipfile.ZipFile(archive) as z:
            raw = z.read('lib/net8.0/'+name+'.dll')
        pg.require(raw == (DLL.parent/(name+'.dll')).read_bytes(), 'Host output differs from formal package DLL')
        expected.append({'PackageId': name, 'PackageVersion':'0.10.3',
                         'SourceSha':published['sourceGitSha'], 'AssemblySha256':digest(raw)})
    return expected


def token(key, user, *, admin=True, impersonating=False):
    # Test principal only: real Main JWT validation uses this task's ephemeral signing key.
    body = {'iss':'CP6','aud':'CP6.Web','sub':user,'exp':int(time.time())+600,
            'tenant_id':'11111111-1111-4111-8111-111111111111'}
    if admin: body['is_platform_admin'] = 'true'
    if impersonating: body['impersonator_id'] = str(uuid.uuid4())
    enc = lambda value: base64.urlsafe_b64encode(json.dumps(value,separators=(',',':')).encode()).rstrip(b'=')
    value = enc({'alg':'HS256','typ':'JWT'})+b'.'+enc(body)
    signature = base64.urlsafe_b64encode(hmac.new(key.encode(),value,hashlib.sha256).digest()).rstrip(b'=')
    return (value+b'.'+signature).decode()


def request(url, bearer=None, headers=None):
    headers = dict(headers or {})
    if bearer: headers['Authorization'] = 'Bearer '+bearer
    try:
        response = urllib.request.urlopen(urllib.request.Request(url,headers=headers),timeout=12)
    except urllib.error.HTTPError as error:
        response = error
    with response:
        raw = response.read()
        try: body = json.loads(raw)
        except (ValueError,UnicodeDecodeError): body = None
        return response.status, dict(response.headers), body, raw


def process_identity(pid):
    script = f"$p=Get-Process -Id {pid} -ErrorAction Stop; $c=Get-CimInstance Win32_Process -Filter 'ProcessId = {pid}'; @{{Pid=$p.Id;Started=$p.StartTime.ToUniversalTime().ToString('O');Executable=$p.Path;Command=$c.CommandLine;Ports=@(Get-NetTCPConnection -State Listen -OwningProcess {pid} -ErrorAction SilentlyContinue | Select-Object LocalAddress,LocalPort)}} | ConvertTo-Json -Depth 4"
    return json.loads(subprocess.check_output(['pwsh','-NoProfile','-Command',script],creationflags=pg.NO_WINDOW))


def main():
    parser=argparse.ArgumentParser();parser.add_argument('--owner',required=True);parser.add_argument('--settings',required=True)
    args=parser.parse_args();c=pg.load_settings(args.settings)
    pg.require(c['Owner']==args.owner,'Explicit owner mismatch');owned=pg.verify(c)
    run=Path(c['RunDirectory'])/('host-'+datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%S%fZ'))
    run.mkdir();key=secrets.token_hex(32);user=str(uuid.uuid4());expected=expectations()
    results={'owner':c['Owner'],'sourceBaseline':subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT).decode().strip(),
             'hostDllSha256':digest(DLL.read_bytes()),'expectedFromFormalArchives':expected,
             'scope':'actual Main process; minimal owned authorization tables only, no migrated application/schema compatibility claim',
             'checks':[],'processes':[]}
    def check(name, condition, evidence=None):
        results['checks'].append({'name':name,'passed':bool(condition),'evidence':evidence})
        write(run/'results.json',results)
        pg.require(condition,'Host check failed: '+name)
    # No fake migrations or full business schema. The existing admin gate needs only these columns.
    pg.sql(c,'CREATE TABLE IF NOT EXISTS public."Sys_Users" ("Id" uuid PRIMARY KEY,"IsPlatformAdmin" boolean NOT NULL,"Enable" boolean NOT NULL);'
           f'INSERT INTO public."Sys_Users" VALUES (\'{user}\',true,true);'
           f'GRANT USAGE ON SCHEMA public TO "{c["Runtime"]}"; GRANT SELECT ON public."Sys_Users" TO "{c["Runtime"]}";'
           f'ALTER ROLE "{c["Runtime"]}" SET default_transaction_read_only=on;')
    config=json.loads((ROOT/'CP6.WebApi/appsettings.json').read_bytes())
    config.update({'Startup':{'SkipDatabaseInitialization':True,'SkipHostedServices':True},
                   'Database':{'Provider':'PostgreSql'}, 'JWT':{'Issuer':'CP6','Audience':'CP6.Web','Secret':key},
                   'ConnectionStrings':{'DefaultConnection':f'Host=127.0.0.1;Port={pg.PORT};Database={c["Database"]};Username={c["Runtime"]};Password={c["RuntimePassword"]};Timeout=3', 'Redis':''},
                   'CrmOidc':{'Enabled':False},'CrmIdentity':{'Enabled':False},'ErpIntegration':{'Enabled':False},
                   'SpaceObservability':{'MetricsEnabled':False},'OperLog':{'IncludeGet':False},
                   'Storage':{'Provider':'Local','LocalRoot':str(run/'uploads')},
                   'Release':{'Version':'fixture-declared','GitSha':'fixture-declared','ApiImageDigest':'unknown','WebImageDigest':'unknown'},
                   'PlatformHostIdentity':{'ExpectedModules':expected}})
    for scenario in ('matching','mismatch','unknown'):
        hostroot=run/scenario;hostroot.mkdir();cfg=copy.deepcopy(config)
        if scenario=='mismatch': cfg['PlatformHostIdentity']['ExpectedModules'][1]['AssemblySha256']='0'*64
        if scenario=='unknown': cfg.pop('PlatformHostIdentity')
        write(hostroot/'appsettings.json',cfg)
        # Clear inherited configuration overrides; no user appsettings.Local or production environment is consulted.
        env={k:v for k,v in os.environ.items() if not ('__' in k or k.startswith(('ASPNETCORE_','DOTNET_','ConnectionStrings','Startup','PlatformHostIdentity','JWT')))}
        env.update(ASPNETCORE_ENVIRONMENT='Development',DOTNET_ENVIRONMENT='Development',ASPNETCORE_URLS='http://127.0.0.1:0',DOTNET_NOLOGO='1')
        out=open(hostroot/'stdout.log','wb');err=open(hostroot/'stderr.log','wb')
        process=subprocess.Popen([str(DOTNET),str(DLL),'--contentRoot',str(hostroot)],cwd=hostroot,env=env,stdout=out,stderr=err,creationflags=pg.NO_WINDOW)
        identity=None;port=None
        try:
            identity=process_identity(process.pid)
            pg.require(Path(identity['Executable']).resolve()==DOTNET.resolve() and str(DLL).replace('\\','/') in identity['Command'].replace('\\','/'),'Host process identity mismatch')
            for _ in range(150):
                pg.require(process.poll() is None,'Main host exited before listening; private logs retained')
                text=(hostroot/'stdout.log').read_text(encoding='utf-8-sig',errors='replace')
                match=re.search(r'Now listening on: http://127\.0\.0\.1:(\d+)',text)
                if match: port=int(match.group(1));break
                time.sleep(.2)
            pg.require(port is not None,'Main startup timed out')
            native=process_identity(process.pid)
            check(scenario+'-owned-listener',native['Ports']==[{'LocalAddress':'127.0.0.1','LocalPort':port}])
            url='http://127.0.0.1:'+str(port)
            bearer=token(key,user)
            if scenario=='matching':
                for label,auth,extra,status in (
                    ('anonymous',None,None,401),('invalid-signature',token(secrets.token_hex(32),user),None,401),
                    ('tenant-admin',token(key,user,admin=False),None,403),
                    ('forged-admin-header',token(key,user,admin=False),{'is_platform_admin':'true','X-Platform-Admin':'true'},403),
                    ('impersonated-admin',token(key,user,impersonating=True),None,403)):
                    code,headers,body,raw=request(url+ENDPOINT,auth,extra)
                    check(label+'-denied',code==status and headers.get('Cache-Control')=='no-store' and b'assemblySha256' not in raw,{'http':code})
                    code,headers,body,raw=request(url+ENDPOINT+'/',auth,extra)
                    check(label+'-trailing-slash-denied',code==status and headers.get('Cache-Control')=='no-store' and b'assemblySha256' not in raw,{'http':code})
            code,headers,body,raw=request(url+ENDPOINT,bearer)
            check(scenario+'-status',code==200 and headers.get('Cache-Control')=='no-store',{'http':code})
            check(scenario+'-actual-pid',body['processId']==process.pid)
            if scenario=='matching':
                loaded=[m for m in body['modules'] if m['observed'] is not None]
                unloaded=[m for m in body['modules'] if m['observed'] is None]
                check('matching-loaded-identity',bool(loaded) and all(m['status']=='Matched' for m in loaded)
                      and all(m['status']=='Unknown' and 'ASSEMBLY_NOT_LOADED' in m['reasonCodes'] for m in unloaded)
                      and body['identityStatus']==('Unknown' if unloaded else 'Matched'),body)
            else:
                check(scenario+'-identity',body['identityStatus']=={'mismatch':'Mismatch','unknown':'Unknown'}[scenario],body)
            check(scenario+'-rollback-refused',body['rollback']['allowed'] is False)
            check(scenario+'-no-sensitive-output',all(s.encode() not in raw for s in (key,c['RuntimePassword'],c['AdminPassword'],str(run),c['Database'])))
            check(scenario+'-no-business-trust-claim',all(body[k]=='Unknown' for k in ('runtimeQualification','businessAcceptance','trustState')))
            if scenario=='matching':
                code,headers,alternate,_=request(url+ENDPOINT+'/',bearer)
                check('trailing-slash-authorized-no-store',code==200 and headers.get('Cache-Control')=='no-store'
                      and alternate['processId']==process.pid and alternate['rollback']['allowed'] is False)
                code,headers,health,_=request(url+'/health/release')
                check('legacy-release-contract',code==200 and headers.get('Cache-Control')=='no-store' and set(health)=={'version','gitSha','apiImageDigest','webImageDigest','latestMigration'} and health['latestMigration'] is None,health)
                for label,column in (('revoked','IsPlatformAdmin'),('disabled','Enable')):
                    pg.sql(c,f'UPDATE public."Sys_Users" SET "{column}"=false WHERE "Id"=\'{user}\';')
                    code,headers,_,raw=request(url+ENDPOINT,bearer)
                    check(label+'-same-token-denied',code==403 and headers.get('Cache-Control')=='no-store' and b'assemblySha256' not in raw)
                    pg.sql(c,f'UPDATE public."Sys_Users" SET "{column}"=true WHERE "Id"=\'{user}\';')
                pg.sql(c,f'REVOKE SELECT ON public."Sys_Users" FROM "{c["Runtime"]}";')
                try:
                    code,headers,problem,raw=request(url+ENDPOINT,bearer)
                    check('unavailable-authority-fails-closed',code==503 and problem=={'code':'IDENTITY_AUTHORITY_UNAVAILABLE'}
                          and headers.get('Cache-Control')=='no-store')
                finally:
                    pg.sql(c,f'GRANT SELECT ON public."Sys_Users" TO "{c["Runtime"]}";')
        finally:
            if process.poll() is None:
                current=process_identity(process.pid)
                pg.require(identity is not None and current['Started']==identity['Started'] and current['Command']==identity['Command'],'Refuse stop: process identity changed')
                process.terminate();process.wait(timeout=15)
            out.close();err.close()
            if port:
                with socket.socket() as s:pg.require(s.connect_ex(('127.0.0.1',port))!=0,'Owned HTTP port still listening')
            results['processes'].append({'scenario':scenario,'pid':process.pid,'started':identity['Started'] if identity else None,'port':port,'stopped':True})
            write(run/'results.json',results)
    results['completed']=True;results['databaseIdentity']={k:owned[k] for k in ('SystemIdentifier','Database','Oid','Owner','Port')}
    write(run/'results.json',results)
    print('Actual Main host checks passed; self-owned processes stopped; results: '+str(run/'results.json'))


if __name__=='__main__':main()

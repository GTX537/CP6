"""CP6 task/seat lifecycle. Stdlib only; no daemon, automatic expiry or force cleanup.

Policy is versioned in CP6; mutable leases share CP6's common Git directory.
Raw git, filesystem copies and desktop worktree creation do not pass this gate.
"""
import argparse
import contextlib
import datetime
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import time

import inventory


class Refusal(RuntimeError):
    def __init__(self, code, message):
        self.code = code
        super().__init__(f'{code}: {message}')


def require(ok, code, message):
    if not ok:
        raise Refusal(code, message)


def now():
    return datetime.datetime.now(datetime.timezone.utc).isoformat()


@contextlib.contextmanager
def exclusive(path):
    path.parent.mkdir(parents=True, exist_ok=True)
    with open(path, 'a+b') as f:
        if f.tell() == 0:
            f.write(b'0'); f.flush()
        deadline = time.monotonic() + 30
        while True:
            try:
                f.seek(0)
                if os.name == 'nt':
                    import msvcrt
                    msvcrt.locking(f.fileno(), msvcrt.LK_NBLCK, 1)
                else:
                    import fcntl
                    fcntl.flock(f, fcntl.LOCK_EX | fcntl.LOCK_NB)
                break
            except OSError:
                require(time.monotonic() < deadline, 'LOCKED', 'another lifecycle operation is running; retry later')
                time.sleep(.1)
        try:
            yield
        finally:
            f.seek(0)
            if os.name == 'nt':
                msvcrt.locking(f.fileno(), msvcrt.LK_UNLCK, 1)
            else:
                fcntl.flock(f, fcntl.LOCK_UN)


class Manager:
    def __init__(self, policy, state_path, backend):
        self.policy, self.path, self.git = policy, Path(state_path), backend

    def read(self):
        if self.path.exists():
            state = json.loads(self.path.read_text(encoding='utf-8'))
            require(state['schema_version'] == 2, 'STATE', 'unsupported runtime schema; do not delete leases')
            return state
        return dict(schema_version=2, tasks={}, slots={s['id']: dict(s, task=None) for s in self.policy['slots']})

    def write(self, state):
        state['updated_at'] = now()
        tmp = self.path.with_suffix('.new')
        with open(tmp, 'w', encoding='utf-8', newline='\n') as f:
            json.dump(state, f, ensure_ascii=False, indent=2); f.write('\n'); f.flush(); os.fsync(f.fileno())
        os.replace(tmp, self.path)

    def layout(self, state):
        rows = self.git.layout(self.policy, state['slots'])
        seats = {inventory.norm(s['path']): s for s in state['slots'].values()}
        nonprimary = [r for r in rows if not r['primary']]
        dev = sum(seats.get(inventory.norm(r['path']), {}).get('kind') != 'integration' for r in nonprimary)
        integ = len(nonprimary) - dev
        limits = self.policy['limits']
        require(dev <= limits['development_worktrees'] and integ <= limits['integration_worktrees'],
                'QUOTA', f'actual development={dev}, integration={integ}; family limit={limits}')
        require(all(inventory.norm(r['path']) in seats for r in nonprimary), 'UNREGISTERED', 'worktree outside fixed seats')
        for key, s in seats.items():
            matches = [r for r in nonprimary if inventory.norm(r['path']) == key]
            require(len(matches) == 1 and matches[0]['repository'] == s['repository'], 'REPOSITORY', f'invalid seat: {s["id"]}')
        return rows

    def clean(self, path):
        ws = self.git.workspace(path)
        require(not ws['dirty'] and not ws['local_files'], 'LOCAL_FILES',
                f'preserve/resolve before switching: {ws["dirty"][:8]} {ws["local_files"][:8]}')
        require(not ws['processes'], 'PROCESSES', f'stop only task-owned processes first: {ws["processes"]}')
        return ws

    def start(self, repository, slot, task, owner, branch, target, baseline, dependencies, source=None):
        require(bool(re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9_.-]{1,79}', task)), 'TASK', 'use a stable task ID')
        branch = branch or 'codex/' + task.lower()
        require(branch == 'codex/' + task.lower(), 'TASK_BRANCH', 'one canonical branch per task/repository')
        key = repository + ':' + task
        with exclusive(self.path.with_suffix('.lock')):
            state = self.read(); self.layout(state)
            require(slot in state['slots'], 'SLOT', 'unknown seat')
            seat = state['slots'][slot]
            verification = seat['kind'] == 'integration'
            require(not source or verification, 'SOURCE', 'source is only for detached integration verification')
            require(seat['repository'] == repository, 'REPOSITORY', 'replace the free seat before cross-repository work')
            require(not seat['task'], 'OCCUPIED', str(seat['task']))
            require(key not in state['tasks'], 'EXISTING_TASK', 'use resume; do not create another task branch')
            ws = self.clean(seat['path'])
            require(ws['repository'] == repository, 'REPOSITORY', seat['path'])
            base = self.git.target(repository, target)
            require(baseline is None or baseline == base, 'BASELINE', f'actual target is {base}')
            require(self.git.saved(repository, ws['head']), 'UNSAVED_HEAD', ws['head'])
            local, remote = self.git.tips(repository)
            duplicates = [b for b in set(local) | set(remote) if b == branch or b.startswith(branch + '-') or b.startswith(branch + '/')]
            if not verification:
                require(not duplicates and not self.git.prs(repository, branch), 'DUPLICATE_BRANCH', str(duplicates))
            source = source or base
            require(bool(re.fullmatch('[0-9a-f]{40}', source)) and (source == base or self.git.saved(repository, source)), 'UNSAVED_HEAD', source)
            current = self.clean(seat['path'])
            require(current['head'] == ws['head'] and current['branch'] == ws['branch'],
                    'HEAD_CHANGED', 'workspace changed during admission; inspect it before retrying')
            record = dict(task=task, repository=repository, owner=owner, branch=branch, workspace=seat['path'],
                          slot=slot, target=target, base=base, status='starting', pr=None, dependencies=dependencies,
                          close_conditions='validation + review + saved disposition + processes stopped + branch/PR disposal + release',
                          created_at=now(), updated_at=now())
            if verification: record.update(branch=None, mode='verification', source=source)
            state['tasks'][key] = record; seat['task'] = key; self.write(state)
            # A crash here keeps the starting lease. Never automatically expire or steal it.
            if verification: self.git.detach(seat['path'], source)
            else: self.git.checkout(seat['path'], branch, base)
            record['status'] = 'active'; self.write(state)
            return record

    def status(self):
        with exclusive(self.path.with_suffix('.lock')):
            return self.read()

    def record_pr(self, repository, task, owner, pr):
        with exclusive(self.path.with_suffix('.lock')):
            state = self.read(); key = repository + ':' + task
            require(key in state['tasks'], 'TASK', key)
            r = state['tasks'][key]
            require(r['owner'] == owner and state['slots'][r['slot']]['task'] == key, 'OWNER', 'not the active task')
            p = self.git.gh(repository, 'pr', 'view', str(pr), '--json', 'headRefName,headRefOid,baseRefName,url')
            ws = self.git.workspace(r['workspace'])
            require(p['headRefName'] == r['branch'] and p['headRefOid'] == ws['head'] and p['baseRefName'] == r['target'], 'PR', 'PR/source/target mismatch')
            r.update(pr=pr, pr_url=p['url'], updated_at=now()); self.write(state)
            return r

    def resume(self, repository, task, owner, head, reason):
        require(bool(reason), 'REASON', 'record why this interrupted/blocked task resumes')
        with exclusive(self.path.with_suffix('.lock')):
            state = self.read(); self.layout(state); key = repository + ':' + task
            require(key in state['tasks'], 'TASK', key)
            record = state['tasks'][key]; seat = state['slots'][record['slot']]
            require(record['owner'] == owner, 'OWNER', 'coordinate an explicit handoff; no automatic takeover')
            require(seat['task'] in (None, key) and seat['repository'] == repository, 'OCCUPIED', 'seat changed or occupied')
            if record.get('mode') == 'verification':
                ws = self.git.workspace(seat['path'])
                require(seat['task'] == key and not ws['branch'] and ws['head'] == head == record['source'],
                        'HEAD_CHANGED', 'inspect interrupted verification before resuming')
                record.update(status='active', resume_reason=reason, updated_at=now()); self.write(state)
                return record
            local, remote = self.git.tips(repository)
            require(local.get(record['branch'], remote.get(record['branch'])) == head, 'HEAD_CHANGED', 'review actual saved branch')
            if seat['task'] is None:
                require(record['status'] == 'deferred', 'CLOSED', 'completed/cancelled tasks cannot silently restart')
                ws = self.clean(seat['path'])
                require(self.git.saved(repository, ws['head']), 'UNSAVED_HEAD', ws['head'])
                seat['task'] = key; record['status'] = 'resuming'; self.write(state)
                self.git.resume_branch(seat['path'], record['branch'], head)
            else:
                ws = self.git.workspace(seat['path'])
                require(ws['head'] == head and ws['branch'] == record['branch'], 'HEAD_CHANGED', 'inspect interrupted checkout first')
            record.update(status='active', resume_reason=reason, updated_at=now()); self.write(state)
            return record

    def finish(self, repository, task, owner, head, outcome, validation, review, report, pr,
               adopted_commit, reason, next_action, dependencies_reviewed):
        require(validation and review and report, 'EVIDENCE', 'validation, review and durable delivery reference required')
        require(dependencies_reviewed, 'DEPENDENCIES', 'confirm undeclared consumers have been checked')
        with exclusive(self.path.with_suffix('.lock')):
            state = self.read(); self.layout(state); key = repository + ':' + task
            require(key in state['tasks'], 'TASK', key)
            r = state['tasks'][key]; seat = state['slots'][r['slot']]
            require(r['owner'] == owner and seat['task'] == key, 'OWNER', 'not the current task lease')
            ws = self.clean(seat['path'])
            verification = r.get('mode') == 'verification'
            require((ws['branch'] == (r['branch'] or '') and ws['head'] == head) or
                    (r['status'] == 'closing' and not ws['branch'] and r.get('finished_head') == head),
                    'HEAD_CHANGED', 'finish must name the exact reviewed task head')
            require(not any(key in t.get('dependencies', []) and t['status'] not in ('merged','cancelled','superseded','verified')
                            for t in state['tasks'].values()), 'DEPENDENCIES', 'another registered task depends on this task')
            target = self.git.target(repository, r['target'])
            local, remote = self.git.tips(repository)
            require(local.get(r['branch'], head) == head and remote.get(r['branch'], head) == head,
                    'REMOTE_CHANGED', 'local/remote branch has newer work; review it first')
            if verification:
                require(outcome == 'verified' and head == r['source'] and self.git.saved(repository, head),
                        'VERIFICATION', 'exact saved source and verified outcome required; no feature branch is created')
                proof = dict(method='verification-of-saved-source', head=head)
            elif outcome == 'merged':
                proof = self.git.adoption(r, head, pr, adopted_commit)
                require(not self.git.dependent_branches(r, head), 'DEPENDENCIES', 'branch/worktree/PR dependency still exists')
            else:
                require(outcome in ('deferred','cancelled','superseded') and reason and next_action,
                        'DISPOSITION', 'concrete reason and recovery/next action required')
                require(remote.get(r['branch']) == head, 'UNSAVED_HEAD', 'push and read back the exact task head first')
                proof = dict(method='saved-remote-branch', ref=r['branch'], head=head)
                require(not any(p['state'] == 'OPEN' for p in self.git.prs(repository, r['branch'])),
                        'OPEN_PR', 'close with disposition or merge the PR before releasing')
            r.update(status='closing', finished_head=head, outcome=outcome, validation=validation, review=review,
                     report=report, pr=pr, proof=proof, reason=reason, next_action=next_action, updated_at=now())
            self.write(state)
            self.git.detach(seat['path'], target)
            self.clean(seat['path'])
            if outcome == 'merged':
                self.git.delete_branch(r, head)
            r.update(status=outcome, branch_disposition='no-task-branch' if verification else ('deleted-local-and-remote' if outcome == 'merged' else 'retained-remote'),
                     workspace_status='fixed-seat-detached-for-reuse', seat_released=True, updated_at=now())
            seat['task'] = None; self.write(state)
            return r

    def bind(self, slot, repository, owner):
        with exclusive(self.path.with_suffix('.lock')):
            state = self.read(); self.layout(state)
            require(slot in state['slots'], 'SLOT', slot)
            seat = state['slots'][slot]
            require(not seat['task'], 'OCCUPIED', str(seat['task']))
            require(not seat.get('replacement'), 'INTERRUPTED_BIND', 'inspect recorded replacement and actual Git state; no automatic takeover')
            require(repository in self.git.roots, 'REPOSITORY', repository)
            if seat['repository'] == repository:
                return seat
            ws = self.clean(seat['path'])
            require(not ws.get('retained_caches'), 'RETAINED_CACHE', 'inspect and preserve reusable caches before removing a seat')
            require(self.git.saved(seat['repository'], ws['head']), 'UNSAVED_HEAD', ws['head'])
            newpath = seat.get('replacement_path', seat['path'])
            require(inventory.norm(newpath) == inventory.norm(seat['path']) or not Path(newpath).exists(),
                    'PATH_EXISTS', newpath)
            base = self.git.target(repository, self.git.default_target(repository))
            current = self.clean(seat['path'])
            require(current['head'] == ws['head'] and current['branch'] == ws['branch'],
                    'HEAD_CHANGED', 'workspace changed during replacement checks')
            old = dict(seat)
            seat['replacement'] = dict(repository=repository, path=newpath, base=base, started_at=now(), owner=owner)
            self.write(state)
            # Normal remove only. No force, recursive shell delete, copying or transient extra seat.
            self.git.replace(old, repository, newpath, base)
            seat.update(repository=repository, path=newpath, owner=owner, task=None)
            seat.pop('replacement'); self.write(state)
            return seat


class GitBackend:
    def __init__(self, policy):
        self.policy = policy
        self.roots = {p['repository']: p['path'] for p in policy['primary_checkouts']}
        for repo, path in self.roots.items():
            require(inventory.repository(inventory.git(path, 'remote', 'get-url', 'origin')) == repo,
                    'REPOSITORY', f'canonical origin mismatch: {path}')

    def default_target(self, repo):
        return next(p.get('target', 'main') for p in self.policy['primary_checkouts'] if p['repository'] == repo)

    def layout(self, policy, bindings):
        records, _, errors = inventory.discover(list(self.roots.values()), policy.get('scan_containers', inventory.DEFAULT_CONTAINERS))
        require(not any('error' in e for e in errors), 'SCAN_ERROR', str(errors)[:500])
        result = []
        for r in records:
            primary = r['is_primary']
            require(not primary or inventory.norm(r['path']) == inventory.norm(self.roots[r['repository']]),
                    'EXTRA_CLONE', r['path'])
            require(Path(r['path']).exists(), 'MISSING_TREE', r['path'])
            result.append(dict(path=r['path'], repository=r['repository'], primary=primary))
        return result

    def workspace(self, path):
        require(not Path(path).is_symlink() and not getattr(Path(path), 'is_junction', lambda: False)(), 'LINK', path)
        origin = inventory.repository(inventory.git(path, 'remote', 'get-url', 'origin'))
        ignored = inventory.git(path, 'ls-files', '--others', '--ignored', '--exclude-standard', '--directory', '-z').split('\0')
        # Only dependency caches are retained by default. Unknown ignored config/output blocks reuse.
        caches = [x for x in ignored if x and x.rstrip('/').split('/')[-1] == 'node_modules']
        ignored = [x for x in ignored if x and x not in caches]
        code = 'Get-CimInstance Win32_Process | Select-Object ProcessId,ParentProcessId,Name,CommandLine,ExecutablePath | ConvertTo-Json -Compress'
        p = subprocess.run(['powershell', '-NoProfile', '-Command', code], capture_output=True, text=True, encoding='utf-8', timeout=60)
        require(p.returncode == 0, 'PROCESS_SCAN', 'could not inspect processes')
        rows = json.loads(p.stdout or '[]'); rows = rows if isinstance(rows, list) else [rows]
        byid = {x['ProcessId']: x for x in rows}; excluded = {os.getpid()}; cur = os.getpid()
        while cur in byid and byid[cur]['ParentProcessId'] not in excluded:
            cur = byid[cur]['ParentProcessId']; excluded.add(cur)
        process = [dict(pid=x['ProcessId'], name=x['Name']) for x in rows if x['ProcessId'] not in excluded
                   and inventory.norm(path) in ((x.get('CommandLine') or '')+' '+(x.get('ExecutablePath') or '')).replace('\\','/').casefold()]
        return dict(repository=origin, head=inventory.git(path, 'rev-parse', 'HEAD'),
                    branch=inventory.git(path, 'branch', '--show-current'),
                    dirty=[x for x in inventory.git(path, 'status', '--porcelain=v1', '-z', '--untracked-files=all').split('\0') if x],
                    local_files=ignored, retained_caches=caches, processes=process)

    def target(self, repo, target):
        require(target == self.default_target(repo), 'TARGET', 'use the registered target; changes need coordinator review')
        root = self.roots[repo]
        inventory.git(root, 'fetch', '--no-tags', 'origin', target)
        actual = inventory.git(root, 'ls-remote', 'origin', 'refs/heads/'+target).split()[0]
        require(inventory.git(root, 'rev-parse', 'refs/remotes/origin/'+target) == actual, 'REMOTE_CHANGED', 'target moved during fetch')
        return actual

    def tips(self, repo):
        root = self.roots[repo]
        local = dict(line.split(' ',1) for line in inventory.git(root, 'for-each-ref', '--format=%(refname:strip=2) %(objectname)', 'refs/heads').splitlines())
        remote = {line.split()[1].removeprefix('refs/heads/'): line.split()[0] for line in inventory.git(root, 'ls-remote', '--heads', 'origin').splitlines()}
        return local, remote

    def saved(self, repo, head):
        _, remote = self.tips(repo)
        if head in remote.values():
            return True
        root = self.roots[repo]
        containing = set(inventory.git(root, 'for-each-ref', '--contains='+head, '--format=%(objectname)', 'refs/remotes/origin').splitlines())
        return bool(containing & set(remote.values()))

    def gh(self, repo, *args):
        r = subprocess.run(['gh', *args, '--repo', 'GTX537/'+repo], capture_output=True, text=True, encoding='utf-8', timeout=120)
        require(r.returncode == 0, 'GITHUB', r.stderr[:400])
        return json.loads(r.stdout or 'null')

    def prs(self, repo, branch):
        return self.gh(repo, 'pr', 'list', '--head', branch, '--state', 'all', '--limit', '100', '--json', 'number,state,headRefName,headRefOid,baseRefName,url')

    def checkout(self, path, branch, base):
        inventory.git(path, 'switch', '--no-track', '-c', branch, base)

    def resume_branch(self, path, branch, head):
        if inventory.git(path, 'show-ref', '--verify', 'refs/heads/'+branch, check=False).returncode:
            inventory.git(path, 'fetch', '--no-tags', 'origin', 'refs/heads/'+branch+':refs/heads/'+branch)
        require(inventory.git(path, 'rev-parse', 'refs/heads/'+branch) == head, 'HEAD_CHANGED', branch)
        inventory.git(path, 'switch', branch)

    def adoption(self, task, head, pr, adopted_commit):
        root = self.roots[task['repository']]; target = self.target(task['repository'], task['target'])
        require(pr is not None, 'PR', 'normal reviewed PR required; no direct-main bypass')
        p = self.gh(task['repository'], 'pr', 'view', str(pr), '--json', 'state,headRefOid,headRefName,baseRefName,mergeCommit,url')
        require(p['headRefOid'] == head and p['headRefName'] == task['branch'] and p['baseRefName'] == task['target'], 'PR', 'PR does not match exact task head/target')
        merge = (p.get('mergeCommit') or {}).get('oid')
        if p['state'] == 'MERGED' and merge and inventory.git(root, 'merge-base', '--is-ancestor', merge, target, check=False).returncode == 0:
            return dict(method='GitHub-merged-PR-in-remote-target (merge/squash/rebase)', pr=p['url'], commit=merge, target_head=target)
        # Cherry-pick adoption requires a closed PR and exact aggregate patch equality.
        require(p['state'] == 'CLOSED' and adopted_commit, 'NOT_ADOPTED', 'PR not merged; provide closed-PR cherry-pick evidence')
        require(inventory.git(root, 'merge-base', '--is-ancestor', adopted_commit, target, check=False).returncode == 0, 'NOT_ADOPTED', adopted_commit)
        def patchid(before, after):
            diff = inventory.git(root, 'diff', '--binary', before, after)
            out = subprocess.run(['git', 'patch-id', '--stable'], input=diff, text=True, capture_output=True, encoding='utf-8', check=True).stdout.split()
            return out[0] if out else None
        task_patch = patchid(task['base'], head)
        require(task_patch and task_patch == patchid(adopted_commit+'^', adopted_commit), 'NOT_ADOPTED', 'patch differs; explicit content review/fix required')
        return dict(method='closed-PR-exact-aggregate-patch', commit=adopted_commit, target_head=target)

    def dependent_branches(self, task, head):
        root = self.roots[task['repository']]; branch = task['branch']; found = []
        for t in inventory.registered(root):
            if t.get('branch') == 'refs/heads/'+branch and inventory.norm(t['worktree']) != inventory.norm(task['workspace']):
                found.append(t['worktree'])
        found.extend('PR#'+str(p['number']) for p in self.gh(task['repository'], 'pr', 'list', '--state', 'open', '--limit', '1000', '--json', 'number,baseRefName,headRefName') if p['baseRefName'] == branch or p['headRefName'] == branch)
        # Ancestor relationships alone include merged main and archives. Explicit upstream
        # dependencies are actionable; other consumers must also be reviewed by the owner.
        for line in inventory.git(root, 'for-each-ref', '--format=%(refname:short) %(upstream:short)', 'refs/heads').splitlines():
            cols = line.split()
            if len(cols) == 2 and cols[0] != branch and cols[1] in (branch, 'origin/'+branch):
                found.append(cols[0])
        return found

    def detach(self, path, target):
        inventory.git(path, 'switch', '--detach', target)

    def delete_branch(self, task, head):
        repo, branch = task['repository'], task['branch']; root = self.roots[repo]
        local, remote = self.tips(repo)
        require(local.get(branch, head) == head and remote.get(branch, head) == head, 'REMOTE_CHANGED', branch)
        if branch in remote:
            self.events_safe(root, head, task['target'])
            # Conditional ref deletion: expected SHA lease prevents deleting a raced push.
            # This does not authorize force-updating any commit history or protected branch.
            proxy = ['-c', 'http.proxy='+os.environ['CP6_GIT_PUSH_PROXY']] if os.environ.get('CP6_GIT_PUSH_PROXY') else []
            result = inventory.git(root, *proxy, 'push', '--no-follow-tags', '--force-with-lease=refs/heads/'+branch+':'+head,
                                   'origin', ':refs/heads/'+branch, check=False)
            require(result.returncode == 0, 'DELETE_FAILED', result.stderr[:500])
            require(branch not in self.tips(repo)[1], 'REMOTE_CHANGED', 'remote branch still exists')
        if branch in local:
            inventory.git(root, 'update-ref', '-d', 'refs/heads/'+branch, head)
        require(inventory.git(root, 'show-ref', '--verify', '--quiet', 'refs/heads/'+branch, check=False).returncode == 1,
                'LOCAL_CHANGED', 'task branch reappeared during cleanup')
        config = inventory.git(root, 'config', '--local', '--get-regexp', '^branch\\.'+re.escape(branch)+r'\.', check=False)
        require(config.returncode in (0, 1), 'CONFIG', 'could not inspect this task branch configuration')
        if config.returncode == 0:
            inventory.git(root, 'config', '--local', '--remove-section', 'branch.'+branch)

    def events_safe(self, root, head, target):
        """Conservative allowlist of already understood event shapes; fail closed on changes."""
        narrow = {'.github/workflows/p10-rfc3161-preflight.yml', 'tests/CP6.Platform.ReleaseTests/P10Rfc3161PreflightWorkflowContractTests.cs'}
        base = inventory.git(root, 'rev-parse', 'origin/'+target)
        paths = set(inventory.git(root, 'diff', '--name-only', base+'...'+head).splitlines())
        for ref in (base, head):
            for file in inventory.git(root, 'ls-tree', '-r', '--name-only', ref, '--', '.github/workflows').splitlines():
                raw = inventory.git(root, 'show', ref+':'+file)
                m = re.search(r'^on:\s*\n(.*?)(?=^[^\s#]|\Z)', raw, re.M | re.S)
                require(m is not None, 'ACTIONS', file)
                body = m.group(1); events = re.findall(r'^  ([\w_]+):', body, re.M)
                safe = events == ['workflow_dispatch']
                if file == '.github/workflows/r2-candidate.yml':
                    safe = events == ['push'] and bool(re.search(r'^    tags:\s*\n\s+- "v\*\.\*\.\*"\s*$', body, re.M)) and not re.search(r'^    (branches|paths)', body, re.M)
                if file == '.github/workflows/p10-rfc3161-preflight.yml':
                    safe = events == ['workflow_dispatch','pull_request'] and set(re.findall(r'^      - "([^"]+)"', body, re.M)) == narrow and not(paths & narrow) and len(paths) < 300
                require(safe, 'ACTIONS', 'review workflow events before remote mutation: '+file)

    def replace(self, old, repository, path, base):
        oldpath = Path(old['path']).resolve(); newpath = Path(path).resolve()
        require(inventory.norm(oldpath) == inventory.norm(old['path']) and inventory.norm(newpath) == inventory.norm(path),
                'LINK', 'resolved replacement paths differ from registered absolute paths')
        require(inventory.norm(oldpath) not in {inventory.norm(v) for v in self.roots.values()}, 'PRIMARY', str(oldpath))
        require(inventory.norm(oldpath) != inventory.norm(Path.cwd()) and not inventory.norm(Path.cwd()).startswith(inventory.norm(oldpath)+'/'), 'CWD', 'cannot remove current execution directory')
        require(inventory.norm(newpath) == inventory.norm(Path(old.get('replacement_path', old['path'])).resolve()), 'PATH', 'unregistered destination')
        inventory.git(self.roots[old['repository']], 'worktree', 'remove', str(oldpath))
        inventory.git(self.roots[repository], 'worktree', 'add', '--detach', str(newpath), base)


def capacity(policy, state):
    discovered, _, discovery_errors = inventory.discover([p['path'] for p in policy['primary_checkouts']], policy.get('scan_containers', inventory.DEFAULT_CONTAINERS))
    paths = list({inventory.norm(p): p for p in policy['capacity']['paths'] + [s['path'] for s in state['slots'].values()] + [r['path'] for r in discovered]}.values())
    existing = [p for p in paths if Path(p).exists()]
    roots = [p for p in existing if not any(inventory.norm(p).startswith(inventory.norm(q)+'/') for q in existing if p != q)]
    rows = [dict(path=p, **inventory.size_scan(p, set())) for p in roots]
    total = sum(r['logical_bytes'] for r in rows); warnings = []
    limits = policy['capacity']
    if total >= limits['warn_total_gib'] * 2**30:
        warnings.append('TOTAL_GROWTH: identify changed paths; no automatic deletion')
    seats = [dict(slot=k, **inventory.size_scan(v['path'], set())) for k,v in state['slots'].items()]
    warnings += ['SEAT_GROWTH: '+r['slot'] for r in seats if r['logical_bytes'] >= limits['warn_seat_gib'] * 2**30]
    disks = {d: shutil.disk_usage(d)._asdict() for d in limits['min_free_gib']}
    warnings += ['LOW_FREE: '+d for d,x in disks.items() if x['free'] < limits['min_free_gib'][d] * 2**30]
    return dict(logical_bytes=total, roots=rows, seats=seats, disks=disks, warnings=warnings, discovery_errors=discovery_errors)


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--registry', default='D:/CP6/docs/development/worktrees/registry.json')
    sub = p.add_subparsers(dest='command', required=True)
    status = sub.add_parser('status'); status.add_argument('--capacity', action='store_true'); status.add_argument('--output')
    bind = sub.add_parser('bind'); bind.add_argument('--slot', required=True); bind.add_argument('--repository', required=True); bind.add_argument('--owner', required=True)
    record = sub.add_parser('record-pr')
    for flag in ('repository','task','owner'): record.add_argument('--'+flag, required=True)
    record.add_argument('--pr', type=int, required=True)
    for name in ('start','resume','finish'):
        s = sub.add_parser(name)
        for flag in ('repository','task','owner'): s.add_argument('--'+flag, required=True)
        if name == 'start':
            s.add_argument('--slot', required=True); s.add_argument('--branch'); s.add_argument('--target'); s.add_argument('--baseline'); s.add_argument('--depends-on', action='append', default=[]); s.add_argument('--source')
        else:
            s.add_argument('--head', required=True); s.add_argument('--reason', default='')
        if name == 'finish':
            s.add_argument('--outcome', choices=['merged','deferred','cancelled','superseded','verified'], required=True)
            for flag in ('validation','review','report'): s.add_argument('--'+flag, required=True)
            s.add_argument('--pr', type=int); s.add_argument('--adopted-commit'); s.add_argument('--next-action', default=''); s.add_argument('--dependencies-reviewed', action='store_true')
    a = p.parse_args(); policy = json.loads(Path(a.registry).read_text(encoding='utf-8-sig'))
    require(policy['schema_version'] == 2, 'POLICY', 'update the canonical registry to lifecycle schema 2')
    git = GitBackend(policy)
    common = Path(inventory.git(git.roots['CP6'], 'rev-parse', '--path-format=absolute', '--git-common-dir'))
    mgr = Manager(policy, common/'cp6-workspaces/state.json', git)
    if a.command == 'status':
        result = mgr.status()
        try: result['actual_layout'] = mgr.layout(result)
        except Refusal as e: result['layout_error'] = str(e)
        result['workspace_checks'] = {}
        for key, seat in result['slots'].items():
            try: result['workspace_checks'][key] = git.workspace(seat['path'])
            except (Refusal, OSError, RuntimeError, subprocess.SubprocessError) as e: result['workspace_checks'][key] = dict(error=str(e))
        result['runtime_path'] = str(mgr.path)
        if a.capacity: result['capacity'] = capacity(policy, result)
        if a.output: Path(a.output).write_text(json.dumps(result, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    elif a.command == 'start':
        result = mgr.start(a.repository, a.slot, a.task, a.owner, a.branch, a.target or git.default_target(a.repository), a.baseline, a.depends_on, a.source)
    elif a.command == 'resume': result = mgr.resume(a.repository, a.task, a.owner, a.head, a.reason)
    elif a.command == 'bind': result = mgr.bind(a.slot, a.repository, a.owner)
    elif a.command == 'record-pr': result = mgr.record_pr(a.repository, a.task, a.owner, a.pr)
    else:
        result = mgr.finish(a.repository, a.task, a.owner, a.head, a.outcome, a.validation, a.review, a.report, a.pr, a.adopted_commit, a.reason, a.next_action, a.dependencies_reviewed)
    print(json.dumps(result, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    sys.stdout.reconfigure(encoding='utf-8'); sys.stderr.reconfigure(encoding='utf-8')
    try: main()
    except (Refusal, OSError, RuntimeError, subprocess.SubprocessError) as exc:
        print(str(exc), file=sys.stderr); sys.exit(2)

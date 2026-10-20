"""Lifecycle behavior, including real OS lock contention; no product checkout is created."""
import importlib.util
import json
import multiprocessing
import tempfile
import unittest
from unittest.mock import patch
from types import SimpleNamespace
from pathlib import Path

if importlib.util.find_spec('manage'):
    import manage
else:
    manage = None


class FakeGit:
    def __init__(self):
        self.head = 'a' * 40
        self.current = 'old-task'
        self.dirty = []
        self.ignored = []
        self.extra = 0
        self.remote = {}
        self.locals = {}
        self.processes = []
        self.adopted = False
        self.deleted = []
        self.dependencies = []
        self.delete_fails = False

    def layout(self, policy, bindings):
        rows = [dict(path=v['path'], repository=v['repository'], primary=False)
                for v in bindings.values()]
        rows += [dict(path='extra'+str(i), repository='CP6', primary=False) for i in range(self.extra)]
        return rows

    def workspace(self, path):
        return dict(repository='CP6', head=self.head, branch=self.current, dirty=self.dirty,
                    local_files=self.ignored, processes=self.processes)

    def target(self, repo, target): return 'a' * 40
    def saved(self, repo, head): return True
    def tips(self, repo): return self.locals.copy(), self.remote.copy()
    def prs(self, repo, branch): return []
    def checkout(self, path, branch, base):
        self.current = branch
        self.head = base
        self.locals[branch] = base
    def adoption(self, task, head, pr, adopted_commit):
        if not self.adopted:
            raise manage.Refusal('NOT_ADOPTED', 'not adopted in remote target')
        return dict(method='verified-test-adoption', target_head='a'*40)
    def dependent_branches(self, task, head): return self.dependencies
    def detach(self, path, target): self.current = ''; self.head = target
    def delete_branch(self, task, head):
        if self.delete_fails: raise manage.Refusal('REMOTE_CHANGED', 'remote tip changed')
        self.deleted.append((task['branch'], head)); self.locals.pop(task['branch'], None)
        self.remote.pop(task['branch'], None)


def occupy_worker(policy, state, gate, queue, task):
    gate.wait()
    try:
        manage.Manager(policy, Path(state), FakeGit()).start('CP6', 'DEV-1', task, task, None, 'main', None, [])
        queue.put('OK')
    except manage.Refusal as e:
        queue.put(e.code)


class LifecycleTests(unittest.TestCase):
    def setUp(self):
        self.assertIsNotNone(manage, 'Unified lifecycle entry manage.py is not implemented')
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.state = Path(self.temp.name)/'state.json'
        self.policy = dict(limits=dict(development_worktrees=2,integration_worktrees=1),
            slots=[dict(id='DEV-1',path='dev1',repository='CP6',kind='development'),
                   dict(id='DEV-2',path='dev2',repository='CP6.AI-Handoff',kind='development'),
                   dict(id='INTEGRATION-1',path='integration',repository='CP6',kind='integration')])
        self.git = FakeGit()
        self.manager = manage.Manager(self.policy, self.state, self.git)

    def start(self, task='TASK-1'):
        return self.manager.start('CP6','DEV-1',task,'OWNER',None,'main',None,[])

    def finish(self, outcome='merged'):
        return self.manager.finish('CP6','TASK-1','OWNER',self.git.head,outcome,
            'related verification passed','review passed','https://example.test/delivery',1,None,
            '','',True)

    def test_over_quota_is_rejected_before_checkout(self):
        self.git.extra = 1
        with self.assertRaisesRegex(manage.Refusal,'QUOTA'):
            self.start()
        self.assertEqual(self.git.current,'old-task')

    def test_same_slot_cannot_be_claimed_twice(self):
        self.start()
        with self.assertRaisesRegex(manage.Refusal,'OCCUPIED'):
            self.start('TASK-2')

    def test_head_changed_during_network_checks_is_preserved(self):
        def target(*args):
            self.git.head='b'*40
            return 'a'*40
        self.git.target=target
        with self.assertRaisesRegex(manage.Refusal,'HEAD_CHANGED'):self.start()
        self.assertEqual(self.git.head,'b'*40)
        self.assertFalse(self.git.locals)
        self.assertIsNone(self.manager.status()['slots']['DEV-1']['task'])

    def test_real_parallel_claims_have_one_winner(self):
        ctx=multiprocessing.get_context('spawn');gate=ctx.Event();queue=ctx.Queue()
        workers=[ctx.Process(target=occupy_worker,args=(self.policy,str(self.state),gate,queue,t)) for t in ['T1','T2']]
        for p in workers:p.start()
        gate.set()
        result=[queue.get(timeout=20) for _ in workers]
        for p in workers:p.join(20);self.assertEqual(p.exitcode,0)
        self.assertEqual(sorted(result),['OCCUPIED','OK'])

    def test_dirty_or_ignored_unique_files_never_release_slot(self):
        self.start();self.git.adopted=True
        for attr in ['dirty','local_files']:
            self.git.dirty=['unfinished.cs'] if attr=='dirty' else []
            self.git.ignored=['.env.local'] if attr=='local_files' else []
            with self.assertRaisesRegex(manage.Refusal,'LOCAL_FILES'):
                self.finish()
            self.assertFalse(self.git.deleted)
            self.assertTrue(self.manager.status()['slots']['DEV-1']['task'])

    def test_unsaved_unadopted_result_is_not_deleted(self):
        self.start()
        with self.assertRaisesRegex(manage.Refusal,'NOT_ADOPTED'):
            self.finish()
        self.assertFalse(self.git.deleted)

    def test_completed_task_deletes_own_branch_and_releases_fixed_slot(self):
        self.start();self.git.adopted=True
        result=self.finish()
        self.assertEqual(result['status'],'merged')
        self.assertIsNone(self.manager.status()['slots']['DEV-1']['task'])
        self.assertEqual(self.git.current,'')
        self.assertEqual(len(self.git.deleted),1)

    def test_stale_record_is_not_automatically_stolen(self):
        self.start()
        d=json.loads(self.state.read_text());d['tasks']['CP6:TASK-1']['updated_at']='2000-01-01T00:00:00+00:00'
        self.state.write_text(json.dumps(d))
        with self.assertRaisesRegex(manage.Refusal,'OCCUPIED'):
            self.start('TASK-2')

    def test_wrong_repository_cannot_use_seat(self):
        with self.assertRaisesRegex(manage.Refusal,'REPOSITORY'):
            self.manager.start('CP6.CRM','DEV-1','TASK-1','OWNER',None,'main',None,[])

    def test_existing_task_branch_is_not_silently_recreated(self):
        self.git.locals['codex/task-1']='b'*40
        with self.assertRaisesRegex(manage.Refusal,'DUPLICATE_BRANCH'):
            self.start()

    def test_changed_remote_branch_cannot_be_deleted(self):
        self.start();self.git.adopted=True;self.git.delete_fails=True
        with self.assertRaisesRegex(manage.Refusal,'REMOTE_CHANGED'):
            self.finish()
        self.assertFalse(self.git.deleted)
        self.assertTrue(self.manager.status()['slots']['DEV-1']['task'])

    def test_deferred_saved_result_can_release_without_branch_deletion(self):
        self.start();self.git.remote['codex/task-1']=self.git.head
        result=self.manager.finish('CP6','TASK-1','OWNER',self.git.head,'deferred',
            'partial validation recorded','review recorded','https://example.test/report',None,None,
            'waiting for contract decision','resume from remote codex/task-1',True)
        self.assertEqual(result['status'],'deferred')
        self.assertFalse(self.git.deleted)
        self.assertIsNone(self.manager.status()['slots']['DEV-1']['task'])

    def test_running_process_prevents_finish(self):
        self.start();self.git.adopted=True;self.git.processes=[{'pid':123,'name':'owned-host'}]
        with self.assertRaisesRegex(manage.Refusal,'PROCESSES'):self.finish()
        self.assertTrue(self.manager.status()['slots']['DEV-1']['task'])

    def test_deferred_unpushed_result_cannot_release(self):
        self.start()
        with self.assertRaisesRegex(manage.Refusal,'UNSAVED_HEAD'):
            self.manager.finish('CP6','TASK-1','OWNER',self.git.head,'deferred','partial','review','report',None,None,'waiting','resume later',True)
        self.assertTrue(self.manager.status()['slots']['DEV-1']['task'])

    def test_declared_dependency_prevents_deleting_result(self):
        self.start();self.git.adopted=True
        data=json.loads(self.state.read_text());data['tasks']['CP6:NEXT']=dict(status='active',dependencies=['CP6:TASK-1'])
        self.state.write_text(json.dumps(data))
        with self.assertRaisesRegex(manage.Refusal,'DEPENDENCIES'):self.finish()
        self.assertFalse(self.git.deleted)

    def test_integration_verification_does_not_create_extra_branch(self):
        r=self.manager.start('CP6','INTEGRATION-1','TASK-1-CHECK','OWNER',None,'main',None,[],self.git.head)
        self.assertIsNone(r['branch']);self.assertFalse(self.git.locals)
        r=self.manager.finish('CP6','TASK-1-CHECK','OWNER',self.git.head,'verified',
            'related checks passed','reviewed','report',None,None,'','',True)
        self.assertEqual(r['branch_disposition'],'no-task-branch')
        self.assertIsNone(self.manager.status()['slots']['INTEGRATION-1']['task'])
        self.assertFalse(self.git.deleted)


class GitSafetyTests(unittest.TestCase):
    def backend(self):
        b=object.__new__(manage.GitBackend);b.roots={'CP6':'unused'}
        b.target=lambda *a:'b'*40
        return b

    def task(self):return dict(repository='CP6',branch='codex/task-1',target='main',base='a'*40)

    def test_squash_uses_pr_mapping_and_remote_ancestry(self):
        b=self.backend();b.gh=lambda *a:dict(state='MERGED',headRefOid='c'*40,headRefName='codex/task-1',baseRefName='main',mergeCommit={'oid':'d'*40},url='verified-pr')
        with patch.object(manage.inventory,'git',return_value=SimpleNamespace(returncode=0)) as command:
            result=b.adoption(self.task(),'c'*40,12,None)
        self.assertEqual(result['commit'],'d'*40)
        self.assertIn('d'*40,command.call_args.args)

    def test_pr_with_newer_source_cannot_prove_old_head(self):
        b=self.backend();b.gh=lambda *a:dict(state='MERGED',headRefOid='e'*40,headRefName='codex/task-1',baseRefName='main',mergeCommit={'oid':'d'*40},url='verified-pr')
        with self.assertRaisesRegex(manage.Refusal,'PR'):b.adoption(self.task(),'c'*40,12,None)

    def test_remote_deletion_is_conditional_and_local_is_not_deleted_on_failure(self):
        b=self.backend();b.events_safe=lambda *a:None
        b.tips=lambda *a:({'codex/task-1':'c'*40},{'codex/task-1':'c'*40})
        with patch.object(manage.inventory,'git',return_value=SimpleNamespace(returncode=1,stderr='expected SHA mismatch')) as command:
            with self.assertRaisesRegex(manage.Refusal,'DELETE_FAILED'):b.delete_branch(self.task(),'c'*40)
        args=command.call_args.args
        self.assertIn('--force-with-lease=refs/heads/codex/task-1:'+('c'*40),args)
        self.assertIn(':refs/heads/codex/task-1',args)
        self.assertNotIn('update-ref',args)


if __name__=='__main__':unittest.main()

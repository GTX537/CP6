"""单卡执行门的行为回归；临时台账不作为业务验收证据。"""
import copy
import unittest

import manage_roadmap as roadmap
import test_manage_roadmap as fixtures


class CardExecutionTests(unittest.TestCase):
    def setUp(self):
        self.fixture = fixtures.TaskDefinitionTests(methodName="runTest")
        self.fixture.setUp()
        self.addCleanup(self.fixture.doCleanups)
        self.fixture.data['dispatch_policy']['card_execution_policy'] = {
            'strict_prerequisites': True, 'started_at': '2026-11-17T00:00:00Z'}

    def complete(self, task):
        evidence = dict(path='docs/design-handbook/source with spaces.md', source_sha='a'*40,
                        scope='registrar fixture', command='fixture', environment='unit fixture',
                        observed_at='2026-11-17T00:00:00Z', result='PASS')
        task.update(state='DONE', entry_gate='CONFIRMED', validation_evidence=[evidence],
                    delivery_evidence=dict(evidence, remote_main_sha='b'*40,
                                           pr_url='https://github.com/GTX537/CP6/pull/1'),
                    assignment=dict(chat_label=task['id'], branch=task['id'], worktree=task['id'],
                                    claimed_at='2026-11-17T00:00:00Z'))

    def split_fixture(self):
        f = self.fixture
        provider, parent = f.data['implementation_tasks']
        self.complete(provider)
        self.complete(parent)
        f.definition['split_children'] = ['WP02-01b']
        for tid, child in [('WP02-01b', True), ('WP02-02', False)]:
            definition = copy.deepcopy(f.definition)
            definition.pop('split_children')
            definition.update(id=tid, dependencies=['WP02-01'], prerequisite_bindings=[])
            if child:
                definition['split_parent'] = 'WP02-01'
            f.definitions['tasks'].append(definition)
            task = {key: copy.deepcopy(definition[key]) for key in roadmap.DEFINITION_METADATA}
            task.update(state='PLANNED', entry_gate='NOT_CHECKED', assignment=None, blockers=[],
                        coverage_role='IMPLEMENTATION', plan_path=parent['plan_path'],
                        card_anchor=tid.lower(), entry_conditions=['Inspect original delivery'])
            f.data['implementation_tasks'].append(task)
            f.data['target_task_coverage'][0]['implementation_task_ids'].append(tid)
        child, consumer = f.data['implementation_tasks'][-2:]
        return parent, child, consumer

    @staticmethod
    def ready(task):
        task.update(state='READY', entry_gate='CONFIRMED', assignment=dict(
            chat_label=task['id'], branch=task['id'], worktree=task['id'],
            claimed_at='2026-11-17T01:00:00Z'))

    def test_new_window_cannot_use_partial_delivery_as_done_prerequisite(self):
        self.fixture.adopt_partial_foundation()
        self.assertIn('WP02-01: unfinished task dependencies', self.fixture.errors())

    def test_split_remainder_can_start_after_parent_but_consumer_must_wait(self):
        _, child, consumer = self.split_fixture()
        self.ready(child)
        self.assertEqual(self.fixture.errors(), [])
        self.ready(consumer)
        self.assertIn('WP02-02: unfinished task dependencies', self.fixture.errors())
        self.complete(child)
        self.assertEqual(self.fixture.errors(), [])

    def test_orphan_suffix_or_missing_remainder_cannot_bypass_gate(self):
        self.split_fixture()
        self.fixture.definitions['tasks'][1].pop('split_parent')
        self.assertTrue(any('split' in error for error in self.fixture.errors()))
        self.fixture.definitions['tasks'][1]['split_parent'] = 'WP02-01'
        self.fixture.definition['split_children'].append('WP02-01c')
        self.assertTrue(any('split' in error for error in self.fixture.errors()))


if __name__ == '__main__':
    unittest.main()

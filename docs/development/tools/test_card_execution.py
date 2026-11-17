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

    def test_open_target_with_complete_coverage_requires_unfinished_gap_owner(self):
        f = self.fixture
        provider, parent = f.data['implementation_tasks']
        self.complete(parent)
        f.definition['dependencies'] = parent['dependencies'] = []
        f.definition['prerequisite_bindings'] = []
        self.assertIn('T-A: open target with complete coverage requires gap owners', f.errors())
        f.targets['T-A']['completion_audit'] = None
        self.assertIn('T-A: open target with complete coverage requires gap owners', f.errors())
        f.targets['T-A']['completion_audit'] = {'gaps': [
            {'gap': '真实消费者仍需核对', 'owner_task_id': provider['id']}]}
        self.assertEqual(f.errors(), [])
        for owner in ('UNKNOWN', parent['id']):
            f.targets['T-A']['completion_audit']['gaps'][0]['owner_task_id'] = owner
            self.assertIn('T-A: gap owner must be one existing unfinished task', f.errors())

    def test_split_parent_done_does_not_trigger_complete_coverage_or_count_child_as_original(self):
        f = self.fixture
        parent, child, consumer = self.split_fixture()
        coverage = [{'target_id': 'T-A', 'implementation_task_ids': [parent['id']]}]
        definitions = {d['id']: d for d in f.definitions['tasks']}
        tasks = {t['id']: t for t in f.data['implementation_tasks']}
        self.assertEqual(roadmap.validate_target_gaps(coverage, f.targets, tasks, definitions), [])
        self.assertEqual(roadmap.card_completion_counts(tasks, definitions),
                         {'original_done': 2, 'fully_done': 1, 'original_total': 3})
        self.complete(child)
        self.assertIn('T-A: open target with complete coverage requires gap owners',
                      roadmap.validate_target_gaps(coverage, f.targets, tasks, definitions))
        self.assertEqual(roadmap.card_completion_counts(tasks, definitions)['fully_done'], 2)
        self.assertEqual(roadmap.card_completion_counts(tasks, definitions)['original_done'], 2)
        f.targets['T-A']['delivery_state'] = 'DONE'
        child['state'] = 'PLANNED'
        self.assertIn('T-A: target DONE with unfinished split remainders',
                      roadmap.validate_target_gaps(coverage, f.targets, tasks, definitions))

    def test_nested_remainder_must_finish_before_original_is_fully_done(self):
        f = self.fixture
        parent, child, _ = self.split_fixture()
        self.complete(child)
        definitions = {d['id']: copy.deepcopy(d) for d in f.definitions['tasks']}
        tasks = {t['id']: copy.deepcopy(t) for t in f.data['implementation_tasks']}
        definitions[child['id']]['split_children'] = ['WP02-01c']
        definitions['WP02-01c'] = {'split_parent': child['id']}
        tasks['WP02-01c'] = {'state': 'PLANNED'}
        self.assertEqual(roadmap.card_completion_counts(tasks, definitions),
                         {'original_done': 2, 'fully_done': 1, 'original_total': 3})
        tasks['WP02-01c']['state'] = 'DONE'
        self.assertEqual(roadmap.card_completion_counts(tasks, definitions),
                         {'original_done': 2, 'fully_done': 2, 'original_total': 3})

    def test_remainder_budget_spends_parent_time_without_increasing_total(self):
        f = self.fixture
        parent, child, _ = self.split_fixture()
        parent['card_execution'] = {'effective_seconds': 487.289}
        child['card_budget_seconds'] = 13912.711
        policy = f.data['dispatch_policy']['card_execution_policy']
        policy.update(size_budget_hours={'M': 4, 'L': 8}, base_card_hours=8,
                      budget_baseline_done_task_ids=['S01-01'])
        definitions = {d['id']: d for d in f.definitions['tasks']}
        tasks = {t['id']: t for t in f.data['implementation_tasks']}
        self.assertEqual(roadmap.validate_card_budgets(policy, tasks, definitions), [])
        child['card_budget_seconds'] = 14400
        self.assertIn('WP02-01: split budgets must conserve parent cap minus effective time',
                      roadmap.validate_card_budgets(policy, tasks, definitions))
        child['card_budget_seconds'] = 13912.711
        policy['base_card_hours'] = 12
        self.assertIn('card budget total differs from original remaining-card pool',
                      roadmap.validate_card_budgets(policy, tasks, definitions))


if __name__ == '__main__':
    unittest.main()

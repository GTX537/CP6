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

    def test_rank_uses_stage_order_for_ties_and_checks_split_prerequisites(self):
        definitions = {
            tid: {"id": tid, "dependencies": dependencies}
            for tid, dependencies in {
                "WP03-05": [], "WP11-65": ["WP02-13"], "WP02-22": [],
                "WP02-13": [], "WP02-13b": ["WP02-13"],
                "downstream": ["WP03-05", "WP11-65"],
            }.items()
        }
        definitions["WP02-13"]["split_children"] = ["WP02-13b"]
        definitions["WP02-13b"]["split_parent"] = "WP02-13"
        tasks = {tid: {"state": "PLANNED"} for tid in definitions}
        tasks["WP02-13"]["state"] = "DONE"
        stage = ["WP11-65", "WP02-22", "WP03-05"]
        self.assertEqual([row["task"] for row in roadmap.rank_stage_candidates(tasks, definitions, stage)],
                         ["WP03-05", "WP02-22"])
        tasks["WP02-13b"]["state"] = "DONE"
        self.assertEqual(roadmap.rank_stage_candidates(tasks, definitions, stage), [
            {"task": "WP11-65", "longest_downstream_edges": 1},
            {"task": "WP03-05", "longest_downstream_edges": 1},
            {"task": "WP02-22", "longest_downstream_edges": 0},
        ])

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

    def test_authorized_supplement_spends_completed_card_surplus_once(self):
        policy = {
            'size_budget_hours': {'M': 4, 'L': 8},
            'base_card_hours': 8,
            'budget_baseline_done_task_ids': [],
        }
        decision = {
            'commit': 'a' * 40,
            'path': 'handoffs/tasks/WP04-03/replies/authorization/MESSAGE.md',
            'pr_url': 'https://github.com/GTX537/CP6.AI-Handoff/pull/366',
        }
        tasks = {
            'WP04-03': {'relative_size': 'L', 'state': 'DONE', 'card_execution': {'effective_seconds': 4415.904}},
            'WP04-38': {'relative_size': 'M', 'state': 'DONE', 'card_execution': {'effective_seconds': 5100}},
        }
        definitions = {'WP04-03': {}, 'WP04-38': {'supplemental_authorization': decision}}
        transfer = {
            'source_task_id': 'WP04-03', 'target_task_id': 'WP04-38', 'seconds': 14400, 'decision': decision,
        }
        policy['supplemental_budget_transfers'] = [transfer]
        self.assertEqual(roadmap.validate_card_budgets(policy, tasks, definitions), [])
        self.assertEqual(roadmap.card_completion_counts(tasks, definitions)['original_total'], 1)

        definitions['WP04-38']['split_children'] = ['WP04-38b']
        definitions['WP04-38b'] = {'split_parent': 'WP04-38'}
        tasks['WP04-38b'] = {'relative_size': 'M', 'state': 'PLANNED', 'card_budget_seconds': 9300}
        self.assertEqual(roadmap.validate_card_budgets(policy, tasks, definitions), [])
        tasks['WP04-38b']['card_budget_seconds'] = 14400
        self.assertIn('WP04-38: split budgets must conserve parent cap minus effective time',
                      roadmap.validate_card_budgets(policy, tasks, definitions))
        tasks['WP04-38b']['card_budget_seconds'] = 9300

        policy['supplemental_budget_transfers'] = [transfer, copy.deepcopy(transfer)]
        self.assertTrue(roadmap.validate_card_budgets(policy, tasks, definitions))
        policy['supplemental_budget_transfers'] = [transfer]
        tasks['WP04-03']['card_execution']['effective_seconds'] = 15000
        self.assertTrue(roadmap.validate_card_budgets(policy, tasks, definitions))
        tasks['WP04-03']['card_execution']['effective_seconds'] = 4415.904
        tasks['WP04-03']['state'] = 'PLANNED'
        self.assertTrue(roadmap.validate_card_budgets(policy, tasks, definitions))


    def test_authorized_supplements_accept_exact_distinct_sources_and_conserve_pool(self):
        refs_root = "handoffs/CP6-DEV-20261012/tasks/WP03-16/replies/"
        refs = {
            target: {"commit": "c7abf2b30f1f21eeed075769cb573f744c666fae",
                     "path": refs_root + f"20261121T152956Z-coord-{slug}-authorization/MESSAGE.md",
                     "pr_url": "https://github.com/GTX537/CP6.AI-Handoff/pull/381"}
            for target, slug in (("WP02-30", "maintenance-contract"),
                                 ("WP03-28", "lead-read-h01"), ("WP03-29", "account-a03"))
        }
        spent = {"WP03-02": 1590.862, "WP03-05": 1854.370, "WP03-15": 4042.361}
        tasks = {source: {"relative_size": "M" if source == "WP03-05" else "L",
                          "state": "DONE", "card_execution": {"effective_seconds": seconds}}
                 for source, seconds in spent.items()}
        tasks.update({target: {"relative_size": "M", "state": "PLANNED"} for target in refs})
        definitions = {source: {} for source in spent}
        definitions.update({target: {"supplemental_authorization": ref} for target, ref in refs.items()})
        transfers = [{"source_task_id": source, "target_task_id": target, "seconds": seconds,
                      "decision": refs[target]}
                     for source, target, seconds in (("WP03-02", "WP02-30", 14400),
                                                     ("WP03-15", "WP03-28", 14400),
                                                     ("WP03-05", "WP03-29", 12545.630),
                                                     ("WP03-02", "WP03-29", 1854.370))]
        policy = {"size_budget_hours": {"M": 4, "L": 8}, "base_card_hours": 20,
                  "budget_baseline_done_task_ids": []}
        def validate(records):
            return roadmap.validate_card_budgets(dict(policy, supplemental_budget_transfers=records),
                                                 tasks, definitions)

        self.assertEqual(validate(transfers), [])
        self.assertEqual(validate(list(reversed(transfers))), [])
        self.assertTrue(validate(transfers + [transfers[-1]]))
        for amount in (0, -1854.370, 1854.369, 1854.371):
            with self.subTest(amount=amount):
                self.assertTrue(validate(transfers[:-1] + [dict(transfers[-1], seconds=amount)]))
        self.assertTrue(validate(transfers[:-1]))

        definitions["WP03-28"]["supplemental_authorization"] = refs["WP03-29"]
        self.assertTrue(validate([record if record["target_task_id"] != "WP03-28"
                                  else dict(record, decision=refs["WP03-29"]) for record in transfers]))
        definitions["WP03-28"]["supplemental_authorization"] = refs["WP03-28"]
        tasks["WP03-02"]["card_execution"]["effective_seconds"] = 12546
        self.assertTrue(validate(transfers))


if __name__ == '__main__':
    unittest.main()

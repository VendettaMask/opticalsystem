"""Archive completed C# results only; never trace, evaluate optics, or modify inputs."""
import copy
import hashlib
import json
from collections import Counter, defaultdict
from pathlib import Path

root = Path(__file__).resolve().parents[4]
base = root / 'artifacts/validation/dsearch-r2-20260926'
out = root / 'labs/InitialStructure/benchmarks/dsearch-r2-20260926'
sha = lambda path: hashlib.sha256(path.read_bytes()).hexdigest()

for profile in ['original', 'holdout']:
    path = base / profile / 'summary.json'
    raw = json.loads(path.read_text())
    if raw['CompletedRuns'] != raw['ExpectedRuns']:
        raise RuntimeError(f'Incomplete {profile}')
    doc = copy.deepcopy(raw)
    removed = 0
    for row in doc['Results']:
        if row.get('SelectedValidation') is not None:
            removed += len(row['SelectedValidation'].pop('Residuals', []))
    groups = defaultdict(list)
    for row in raw['Results']:
        groups[row['Specification']].append(row)
    doc['Aggregate'] = {
        'SuccessfulRuns': sum(row['Success'] for row in raw['Results']),
        'SpecificationsMeetingFourOfFive': sum(sum(row['Success'] for row in rows) >= 4 for rows in groups.values()),
        'SearchSecondsTotal': sum(row['SearchSeconds'] for row in raw['Results']),
        'MeasuredSearchRaysTotal': sum(row['MeasuredSearchRays'] for row in raw['Results']),
        'FailedTrials': sum(row['FailedTrials'] for row in raw['Results']),
        'VerificationErrors': sum(len(row['VerificationErrors']) for row in raw['Results']),
        'IdenticalReloads': sum(row.get('ReloadSnapshotIdentical', row.get('ReloadIdentical')) is True for row in raw['Results']),
        'PerSpecification': {
            name: {
                'SuccessfulSeeds': [row['Seed'] for row in rows if row['Success']],
                'FailedSeeds': [row['Seed'] for row in rows if not row['Success']],
            } for name, rows in groups.items()
        },
    }
    doc['ArchiveProvenance'] = {
        'RawSummaryPath': str(path.relative_to(root)), 'RawSummarySha256': sha(path),
        'RemovedField': 'Results[].SelectedValidation.Residuals', 'RemovedValues': removed,
        'Preserved': 'All seeds, physical metrics, constraints, costs, errors, input/binary hashes and export/reload status.',
    }
    previous = json.loads((root / 'artifacts/validation/initial-structure-s4-v8-20260926' / profile / 'summary.json').read_text())
    if raw['InputSha256'] != previous['InputSha256']:
        raise RuntimeError(f'Input hashes changed for {profile}')
    (out / f'{profile}.json').write_text(json.dumps(doc, indent=2, ensure_ascii=False) + '\n')
    print(profile, doc['Aggregate'], flush=True)

audit = []
for path in sorted((base / 'original').glob('*/101/checkpoint.json')):
    checkpoint = json.loads(path.read_text())['checkpoint']
    branches = {}
    operations = Counter()
    branch_operations = defaultdict(Counter)
    forms = set()
    for trial in checkpoint['trials']:
        operations[trial['operation']] += trial['chargedEvaluations']
        ancestor = trial.get('parentTrialId') or trial['trialId']
        branches[trial['trialId']] = branches.get(ancestor, ancestor)
        branch_operations[branches[trial['trialId']]][trial['operation']] += 1
        proof = trial.get('bootstrapProof')
        if proof and trial.get('finalValidation') and any(step['operation'] == 'binary-form-initialization' for step in proof['steps']):
            forms.add(trial['family']['binaryStart']['signs'])
    audit.append({
        'Specification': checkpoint['specification']['name'], 'Seed': 101,
        'CheckpointSha256': sha(path), 'RootFormsCompleted': sorted(forms),
        'EvaluationsByOperation': dict(operations), 'TrialCount': len(checkpoint['trials']),
        'OperationsByRootBranch': {key: dict(value) for key, value in branch_operations.items()},
    })
(out / 'branch-audit.json').write_text(json.dumps(audit, indent=2, ensure_ascii=False) + '\n')
(out / 'batch.json').write_bytes((base / 'batch.json').read_bytes())

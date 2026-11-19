"""Verify actual frozen archives and prepare a traceable payload.

Never reads account data or authenticates. The regression runner reuses code
extracted from the exact binary, with all network connections denied.
"""
from __future__ import annotations
import argparse
import hashlib
import importlib.metadata
import json
import marshal
import shutil
import subprocess
import sys
import types
from pathlib import Path
from PyInstaller.archive.readers import CArchiveReader


def sha(data):
    return hashlib.sha256(data).hexdigest()


def save(path, obj):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(obj, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def signature(code):
    if not isinstance(code, types.CodeType):
        return code
    return (code.co_code, tuple(signature(c) for c in code.co_consts), code.co_names,
            code.co_varnames, code.co_freevars, code.co_cellvars, code.co_flags,
            code.co_argcount, code.co_posonlyargcount, code.co_kwonlyargcount)


def inspect_worker(repo, executable, out):
    platform = executable.parent.name
    archive = CArchiveReader(str(executable))
    embedded = json.loads(archive.extract('build-manifest.json'))
    save(out / f'{platform}-embedded-manifest.json', embedded)
    assert embedded['protocol'] == 1
    code = marshal.loads(archive.extract('worker'))
    source = repo / 'Integrations' / 'Drops' / platform / 'worker.py'
    assert signature(code) == signature(compile(source.read_bytes(), str(source), 'exec')), f'{platform} worker bytecode differs'
    members = list(archive.toc)
    assert not any(any(s in member.lower() for s in ('cookies.jar', 'claim-journal', 'approved-river', 'audit_jsonl', 'browser/profile')) for member in members)
    pyz = archive.open_embedded_archive('PYZ.pyz')
    shared = pyz.extract('Shared.protocol')
    assert signature(shared) == signature(compile((repo / 'Integrations/Drops/Shared/protocol.py').read_bytes(), '', 'exec')), f'{platform} protocol differs'
    core_files = {}
    if platform in ('twitch', 'soop'):
        for name in members:
            normalized = name.replace('\\', '/')
            if normalized.startswith('core/') and normalized.endswith('.py'):
                data = archive.extract(name)
                core_files[normalized.removeprefix('core/')] = sha(data)
                if platform == 'twitch':
                    assert data == (repo / 'Integrations/Drops/twitch' / normalized).read_bytes(), f'Stale Twitch {normalized}'
                    dest = out / 'extracted-twitch' / normalized
                    dest.parent.mkdir(parents=True, exist_ok=True)
                    dest.write_bytes(data)
        if platform == 'soop':
            for module in ('scheduling','watch_evidence','game_catalog'):
                assert signature(pyz.extract(module)) == signature(compile((repo/f'Integrations/Drops/soop/{module}.py').read_bytes(), '', 'exec')), f'Stale SOOP {module}'
            assert archive.extract('game-catalog.official.json') == (repo/'Integrations/Drops/soop/game-catalog.official.json').read_bytes(), 'Stale SOOP official game directory'
            assert core_files == embedded['soopCore']['files']
            baseline = json.loads((repo / 'Integrations/Drops/soop/core-baseline.json').read_text('utf-8-sig'))
            assert embedded['soopCore']['sha256'] == baseline['sha256']
            assert embedded['soopCore']['commit'] == baseline['commit']
            assert all(embedded['soopCore']['capabilities'].values())
        else:
            assert {'claim_guard.py', 'campaign_priority.py', 'settings_store.py'} <= core_files.keys()
            target = out / 'extracted-twitch'
            # Execute the frozen module's actual bytecode, not source reconstructed
            # from today's checkout. All imports are from extracted binary code.
            (target / 'worker.marshal').write_bytes(archive.extract('worker'))
            for module in ('headless_gui', 'visual_metadata', 'gui', 'Shared', 'Shared.protocol'):
                item = pyz.extract(module)
                relative = module.replace('.', '/') + ('/__init__' if module == 'Shared' else '')
                dest = target / (relative + '.marshal')
                dest.parent.mkdir(parents=True, exist_ok=True)
                dest.write_bytes(marshal.dumps(item))
            for name in members:
                norm = name.replace('\\', '/')
                if norm.startswith('core/lang/'):
                    dest = target / norm
                    dest.parent.mkdir(parents=True, exist_ok=True)
                    dest.write_bytes(archive.extract(name))
    files = {k: v for k, v in embedded['adapterSources']['files'].items()
             if k.startswith(platform + '/') or k.startswith('Shared/')}
    return {'platform': platform, 'binarySha256': sha(executable.read_bytes()),
            'baseCommit': embedded['commit'], 'embeddedWorkingTreeModified': embedded['dirty'],
            'sourceSha256': sha(json.dumps(files, sort_keys=True, separators=(',', ':')).encode()),
            'sourceFiles': files, 'protocol': 1, 'coreFiles': core_files,
            'core': embedded.get('soopCore'), 'dependencies': embedded['dependencies'],
            'workerBytecodeMatchesCurrentSource': True, 'sharedProtocolMatches': True,
            'rebuilt': platform == 'twitch',
            'buildPurpose': embedded['purpose'],
            'distributionStatus': 'PUBLIC' if embedded['purpose'] == 'Distribution' else 'LOCAL-ONLY',
            'embeddedManifest': f'{platform}-embedded-manifest.json'}


def prepare(repo, candidate, baseline):
    workers = candidate / 'workers'
    if baseline:
        original = json.loads((baseline / 'candidate.json').read_text('utf-8-sig'))
        for platform in ('soop', 'bilibili', 'youtube'):
            src = baseline / 'workers' / platform
            binary = src / f'{platform}-worker.exe'
            assert sha(binary.read_bytes()) == original['workerBuilds'][platform]['sha256']
            shutil.copytree(src, workers / platform)
    records = {}
    for platform in ('twitch', 'soop', 'bilibili', 'youtube'):
        records[platform] = inspect_worker(repo, workers / platform / f'{platform}-worker.exe', candidate / 'manifests')
        if baseline is None:
            expected = subprocess.check_output(['git','rev-parse','HEAD'],cwd=repo,text=True).strip()
            assert records[platform]['baseCommit'] == expected and not records[platform]['embeddedWorkingTreeModified']
            records[platform]['rebuilt'] = True
    save(candidate / 'manifests/workers.json', records)
    # Keep each original manifest intact. A mixed-source bundle is not falsely
    # represented as one historical Git commit.
    purposes = {record['buildPurpose'] for record in records.values()}
    assert len(purposes) == 1, 'Mixed Worker build purposes'
    save(workers / 'component-manifest.json', {'protocol': 1, 'purpose': purposes.pop(), 'workers': records})
    for platform, record in records.items():
        shutil.copyfile(candidate / 'manifests' / record['embeddedManifest'], workers / f'{platform}-build-manifest.json')
    print('PASS actual frozen Worker bytecode, embedded manifests, exact Twitch Core and SOOP capabilities')


def licenses(candidate):
    target = candidate / 'licenses' / 'Python'
    included = []
    for dist in importlib.metadata.distributions():
        name = dist.metadata['Name']
        items = []
        for relative in dist.files or []:
            if (Path(relative).suffix.lower() not in ('.py', '.pyc', '.pyo') and
                    any(p.lower().startswith(('license', 'copying', 'notice')) for p in Path(relative).parts)):
                src = Path(dist.locate_file(relative))
                if src.is_file():
                    dest = target / name / Path(relative).name
                    dest.parent.mkdir(parents=True, exist_ok=True)
                    shutil.copyfile(src, dest)
                    items.append({'file': dest.relative_to(candidate / 'licenses').as_posix(), 'sha256': sha(src.read_bytes())})
        included.append({'name': name, 'version': dist.version, 'license': dist.metadata.get('License-Expression') or dist.metadata.get('License'), 'notices': items})
    base = Path(sys.base_prefix)
    for name in ('LICENSE.txt', 'LICENSE'):
        if (base / name).is_file():
            shutil.copyfile(base / name, target / 'Python-LICENSE.txt')
            break
    save(candidate / 'licenses/dependencies.json', included)
    print('PASS dependency notices collected')


def extracted_tests(repo, candidate):
    extracted = candidate / 'manifests/extracted-twitch'
    tests = extracted / 'tests'
    shutil.copytree(repo / 'Integrations/Drops/twitch/tests', tests, dirs_exist_ok=True, ignore=shutil.ignore_patterns('__pycache__'))
    # Source-inspection assertions also need a readable wrapper. Its code was
    # compared against the archive before extraction; execution uses marshal.
    shutil.copyfile(repo / 'Integrations/Drops/twitch/worker.py', extracted / 'worker.py')
    runner = extracted / 'run_tests.py'
    runner.write_text('''import sys, marshal, types, socket, unittest
from pathlib import Path
root=Path(__file__).resolve().parent
connect=socket.socket.connect
connect_ex=socket.socket.connect_ex
def local_only(original):
    def guarded(sock,address,*a,**k):
        if not isinstance(address,tuple) or address[0] not in ("127.0.0.1","::1","localhost"):
            raise RuntimeError("Binary regression denies remote network connections")
        return original(sock,address,*a,**k)
    return guarded
socket.socket.connect=local_only(connect)
socket.socket.connect_ex=local_only(connect_ex)
sys.path[:0]=[str(root/"tests"),str(root),str(root/"core")]
for name in ("Shared","Shared.protocol","headless_gui","visual_metadata","gui","worker"):
    relative=name.replace(".","/")+("/__init__" if name=="Shared" else "")
    module=types.ModuleType(name);module.__file__=str(root/(relative+".py"))
    if name=="Shared": module.__path__=[str(root/"Shared")]
    sys.modules[name]=module
    exec(marshal.loads((root/(relative+".marshal")).read_bytes()),module.__dict__)
suite=unittest.defaultTestLoader.discover(str(root/"tests"),pattern="test*.py")
result=unittest.TextTestRunner(verbosity=2).run(suite)
raise SystemExit(not result.wasSuccessful())
''', encoding='utf-8')
    with (candidate / 'validation/twitch-binary-extracted-tests.log').open('w', encoding='utf-8') as log:
        result = subprocess.run([sys.executable, str(runner)], stdout=log, stderr=subprocess.STDOUT)
    assert result.returncode == 0, 'Tests of binary-extracted code failed; see log'
    print('PASS regression of code extracted from the final frozen Twitch Worker; real network denied')


if __name__ == '__main__':
    p = argparse.ArgumentParser()
    p.add_argument('stage', choices=('prepare', 'licenses', 'extracted-tests'))
    p.add_argument('--repo', type=Path, required=True)
    p.add_argument('--candidate', type=Path, required=True)
    p.add_argument('--baseline', type=Path)
    a = p.parse_args()
    if a.stage == 'prepare': prepare(a.repo, a.candidate, a.baseline)
    elif a.stage == 'licenses': licenses(a.candidate)
    else: extracted_tests(a.repo, a.candidate)

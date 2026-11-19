"""Execute exact EXE-extracted bytecode; remote connections forbidden."""
import importlib.util, marshal, os, socket, sys, types, unittest
from pathlib import Path
from PyInstaller.archive.readers import CArchiveReader

import argparse
parser=argparse.ArgumentParser();parser.add_argument('--repo',type=Path,required=True);parser.add_argument('--candidate',type=Path,required=True);args=parser.parse_args()
repo=args.repo
candidate=args.candidate
out=candidate/'manifests/extracted-soop'
core=out/'core';core.mkdir(parents=True,exist_ok=True)
archive=CArchiveReader(str(candidate/'workers/soop/soop-worker.exe'))
for name in archive.toc:
    normalized=name.replace('\\','/')
    if normalized.startswith('core/') and normalized.endswith('.py'):
        (core/normalized[5:]).write_bytes(archive.extract(name))
os.environ['CLOUDLIGHT_SOOP_CORE']=str(core)
connect=socket.socket.connect
connect_ex=socket.socket.connect_ex
def local_only(original):
    def guarded(sock,address,*args,**kwargs):
        if not isinstance(address,tuple) or address[0] not in ('127.0.0.1','::1','localhost'):
            raise RuntimeError('Frozen SOOP replay forbids real network connections')
        return original(sock,address,*args,**kwargs)
    return guarded
socket.socket.connect=local_only(connect)
socket.socket.connect_ex=local_only(connect_ex)
pyz=archive.open_embedded_archive('PYZ.pyz')
(out/'game-catalog.official.json').write_bytes(archive.extract('game-catalog.official.json'))
for name in ('Shared','Shared.protocol','watch_evidence','game_catalog','scheduling','credentials','api_compat','worker'):
    module=types.ModuleType(name)
    relative=name.replace('.','/')+('/__init__' if name=='Shared' else '')
    module.__file__=str(repo/'Integrations/Drops'/(relative+'.py')) if name.startswith('Shared') else str(repo/'Integrations/Drops/soop'/(name+'.py'))
    if name=='game_catalog': module.__file__=str(out/'game_catalog.py')
    if name=='Shared':module.__path__=[str(repo/'Integrations/Drops/Shared')]
    sys.modules[name]=module
    code=marshal.loads(archive.extract('worker')) if name=='worker' else pyz.extract(name)
    exec(code,module.__dict__)
tests=repo/'Integrations/Drops/soop/tests'
sys.path.insert(0,str(tests))
suite=unittest.defaultTestLoader.discover(str(tests),pattern='test*.py')
result=unittest.TextTestRunner(verbosity=2).run(suite)
print('FROZEN CODE REPLAY: real remote connections denied; real claims and watching = 0')
raise SystemExit(not result.wasSuccessful())

"""Map every retained v1 contract to v2, including routed owner actions and reusable visual projections.

Static evidence is separate from executed tests; a mapped row is never a claim of a real account action.
"""
import json
import pathlib
import runpy
import re
import xml.etree.ElementTree as ET

ROOT=pathlib.Path(__file__).resolve().parents[1]
OUT=ROOT/'docs/ui-redesign-v2'
lib=runpy.run_path(str(ROOT/'build-support/UiMigrationAudit.py'),run_name='audit_library')
baseline=json.loads((ROOT/'docs/ui-redesign/inventory.json').read_text(encoding='utf-8-sig'))
files=sorted(p for folder in ('Views','Controls','Stats') for p in (ROOT/folder).rglob('*') if p.name.endswith(('.xaml','.xaml.cs')))
current=lib['inventory']((str(p.relative_to(ROOT)).replace('\\','/'),p.read_text(encoding='utf-8-sig')) for p in files)
for path in (p for p in files if p.suffix=='.xaml'):
 for element in ET.parse(path).getroot().iter():
  action=next((v for k,v in element.attrib.items() if k.endswith('WorkspaceAction.Name')),None)
  if action and action.startswith('legacy:'):
   current.append(dict(file=str(path.relative_to(ROOT)),kind=element.tag.split('}')[-1],name='',identity='',contract={**({'Visibility':element.get('Visibility')} if element.get('Visibility') else {}),'Click':action[7:]}))
  tag=element.get('Tag','')
  if tag.startswith('account:') and element.get('Click')=='OnMenuAction':
   current.append(dict(file=str(path.relative_to(ROOT)),kind='MenuItem',name='',identity='',contract={'Click':{'account:stats':'OnStats','account:edit':'OnEdit','account:delete':'OnDelete'}.get(tag,'')}))
routes=(ROOT/'Views/Pages/DropsPage.Workspaces.cs').read_text(encoding='utf-8-sig')
def normalize(value):
 # Same VM command, now resolved through the composed workspace instead of the deleted namescope.
 value=value.replace('ElementName=DropsRoot','RelativeSource={RelativeSource AncestorType={x:Type UserControl}}')
 return re.sub(r'\s+','',value)
for row in baseline:
 row['evidence']=[]
 candidates=[n for n in current if n['identity']==row['identity']]
 if candidates: row['evidence']=['原契约保留',candidates[0]['file']];continue
 if row['contract']:
  candidates=[n for n in current if all(normalize(n['contract'].get(k,''))==normalize(v) for k,v in row['contract'].items())]
  if candidates:row['evidence']=['同一绑定 / Command（共享模板或控件更名）',candidates[0]['file']];continue
 if row['name']:
  candidates=[n for n in current if n['name']==row['name'] and n['kind']==row['kind']]
  if candidates:
   row['evidence']=['同名输入 / 控件保留',candidates[0]['file']];continue
 events=[v for k,v in row['contract'].items() if re.fullmatch('On[A-Za-z0-9_]+',v)]
 if events:
  candidates=[n for n in current if all(v in n['contract'].values() or f'legacy:{v}' in n['contract'].values() for v in events)]
  if candidates:row['evidence']=['原事件或 owner 路由',candidates[0]['file']];continue
  if all(f'case "{v}"' in routes for v in events):
   # A route alone is not proof of a reachable control.
   row['route_only']=events
 if row['kind']=='confirmation/command':
  found=next((p for p in files if row['identity'] in p.read_text(encoding='utf-8-sig')),None)
  if found:row['evidence']=['原确认 / Command 实现',str(found.relative_to(ROOT))];continue
 # Deliberate visual replacements, with concrete new binding evidence. These are not click-test claims.
 replacements={
  'TwitchChannelText':('Views/Drops/Twitch/TwitchOverview.xaml','TwitchVisual.Channel'),
  'TwitchCurrentProgressText':('Controls/RewardHero.xaml','Binding ProgressText'),
  'TwitchCurrentProgress':('Controls/RewardHero.xaml','Binding Percent'),
  'TwitchConnectionStage':('Views/Drops/Twitch/TwitchOverview.xaml','TwitchLoginVisibility'),
  'BilibiliDetails.LoginStateText':('Views/Drops/Bilibili/BilibiliOverview.xaml','BilibiliDetails.AccountStatus'),
  'Bilibili.ConnectionStateText':('Views/Drops/Bilibili/BilibiliOverview.xaml','BilibiliDetails.ProxyModeText'),
  'RegionGuide.BackupSizeText':('Views/Pages/RegionFilesPage.xaml','RegionGuide.BackupSizeText'),
  'SnapshotCount':('Views/Pages/SnapshotsPage.xaml','SnapshotCount'),
  'BilibiliDetails.QrAreaVisibility':('Views/Drops/Bilibili/BilibiliOverview.xaml','BilibiliDetails.QrImageVisibility'),
 }
 for old,(file,new) in replacements.items():
  if any(re.search(r'\{Binding\s+'+re.escape(old)+r'[,}]',v) for v in row['contract'].values()) and new in (ROOT/file).read_text(encoding='utf-8-sig'):
   row['evidence']=['结构化可视化替代：'+old+' → '+new,file];break

OUT.mkdir(exist_ok=True)
(OUT/'feature-mapping.json').write_text(json.dumps(baseline,ensure_ascii=False,indent=2),encoding='utf-8')
missing=[r for r in baseline if not r['evidence']]
(OUT/'mapping-review.json').write_text(json.dumps(missing,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({'baseline':len(baseline),'mapped':len(baseline)-len(missing),'review':len(missing)},ensure_ascii=False))
def esc(value):return str(value).replace('|','\\|').replace('\n',' ')
lines=['# 第二代 UI 功能迁移矩阵','','基线为上一轮最终 763 条 UI 契约（早期 754）；不是 763 个独立业务功能。每行保留原事件、命令、输入或状态绑定来源。共享视觉投影和组件作用域变化单独注明。静态映射不等于真实账号操作已执行。','',f'静态映射 {len(baseline)-len(missing)} / {len(baseline)}；待核对 {len(missing)}。运行证据见 regression-results.md；真实登录、Claim、游戏覆盖按用户要求不执行。','','| ID | 原页面 / 控件 | 原事件或绑定 | 新入口 / 证据 | 验证层级 |','|---|---|---|---|---|']
for r in baseline:
 lines.append('| '+' | '.join(map(esc,[r['id'],r['file']+' / '+(r['name'] or r['label'] or r['kind']),json.dumps(r['contract'],ensure_ascii=False),' · '.join(r['evidence']) or '待核对','静态；运行验证按测试记录']))+' |')
(OUT/'feature-mapping.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
for r in missing:print(r['id'],r['kind'],r['name'],r['label'],r['contract'])

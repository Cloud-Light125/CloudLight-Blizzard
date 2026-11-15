"""Inventory the baseline UI contract and compare the migrated front end.

Static reachability is deliberately reported separately from runtime verification.
Run with Python's standard library; no packages or credentials are required.
"""
import collections
import json
import pathlib
import re
import subprocess
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parents[1]
OUT = ROOT / 'docs/ui-redesign'
BASE = '813e6deda6ded9be8d76392594e38e859875738d'
NS = '{http://schemas.microsoft.com/winfx/2006/xaml/presentation}'
X = '{http://schemas.microsoft.com/winfx/2006/xaml}'
CONTROLS = set('Button RadioButton CheckBox ToggleButton ComboBox TextBox PasswordBox Expander ListBox ListView DataGrid ItemsControl MenuItem Hyperlink'.split())
EVENTS = set('Click Checked Unchecked SelectionChanged TextChanged PasswordChanged MouseDoubleClick RequestNavigate Loaded Unloaded KeyDown PreviewKeyDown Closing Closed'.split())


def git(*args):
    return subprocess.check_output(['git', *args], cwd=ROOT).decode('utf-8-sig')


def inventory(files):
    rows = []
    for file, source in files:
        if file.endswith('.xaml'):
            tree = ET.fromstring(source)
            def walk(el, location):
                kind = el.tag.split('}')[-1]
                title = el.get('Header', el.get('Title', ''))
                if title and not title.startswith('{'):
                    location = location + ' / ' + title
                if el.get(X+'Name','').endswith('Panel'):
                    location += ' / '+el.get(X+'Name')[:-5]
                if el.get('Tag','').startswith('workspace:'):
                    location += ' / '+el.get('Tag')[10:]
                attrs = {k.split('}')[-1]: v for k, v in el.attrib.items()}
                contracts = {k: v for k, v in attrs.items() if k in EVENTS or 'Command' in k or ('{Binding ' in v)}
                if kind in CONTROLS or contracts:
                    name = el.get(X+'Name', el.get('Name', ''))
                    # Layout and visual copy may change; behavior and data paths may not disappear.
                    identity = [kind, name, sorted(contracts.items())]
                    if not contracts and not name:
                        identity += [attrs.get('Content', attrs.get('Header', ''))]
                    rows.append(dict(file=file, kind=kind, name=name,
                        label=attrs.get('Content', attrs.get('Header', attrs.get('Text', ''))),
                        location=location, contract=contracts, identity=json.dumps(identity, ensure_ascii=False)))
                for child in el:
                    walk(child, location)
            walk(tree, pathlib.Path(file).stem)
        else:
            for i, line in enumerate(source.splitlines(), 1):
                if re.search(r'MessageBox\.Show|ShowDialog\(|\bICommand\b|\bCommand\s*=>', line):
                    rows.append(dict(file=file, kind='confirmation/command', name='', label=line.strip(),
                        location=f'{file}:{i}', contract={}, identity=line.strip()))
    return rows


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    files = [x for x in git('ls-tree','-r','--name-only',BASE).splitlines()
        if (x.startswith(('Views/','Controls/','Stats/')) and x.endswith(('.xaml','.xaml.cs')) and '/Theme/' not in x)]
    baseline = inventory((f,git('show',f'{BASE}:{f}')) for f in files)
    current_files = sorted({str(p.relative_to(ROOT)).replace('\\','/') for d in ('Views','Controls','Stats')
        if (ROOT/d).exists() for p in (ROOT/d).rglob('*') if p.name.endswith(('.xaml','.xaml.cs'))})
    current = inventory((f,(ROOT/f).read_text(encoding='utf-8-sig')) for f in current_files)
    remaining = list(current)
    for i,r in enumerate(baseline,1):
        r['id'] = f'UI-{i:04}'
        match = next((n for n in remaining if n['identity']==r['identity']),None)
        if match is None:
            # Adding a name for a filter/test hook is not removal of an unnamed baseline control.
            match = next((n for n in remaining if n['kind']==r['kind'] and (not r['name'] or n['name']==r['name'])
                and all(n['contract'].get(k)==v for k,v in r['contract'].items())
                and (r['name'] or n['label']==r['label'])),None)
        r['preserved'] = match is not None
        if match is not None: remaining.remove(match)
        r['new_location'] = match['location'] if match else '需核对'
    (OUT/'inventory.json').write_text(json.dumps(baseline,ensure_ascii=False,indent=2),encoding='utf-8')
    def esc(v): return str(v).replace('|','\\|').replace('\n',' ')
    lines = ['# UI 功能迁移映射', '', f'基线：`{BASE}`。每行对应实际控件、绑定、事件或确认入口；静态保留不等于运行验证。', '',
        '| ID | 原有页面/功能 | 原控件与事件/绑定 | 重构后页面与入口 | 保留 | 验证 |','|---|---|---|---|---|---|']
    for r in baseline:
        lines.append('| '+' | '.join(map(esc,[r['id'],r['location']+' / '+r['label'],
            r['kind']+' '+r['name']+' '+json.dumps(r['contract'],ensure_ascii=False),r['new_location'],
            '是' if r['preserved'] else '待人工核对','静态契约匹配' if r['preserved'] else '待核对']))+' |')
    (OUT/'migration-map.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
    summary=dict(baseline=BASE,total=len(baseline),statically_preserved=sum(r['preserved'] for r in baseline),
        missing=[{'id':r['id'],'file':r['file'],'kind':r['kind'],'name':r['name'],'label':r['label'],'contract':r['contract']} for r in baseline if not r['preserved']],
        per_file=dict(collections.Counter(r['file'] for r in baseline)))
    (OUT/'inventory-summary.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2),encoding='utf-8')
    properties = re.findall(r'^\s*public\s+[\w<>?\[\], ]+\s+(\w+)\s*\{\s*get;\s*set;\s*\}', git('show',f'{BASE}:Services/AppSettings.cs'),re.M)
    settings_source=(ROOT/'Services/AppSettings.cs').read_text(encoding='utf-8-sig')
    missing_settings=[p for p in properties if not re.search(r'\b'+re.escape(p)+r'\s*\{\s*get;\s*set;',settings_source)]
    feature_lines=['# 前端功能盘点','',f'基线 UI 契约 {len(baseline)} 条，包含所有 Views、Controls 与 Stats XAML、绑定、交互控件和确认/命令代码入口。',
        '这是可审计的声明条目数，不是互不重复的业务功能数。每条入口可能包含多项状态或保护。','',
        '| 源文件 | 契约数量 |','|---|---|']
    feature_lines += [f'| {f} | {n} |' for f,n in summary['per_file'].items()]
    feature_lines += ['',f'## 原有设置字段（{len(properties)}）','',f'缺失字段：{missing_settings or "无"}。序列化、默认值与保存服务未改动；仅扩展总览导航取值。','']
    feature_lines += [f'- `{p}`' for p in properties]
    feature_lines += ['', '## 页面事件与 Command 对照','']
    for f in files:
        if not f.endswith('.xaml.cs'):continue
        source=git('show',f'{BASE}:{f}')
        handlers=re.findall(r'^\s*(?:private|public|internal|protected)\s+(?:async\s+)?(?:void|Task(?:<[^>]+>)?)\s+(On\w+)\s*\(',source,re.M)
        if handlers: feature_lines += [f'### {f}','',', '.join(f'`{h}`' for h in handlers),'']
    (OUT/'feature-inventory.md').write_text('\n'.join(feature_lines)+'\n',encoding='utf-8')
    print(json.dumps({k:v for k,v in summary.items() if k!='per_file'},ensure_ascii=False))
    if summary['missing'] or missing_settings:
        raise SystemExit(1)


if __name__=='__main__': main()

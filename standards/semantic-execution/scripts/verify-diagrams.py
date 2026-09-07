from pathlib import Path
import re, sys
root=Path(__file__).resolve().parent.parent
diagrams=root/'diagrams'
errors=[]
pumls={p.stem for p in diagrams.glob('*.puml')}
svgs={p.stem for p in diagrams.glob('*.svg')}
for p in sorted(pumls-svgs): errors.append(f'Missing SVG for {p}.puml')
for s in sorted(svgs-pumls): errors.append(f'SVG has no PlantUML source: {s}.svg')
for md in sorted(root.glob('*.md')):
    t=md.read_text(encoding='utf-8')
    for rel in re.findall(r'!\[[^\]]*\]\((diagrams/[^)]+\.svg)\)',t):
        if not (root/rel).exists(): errors.append(f'{md.name}: missing {rel}')

# Diagram policy: rendered diagrams must be SVG artifacts backed by PlantUML.
# The canonical README architecture diagram is explicitly checked so it cannot
# regress to an ASCII/text flowchart.
readme=root/'README.md'
if readme.exists():
    t=readme.read_text(encoding='utf-8')
    if re.search(r'```text\s*\n\s*Caller\s*\n.*?Evidence\s*\n```', t, re.S):
        errors.append('README.md: canonical architecture diagram must be PlantUML-backed, not an inline text diagram')
    for rel in re.findall(r'!\[[^\]]*\]\((diagrams/[^)]+\.svg)\)', t):
        stem=Path(rel).stem
        if stem not in pumls:
            errors.append(f'README.md: {rel} has no matching PlantUML source')
if errors:
    print('Diagram verification failed:')
    print('\n'.join(' - '+e for e in errors)); sys.exit(1)
print(f'OK: {len(pumls)} PlantUML sources have matching SVGs and all Markdown embeds resolve.')

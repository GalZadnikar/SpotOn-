import json, os
from core import *
from graph import analyse
from pick import est

GROW = {(0, -1): 'up', (0, 1): 'down', (-1, 0): 'left', (1, 0): 'right'}
CN = ['coral', 'green', 'purple', 'orange']
KIND = {1: 'one colour', 2: 'two colours', 3: 'three colours'}

def make_note(c):
    f = c['f']; lv = Level(c['rows'], c['colors'])
    groups = []
    for k, added, trimmed in f['seq']:
        if groups and groups[-1][0] == k: groups[-1][1] += 1; groups[-1][2] |= trimmed
        else: groups.append([k, 1, trimmed])
    parts = []
    first_trim = True
    for k, n, tr in groups:
        p = lv.pumps[k]
        d = GROW[(p[2], p[3])]
        s = ('%s %s %d' % (CN[p[4]], d, n)) if lv.ncol > 1 else ('%s %d' % (d, n))
        if tr:
            s += ' (wall trims it)' if first_trim else ' (trimmed)'
            first_trim = False
        parts.append(s)
    note = 'generated, %s, est. difficulty %.1f. Grow %s.' % (KIND[lv.ncol], min(5.0, est(f)), ', '.join(parts))
    tags = []
    if f['mode'] != 'full': tags.append('Only the marks need covering.')
    if f['switches'] >= 2 and f['bits'] >= 2: tags.append('Order matters.')
    if f['over']: tags.append('One press too many spoils it.')
    if f['decoys']: tags.append('One pump is not needed.')
    return note + (' ' + ' '.join(tags) if tags else '')

orig = parse_file(open('levels_now.txt').read())
ch = json.load(open('chosen.json'))
new = [dict(par=c['f']['par'], rating='', note=make_note(c), rows=c['rows'], colors=c['colors'], _c=c)
       for n in ('1', '2', '3') for c in ch[n]]
# every rated level is the designer's: it stays exactly as it is, in its place. Only unrated ones are replaced.
out = [d for d in orig if d['rating'].strip()]
KEPT = len(out)
assert all(orig[i]['rating'].strip() for i in range(KEPT)), 'rated levels are not all at the front'
out += new

# verify everything with the port of Solver.cs
bad = 0
for i, d in enumerate(out):
    lv = Level(d['rows'], d['colors'])
    r = solve(lv)
    w = set(len(x) for x in d['rows'])
    okc = d['colors'] is None or (len(d['colors']) == len(d['rows']) and all(len(a) == len(b) for a, b in zip(d['rows'], d['colors'])))
    problems = []
    if not r['solvable']: problems.append('NOT SOLVABLE')
    elif r['par'] != d['par']: problems.append('par %d != solver %d' % (d['par'], r['par']))
    if len(w) != 1: problems.append('ragged rows')
    if not okc: problems.append('colour layer size')
    if lv.edge_floor: problems.append('floor on edge')
    if sum(row.count('H') for row in d['rows']) != 1: problems.append('head count')
    for p in lv.pumps:
        if not p[5]: problems.append('dead pump')
    if lv.W > 16 or lv.H > 16: problems.append('too big for editor')
    if problems: bad += 1
    print('L%-2d par %-2d states %-5d %s' % (i + 1, d['par'], r['states'], '; '.join(problems) or 'ok'))
print('problems:', bad, 'levels:', len(out))
os.makedirs('/mnt/user-data/outputs', exist_ok=True)
open('/mnt/user-data/outputs/Levels.txt', 'w', newline='\n').write(write_file(out))
print('kept', KEPT, 'rated levels')
for i, d in enumerate(out):
    if i >= KEPT: print('%d: T %d/%d  %s' % (i + 1, len(Level(d['rows'], d['colors']).targets), bin(Level(d['rows'], d['colors']).floor).count('1'), d['note']))

import sys, json
from core import *
from pick import *
GROW = {(0, -1): 'up', (0, 1): 'down', (-1, 0): 'left', (1, 0): 'right'}
CN = ['coral', 'green', 'purple', 'orange']
def outline(lv, f):
    groups = []
    for k, added, trimmed in f['seq']:
        if groups and groups[-1][0] == k: groups[-1][1] += 1; groups[-1][2] |= trimmed
        else: groups.append([k, 1, trimmed])
    parts = []
    for k, n, tr in groups:
        p = lv.pumps[k]
        parts.append('%s%s x%d%s' % (GROW[(p[2], p[3])], (' ' + CN[p[4]]) if lv.ncol > 1 else '', n, ' (trimmed)' if tr else ''))
    return ', '.join(parts)
def show(c, tag=''):
    f = c['f']; lv = Level(c['rows'], c['colors'])
    print('%s est %.1f (model %.1f) int %.1f | par %d presses %d pumps %d decoy %d | trim %d sw %d traps %d bits %.1f prand %.2f over %d | states %d wins %d mode %s' % (
        tag, est(f), f['rating'], interest(c), f['par'], f['presses'], f['pumps'], f['decoys'], f['partial'], f['switches'], f['traps'], f['bits'], f['p_random'], f['over'], f['states'], f['win_comps'], f['mode']))
    print('   ' + outline(lv, f))
    fin = lv.render(tuple(f['final'])).split('\n')
    for i, r in enumerate(c['rows']):
        print('   ' + r + '   ' + (c['colors'][i] if lv.ncol > 1 else '') + '   ' + fin[i])
if __name__ == '__main__':
    n = int(sys.argv[1]); lo = float(sys.argv[2]); hi = float(sys.argv[3]); k = int(sys.argv[4])
    L = [c for c in load('pool/c%d.jsonl' % n) if ok(c) and lo <= c['f']['rating'] < hi]
    L.sort(key=interest, reverse=True)
    shown = []
    for c in L:
        sig = (len(c['rows'][0]), len(c['rows']))
        if sum(1 for s in shown if s == sig) >= 1: continue
        shown.append(sig); show(c)
        if len(shown) >= k: break

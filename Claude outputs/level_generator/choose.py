import json, sys, random
from core import *
from pick import load, ok, interest, est, tier_ok
from view import show, outline

SLOTS = {
    1: [2.0, 2.3, 2.7, 3.0, 3.0, 3.3, 3.9, 3.2, 3.7, 4.0, 4.2, 4.8, 4.3, 4.7, 5.0],
    2: [1.3, 1.8, 2.2, 2.7, 3.0, 3.3, 3.9, 3.2, 3.8, 4.1, 4.8, 4.3, 4.8, 5.0],
    3: [1.8, 2.2, 2.8, 3.0, 3.3, 3.8, 4.0, 4.3, 4.7, 4.9, 5.1],
}

def regions(c):
    lv = Level(c['rows'], c['colors'])
    cells = {}
    for b, col in lv.targets.items():
        i = b.bit_length() - 1
        cells[(i % lv.W, i // lv.W)] = col
    seen = set(); n = 0
    for p in cells:
        if p in seen: continue
        n += 1; st = [p]; seen.add(p)
        while st:
            x, y = st.pop()
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                q = (x + dx, y + dy)
                if q in cells and q not in seen and cells[q] == cells[p]: seen.add(q); st.append(q)
    return n

def walls(c):
    return tuple(''.join('#' if ch in '#^v<>' else '.' for ch in r) for r in c['rows'])

def choose(ban=()):
    chosen = {}
    used_walls = set()
    for n in (1, 2, 3):
        pool = [c for c in load('pool/c%d.jsonl' % n) if ok(c)]
        for c in pool: c['int'] = interest(c)
        picks = []
        sizes = {}
        for si, want in enumerate(SLOTS[n]):
            best = None; bests = -1e9
            prefer_open = (si % 3 == 2)
            for c in pool:
                f = c['f']
                if abs(est(f) - want) > 0.22 or not tier_ok(f, want): continue
                if want >= 4.9 and f['states'] < 900: continue
                key = '\n'.join(c['rows'])
                if key in ban: continue
                w = walls(c)
                if w in used_walls: continue
                sig = (len(c['rows'][0]), len(c['rows']))
                if sizes.get(sig, 0) >= 2: continue
                if picks and (len(picks[-1]['rows'][0]), len(picks[-1]['rows'])) == sig: continue
                if n > 1:
                    r = regions(c)
                    if r > (n + 1 if want <= 3.5 else n + 2): continue
                s = c['int']
                if prefer_open: s += 1.2 * (f['mode'] == 'line') + 0.4 * (f['mode'] == 'spots')
                else: s += 0.5 * (f['mode'] == 'full')
                if picks and picks[-1]['f']['pumps'] == f['pumps']: s -= 0.3
                s -= 0.25 * max(0, f['xcount'] - 3)
                if s > bests: bests = s; best = c
            if best is None:
                print('no candidate for', n, want); continue
            best['want'] = want
            picks.append(best)
            used_walls.add(walls(best))
            sig = (len(best['rows'][0]), len(best['rows']))
            sizes[sig] = sizes.get(sig, 0) + 1
        chosen[n] = picks
    return chosen

if __name__ == '__main__':
    ban = set()
    try: ban = set(json.load(open('ban.json')))
    except Exception: pass
    ch = choose(ban)
    json.dump(ch, open('chosen.json', 'w'))
    only = int(sys.argv[1]) if len(sys.argv) > 1 else 0
    idx = 15
    for n in (1, 2, 3):
        for c in ch[n]:
            if not only or only == n: show(c, 'L%d [%dc want %.1f]' % (idx, n, c['want']))
            idx += 1

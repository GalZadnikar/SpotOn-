import json, sys, math, collections
from core import *

def load(path):
    seen = set(); out = []
    for l in open(path):
        c = json.loads(l)
        key = '\n'.join(c['rows']) + '|' + '\n'.join(c['colors'])
        if key in seen: continue
        seen.add(key); out.append(c)
    return out

def est(f):
    """Model fitted on the 14 rated levels, with a floor for small-but-tricky levels
    (the rated set has none of those, so the fit alone would call them easy)."""
    return max(f['rating'], 1.5 + 0.6 * min(f['bits'], 4.5))

TIERS = [  # want-range, bits range, states range, max presses
    (0.0, 2.5, (0.0, 1.2), (40, 300), 6),
    (2.5, 3.5, (1.0, 3.6), (110, 700), 7),
    (3.5, 4.5, (1.5, 6.5), (200, 2500), 9),
    (4.5, 9.9, (1.5, 99.0), (400, 99999), 12),
]
def tier_ok(f, want):
    for lo, hi, (b0, b1), (s0, s1), pmax in TIERS:
        if lo <= want < hi:
            return b0 <= f['bits'] <= b1 and s0 <= f['states'] <= s1 and f['presses'] <= pmax
    return False

def ok(c):
    f = c['f']; rows = c['rows']
    W = len(rows[0]); H = len(rows)
    if f['par'] < 4 or f['par'] > 45: return False
    if f['targets'] > 9 or f['final_cells'] > 16: return False
    if f['presses'] > 12 or f['xcount'] > 5 or f['decoys'] > 1: return False
    if f['floor'] < 12: return False
    if f['footprint'] / f['floor'] < 0.2: return False
    if est(f) >= 2.6 and f['pumps_used'] < 2: return False
    return True

def density(c):
    return c['f']['targets'] / c['f']['floor']

def interest(c):
    f = c['f']; rows = c['rows']
    W = len(rows[0]); H = len(rows)
    s = 0.0
    s += 1.0 * min(f['partial'], 2)
    s += 0.35 * min(f['switches'], 3)
    s += 0.3 * min(f['bits'], 4)
    s += 0.6 * (f['win_comps'] == 1)
    s += 0.8 * f['footprint'] / f['floor']
    s -= 0.4 * f['decoys']
    s -= 0.08 * max(0, f['presses'] - 7)
    s += 0.8 * f['floor'] / ((W - 2) * (H - 2))      # tidy, not ragged
    s -= 0.04 * max(0, f['walk'] - 16)
    return s

if __name__ == '__main__':
    for n in (1, 2, 3):
        L = [c for c in load('pool/c%d.jsonl' % n) if ok(c)]
        h = collections.Counter(min(6, max(0, round(c['f']['rating']))) for c in L)
        print(n, 'colours:', len(L), sorted(h.items()))

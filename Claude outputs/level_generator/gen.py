"""Level generator: random room layout -> play forward -> pick a goal -> marks + forbidden tiles -> score."""
import random, json, sys, time, math
from core import *
from graph import explore, analyse, popcount

CW, CH = 10, 10     # largest interior


def carve_layout(rng):
    g = [[False] * CW for _ in range(CH)]
    def rect(x0, y0, w, h, val=True):
        for y in range(y0, y0 + h):
            for x in range(x0, x0 + w):
                g[y][x] = val
    def touches(x0, y0, w, h):
        for y in range(y0, y0 + h):
            for x in range(x0, x0 + w):
                for dx, dy in ((0, 0), (1, 0), (-1, 0), (0, 1), (0, -1)):
                    xx, yy = x + dx, y + dy
                    if 0 <= xx < CW and 0 <= yy < CH and g[yy][xx]: return True
        return False
    w = rng.randint(4, 9); h = rng.randint(3, 7)
    x0 = rng.randint(0, CW - w); y0 = rng.randint(0, CH - h)
    rect(x0, y0, w, h)
    for _ in range(rng.choice([0, 0, 1, 1, 2])):
        for _try in range(20):
            kind = rng.choice(['wing', 'wing', 'shaft', 'shaft', 'room'])
            if kind == 'wing': w2, h2 = rng.randint(2, 5), rng.randint(2, 4)
            elif kind == 'shaft':
                w2, h2 = (rng.randint(1, 2), rng.randint(2, 5))
                if rng.random() < 0.5: w2, h2 = h2, w2
            else: w2, h2 = rng.randint(2, 4), rng.randint(2, 4)
            if w2 > CW or h2 > CH: continue
            x2 = rng.randint(0, CW - w2); y2 = rng.randint(0, CH - h2)
            if touches(x2, y2, w2, h2):
                rect(x2, y2, w2, h2); break
    # partition with a door
    if rng.random() < 0.25:
        for _try in range(10):
            if rng.random() < 0.5:
                y = rng.randint(1, CH - 2)
                xs = [x for x in range(CW) if g[y][x]]
                if len(xs) >= 2 and any(g[y - 1][x] for x in xs) and any(g[y + 1][x] for x in xs):
                    doors = rng.sample(xs, 1 if rng.random() < 0.7 else 2)
                    for x in xs:
                        if x not in doors: g[y][x] = False
                    break
            else:
                x = rng.randint(1, CW - 2)
                ys = [y for y in range(CH) if g[y][x]]
                if len(ys) >= 2 and any(g[y][x - 1] for y in ys) and any(g[y][x + 1] for y in ys):
                    doors = rng.sample(ys, 1 if rng.random() < 0.7 else 2)
                    for y in ys:
                        if y not in doors: g[y][x] = False
                    break
    # pillars
    for _ in range(rng.choice([0, 1, 1, 2, 2, 3, 4])):
        fl = [(x, y) for y in range(CH) for x in range(CW) if g[y][x]]
        if not fl: break
        x, y = rng.choice(fl)
        g[y][x] = False
        if rng.random() < 0.3:
            dx, dy = rng.choice(((1, 0), (0, 1)))
            if 0 <= x + dx < CW and 0 <= y + dy < CH: g[y + dy][x + dx] = False
    # keep the largest connected part
    seen = set(); best = []
    for y in range(CH):
        for x in range(CW):
            if g[y][x] and (x, y) not in seen:
                comp = [(x, y)]; seen.add((x, y)); i = 0
                while i < len(comp):
                    cx, cy = comp[i]; i += 1
                    for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                        nx, ny = cx + dx, cy + dy
                        if 0 <= nx < CW and 0 <= ny < CH and g[ny][nx] and (nx, ny) not in seen:
                            seen.add((nx, ny)); comp.append((nx, ny))
                if len(comp) > len(best): best = comp
    if len(best) < 16: return None
    xs = [c[0] for c in best]; ys = [c[1] for c in best]
    W = max(xs) - min(xs) + 3; H = max(ys) - min(ys) + 3
    grid = [['#'] * W for _ in range(H)]
    for x, y in best: grid[y - min(ys) + 1][x - min(xs) + 1] = '.'
    return grid


def place_pumps(rng, grid, n, ncol):
    H = len(grid); W = len(grid[0])
    cand = []
    for y in range(H):
        for x in range(W):
            if grid[y][x] != '#': continue
            if (x in (0, W - 1)) and (y in (0, H - 1)): continue
            for ch, (dx, dy) in PUMP.items():
                nx, ny = x + dx, y + dy
                if 0 <= nx < W and 0 <= ny < H and grid[ny][nx] == '.':
                    cand.append((x, y, ch))
    if len(cand) < n: return None
    rng.shuffle(cand)
    used = set(); pumps = []
    for x, y, ch in cand:
        if (x, y) in used: continue
        used.add((x, y)); pumps.append((x, y, ch))
        if len(pumps) == n: break
    if len(pumps) < n: return None
    # colours: every colour above 0 needs at least one pump
    cols = [rng.randrange(ncol) for _ in pumps]
    need = list(range(1, ncol))
    if len(pumps) < len(need): return None
    slots = rng.sample(range(len(pumps)), len(need))
    for s, c in zip(slots, need): cols[s] = c
    return [(x, y, ch, c) for (x, y, ch), c in zip(pumps, cols)]


def build(grid, pumps, head, targets=None, forbidden=(), ncol=1, body=()):
    """targets: dict (x,y)->colour"""
    H = len(grid); W = len(grid[0])
    m = [row[:] for row in grid]
    c = [['.'] * W for _ in range(H)]
    for x, y, ch, col in pumps:
        m[y][x] = ch; c[y][x] = str(col)
    m[head[1]][head[0]] = 'H'; c[head[1]][head[0]] = '0'
    for x, y in body: m[y][x] = 'P'; c[y][x] = '0'
    for (x, y), col in (targets or {}).items():
        m[y][x] = 'T'; c[y][x] = str(col)
    for x, y in forbidden: m[y][x] = 'X'
    return [''.join(r) for r in m], [''.join(r) for r in c]


def bits_of(lv, cells):
    b = 0
    for x, y in cells: b |= 1 << (y * lv.W + x)
    return b


def candidates(rng, ncol, want_big=False, max_states=9000):
    """Yield scored levels built on one random layout."""
    grid = carve_layout(rng)
    if grid is None: return []
    H = len(grid); W = len(grid[0])
    if W > 12 or H > 12: return []
    if ncol == 1: n = rng.choice([1, 2, 2, 2, 3, 3, 4])
    elif ncol == 2: n = rng.choice([1, 2, 2, 2, 3, 3, 4])
    else: n = rng.choice([2, 3, 3, 3, 4])
    pumps = place_pumps(rng, grid, n, ncol)
    if pumps is None: return []
    fl = [(x, y) for y in range(H) for x in range(W) if grid[y][x] == '.']
    head = rng.choice(fl)
    body = []
    if rng.random() < 0.15:                 # start as a small bar, like the designer's later levels
        dx, dy = rng.choice(((1, 0), (0, 1)))
        for k in range(1, rng.choice([2, 2, 3])):
            q = (head[0] + dx * k, head[1] + dy * k)
            if q in fl: body.append(q)
            else: break
    nfloor = len(fl)
    rows, cols = build(grid, pumps, head, body=body)
    lv = Level(rows, cols)
    G = explore(lv, max_states)
    if G is None: return []
    states, edges = G
    if len(states) < 25: return []
    # press depth per state (fewest presses), plain BFS depth too
    N = len(states)
    press = [10 ** 6] * N; press[0] = 0
    dq = deque([0])
    while dq:                       # 0-1 BFS
        i = dq.popleft()
        for d, k, j in edges[i]:
            w = 0 if k == 'm' else 1
            if press[i] + w < press[j]:
                press[j] = press[i] + w
                (dq.appendleft if w == 0 else dq.append)(j)
    headbit = 0
    for q in [head] + body: headbit |= 1 << (q[1] * W + q[0])
    pool = [i for i in range(N) if 1 <= press[i] <= 12 and not (sum(states[i]) & headbit) and all(states[i][c] for c in range(ncol))
            and popcount(sum(states[i])) <= 24]
    if ncol == 1 and rng.random() < 0.15:
        pool += [i for i in range(N) if press[i] == 1 and not (sum(states[i]) & headbit)]
    if not pool: return []
    out = []
    for gi in rng.sample(pool, min(4, len(pool))):
        st = states[gi]
        cells = lv.cells(st)
        allm = sum(st)
        mode = rng.choice(['full', 'full', 'full', 'line', 'line', 'spots'])
        T = dict(cells)
        if mode == 'line' and len(cells) >= 3:
            xs = [c[0] for c in cells]; ys = [c[1] for c in cells]
            side = rng.choice(['top', 'bottom', 'left', 'right'])
            if side == 'top': T = {c: v for c, v in cells.items() if c[1] == min(ys)}
            elif side == 'bottom': T = {c: v for c, v in cells.items() if c[1] == max(ys)}
            elif side == 'left': T = {c: v for c, v in cells.items() if c[0] == min(xs)}
            else: T = {c: v for c, v in cells.items() if c[0] == max(xs)}
        elif mode == 'spots' and len(cells) >= 4:
            cl = list(cells)
            a = rng.choice(cl)
            b = max(cl, key=lambda c: abs(c[0] - a[0]) + abs(c[1] - a[1]))
            a = max(cl, key=lambda c: abs(c[0] - b[0]) + abs(c[1] - b[1]))
            T = {a: cells[a], b: cells[b]}
            if rng.random() < 0.4:
                c3 = rng.choice(cl); T[c3] = cells[c3]
        if len(set(T.values())) < ncol or len(T) < 2:
            T = dict(cells); mode = 'full'
        # open levels: few marks in a lot of room (a small share of crowded ones is let through)
        if len(T) > max(2, 0.2 * nfloor) and rng.random() < 0.9:
            cl = list(cells)
            T = None
            for _t in range(6):
                a = rng.choice(cl)
                line = [c for c in cl if c[1] == a[1]] if rng.random() < 0.5 else [c for c in cl if c[0] == a[0]]
                cand = {c: cells[c] for c in line}
                if len(line) < 2:
                    b = max(cl, key=lambda c: abs(c[0] - a[0]) + abs(c[1] - a[1]))
                    cand = {a: cells[a], b: cells[b]}
                if 2 <= len(cand) <= max(2, 0.2 * nfloor) and len(set(cand.values())) == ncol:
                    T = cand; mode = 'line'; break
            if T is None: continue
        tb = bits_of(lv, T)
        tcol = [bits_of(lv, [c for c, v in T.items() if v == k]) for k in range(ncol)]
        # other reachable positions that would also win: collect what they cover beyond the goal blob
        extras = []
        for s in states:
            a = sum(s)
            if a & tb != tb or s == st: continue
            if any((s[k] & tcol[k]) != tcol[k] for k in range(ncol)): continue
            e = a & ~allm
            if e: extras.append(e)
        X = 0
        xmode = rng.choice(['hit', 'hit', 'hit', 'some', 'none']) if mode == 'full' else rng.choice(['hit', 'some', 'some', 'none'])
        kmax = {'hit': 5, 'some': rng.randint(1, 2), 'none': 0}[xmode]
        rem = extras
        # prefer tiles next to the marks
        near = 0
        for dd in DIRS: near |= lv.shift(tb, dd)
        near &= lv.floor & ~allm
        while rem and popcount(X) < kmax:
            cnt = {}
            for e in rem:
                m = e
                while m:
                    low = m & -m; cnt[low] = cnt.get(low, 0) + 1; m ^= low
            bestb = max(cnt, key=lambda b: (cnt[b] + (0.5 * len(rem) if b & near else 0), rng.random()))
            if bestb & headbit:
                del cnt[bestb]
                if not cnt: break
                bestb = max(cnt, key=lambda b: cnt[b])
            X |= bestb
            rem = [e for e in rem if not (e & bestb)]
        if xmode == 'hit' and rem and mode == 'full' and rng.random() < 0.7:
            continue        # shape could not be pinned down
        Xc = [(i % W, i // W) for i in range(W * H) if (X >> i) & 1]
        rows2, cols2 = build(grid, pumps, head, T, Xc, body=body)
        lv2 = Level(rows2, cols2)
        f = analyse(lv2, G)
        if f is None: continue
        used = set(s[0] for s in f['seq'])
        unneeded = [i for i in range(len(lv2.pumps)) if i not in used]
        keep_decoy = 1 if rng.random() < 0.4 else 0
        if len(unneeded) > keep_decoy:
            drop = set(rng.sample(unneeded, len(unneeded) - keep_decoy))
            # pumps are stored in reading order in Level; match by cell
            dropcells = set((lv2.pumps[i][0], lv2.pumps[i][1]) for i in drop)
            pumps2 = [p for p in pumps if (p[0], p[1]) not in dropcells]
            if ncol > 1 and set(range(1, ncol)) - set(p[3] for p in pumps2): continue
            rows2, cols2 = build(grid, pumps2, head, T, Xc, body=body)
            lv2 = Level(rows2, cols2)
            f = analyse(lv2, None, max_states)
            if f is None: continue
            used = set(s[0] for s in f['seq'])
        f = dict(f)
        f['decoys'] = len(lv2.pumps) - len(used)
        f['mode'] = mode; f['targets'] = len(T); f['xcount'] = len(Xc)
        f['colors_in_targets'] = len(set(T.values()))
        f['final'] = list(f['final']); f['moves'] = [[d[0], d[1], k] for d, k in f['moves']]
        out.append(dict(rows=rows2, colors=cols2, f=f, ncol=ncol))
    return out


if __name__ == '__main__':
    ncol = int(sys.argv[1]); seed = int(sys.argv[2]); seconds = float(sys.argv[3]); outp = sys.argv[4]
    rng = random.Random(seed)
    t0 = time.time(); n = 0; tried = 0
    with open(outp, 'w') as fo:
        while time.time() - t0 < seconds:
            tried += 1
            for c in candidates(rng, ncol):
                fo.write(json.dumps(c) + '\n'); n += 1
    print('ncol', ncol, 'layouts', tried, 'levels', n, file=sys.stderr)

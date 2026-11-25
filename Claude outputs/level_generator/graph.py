"""Shared state-graph tools: explore once per layout, then score many target choices."""
from core import *
import math, sys
sys.setrecursionlimit(1000000)


def explore(lv, max_states=8000):
    """Everything reachable, ignoring targets (nothing is terminal)."""
    start = lv.start
    idx = {start: 0}
    states = [start]
    edges = []
    i = 0
    apply = lv.apply
    while i < len(states):
        st = states[i]
        out = []
        for d in DIRS:
            ns, kind = apply(st, d)
            if ns is None: continue
            j = idx.get(ns)
            if j is None:
                j = len(states)
                if j >= max_states: return None
                idx[ns] = j; states.append(ns)
            out.append((d, kind, j))
        edges.append(out)
        i += 1
    return states, edges


def popcount(x):
    return bin(x).count('1')


def analyse(lv, G=None, max_states=8000):
    """Difficulty features of level lv (with its targets / forbidden tiles).
    G = explore() of the same layout (targets do not change what is reachable, they only end the game)."""
    if G is None:
        G = explore(lv, max_states)
        if G is None: return None
    states, edges = G
    N = len(states)
    solved = [False] * N
    # BFS from the start, not expanding solved states
    depth = [-1] * N
    depth[0] = 0
    solved[0] = lv.solved(states[0])
    order = [0]
    q = deque([0])
    while q:
        i = q.popleft()
        if solved[i]: continue
        for d, k, j in edges[i]:
            if depth[j] < 0:
                depth[j] = depth[i] + 1
                solved[j] = lv.solved(states[j])
                order.append(j); q.append(j)
    wins = [i for i in order if solved[i]]
    if not wins: return None
    n = len(order)
    par = min(depth[i] for i in wins)
    rev = {i: [] for i in order}
    adj = {i: [] for i in order}
    for i in order:
        if solved[i]: continue
        for d, k, j in edges[i]:
            rev[j].append(i)
            if k == 'm': adj[i].append(j); adj[j].append(i)
    dist = {}
    dq = deque(wins)
    for w in wins: dist[w] = 0
    while dq:
        j = dq.popleft()
        for i in rev[j]:
            if i not in dist: dist[i] = dist[j] + 1; dq.append(i)
    comp = {}
    ncomp = 0
    for s in order:
        if s in comp: continue
        comp[s] = ncomp; st = [s]
        while st:
            a = st.pop()
            for b in adj[a]:
                if b not in comp: comp[b] = ncomp; st.append(b)
        ncomp += 1
    cwin = [False] * ncomp
    calive = [False] * ncomp
    for i in order:
        if solved[i]: cwin[comp[i]] = True
        if i in dist: calive[comp[i]] = True
    children = [set() for _ in range(ncomp)]
    for i in order:
        if solved[i]: continue
        for d, k, j in edges[i]:
            if k != 'm': children[comp[i]].add((comp[j], k))
    memo = {}
    def pwin(c):
        v = memo.get(c)
        if v is not None: return v
        if cwin[c]: memo[c] = 1.0; return 1.0
        ch = children[c]
        if not ch: memo[c] = 0.0; return 0.0
        v = sum(pwin(t) for t, _ in ch) / len(ch)
        memo[c] = v
        return v
    p_random = pwin(comp[0])
    cur = 0
    presses = 0
    seq = []            # (pump index, cells added, trimmed?)
    traps = 0
    bits = 0.0
    path_moves = []
    footprint = 0
    while not solved[cur]:
        footprint |= sum(states[cur])
        for d, k, j in edges[cur]:
            if dist.get(j, -1) == dist[cur] - 1: break
        c = comp[cur]
        if k != 'm':
            presses += 1
            ch = children[c]
            good = sum(1 for t, _ in ch if calive[t])
            if good < len(ch): traps += 1
            bits += math.log2(len(ch) / max(1, good))
            a = sum(states[cur]); b = sum(states[j])
            pd = (lv.pumps[k][2], lv.pumps[k][3])
            front = popcount(lv.shift(a, pd) & ~a)
            seq.append((k, popcount(b) - popcount(a), popcount(b) - popcount(a) < front))
        path_moves.append((d, k))
        cur = j
    footprint |= sum(states[cur])
    final = states[cur]
    # overshoot: from the shape that wins, is one more press possible and fatal?
    fc = comp[cur]
    # the winning component's states other than the solved one may still pump on
    over = 0
    for i in order:
        if comp[i] == fc and not solved[i]:
            for d, k, j in edges[i]:
                if k != 'm' and j not in dist: over = 1
    useful = set()
    for i in order:
        if solved[i] or i not in dist: continue
        for d, k, j in edges[i]:
            if k != 'm' and j in dist: useful.add(k)
    switches = sum(1 for a, b in zip(seq, seq[1:]) if a[0] != b[0])
    partial = sum(1 for s in seq if s[2])
    alive_n = len(dist)
    f = dict(par=par, states=n, comps=ncomp, presses=presses, pumps=len(lv.pumps),
             pumps_used=len(set(s[0] for s in seq)), useful=sorted(useful), switches=switches, traps=traps,
             bits=bits, p_random=p_random, dead_frac=1 - alive_n / n, partial=partial, over=over,
             final_cells=popcount(sum(final)), win_comps=sum(cwin), wins=len(wins), ncol=lv.ncol,
             walk=par - presses, floor=popcount(lv.floor), seq=seq, moves=path_moves, final=final,
             footprint=popcount(footprint))
    f['rating'] = predict(f)
    return f


def predict(f):
    """Calibrated on the designer's ratings of levels 1-14 (see fit.py)."""
    S = math.log2(f['states'])                                  # how much there is to explore
    R = min(6.0, -math.log2(max(f['p_random'], 1e-3)))          # how unlikely blind pumping is to win
    W = math.log2(1 + f['walk'])                                # carrying the shape around
    return -1.73 + 0.33 * S + 0.24 * R + 0.24 * f['pumps'] + 0.39 * W     # refitted on levels 1-15


if __name__ == '__main__':
    levels = parse_file(open(sys.argv[1]).read())
    n = int(sys.argv[2]) if len(sys.argv) > 2 else 14
    for i, d in enumerate(levels[:n]):
        lv = Level(d['rows'], d['colors'])
        f = analyse(lv, max_states=200000)
        if f is None: print(i + 1, 'UNSOLVABLE / too big'); continue
        print('%2d rated %-2s pred %.1f  par %2d (file %2d) presses %2d pumps %d useful %s sw %d part %d traps %d bits %.1f prand %.2f over %d states %d wins %d' % (
            i + 1, d['rating'], f['rating'], f['par'], d['par'], f['presses'], f['pumps'], f['useful'], f['switches'], f['partial'], f['traps'], f['bits'], f['p_random'], f['over'], f['states'], f['win_comps']))

"""Python port of ExpandPuzzle Rules + Solver (GridLogic.cs / Solver.cs), bitmask based."""
from collections import deque
import math

PUMP = {'^': (0, -1), 'v': (0, 1), '<': (-1, 0), '>': (1, 0)}
# Rules.Dirs order: up, right, down, left   (row coordinates: up = row-1)
DIRS = [(0, -1), (1, 0), (0, 1), (-1, 0)]
DNAME = {(0, -1): 'U', (1, 0): 'R', (0, 1): 'D', (-1, 0): 'L'}


def parse_file(text):
    levels = []
    cur = None
    incol = False
    for line in text.replace('\r', '').split('\n') + ['===']:
        line = line.rstrip()
        if line.startswith(';'):
            continue
        if line.startswith('==='):
            if cur and cur['rows']:
                if not cur['colors']:
                    cur['colors'] = None
                levels.append(cur)
            body = line[3:].strip()
            note = ''
            i = body.find('note=')
            if i >= 0:
                note = body[i + 5:].strip()
                body = body[:i]
            d = {'par': 0, 'rating': '', 'note': note, 'rows': [], 'colors': []}
            for tok in body.split(' '):
                if '=' in tok:
                    k, v = tok.split('=', 1)
                    if k == 'par':
                        try: d['par'] = int(v)
                        except ValueError: pass
                    elif k == 'rating':
                        d['rating'] = v
            cur = d
            incol = False
            continue
        if cur is None or not line:
            continue
        if line.startswith('---'):
            incol = True
            continue
        (cur['colors'] if incol else cur['rows']).append(line)
    return levels


def write_file(levels):
    out = ["; ExpandPuzzle levels. Edit with Tools > Expand Puzzle > Level Editor (or by hand).",
           "; '=== par=N rating=R note=...' starts a level, then the map rows; '---' starts the optional colour layer.",
           "; Map: # wall, . floor, H blob head, P blob body, T target, X forbidden, ^ v < > pump.",
           "; Colour layer: a digit on a pump, target or blob cell (0 coral, 1 green, 2 purple, 3 orange).", ""]
    for d in levels:
        out.append("=== par=%d rating=%s note=%s" % (d['par'], d.get('rating', ''), d.get('note', '').replace('\n', ' ')))
        out.extend(d['rows'])
        if d.get('colors'):
            out.append('---')
            out.extend(d['colors'])
        out.append('')
    return '\n'.join(out) + '\n'


class Level:
    def __init__(self, rows, colors=None):
        self.rows = list(rows)
        self.colors = list(colors) if colors else None
        self.H = len(rows)
        self.W = max(len(r) for r in rows)
        W = self.W
        self.floor = 0
        self.targets = {}      # bit -> colour (-1 any)
        self.forbidden = 0
        self.pumps = []        # (cellx, celly, dx, dy, colour, nozzle_bit or -1)
        head = None
        body = []
        for r, row in enumerate(rows):
            for x, ch in enumerate(row):
                col = -1
                if colors and r < len(colors) and x < len(colors[r]) and colors[r][x].isdigit():
                    col = int(colors[r][x])
                b = 1 << (r * W + x)
                if ch == '.': self.floor |= b
                elif ch == 'H': self.floor |= b; head = (b, max(0, col))
                elif ch == 'P': self.floor |= b; body.append((b, max(0, col)))
                elif ch == 'T': self.floor |= b; self.targets[b] = col
                elif ch == 'X': self.floor |= b; self.forbidden |= b
                elif ch in PUMP:
                    dx, dy = PUMP[ch]
                    self.pumps.append([x, r, dx, dy, col])
        self.ncol = 1
        for p in self.pumps: self.ncol = max(self.ncol, p[4] + 1)
        for c in self.targets.values(): self.ncol = max(self.ncol, c + 1)
        cells = ([head] if head else []) + body
        for b, c in cells: self.ncol = max(self.ncol, c + 1)
        st = [0] * self.ncol
        for b, c in cells: st[c] |= b
        self.start = tuple(st)
        self.has_head = head is not None
        self.target_all = 0
        self.target_by_col = [0] * self.ncol
        self.target_any = 0
        for b, c in self.targets.items():
            self.target_all |= b
            if c < 0: self.target_any |= b
            else: self.target_by_col[c] |= b
        # pump helpers
        for p in self.pumps:
            x, r, dx, dy, col = p
            nx, ny = x + dx, r + dy
            nb = 0
            if 0 <= nx < W and 0 <= ny < self.H:
                nb = 1 << (ny * W + nx)
            p.append(nb if (nb & self.floor) else 0)
        self.uses_color = any(p[4] >= 0 for p in self.pumps) or any(c >= 0 for c in self.targets.values())
        # floor on the edge breaks the bit tricks (and the game wants a wall ring anyway)
        self.edge_floor = False
        for r in range(self.H):
            for x in range(W):
                if (self.floor >> (r * W + x)) & 1 and (x == 0 or r == 0 or x == W - 1 or r == self.H - 1):
                    self.edge_floor = True

    def shift(self, m, d):
        dx, dy = d
        if dx == 1: return m << 1
        if dx == -1: return m >> 1
        if dy == 1: return m << self.W
        return m >> self.W

    def solved(self, st):
        allm = 0
        for m in st: allm |= m
        if not self.targets: return False
        if (allm & self.target_all) != self.target_all: return False
        if allm & self.forbidden: return False
        for c in range(self.ncol):
            if (st[c] & self.target_by_col[c]) != self.target_by_col[c]: return False
        return True

    def apply(self, st, d):
        """returns (newstate, kind) kind 'm' move / pump index ; or (None, None)"""
        allm = 0
        for m in st: allm |= m
        moved = self.shift(allm, d)
        if moved & ~self.floor == 0 and not (d == (0, -1) and (allm & ((1 << self.W) - 1))) :
            return tuple(self.shift(m, d) for m in st), 'm'
        for i, p in enumerate(self.pumps):
            if p[2] == -d[0] and p[3] == -d[1] and p[5] and (allm & p[5]):
                pd = (p[2], p[3])
                newall = self.shift(allm, pd) & self.floor & ~allm
                if not newall: return None, None
                ns = list(st)
                if p[4] >= 0:
                    ns[p[4]] |= newall
                else:
                    for c in range(self.ncol):
                        ns[c] |= self.shift(st[c], pd) & newall
                return tuple(ns), i
        return None, None

    def cells(self, st):
        out = {}
        for c, m in enumerate(st):
            i = 0
            while m:
                if m & 1: out[(i % self.W, i // self.W)] = c
                m >>= 1; i += 1
        return out

    def render(self, st):
        cs = self.cells(st)
        lines = []
        for r in range(self.H):
            s = ''
            for x in range(self.W):
                ch = self.rows[r][x] if x < len(self.rows[r]) else '#'
                if ch in 'HP': ch = '.'
                if (x, r) in cs:
                    c = cs[(x, r)]
                    ch = 'abcd'[c] if ch == '.' else ('ABCD'[c] if ch == 'T' else '!')
                s += ch
            lines.append(s)
        return '\n'.join(lines)


def solve(lv, max_states=200000):
    """BFS, same as Solver.cs. returns dict(solvable, par, moves, states, complete)."""
    start = lv.start
    if lv.solved(start):
        return dict(solvable=True, par=0, moves=[], states=1, complete=True)
    parent = {start: None}
    q = deque([(start, 0)])
    while q:
        st, dep = q.popleft()
        for d in DIRS:
            ns, kind = lv.apply(st, d)
            if ns is None or ns in parent: continue
            parent[ns] = (st, d, kind)
            if lv.solved(ns):
                moves = []
                cur = ns
                while parent[cur] is not None:
                    p, dd, k = parent[cur]
                    moves.append((dd, k)); cur = p
                moves.reverse()
                return dict(solvable=True, par=dep + 1, moves=moves, states=len(parent), complete=True)
            if len(parent) >= max_states:
                return dict(solvable=False, par=0, moves=[], states=len(parent), complete=False)
            q.append((ns, dep + 1))
    return dict(solvable=False, par=0, moves=[], states=len(parent), complete=True)


def sol_string(moves):
    return ' '.join(DNAME[d] + ('' if k == 'm' else '*') for d, k in moves)

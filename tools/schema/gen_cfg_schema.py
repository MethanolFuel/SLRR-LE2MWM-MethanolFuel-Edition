#!/usr/bin/env python3
"""Derives per-keyword argument schemas from the shipped .cfg corpus (reproducible; run with python3 -I).

Usage: python3 -I tools/schema/gen_cfg_schema.py <game-root> <out.json> [--stats stats.json]

Mirrors CfgDocument tokenisation (Latin-1, ';' or '//' comments, '#'/'[' lines skipped, stops at 'eof').
Per keyword the schema records:
  min/max   arg-count range; count shapes seen on < OUTLIER of the keyword's lines are dropped (defects)
  args      per position, the token types seen on >= OUTLIER of the lines that reach that position
  enforce   false for keywords with fewer than MIN_SUPPORT lines (too little evidence)
Token types: hex, int, float, vector (a,b,c), pair (rpm:value), path, word.
"""
import os
import re
import sys
import json
import collections

OUTLIER = 0.002
MIN_SUPPORT = 3
TYPOS = {'stcokpart', 's'}  # known corpus typos (see CfgKeywords); not part of the language
INT = re.compile(r'^[+-]?\d+$')
FLOAT = re.compile(r'^[+-]?(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?$')
HEX = re.compile(r'^0[xX][0-9a-fA-F]{1,8}$')


def num(t):
    return bool(INT.match(t) or FLOAT.match(t))


def classify(t):
    if HEX.match(t):
        return 'hex'
    if INT.match(t):
        return 'int'
    if FLOAT.match(t):
        return 'float'
    if ',' in t:
        parts = t.split(',')
        if len(parts) > 1 and all(num(p) for p in parts if p != ''):
            return 'vector'
    if ':' in t:
        a, _, b = t.partition(':')
        if num(a) and num(b):
            return 'pair'
    if '\\' in t or '/' in t or re.search(r'\.[A-Za-z0-9]{2,5}$', t):
        return 'path'
    return 'word'


def lines(path):
    text = open(path, 'rb').read().decode('latin-1')
    for ln, line in enumerate(text.split('\n'), 1):
        line = line.rstrip('\r')
        cuts = [i for i in (line.find(';'), line.find('//')) if i >= 0]
        code = (line[:min(cuts)] if cuts else line).strip(' \t')
        if not code or code[0] in '#[':
            continue
        tk = code.split()
        if tk[0] == 'eof':
            break
        yield ln, tk[0], tk[1:]


def main():
    root, out = sys.argv[1], sys.argv[2]
    stats_out = sys.argv[sys.argv.index('--stats') + 1] if '--stats' in sys.argv else None
    kw = collections.defaultdict(lambda: {'n': 0, 'counts': collections.Counter(),
                                          'pos': collections.defaultdict(collections.Counter),
                                          'files': set(), 'ex': {}})
    nfiles = 0
    for d, dirs, fs in os.walk(root):
        dirs.sort()
        for f in sorted(fs):
            if f.lower().endswith('.cfg'):
                p = os.path.join(d, f)
                nfiles += 1
                for ln, k, args in lines(p):
                    e = kw[k]
                    e['n'] += 1
                    e['counts'][len(args)] += 1
                    e['files'].add(p)
                    e['ex'].setdefault(len(args), (os.path.relpath(p, root), ln))
                    for i, a in enumerate(args):
                        e['pos'][i][classify(a)] += 1
    schema = {}
    for k, e in sorted(kw.items()):
        if k in TYPOS:
            continue
        n = e['n']
        keep = {c: v for c, v in e['counts'].items() if v / n >= OUTLIER} or dict(e['counts'])
        lo, hi = min(keep), max(keep)
        positions = []
        for i in range(hi):
            c = e['pos'].get(i, {})
            tot = sum(c.values())
            ts = sorted(t for t, v in c.items() if v / max(tot, 1) >= OUTLIER)
            positions.append(ts or ['word'])
        schema[k] = {'lines': n, 'files': len(e['files']), 'min': lo, 'max': hi,
                     'args': positions, 'enforce': n >= MIN_SUPPORT}
    json.dump({'corpusFiles': nfiles, 'outlier': OUTLIER, 'keywords': schema},
              open(out, 'w'), indent=1, sort_keys=True)
    if stats_out:
        json.dump({k: {'counts': dict(sorted(e['counts'].items())),
                       'ex': {str(c): v for c, v in e['ex'].items()}} for k, e in kw.items()},
                  open(stats_out, 'w'), indent=1, sort_keys=True)
    print(f'{nfiles} cfg files, {len(schema)} keywords')


main()

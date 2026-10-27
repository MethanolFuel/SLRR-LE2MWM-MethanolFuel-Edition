#!/usr/bin/env python3
"""Static native-usage / call-closure / dependency analysis over all shipped .class files.

Usage (from the repo root):  python3 -I tools/analysis/native_usage.py [REPO_ROOT] [OUT_DIR]
Reads only data; executes nothing from the repo.  Outputs (default OUT_DIR = docs/research):
  native-usage-natives.tsv   per native: registration, call sites, calling classes, closure reach, priority
  native-usage-classes.tsv   per class: natives in its closure (3 resolution tiers)
  native-usage-curve.tsv     cumulative 'classes fully runnable after the first N natives'
  native-usage-order.json    the proposed implementation order
  class-deps.tsv             class dependency graph
  engine-dependents.tsv      classes depending on each java.render / java.sound / java.game engine class
  native-usage-summary.json  headline numbers used by native-usage.md

Resolution tiers (see docs/research/native-usage.md):
  A  exact METHOD refs + constructor/initializer chains + super calls + by-name calls whose receiver
     type is known from the call path (field descriptor, class, typed local, implicit this)
  B  A + every override of a resolved target in any subclass (class-hierarchy analysis)
  C  B + untyped by-name calls (receiver type unknown) matched on name + arity in every class
"""
import json
import os
import re
import struct
import sys
from collections import defaultdict, Counter

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import tufa_lib as T  # noqa: E402

ROOT = sys.argv[1] if len(sys.argv) > 1 else "."
OUT = sys.argv[2] if len(sys.argv) > 2 else os.path.join(ROOT, "docs", "research")

OP_NEW = 30
OP_NEWARRAY = 31


def nparams(desc):
    """Number of parameters in a dotted descriptor like (ILjava.lang.String;[F)V."""
    inner = desc[1:desc.index(")")]
    n, i = 0, 0
    while i < len(inner):
        while inner[i] == "[":
            i += 1
        if inner[i] == "L":
            i = inner.index(";", i)
        i += 1
        n += 1
    return n


def param_types(desc):
    inner = desc[1:desc.index(")")]
    out, i = [], 0
    while i < len(inner):
        j = i
        while inner[i] == "[":
            i += 1
        if inner[i] == "L":
            i = inner.index(";", i)
        i += 1
        out.append(inner[j:i])
    return out


def type_to_class(t):
    return t[1:-1] if t and t.startswith("L") and t.endswith(";") else None


class CI:
    def __init__(self, name):
        self.name = name
        self.defs = []
        self.super = None
        self.ifaces = []
        self.methods = {}  # (name, desc) -> dict(flags, bodies=[(Cls, idx)], native)
        self.sinit = []   # (Cls, body idx)
        self.iinit = []
        self.fields = {}  # name -> desc


def load():
    cs = T.load_corpus(ROOT)
    classes = {}
    for c in cs:
        ci = classes.setdefault(c.name, CI(c.name))
        ci.defs.append(c)
        ci.super = ci.super or c.super
        for i in c.ifaces:
            if i not in ci.ifaces:
                ci.ifaces.append(i)
        for m in c.methods:
            static, flags, n, d, body = m[0], m[1], c.s(m[2]), c.s(m[3]), m[4]
            e = ci.methods.setdefault((n, d), dict(flags=flags, bodies=[], native=bool(flags & 0x40), static=bool(flags & 8)))
            if flags & 0x40:
                e["native"] = True
            elif body != T.NONE:
                e["bodies"].append((c, body))
        for f in c.fields:
            static, flags, n, d, val = f
            ci.fields[c.s(n)] = c.s(d)
            if val != T.NONE:
                (ci.sinit if static else ci.iinit).append((c, val))
    return cs, classes


def read_tsv(path):
    rows = []
    with open(path, encoding="utf-8") as f:
        next(f)
        for ln in f:
            p = ln.rstrip("\n").split("\t")
            if len(p) >= 4:
                rows.append(p)
    return rows


def main():
    cs, classes = load()
    redline = {(r[1], r[2], r[3]) for r in read_tsv(os.path.join(ROOT, "docs/research/natives-redline.tsv"))}
    gi_extra = {(r[1], r[2], r[3]) for r in read_tsv(os.path.join(ROOT, "docs/research/natives-slrr_gi-extra.tsv"))}
    # Slrr_GI.exe also registers Chassis.getIndexedAddr from its .code cave (stub 0x7c04a1 -> 0x7c0580 -> 0x416b00),
    # a form the Phase 2 pattern matcher did not see (371 call sites to 0x416b00 in GI = 360 + 11).
    gi_extra.add(("java.game.parts.bodypart.Chassis", "getIndexedAddr", "(I)I"))
    regnames = {(k[0], k[1]) for k in redline | gi_extra}
    declared = {(cn, n, d) for cn, ci in classes.items() for (n, d), m in ci.methods.items() if m["native"]}
    natives = sorted(redline | gi_extra | declared)
    nidx = {k: i for i, k in enumerate(natives)}
    NN = len(natives)

    # subclasses
    subs = defaultdict(list)
    for cn, ci in classes.items():
        if ci.super:
            subs[ci.super].append(cn)

    def all_subs(cn):
        out, st = [], list(subs.get(cn, []))
        while st:
            x = st.pop()
            out.append(x)
            st.extend(subs.get(x, []))
        return out

    # implementors of interfaces count as subclasses too
    for cn, ci in classes.items():
        for i in ci.ifaces:
            subs[i].append(cn)

    by_name_arity = defaultdict(list)  # (name, nparams) -> [(cn, name, desc)]
    for cn, ci in classes.items():
        for (n, d) in ci.methods:
            if n != "<init>":
                by_name_arity[(n, nparams(d))].append((cn, n, d))

    def chain(cn):
        seen = set()
        while cn and cn in classes and cn not in seen:
            seen.add(cn)
            yield cn
            for i in classes[cn].ifaces:
                if i in classes and i not in seen:
                    yield i
            cn = classes[cn].super

    def resolve(cn, name, desc=None, arity=None):
        """-> list of method keys (cn, name, desc), walking the superclass chain."""
        for c in chain(cn):
            ms = classes[c].methods
            if desc is not None and (name, desc) in ms:
                return [(c, name, desc)]
            if arity is not None or desc is not None:
                a = arity if arity is not None else nparams(desc)
                hit = [(c, n, d) for (n, d) in ms if n == name and nparams(d) == a]
                if hit:
                    return hit
        return []

    def ancestors(cn):
        return [c for c in chain(cn) if c in classes and (c == cn or c in _supers(cn))]

    def _supers(cn):
        out, seen = set(), set()
        while cn in classes and cn not in seen:
            seen.add(cn)
            cn = classes[cn].super
            if cn:
                out.add(cn)
        return out

    # ------------------------------------------------------------------ edges
    # Node keys: ("m", cn, name, desc) | ("s", cn) static-init | ("i", cn) instance-init
    edges = {tier: defaultdict(set) for tier in "ABC"}
    # per-site records for native ranking
    site_exact = Counter()       # native -> call sites resolved through METHOD refs
    site_typed = Counter()       # native -> sites resolved through typed by-name calls
    site_cand = Counter()        # native -> untyped by-name sites that could be this native (name+arity)
    callers_exact = defaultdict(set)
    callers_typed = defaultdict(set)
    callers_cand = defaultdict(set)
    unresolved = Counter()
    stats = Counter()

    def touch_static(node_edges, cn):
        for c in chain(cn):
            if classes[c].sinit:
                node_edges.add(("s", c))

    def add_target(tier_sets, key):
        """key = method key; A gets key, B also gets overrides."""
        tier_sets["A"].add(("m",) + key)
        tier_sets["B"].add(("m",) + key)
        tier_sets["C"].add(("m",) + key)
        cn, n, d = key
        if n == "<init>":
            return
        for sc in all_subs(cn):
            if (n, d) in classes[sc].methods:
                tier_sets["B"].add(("m", sc, n, d))
                tier_sets["C"].add(("m", sc, n, d))

    def analyze_body(owner, c, body, locals_init, node_key):
        """Return per-tier successor sets for one body."""
        ts = {"A": set(), "B": set(), "C": set()}
        ins = c.bodies[body]
        ltype = dict(locals_init)
        new_stack = []
        i = 0
        L = len(ins)
        while i < L:
            op, _, a = ins[i]
            if op == 0x02:  # DECLLOCAL slot: type from next TYPE
                j = i + 1
                if j < L and ins[j][0] == 0x2E:
                    j += 1
                if j < L and ins[j][0] == 0x06:
                    ltype[a] = c.s(ins[j][2])
            elif op == 0x07 and a == OP_NEW and i + 1 < L and ins[i + 1][0] == 0x06:
                new_stack.append(c.s(ins[i + 1][2]))
            elif op == 0x07 and a == OP_NEWARRAY:
                new_stack.append(None)
            elif op == 0x21:  # CALLINIT
                t = new_stack.pop() if new_stack else None
                ncls = type_to_class(t)
                n = ins[i - 1][2] if i > 0 and ins[i - 1][0] == 0x27 else 0
                if ncls and ncls in classes:
                    stats["new_sites"] += 1
                    touch_static(ts["A"], ncls)
                    for c2 in chain(ncls):
                        if classes[c2].iinit:
                            ts["A"].add(("i", c2))
                    hit = [(ncls, "<init>", d) for (nn, d) in classes[ncls].methods if nn == "<init>" and nparams(d) == n]
                    for h in hit:
                        add_target(ts, h)
                elif ncls:
                    unresolved[("new", ncls)] += 1
            elif op in (0x22, 0x23):  # THISINIT / SUPERINIT
                n = ins[i - 1][2] if i > 0 and ins[i - 1][0] == 0x27 else 0
                tc = owner if op == 0x22 else classes[owner].super
                if tc and tc in classes:
                    hit = [(tc, "<init>", d) for (nn, d) in classes[tc].methods if nn == "<init>" and nparams(d) == n]
                    for h in hit:
                        add_target(ts, h)
                    if op == 0x23:
                        for c2 in chain(tc):
                            if classes[c2].iinit:
                                ts["A"].add(("i", c2))
                elif tc:
                    unresolved[("super", tc)] += 1
            elif op == 0x1A:  # METHOD ref
                rc, rn, rd = c.ref(a)
                stats["method_refs"] += 1
                touch_static(ts["A"], rc) if rc in classes else None
                res = resolve(rc, rn, rd) if rc in classes else []
                if res:
                    for key in res:
                        add_target(ts, key)
                        if key in nidx_cache and classes[key[0]].methods[(key[1], key[2])]["native"]:
                            site_exact[key] += 1
                            callers_exact[key].add(owner)
                else:
                    unresolved[("method", rc, rn, rd)] += 1
            elif op == 0x1B:  # FIELD ref: static init touch
                rc = c.ref(a)[0]
                if rc in classes:
                    touch_static(ts["A"], rc)
            elif op == 0x12:  # MNAME (by-name call as last path component)
                name = c.s(a)
                # find ARGC: scan back for the CALL, then the ARGC before it
                j = i - 1
                depth = 0
                while j >= 0 and ins[j][0] != 0x24:
                    j -= 1
                argc = ins[j - 1][2] if j > 0 and ins[j - 1][0] == 0x27 else None
                # receiver component = previous instruction (if NAME k>1 chain) else implicit this
                recv = None
                prev = ins[i - 1] if i > 0 else None
                if prev and prev[0] == 0x11:
                    recv = owner  # NAME 1 + MNAME : unqualified call
                elif prev and prev[0] == 0x1B:
                    recv = type_to_class(c.ref(prev[2])[2])
                elif prev and prev[0] == 0x19:
                    recv = c.cname(prev[2])
                elif prev and prev[0] == 0x01:
                    t = ltype.get(prev[2])
                    recv = type_to_class(t) if t else None
                    if prev[2] == 0 and t is None:
                        recv = owner if not node_key[3] else None
                res = []
                if recv and recv in classes and argc is not None:
                    res = resolve(recv, name, None, argc)
                if res:
                    stats["mname_typed"] += 1
                    for key in res:
                        add_target(ts, key)
                        if classes[key[0]].methods[(key[1], key[2])]["native"]:
                            site_typed[key] += 1
                            callers_typed[key].add(owner)
                else:
                    stats["mname_untyped"] += 1
                    cands = by_name_arity.get((name, argc), []) if argc is not None else []
                    for key in cands:
                        ts["C"].add(("m",) + key)
                        if classes[key[0]].methods[(key[1], key[2])]["native"]:
                            site_cand[key] += 1
                            callers_cand[key].add(owner)
            elif op in (0x25, 0x26):  # CALLBYNAME / SUPERCALL
                name = c.s(a)
                argc = ins[i - 1][2] if i > 0 and ins[i - 1][0] == 0x27 else None
                if op == 0x26:
                    sup = classes[owner].super
                    res = resolve(sup, name, None, argc) if sup in classes and argc is not None else []
                    for key in res:
                        add_target(ts, key)
                        stats["supercall_resolved"] += 1
                        if classes[key[0]].methods[(key[1], key[2])]["native"]:
                            site_typed[key] += 1
                            callers_typed[key].add(owner)
                    if not res:
                        unresolved[("supercall", name)] += 1
                else:
                    stats["callbyname"] += 1
                    cands = by_name_arity.get((name, argc), []) if argc is not None else []
                    for key in cands:
                        ts["C"].add(("m",) + key)
                        if classes[key[0]].methods[(key[1], key[2])]["native"]:
                            site_cand[key] += 1
                            callers_cand[key].add(owner)
            i += 1
        for t in "BC":
            ts[t] |= ts["A"]
        ts["C"] |= ts["B"]
        return ts

    nidx_cache = nidx
    nodes = []
    for cn, ci in classes.items():
        for (n, d), m in ci.methods.items():
            nodes.append(("m", cn, n, d))
        nodes.append(("s", cn))
        nodes.append(("i", cn))

    for node in nodes:
        cn = node[1]
        ci = classes[cn]
        if node[0] == "m":
            m = ci.methods[(node[2], node[3])]
            if m["native"]:
                continue
            for (c, bi) in m["bodies"]:
                loc = {}
                k = 0
                if not m["static"]:
                    loc[0] = "L%s;" % cn
                    k = 1
                for t in param_types(node[3]):
                    loc[k] = t
                    k += 1
                ts = analyze_body(cn, c, bi, loc, (node[0], cn, node[2], m["static"]))
                for t in "ABC":
                    edges[t][node] |= ts[t]
        else:
            lst = ci.sinit if node[0] == "s" else ci.iinit
            for (c, bi) in lst:
                ts = analyze_body(cn, c, bi, {0: "L%s;" % cn} if node[0] == "i" else {}, ("x", cn, "", node[0] == "s"))
                for t in "ABC":
                    edges[t][node] |= ts[t]

    # ------------------------------------------------------------------ closure (bitsets)
    def direct_bits(node):
        if node[0] == "m":
            m = classes[node[1]].methods[(node[2], node[3])]
            if m["native"]:
                return 1 << nidx[(node[1], node[2], node[3])]
        return 0

    nodeset = set(nodes)

    def closure(tier):
        bits = {n: direct_bits(n) for n in nodes}
        preds = defaultdict(set)
        for n, ss in edges[tier].items():
            for s in ss:
                if s in nodeset:
                    preds[s].add(n)
        work = [n for n in nodes if bits[n]]
        while work:
            s = work.pop()
            b = bits[s]
            for p in preds.get(s, ()):
                nb = bits[p] | b
                if nb != bits[p]:
                    bits[p] = nb
                    work.append(p)
        return bits

    clos = {t: closure(t) for t in "ABC"}

    # per-class requirement sets
    def req(tier, cn, kind):
        ci = classes[cn]
        b = clos[tier][("s", cn)] | clos[tier][("i", cn)]
        for (n, d), m in ci.methods.items():
            if m["native"]:
                continue
            if kind == "all" or n == "<init>":
                b |= clos[tier][("m", cn, n, d)]
        return b

    cnames = sorted(classes)
    R = {(t, k): {cn: req(t, cn, k) for cn in cnames} for t in "ABC" for k in ("all", "inst")}

    def popcount(x):
        return bin(x).count("1")

    # per-method requirement (methods with bodies)
    mnodes = [n for n in nodes if n[0] == "m" and not classes[n[1]].methods[(n[2], n[3])]["native"]
              and classes[n[1]].methods[(n[2], n[3])]["bodies"]]

    # ------------------------------------------------------------------ ordering
    def runnable_counts(order_bits, req_map, upto):
        mask = 0
        out = []
        reqs = list(req_map.values())
        for k in range(upto + 1):
            if k > 0:
                mask |= order_bits[k - 1]
            out.append(sum(1 for r in reqs if r & ~mask == 0))
        return out

    def order_by(score):
        ids = sorted(range(NN), key=lambda i: (-score(i), natives[i]))
        return ids

    # popularity: number of classes whose closure (tier B, all) contains the native
    pop = [sum(1 for cn in cnames if R[("B", "all")][cn] >> i & 1) for i in range(NN)]
    sites_total = [site_exact[natives[i]] + site_typed[natives[i]] for i in range(NN)]
    order_sites = order_by(lambda i: sites_total[i])
    order_pop = order_by(lambda i: pop[i])

    def greedy(req_map):
        reqs = {cn: r for cn, r in req_map.items()}
        mask = 0
        order = []
        remaining = set(range(NN))
        while remaining:
            rem = {cn: r & ~mask for cn, r in reqs.items() if r & ~mask}
            best, bs = None, -1.0
            sc = defaultdict(float)
            for cn, r in rem.items():
                k = popcount(r)
                w = 1.0 / (k * k)
                x = r
                while x:
                    lb = x & -x
                    sc[lb.bit_length() - 1] += w
                    x ^= lb
            cand = [i for i in remaining]
            for i in cand:
                s = sc.get(i, 0.0) + 1e-9 * pop[i]
                if s > bs or (s == bs and natives[i] < natives[best]):
                    best, bs = i, s
            order.append(best)
            remaining.discard(best)
            mask |= 1 << best
        return order

    order_greedy = greedy(R[("B", "all")])
    bit = [1 << i for i in range(NN)]
    ob = {name: [bit[i] for i in o] for name, o in
          (("greedy", order_greedy), ("sites", order_sites), ("pop", order_pop))}
    curves = {}
    for nm, o in ob.items():
        curves[nm] = runnable_counts(o, R[("B", "all")], NN)
    curve_cols = {}
    for t in "ABC":
        for k in ("all", "inst"):
            curve_cols[f"{t}_{k}"] = runnable_counts(ob["greedy"], R[(t, k)], NN)
    mreq = {n: clos["B"][n] for n in mnodes}
    curve_methods = runnable_counts(ob["greedy"], mreq, NN)

    exit_cls = ["java.game.GameLogic", "java.game.Catalog", "java.game.OptionsDialog"]
    exit_info = {}
    for t in "ABC":
        for k in ("all", "inst"):
            u = 0
            for cn in exit_cls:
                u |= R[(t, k)][cn]
            exit_info[f"{t}_{k}"] = dict(count=popcount(u), natives=["%s.%s%s" % natives[i] for i in range(NN) if u >> i & 1],
                                         per_class={cn: popcount(R[(t, k)][cn]) for cn in exit_cls})

    # ------------------------------------------------------------------ outputs
    os.makedirs(OUT, exist_ok=True)
    rank = {natives[i]: r + 1 for r, i in enumerate(order_greedy)}
    with open(os.path.join(OUT, "native-usage-natives.tsv"), "w", encoding="utf-8") as f:
        f.write("rank\tpackage_group\tclass\tmethod\tdescriptor\tregistered_redline\tregistered_gi_only\tdeclared_native_in_class\tregistered_by_class_method_name\t"
                "sites_exact\tsites_typed_byname\tsites_untyped_candidates\tcalling_classes_exact\tcalling_classes_typed\t"
                "closure_classes_A\tclosure_classes_B\tclosure_classes_C\n")
        for i in order_greedy:
            k = natives[i]
            grp = ".".join(k[0].split(".")[:2])
            ca = sum(1 for cn in cnames if R[("A", "all")][cn] >> i & 1)
            cb = sum(1 for cn in cnames if R[("B", "all")][cn] >> i & 1)
            cc = sum(1 for cn in cnames if R[("C", "all")][cn] >> i & 1)
            f.write("\t".join(map(str, [rank[k], grp, k[0], k[1], k[2], int(k in redline), int(k in gi_extra and k not in redline),
                                         int(k in declared), int((k[0], k[1]) in regnames), site_exact[k], site_typed[k], site_cand[k],
                                         len(callers_exact[k]), len(callers_typed[k]), ca, cb, cc])) + "\n")
    with open(os.path.join(OUT, "native-usage-classes.tsv"), "w", encoding="utf-8") as f:
        f.write("class\tfiles\tmethods\tnatives_all_A\tnatives_all_B\tnatives_all_C\tnatives_inst_B\tnative_list_all_B\n")
        for cn in cnames:
            ci = classes[cn]
            lst = [natives[i][0].split(".")[-1] + "." + natives[i][1] for i in range(NN) if R[("B", "all")][cn] >> i & 1]
            f.write("\t".join(map(str, [cn, ";".join(sorted({os.path.relpath(d.file, ROOT) for d in ci.defs})), len(ci.methods),
                                         popcount(R[("A", "all")][cn]), popcount(R[("B", "all")][cn]), popcount(R[("C", "all")][cn]),
                                         popcount(R[("B", "inst")][cn]), ",".join(lst)])) + "\n")
    with open(os.path.join(OUT, "native-usage-curve.tsv"), "w", encoding="utf-8") as f:
        f.write("N\tadded_native(greedy)\trunnable_classes_B_all\trunnable_classes_B_inst\trunnable_classes_A_all\trunnable_classes_C_all\t"
                "runnable_methods_B\tby_sites_B_all\tby_popularity_B_all\n")
        for n in range(NN + 1):
            nm = "" if n == 0 else "%s.%s%s" % (natives[order_greedy[n - 1]][0], natives[order_greedy[n - 1]][1], natives[order_greedy[n - 1]][2])
            f.write("\t".join(map(str, [n, nm, curve_cols["B_all"][n], curve_cols["B_inst"][n], curve_cols["A_all"][n],
                                         curve_cols["C_all"][n], curve_methods[n], curves["sites"][n], curves["pop"][n]])) + "\n")
    json.dump([dict(rank=r + 1, **dict(zip(("class", "method", "descriptor"), natives[i]))) for r, i in enumerate(order_greedy)],
              open(os.path.join(OUT, "native-usage-order.json"), "w"), indent=1)

    # ------------------------------------------------------------------ dependency graph
    desc_re = re.compile(r"L([A-Za-z_][\w.$]*);")
    deps = {}  # cn -> {dep: kind}
    for cn, ci in classes.items():
        d = defaultdict(set)
        if ci.super:
            d[ci.super].add("super")
        for i in ci.ifaces:
            d[i].add("iface")
        for c in ci.defs:
            for e in c.cons:
                if e[0] == 4:
                    nm = c.s(e[1])
                    while nm and nm.startswith("["):
                        nm = nm[1:]
                    if nm and nm.startswith("L") and nm.endswith(";"):
                        nm = nm[1:-1]
                    if nm and len(nm) > 1 and nm != cn:
                        d[nm].add("ref")
            for e in c.cons:
                if e[0] == "s":
                    for m in desc_re.findall(e[1]):
                        if m != cn and m not in d:
                            d[m].add("sig")
        deps[cn] = d
    with open(os.path.join(OUT, "class-deps.tsv"), "w", encoding="utf-8") as f:
        f.write("class\tdepends_on\tkinds\tdep_in_corpus\n")
        for cn in cnames:
            for dn in sorted(deps[cn]):
                f.write(f"{cn}\t{dn}\t{','.join(sorted(deps[cn][dn]))}\t{int(dn in classes)}\n")
    rdeps = defaultdict(set)
    rstrong = defaultdict(set)
    for cn, d in deps.items():
        for dn, k in d.items():
            rdeps[dn].add(cn)
            if k - {"sig"}:
                rstrong[dn].add(cn)

    def trans(dn):
        seen, st = set(), [dn]
        while st:
            x = st.pop()
            for y in rstrong.get(x, ()):
                if y not in seen:
                    seen.add(y)
                    st.append(y)
        return seen

    eng = [cn for cn in sorted(set(classes) | set(rdeps))
           if cn.startswith(("java.render.", "java.sound.")) or (cn.startswith("java.game.") and cn.count(".") == 2)
           or cn in ("java.game.parts.Part", "java.game.parts.WheelRef", "java.game.parts.SfxTable", "java.game.parts.DynoData",
                     "java.game.parts.bodypart.Chassis")]
    with open(os.path.join(OUT, "engine-dependents.tsv"), "w", encoding="utf-8") as f:
        f.write("engine_class\tin_corpus\tnative_methods\tdirect_dependents\tdirect_dependents_strong\ttransitive_dependents_strong\t"
                "dependent_files\tinheritance_descendants\n")
        for dn in eng:
            ci = classes.get(dn)
            nat = sum(1 for m in ci.methods.values() if m["native"]) if ci else 0
            tr = trans(dn)
            files = {os.path.relpath(d.file, ROOT) for x in rstrong.get(dn, ()) for d in classes[x].defs}
            f.write("\t".join(map(str, [dn, int(dn in classes), nat, len(rdeps[dn]), len(rstrong[dn]), len(tr), len(files),
                                         len({x for x in all_subs(dn)})])) + "\n")

    # ------------------------------------------------------------------ summary json
    summ = dict(
        classes=len(classes), class_defs=len(cs), files=len({c.file for c in cs}), natives_total=NN,
        natives_redline=len(redline), natives_gi_extra=len(gi_extra - redline), natives_declared=len(declared),
        declared_not_registered_by_name=sorted("%s.%s%s" % k for k in declared if (k[0], k[1]) not in regnames),
        declared_not_registered=sorted("%s.%s%s" % k for k in declared - redline - gi_extra),
        registered_not_declared=sorted("%s.%s%s" % k for k in (redline | gi_extra) - declared),
        stats=dict(stats), unresolved_top=[[list(k), v] for k, v in unresolved.most_common(40)],
        unresolved_total=sum(unresolved.values()),
        classes_zero_natives={t + "_" + k: sum(1 for cn in cnames if R[(t, k)][cn] == 0) for t in "ABC" for k in ("all", "inst")},
        file_level_runnable_at={}, curve_checkpoints={},
        methods_with_body=len(mnodes), exit_criteria=exit_info,
    )
    for n in (0, 1, 2, 3, 5, 8, 10, 15, 20, 30, 40, 50, 75, 100, 150, 200, 250, 300, NN):
        if n <= NN:
            summ["curve_checkpoints"][n] = dict(greedy_B_all=curve_cols["B_all"][n], greedy_B_inst=curve_cols["B_inst"][n],
                                                greedy_A_all=curve_cols["A_all"][n], greedy_C_all=curve_cols["C_all"][n],
                                                sites_B_all=curves["sites"][n], pop_B_all=curves["pop"][n],
                                                methods_B=curve_methods[n])
    # file-level
    for t in ("A", "B", "C"):
        mask_by_file = defaultdict(int)
        for cn in cnames:
            for d in classes[cn].defs:
                mask_by_file[d.file] |= R[(t, "all")][cn]
        summ["file_level_runnable_at"][t] = {}
        for n in (0, 5, 10, 20, 50, 100, 200, NN):
            m = 0
            for i in order_greedy[:n]:
                m |= 1 << i
            summ["file_level_runnable_at"][t][n] = sum(1 for r in mask_by_file.values() if r & ~m == 0)
    summ["pop_top"] = [["%s.%s%s" % natives[i], pop[i]] for i in order_pop[:25]]
    summ["sites_top"] = [["%s.%s%s" % natives[i], sites_total[i]] for i in order_sites[:25]]
    summ["greedy_top"] = ["%s.%s%s" % natives[i] for i in order_greedy[:40]]
    summ["natives_with_zero_sites_and_zero_reach"] = ["%s.%s%s" % natives[i] for i in range(NN)
                                                      if sites_total[i] == 0 and pop[i] == 0 and site_cand[natives[i]] == 0]
    # per package group
    grp = defaultdict(lambda: Counter())
    for i in range(NN):
        g = ".".join(natives[i][0].split(".")[:2]) if natives[i][0].count(".") >= 1 else natives[i][0]
        k = natives[i][0]
        grp[k]["natives"] += 1
        grp[k]["sites"] += sites_total[i]
        grp[k]["cand_sites"] += site_cand[natives[i]]
    summ["by_class"] = {k: dict(v) for k, v in sorted(grp.items(), key=lambda kv: -kv[1]["sites"])}
    pk = defaultdict(Counter)
    for k, v in grp.items():
        g = ".".join(k.split(".")[:2])
        pk[g]["natives"] += v["natives"]
        pk[g]["sites"] += v["sites"]
    summ["by_package"] = {k: dict(v) for k, v in pk.items()}
    json.dump(summ, open(os.path.join(OUT, "native-usage-summary.json"), "w"), indent=1)
    print(json.dumps({k: summ[k] for k in ("classes", "class_defs", "files", "natives_total", "stats", "unresolved_total", "classes_zero_natives")}, indent=1))
    print(json.dumps(summ["curve_checkpoints"], indent=0)[:3000])


if __name__ == "__main__":
    main()

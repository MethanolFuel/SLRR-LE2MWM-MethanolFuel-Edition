#!/usr/bin/env python3
"""RawEdit catalog: every use of java.lang.RawEdit in shipped .class files and .java sources, plus a static
mapping of each absolute address to the executables (StreetLegal_Redline.exe / Slrr_GI.exe).

Usage (from repo root):  python3 -I tools/analysis/rawedit_catalog.py [REPO_ROOT] [OUT_DIR]
Outputs (default OUT_DIR = docs/research): rawedit-addresses.tsv, rawedit-callsites.tsv, rawedit.json,
and the generated markdown table fragment rawedit-table.md.fragment (consumed when writing rawedit.md).
Static only: the exes are parsed as data, nothing is executed.
"""
import bisect
import json
import os
import re
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import tufa_lib as T  # noqa: E402
import pe_addr_xref as P  # noqa: E402

ROOT = sys.argv[1] if len(sys.argv) > 1 else "."
OUT = sys.argv[2] if len(sys.argv) > 2 else os.path.join(ROOT, "docs", "research")

# name -> (config key in the exe's GetConfigData loader or "", group, proposed property, type, meaning, confidence)
# Meanings come from: the config loader (0x427bf0), author tooltips in SlrrExhaustiveSettingsEditor.exe (UTF-16
# user strings, mapped by control order), and the instructions that reference the address (see evidence columns).
K = {
 "I_SoftwareFrustrumCullReturnOverwrite": ("", "Graphics", "Graphics.ObjectsBehindCameraUnloadMode", "int", "1 = unload objects behind the camera (vanilla, slow); 4 = never unload. Editor tooltip; GI default 4", "M"),
 "I_DisableFlop": ("", "Damage", "Damage.DoorsOpenOnCollision", "bool", "0 = doors do not open on collision (name says Disable, tooltip says '== 0 disables'); GI default 1", "M"),
 "I_cfg_mem_instance_current": ("", "ResourceBudget", "ResourceBudget.InstancesInUse", "int (read-only)", "live instance counter: ++/-- by instance create/destroy, compared with instance_max/min in the budget routine 0x537ed0", "H"),
 "I_cfg_mem_instance_max": ("mem_instance_max", "ResourceBudget", "ResourceBudget.InstanceMax", "int", "script/render instance budget (max); loader stores config key mem_instance_max here", "H"),
 "I_cfg_mem_instance_min": ("mem_instance_min", "ResourceBudget", "ResourceBudget.InstanceMin", "int", "instance budget lower watermark; config key mem_instance_min", "H"),
 "I_cfg_mem_sound_max": ("mem_sound_max", "ResourceBudget", "ResourceBudget.SoundMax", "int (bytes)", "sound memory budget (max); config key mem_sound_max (optional key)", "H"),
 "I_cfg_mem_sound_min": ("mem_sound_min", "ResourceBudget", "ResourceBudget.SoundMin", "int (bytes)", "sound memory lower watermark; config key mem_sound_min", "H"),
 "I_cfg_mem_texture_current": ("", "ResourceBudget", "ResourceBudget.TextureBytesInUse", "int (read-only)", "running texture byte total: add/sub in the texture load/unload routines (0x5065f0, 0x506c70, 0x507530, 0x5062a0)", "H"),
 "I_cfg_mem_texture_max": ("mem_texture_max", "ResourceBudget", "ResourceBudget.TextureMax", "int (bytes)", "texture memory budget (max); config key mem_texture_max", "H"),
 "I_cfg_mem_texture_min": ("mem_texture_min", "ResourceBudget", "ResourceBudget.TextureMin", "int (bytes)", "texture memory lower watermark; config key mem_texture_min", "H"),
 "I_cfg_mem_vertex_current": ("", "ResourceBudget", "ResourceBudget.VertexBytesInUse", "int (read-only)", "running vertex-buffer byte total (add/sub in 0x4ffe90, 0x500380, 0x500ad0, 0x504070, 0x5041c0)", "H"),
 "I_cfg_mem_vertex_max": ("mem_vertex_max", "ResourceBudget", "ResourceBudget.VertexMax", "int (bytes)", "vertex memory budget (max); config key mem_vertex_max", "H"),
 "I_cfg_mem_vertex_min": ("mem_vertex_min", "ResourceBudget", "ResourceBudget.VertexMin", "int (bytes)", "vertex memory lower watermark; config key mem_vertex_min", "H"),
 "I_cfg_skidmarks_max": ("skidmark_max", "ResourceBudget", "ResourceBudget.SkidmarksMax", "int", "maximum skid-mark length/count (compared at 0x521d0c); config key skidmark_max", "H"),
 "I_resource_loadrate": ("resource_loadrate", "ResourceBudget", "ResourceBudget.LoadsPerFrame", "int", "max resource load operations per frame (read by 0x537790/0x5378d0); config key resource_loadrate", "H"),
 "I_AISlideIgnoreMod": ("", "Ai", "Ai.SlideIgnoreMod", "int", "higher = AI ignores sliding more (it mistook sideways slopes for power-slides). Tooltip: default 127, 472 acceptable; GI default 172", "M"),
 "I_DisableCruiseSteerOverWrite": ("", "Drivetrain", "Drivetrain.CruiseSteerOverwriteDisabled", "bool", "referenced only by the .code cave (0x7c0564, cmp [..],0) next to CruiseBreakWhenOverLimit: cruise-control steering override switch", "L"),
 "F_ClutchOnInAutoShiftDeadTime": ("", "Drivetrain", "Drivetrain.AutoShiftClutchDuringShift", "float", "clutch amount right after an automatic shift (0 = no rev-limiter hit on shift). Tooltip; GI default 0.5", "M"),
 "F_ThrottleOnInAutoShiftDeadTime": ("", "Drivetrain", "Drivetrain.AutoShiftThrottleDuringShift", "float", "same for throttle; GI default 0.15", "M"),
 "F_CarDeformationOnCollisionMult": ("", "Damage", "Damage.BodyDeformationMult", "float", "scale of car body bend on collision (<1 = bends less). Written together with ...Minus = -value", "M"),
 "F_CarDeformationOnCollisionMultMinus": ("", "Damage", "(derived: -BodyDeformationMult)", "float", "negated copy of the above (source comment: must be set together)", "M"),
 "F_CarPartDetachmentMult": ("", "Damage", "Damage.PartDetachmentMult", "float", "<1 = parts must be damaged more before detaching", "M"),
 "F_CarChassisDeformationOnCollisionMult": ("", "Damage", "Damage.ChassisSlotDeformationMult", "float", "<1 = the car slots (wheels) bend less; read at 0x48f266", "M"),
 "F_WearMultEngine": ("", "Wear", "Wear.EngineWearMult", "float", "scales part wear from use; tooltip range 0-100; GI default 5", "M"),
 "F_WearMultTyre": ("", "Wear", "Wear.TyreWearMult", "float", "scales tyre wear (tooltip 0-1); GI default 0.6", "M"),
 "F_TrafficFlatBonus": ("", "Ai", "Ai.TrafficHeightOffset", "float", "flat value added to the height of every traffic car (tooltip); GI default -0.13", "M"),
 "F_AIOverSteering": ("", "Ai", "Ai.OverSteering", "float", "AI steering gain, non-traffic cars", "M"),
 "F_AIOverBreaking": ("", "Ai", "Ai.OverBraking", "float", "AI braking gain, non-traffic cars", "M"),
 "F_AIOverThrotlling": ("", "Ai", "Ai.OverThrottling", "float", "AI throttle gain, non-traffic cars; GI default 1.1", "M"),
 "F_cfg_deformation": ("deformation", "Damage", "Damage.OverallDeformation", "float", "overall collision deformation; 0 = car invincible (tooltip); config key deformation, vanilla default 1.0", "H"),
 "F_cfg_engine_inertia_factor": ("engine_inertia_factor", "Drivetrain", "Drivetrain.EngineInertiaFactor", "float", "divisor in the vehicle step (0x4558de); config key engine_inertia_factor; vanilla 4, GI 5", "H"),
 "F_cfg_external_damage": ("external_damage", "Damage", "Damage.ExternalDamageScale", "float", "config key external_damage (read at 0x442791, 0x46e362)", "H"),
 "F_cfg_internal_damage": ("internal_damage", "Damage", "Damage.InternalDamageScale", "float", "config key internal_damage (vehicle step, updateDamage path 0x43c282)", "H"),
 "F_cfg_particle_density": ("particle_density", "Graphics", "Graphics.ParticleDensity", "float", "multiplies emitter counts in the particle code 0x51bf70; config key particle_density", "H"),
 "I_bDrawHeadlightRays": ("headlight_rays", "Graphics", "Graphics.DrawHeadlightRays", "bool", "config key headlight_rays (read by the lights code 0x50eba0)", "H"),
 "I_bDrawHeadlightFlares": ("flares", "Graphics", "Graphics.DrawHeadlightFlares", "bool", "config key flares", "H"),
 "I_ShadowSize": ("shadow_size", "Graphics", "Graphics.ShadowSize", "int", "config key shadow_size (shadow map code 0x4fc1b0, 0x4fc590)", "H"),
 "I_Shadows": ("shadows", "Graphics", "Graphics.ShadowMode", "int", "config key shadows (0..2)", "H"),
 "I_TextureSize": ("texture_size", "Graphics", "Graphics.TextureSize", "int", "config key texture_size (texture loaders 0x506c70, 0x507530)", "H"),
 "F_cfg_ShadowDetail": ("shadow_detail", "Graphics", "Graphics.ShadowDetail", "float", "config key shadow_detail (compared at 0x50b5d3)", "H"),
 "F_cfg_ObjectDetail": ("object_detail", "Graphics", "Graphics.ObjectDetail", "float", "config key object_detail (draw-distance/LOD code 0x5100b0, 0x510fc0)", "H"),
 "F_cfg_VideoGamma": ("video_gamma", "Graphics", "Graphics.VideoGamma", "float", "config key video_gamma, stored as constant/gamma (loader does fdivr [0x5f08f0]); used in the D3D reset path 0x4ba230", "H"),
 "F_WheelGroundFeedbackFactor": ("wheel_gndfeedback_factor", "Physics", "Physics.WheelGroundFeedbackFactor", "float", "config key wheel_gndfeedback_factor; multiplier in the vehicle step (0x4563cb); vanilla 2.8, GI 11", "H"),
 "F_WheelBreakeFactor": ("wheel_brake_factor", "Physics", "Physics.WheelBrakeFactor", "float", "config key wheel_brake_factor; multiplier in the vehicle step (0x455c6f); vanilla 4, GI 2.3", "H"),
 "F_LastFrameDrawTime": ("", "Clock", "Clock.LastFrameSeconds", "float (read-only)", "frame-time accumulator updated in the main frame routine 0x428960; used as divisor for FPS display (0x479fb0)", "M"),
 "F_GameTimeSeconds": ("", "Clock", "Clock.GameSeconds", "float (read-only)", "game clock written by the frame routine 0x428960", "M"),
 "F_Gravity": ("", "Physics", "Physics.Gravity", "float", "gravity acceleration; GI default -9.8067 (physics step 0x4a4091, vehicle 0x45c656)", "M"),
 "F_CollisionForceMultMapObjects_1": ("", "Physics", "Physics.CollisionForceMapObjects[0]", "float", "force scale vs map objects (x component); read by the .code cave 0x7c0093", "M"),
 "F_CollisionForceMultMapObjects_2": ("", "Physics", "Physics.CollisionForceMapObjects[1]", "float", "second component", "M"),
 "F_CollisionForceMultVehicle_1": ("", "Physics", "Physics.CollisionForceVehicles[0]", "float", "force scale vs other vehicles", "M"),
 "F_CollisionForceMultVehicle_2": ("", "Physics", "Physics.CollisionForceVehicles[1]", "float", "second component", "M"),
 "F_FrictionOnCollision_1": ("", "Physics", "Physics.CollisionFriction[0]", "float", "friction of non-wheel parts on collision", "M"),
 "F_FrictionOnCollision_2": ("", "Physics", "Physics.CollisionFriction[1]", "float", "second component", "M"),
 "F_WheelFrictionMult": ("", "Physics", "Physics.WheelFrictionMult", "float", "wheel friction scale (.code cave 0x7c024a)", "M"),
 "F_WheelFriction_XMult": ("", "Physics", "Physics.WheelFrictionLateralMult", "float", "wheel friction, X axis", "M"),
 "F_WheelSilictionMult": ("", "Physics", "Physics.WheelSlipMult", "float", "name has a typo ('Siliction'); wheel slip-related scale used twice in cave 0x7c025d", "L"),
 "F_WheelStiffnessMult": ("", "Physics", "Physics.WheelStiffnessMult", "float", "wheel stiffness scale", "M"),
 "F_WheelRollResMult": ("", "Physics", "Physics.WheelRollResistanceMult", "float", "rolling resistance scale", "M"),
 "F_WheelBearingMult": ("", "Physics", "Physics.WheelBearingMult", "float", "bearing friction scale", "M"),
 "F_WheelMaxLoadMult": ("", "Physics", "Physics.WheelMaxLoadMult", "float", "wheel max load scale", "M"),
 "F_WheelLoadSmoothMult": ("", "Physics", "Physics.WheelLoadSmoothMult", "float", "wheel load smoothing scale", "M"),
 "F_NativeSpring_Var1": ("", "Physics", "Physics.Spring.Hardness", "float", "source comment: (hardness)", "M"),
 "F_NativeSpring_Var2": ("", "Physics", "Physics.Spring.InverseRebound", "float", "source comment: (inverse-rebound)", "M"),
 "F_NativeSpring_Var3": ("", "Physics", "Physics.Spring.InboundInverseSoftness", "float", "source comment: (inbound inverse-softness)", "M"),
 "F_NativeSpring_Var4": ("", "Physics", "Physics.Spring.LengthAndBumpiness", "float", "source comment: (length and bumpiness)", "M"),
 "F_NativeSpring_Var5": ("", "Physics", "Physics.Spring.MaxInverseContraction", "float", "source comment: (max inverse-contraction)", "M"),
 "F_NativeSpring_Var6": ("", "Physics", "Physics.Spring.CollisionRelated", "float", "source comment: (collision related); GI default 3.86", "M"),
 "F_NativeSpring_Var8": ("", "Physics", "Physics.Spring.Length", "float", "source comment: (spring length); 7 refs in cave 0x7c03fe..0x7c0486", "M"),
 "F_CruiseBreakWhenOverLimit": ("", "Drivetrain", "Drivetrain.CruiseBrakeWhenOverLimit", "float", "cruise-control: braking when over the speed limit; GI default 0.5", "L"),
}

# Native-object relative offsets used by sources / classes
REL = [
 ("WheelRef.ptr + 0xC8", "float", "WheelRef", "Physics.Wheel.SteerAngle (WheelRef.getSteer reads [obj+0xC8], WheelRef.setSteer writes it; both natives at 0x440e50/0x440e90 in Redline)", "H (field identity); M (script uses it as max steer)"),
 ("Chassis.getIndexedAddr(0x20FC - k)", "float/int", "Chassis", "named telemetry/state fields of the native chassis object (RPM, Mileage, gear, pedals, LEDs). Offsets are relative to the Chassis C++ object; see table in rawedit.md", "M"),
 ("SfxTable.AddItemPtr(...) + 0x10..0x20", "float", "SfxTable", "fields of the freshly added sound item (+0x10 pitch; +0x14..+0x20 unknown) - all uses are commented out in the source", "L"),
]

OP_CALL = 0x24


def parse_sources():
    out = []
    pat = re.compile(r"RawEdit\.(\w+)|Address_[IF]_\w+|getIndexedAddr|getIndexedData")
    for d, dirs, fs in os.walk(ROOT):
        dirs[:] = [x for x in dirs if not x.startswith(".")]
        for f in fs:
            if not f.endswith(".java"):
                continue
            p = os.path.join(d, f)
            rel = os.path.relpath(p, ROOT)
            for i, ln in enumerate(open(p, encoding="latin1").read().split("\n"), 1):
                if pat.search(ln) and "static native" not in ln:
                    code = ln.strip()
                    if rel.endswith("lang/src/RawEdit.java") and ("new IntValueAddress" in ln or "new FloatValueAddress" in ln):
                        continue
                    out.append((rel, i, code.startswith("//") or "//" in ln.split("RawEdit")[0], code[:160]))
    return out


def class_sites(classes):
    """All instructions in shipped classes that reference java.lang.RawEdit (member refs, class refs, TYPE)."""
    sites = []
    for c in classes:
        if c.name == "java.lang.RawEdit":
            continue
        for bi, body in enumerate(c.bodies):
            for i, (op, line, a) in enumerate(body):
                s = None
                if op in (0x1A, 0x1B):
                    rc, rn, rd = c.ref(a)
                    if rc == "java.lang.RawEdit":
                        s = f"{rc}.{rn}{rd}"
                elif op == 0x19 and c.cname(a) == "java.lang.RawEdit":
                    s = "class-ref"
                elif op in (0x12, 0x25, 0x26) and c.s(a) in ("getI", "getF", "setI", "setF", "CentCur"):
                    s = "by-name " + c.s(a)
                if s:
                    win = []
                    for j in range(max(0, i - 10), min(len(body), i + 3)):
                        o, ln, x = body[j]
                        win.append(f"{o:02X}" + ("" if x is None else ":" + (c.s(x) if o in (0x09, 0x12) else str(x))))
                    sites.append(dict(file=os.path.relpath(c.file, ROOT), cls=c.name, body=bi, line=line, ref=s, op=f"{op:02X}",
                                      window=" ".join(win)))
    merged = {}
    for x in sites:
        k = (x["cls"], x["body"], x["line"])
        if k in merged:
            if x["ref"] not in merged[k]["ref"]:
                merged[k]["ref"] += " + " + x["ref"]
        else:
            merged[k] = x
    return list(merged.values())


def rawedit_addresses(classes):
    """Address table from RawEdit.class static initialisers (name, kind, address)."""
    re_cls = [c for c in classes if c.name == "java.lang.RawEdit"][0]
    out = []
    for f in re_cls.fields:
        static, flags, n, d, val = f
        if val == T.NONE:
            continue
        body = re_cls.bodies[val]
        addr = next((a for (op, _, a) in body if op == 0x0B), None)
        typ = None
        for (op, _, a) in body:
            if op == 0x06:
                typ = re_cls.s(a)
        out.append((re_cls.s(n), typ, addr))
    methods = [(re_cls.s(m[2]), re_cls.s(m[3]), hex(m[1])) for m in re_cls.methods]
    return out, methods


def main():
    classes = T.load_corpus(ROOT)
    addrs, methods = rawedit_addresses(classes)
    src_addrs = {}
    srctxt = open(os.path.join(ROOT, "system/Scripts/lang/src/RawEdit.java"), encoding="latin1").read()
    for t, n, a in re.findall(r"(\w+Address) (Address_\w+)\s*=\s*new \w+\((0x[0-9A-Fa-f]+)\)", srctxt):
        src_addrs[n] = (t, int(a, 16))
    cls_map = {n: a for n, t, a in addrs}
    mism = [n for n in src_addrs if cls_map.get(n) != src_addrs[n][1]]

    R = P.Image(os.path.join(ROOT, "StreetLegal_Redline.exe"))
    G = P.Image(os.path.join(ROOT, "Slrr_GI.exe"))
    R.build()
    G.build()
    natrows = [l.split("\t") for l in open(os.path.join(ROOT, "docs/research/natives-redline.tsv")).read().split("\n")[1:] if l]
    fnames = {}
    for r in natrows:
        fnames.setdefault(int(r[4], 16), []).append(r[1].split(".")[-1] + "." + r[2])
    starts = sorted(set(fnames) | set(R._funcs))

    def func_label(img, x):
        f = img.func_of(x)
        if img is G and x >= 0x7c0000:
            tgt = None
            for ins in G.disasm_at(x, 8):
                if ins.mnemonic == "jmp" and ins.op_str.startswith("0x"):
                    t = int(ins.op_str, 16)
                    if t < 0x7c0000:
                        tgt = t
                        break
            if tgt:
                vf = R.func_of(tgt)
                return f"cave->{tgt:#x}(fn {vf:#x})" if vf else f"cave->{tgt:#x}"
            return "cave"
        i = bisect.bisect_right(starts, x) - 1
        s = starts[i]
        if s in fnames and (f is None or f <= s):
            return f"{fnames[s][0]}@{s:#x}"
        return f"{f:#x}" if f else "?"

    def former_string(img, a):
        """Longest printable run (>=6 chars) overlapping [a-8, a+12) in the image, or ''."""
        o = a - img.base
        m = img.mem
        best = ""
        i = o - 8
        while i < o + 12:
            if 32 <= m[i] < 127:
                st = i
                while st > 0 and 32 <= m[st - 1] < 127:
                    st -= 1
                e = i
                while e < len(m) and 32 <= m[e] < 127:
                    e += 1
                if e - st >= 6 and e - st > len(best):
                    best = bytes(m[st:e]).decode("latin1")[:48]
                i = e
            else:
                i += 1
        return best

    def rdata_val(img, ins):
        for op in ins.operands:
            if op.type == 3 and op.mem.base == 0 and 0x5e7000 <= (op.mem.disp & 0xFFFFFFFF) < 0x607000:
                try:
                    return f"{img.f32(op.mem.disp & 0xFFFFFFFF):.6g}"
                except Exception:
                    return None
        return None

    rows = []
    for name, (t, a) in src_addrs.items():
        short = name[len("Address_"):]
        k = K.get(short)
        sec, has = R.section_of(a)
        rv = R.read(a, 4) if has else b""
        gv = G.read(a, 4) if has else b""
        def val(b, typ):
            if not b:
                return ""
            return f"{struct.unpack('<f', b)[0]:.6g}" if typ.startswith("Float") else str(struct.unpack('<i', b)[0])
        nonpush = any(R.disasm_at(x)[0].mnemonic != "push" for x in R.xrefs(a))
        r_ascii = "" if (nonpush or not has) else former_string(R, a)
        rrefs = R.xrefs(a)
        grefs = G.xrefs(a)
        # references that exist only in GI (new code) vs patched vanilla sites
        gsites = []
        for x in grefs:
            vanilla = ""
            if x < 0x7c0000:
                gi_b, r_b = G.read(x, 8), R.read(x, 8)
                if gi_b != r_b:
                    try:
                        ri = R.disasm_at(x)[0]
                        vanilla = f"{ri.mnemonic} {ri.op_str}"
                        cv = rdata_val(R, ri)
                        if cv is not None:
                            vanilla += f" (={cv})"
                    except Exception:
                        vanilla = "(vanilla code replaced)"
            gi_i = G.disasm_at(x)[0]
            gsites.append(dict(va=f"{x:#x}", where=func_label(G, x), ins=f"{gi_i.mnemonic} {gi_i.op_str}", vanilla_at_site=vanilla))
        rsites = []
        for x in rrefs:
            ri = R.disasm_at(x)[0]
            rsites.append(dict(va=f"{x:#x}", where=func_label(R, x), ins=f"{ri.mnemonic} {ri.op_str}"))
        origin = "mod variable over former vanilla string" if r_ascii else "vanilla variable"
        rows.append(dict(
            name=short, type=("float" if t.startswith("Float") else "int"), address=f"{a:#010x}", class_address=(f"{cls_map.get(name):#010x}" if cls_map.get(name) else ""),
            section=sec, initialised_in_file=has,
            redline_value=val(rv, t), redline_ascii=r_ascii, gi_default=val(gv, t),
            config_key=k[0] if k else "", group=k[1] if k else "", proposed_property=k[2] if k else "", prop_type=k[3] if k else "",
            meaning=k[4] if k else "", confidence=k[5] if k else "",
            origin=origin, hooked_functions=sorted({g["where"].split("fn ")[-1].rstrip(")") if "fn " in g["where"] else g["where"] for g in gsites if "push" not in g["ins"]}), redline_refs=len(rrefs), gi_refs=len(grefs),
            gi_refs_in_cave=sum(1 for x in grefs if x >= 0x7c0000), gi_patched_sites=sum(1 for s in gsites if s["vanilla_at_site"]),
            redline_sites=rsites[:12], gi_sites=gsites[:12]))
    # Redline pushes of the address as an immediate are vanilla string pushes when the Redline bytes are ASCII
    for r in rows:
        if r["redline_ascii"] or not r["initialised_in_file"] is False and r["origin"].startswith("mod"):
            pass

    # GI data/text patch inventory (summary numbers)
    ra = P.pefile.PE(os.path.join(ROOT, "StreetLegal_Redline.exe"))
    ga = P.pefile.PE(os.path.join(ROOT, "Slrr_GI.exe"))
    patch = {}
    for s in ra.sections:
        n = s.Name.rstrip(b"\0").decode()
        da = s.get_data()
        sb = [x for x in ga.sections if x.Name == s.Name][0]
        db = sb.get_data()
        L = min(len(da), len(db))
        ranges, i = [], 0
        while i < L:
            if da[i] != db[i]:
                j = i
                while j < L and (da[j] != db[j] or (j + 1 < L and da[j + 1] != db[j + 1]) or (j + 2 < L and da[j + 2] != db[j + 2])):
                    j += 1
                ranges.append((i, j))
                i = j
            else:
                i += 1
        patch[n] = dict(ranges=len(ranges), bytes=sum(j - i for i, j in ranges))
    cave = [s for s in ga.sections if s.Name.startswith(b".code")][0].get_data()
    patch[".code"] = dict(nonzero_bytes=sum(1 for b in cave if b), last_used=f"{0x7c0000 + max(i for i, b in enumerate(cave) if b):#x}")

    indexed = []
    for ln in open(os.path.join(ROOT, "parts/scripts/bodypart/Src/Chassis.java"), encoding="latin1").read().split("\n"):
        m = re.match(r"\s*public static final int (IndexedData_\w+)\s*=\s*(0x[0-9A-Fa-f]+)(?:\s*-\s*(0x[0-9A-Fa-f]+))?\s*;\s*(?://\s*(.*))?", ln)
        if m:
            v = int(m.group(2), 16) - (int(m.group(3), 16) if m.group(3) else 0)
            indexed.append(dict(name=m.group(1), offset=f"{v:#x}", expr=(m.group(2) + ("-" + m.group(3) if m.group(3) else "")), comment=m.group(4) or ""))
    src_use = parse_sources()
    csites = class_sites(classes)
    sum_ = dict(
        addresses=len(rows), by_origin={o: sum(1 for r in rows if r["origin"] == o) for o in {r["origin"] for r in rows}},
        class_vs_source_address_mismatches=mism, rawedit_class_methods=methods, class_sites=len(csites),
        class_files_with_sites=sorted({s["file"] for s in csites}), java_source_hits=len(src_use),
        java_source_files=sorted({s[0] for s in src_use}), patch_inventory=patch,
        by_confidence={c: sum(1 for r in rows if r["confidence"] == c) for c in "HML"},
        by_group={g: sum(1 for r in rows if r["group"] == g) for g in sorted({r["group"] for r in rows})},
        relative_offsets=REL,
    )
    os.makedirs(OUT, exist_ok=True)
    with open(os.path.join(OUT, "rawedit-chassis-offsets.tsv"), "w", encoding="utf-8") as f:
        f.write("name\toffset\texpression\tsource_comment\n")
        for x in indexed:
            f.write(f"{x['name']}\t{x['offset']}\t{x['expr']}\t{x['comment']}\n")
    json.dump(dict(summary=sum_, chassis_indexed=indexed, addresses=rows, class_sites=csites,
                   java_sites=[dict(file=a, line=b, commented=c, text=d) for a, b, c, d in src_use]),
              open(os.path.join(OUT, "rawedit.json"), "w"), indent=1)
    cols = ["name", "type", "address", "section", "redline_value", "redline_ascii", "gi_default", "config_key", "group",
            "proposed_property", "prop_type", "confidence", "origin", "redline_refs", "gi_refs", "gi_refs_in_cave", "gi_patched_sites", "meaning"]
    with open(os.path.join(OUT, "rawedit-addresses.tsv"), "w", encoding="utf-8") as f:
        f.write("\t".join(cols) + "\n")
        for r in rows:
            f.write("\t".join(str(r[c]).replace("\t", " ") for c in cols) + "\n")
    with open(os.path.join(OUT, "rawedit-callsites.tsv"), "w", encoding="utf-8") as f:
        f.write("kind\tfile\tline_or_body\tcommented\ttext\n")
        for s in csites:
            f.write(f"class\t{s['file']}\tbody{s['body']}/L{s['line']}\t0\t{s['ref']} :: {s['window']}\n")
        for a, b, c, d in src_use:
            f.write(f"java\t{a}\t{b}\t{int(c)}\t{d}\n")
    print(json.dumps({k: v for k, v in sum_.items() if k not in ("relative_offsets", "rawedit_class_methods")}, indent=1))
    # markdown fragment
    with open(os.path.join(OUT, "rawedit-table.md.fragment"), "w", encoding="utf-8") as f:
        f.write("| Name | Addr | Type | Redline value or nearby text | GI default | Config key | Redline / GI refs (cave) | Conf | Proposed property |\n|---|---|---|---|---|---|---|---|---|\n")
        for r in rows:
            rv = ("`" + r["redline_ascii"] + "` (text)") if r["redline_ascii"] else (r["redline_value"] or "BSS")
            f.write(f"| {r['name']} | {r['address'][2:].lstrip('0')} | {r['type']} | {rv} | {r['gi_default'] or 'BSS'} | {r['config_key'] or '-'} | "
                    f"{r['redline_refs']} / {r['gi_refs']} ({r['gi_refs_in_cave']}) | {r['confidence']} | {r['proposed_property']} |\n")


if __name__ == "__main__":
    main()

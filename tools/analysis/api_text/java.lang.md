# java.lang

Script roots: `system\Scripts\lang\*.class` (16 files). All of `java.lang` is script code except the natives below.
The VM itself (opcodes, `Thread::run`) is specified in `docs/research/script-vm-opcodes.md`; this page covers the
classes the VM and the content assume.

## Threading, ordering and cooperation model

Facts (H unless noted):

* A `Thread` is a cooperative VM thread. `Thread::run(float budget)` gives each thread a time slice per engine tick
  and checks the suspend flags after every instruction (docs/research/script-vm-opcodes.md section 1). There is no
  preemption inside an instruction and no exception mechanism: a script error prints `!Script error` /
  `Thread: %s` and stops THAT thread (docs section 5).
* Threads are named. The names the engine uses itself (strings in the exe): `THRD-CLASSVAR-INI`,
  `THRD-INSTVAR-INI` (field initialisers), `THRD-CREATE` (the constructor call made by `GameRef.create`),
  `THRD-RUNVMI` (event/callback invocations), `THRD-FIN` (finalizers). Script `new Thread(target, "name")` adds user threads.
* `Object.wait()` / `notify()` / `notifyAll()` work on any object and are the only synchronisation primitive. Seen
  patterns: (1) worker loop `while(1){ obj.wait(); work; }` woken by `obj.notify()` (GameLogic clock text, GameType async
  message port, Mechanic/CarLot workers); (2) `Signal.set(v)`/`waitFor(v)`; (3) `LoadingScreen` waits on
  `Frontend.render`, which the engine notifies once per frame (H: the per-frame code at 0x428db9 loads class
  `java.render.Frontend`, looks up its field `render` and calls 0x408ce0, which is exactly the routine the native
  `Object.notifyAll` (0x47c970) tail-jumps to; script side: `LoadingScreen.run` is
  `while(1){ Frontend.render.wait(); ... }`). A C# VM therefore has to run `notifyAll(Frontend.render)` every frame.
* Timing: `Thread.sleep(ms)` takes milliseconds (float). `GameType.addTimer(seconds,id)` takes seconds and fires
  `handleEvent(self, EVENT_TIME, id)` once.
* `GameLogic implements Runnable` with a **static** `run()` and hands `this` to `new Thread(this, ...)`: the VM must resolve
  `run` on the target by name including static methods (M).
* Async dispatch: a `GameType` that called `enterAsyncMode()` has `asyncMode=1`. The exe reads that field by name when it
  delivers an event (xref at 0x425f12) and, hypothesis, uses `handleEventAsync(...)` which only does
  `mp.putMessage(...)` into the object's `MessagePort`; the object's own thread (`GameType.run`) drains the port and calls
  `handleMessage(Message)` / `handleMessage(Event)`. 44 part classes define `handleMessage(Event)` (tuning dialogs).
* There is no `synchronized`, `volatile`, `try` or `throw` in any shipped script (the in-engine compiler rejects them).

{{CLASS java.lang.Object}}

{{CLASS java.lang.Native}}

{{CLASS java.lang.Runnable}}

{{CLASS java.lang.Thread}}

{{CLASS java.lang.Signal}}

## Strings and numbers

{{CLASS java.lang.String}}

{{CLASS java.lang.Integer}}

{{CLASS java.lang.Float}}

{{CLASS java.lang.Math}}

Not available natively: sin, cos, tan, atan2, abs, min, max, floor, pow. The shipped scripts that need trigonometry use
`Vector3.rotate`/`Ypr` or hand-written polynomials; whatever C# implements must not add semantics beyond the four
natives (extra natives would only be dead code).

## System

{{CLASS java.lang.System}}

## Vectors

{{CLASS java.lang.Vector3}}

{{CLASS java.lang.Ypr}}

`Pori` (position+orientation pair) is script only (add/diff/mul). `Class` is an empty marker class.

## GameType

{{CLASS java.lang.GameType}}

The lifecycle of a GameType, the callbacks the engine invokes on it and the notification/timer protocol are in
[entry-points.md](entry-points.md).

## RawEdit (Slrr_GI.exe only)

{{CLASS java.lang.RawEdit}}

`RawEdit` is the mod's backdoor into the **patched** executable `Slrr_GI.exe`: five extra natives peek/poke absolute
process addresses of that exe. A raw memory model cannot be supported, but the address list below is a precise
catalogue of engine-internal tuning variables (physics multipliers, AI aggressiveness, memory budgets, video toggles)
that the C# engine has to expose as typed settings if the mod's scripts are to keep working. The table is parsed from
`system/Scripts/lang/src/RawEdit.java`:

{{RAWEDIT}}

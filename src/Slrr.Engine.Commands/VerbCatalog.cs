namespace Slrr.Engine.Commands;

/// <summary>Which native object kind parses a verb (grouping by exe string address, entry-points.md section 5).</summary>
public enum VerbGroup
{
    /// <summary>Controller / player (0x60d840..0x60d90c, copy 0x612b1c..0x612c14). Bot brains accept the same verbs.</summary>
    Controller,

    /// <summary>Bot brain AI verbs (0x60d914..0x60da38), native kind <c>bot</c> (sl.rpk:0x6E BOTBRAIN).</summary>
    Brain,

    /// <summary>Camera objects (0x60dcb4..0x60e008), native kind <c>camera</c> (GameRef.RID_CAMERA = system.rpk:0x33).</summary>
    Camera,

    /// <summary>Cars (0x610474..0x6108fc), native kind <c>car</c>.</summary>
    Car,

    /// <summary>Mouse cursor (0x610cd8..0x610da4), native kind <c>cursor</c>.</summary>
    Cursor,

    /// <summary>Map / ground (0x610fac..0x610fd8).</summary>
    Ground,

    /// <summary>Part structure verbs (0x6124b4..0x6125c4 and the car's add_/rem_ structure verbs): owned by Slrr.Engine.Parts.</summary>
    Part,
}

/// <summary>What this project does with a verb.</summary>
public enum VerbStatus
{
    /// <summary>Parsed with the exe format and changes state that drives behaviour (camera pose, AI input, events).</summary>
    Implemented,

    /// <summary>Parsed with the exe format and stored for other areas (renderer, physics, UI); no behaviour here.</summary>
    Recorded,

    /// <summary>Not handled by this project (part structure verbs belong to Slrr.Engine.Parts).</summary>
    NotOwned,
}

/// <summary>
/// One command verb: exe sscanf format (MEASURED: string next to the verb word in StreetLegal_Redline.exe; null = plain
/// verb matched by string compare), its address, the number of shipped script call sites that send it (MEASURED:
/// <c>command(..)</c> / <c>queueEvent(.., EVENT_COMMAND, ..)</c> sites with a literal verb over all 1,692 .class files,
/// see docs/race/commands.md) and what this project does with it.
/// </summary>
public sealed record VerbSpec(string Verb, VerbGroup Group, string? Format, int Va, int ScriptSites, VerbStatus Status, string Note);

/// <summary>The complete verb vocabulary of the exe's command parsers plus the script call-site census.</summary>
public static class VerbCatalog
{
    /// <summary>Call sites (command/queueEvent EVENT_COMMAND) with a literal verb in the shipped classes (MEASURED).</summary>
    public const int TotalVerbSites = 526;

    /// <summary>Call sites whose command text is not a literal (MEASURED: ROCTrack <c>roc.getCurrentAIParam()</c>, GameRef.command(p0)).</summary>
    public const int NonLiteralSites = 2;

    private static readonly VerbSpec[] All =
    [
        // ---------------------------------------------------------------- controller / player (+ brain copies)
        new("controllable", VerbGroup.Controller, "%*s %d", 0x60d8cc, 16, VerbStatus.Implemented, "binds the controlled object (car or free camera); answers GII_OWNER"),
        new("leave", VerbGroup.Controller, "%*s %d", 0x60d8dc, 15, VerbStatus.Implemented, "unbinds the controlled object"),
        new("camera", VerbGroup.Controller, "%*s %d", 0x60d8a8, 16, VerbStatus.Implemented, "camera that renders the controller's view"),
        new("renderinstance", VerbGroup.Controller, "%*s %d", 0x60d898, 8, VerbStatus.Recorded, "driver figure render instance"),
        new("viewport", VerbGroup.Controller, "%*s %d", 0x60d85c, 6, VerbStatus.Recorded, "viewport of the controller's view (0 = none)"),
        new("osd", VerbGroup.Controller, "%*s %d", 0x60d868, 6, VerbStatus.Recorded, "HUD Osd bound to the controller (car copy: %d %d)"),
        new("device", VerbGroup.Controller, "%*s %d", 0x60d848, 1, VerbStatus.Recorded, "input device of the controller"),
        new("setcontrol", VerbGroup.Controller, null, 0x612b40, 1, VerbStatus.Recorded, "re-reads the control set (Input)"),
        new("control", VerbGroup.Controller, "%*s %d", 0x60d8ec, 0, VerbStatus.Recorded, "no script sends it"),
        new("lost", VerbGroup.Controller, "%*s %d", 0x60d8fc, 0, VerbStatus.Recorded, "no script sends it"),
        new("team", VerbGroup.Controller, "%*s %d", 0x60d90c, 0, VerbStatus.Recorded, "no script sends it (car copy 0x6105c4)"),

        // ---------------------------------------------------------------- bot brain
        new("AI_BeginRace", VerbGroup.Brain, "%*s %f", 0x60d960, 9, VerbStatus.Implemented, "arms a race start; the float is stored as reaction delay (role GUESSED)"),
        new("AI_params", VerbGroup.Brain, "%*s %f %f", 0x60da38, 2, VerbStatus.Recorded, "two driving parameters (roles unknown)"),
        new("AI_suspend", VerbGroup.Brain, null, 0x60d914, 2, VerbStatus.Implemented, "brain stops producing input"),
        new("AI_follow", VerbGroup.Brain, "%*s %f,%f,%f %d", 0x60d990, 2, VerbStatus.Implemented, "follow object id at an offset in its frame"),
        new("AI_race", VerbGroup.Brain, "%*s %f,%f,%f %d", 0x60d940, 1, VerbStatus.Implemented, "race to x,y,z against opponent car id"),
        new("AI_GoToTarget", VerbGroup.Brain, "%*s %f,%f,%f %f,%f,%f", 0x60d9d4, 1, VerbStatus.Implemented, "drive straight to x,y,z (second point optional)"),
        new("AI_GoToTraffic", VerbGroup.Brain, null, 0x60d9a0, 1, VerbStatus.Implemented, "hand the car to the traffic director"),
        new("AI_GoToTrafficSlow", VerbGroup.Brain, null, 0x60d9b0, 0, VerbStatus.Implemented, "as AI_GoToTraffic (slow variant)"),
        new("AI_stop", VerbGroup.Brain, null, 0x60d9ec, 1, VerbStatus.Implemented, "brake to standstill"),
        new("AI_level", VerbGroup.Brain, "%*s %f", 0x60da00, 1, VerbStatus.Implemented, "skill level; scales the AI target speed (scale GUESSED)"),
        new("AI_spline", VerbGroup.Brain, "%*s %f %s %d", 0x60d974, 1, VerbStatus.Recorded, "follow a named spline (no script path reaches it in the shipped flow)"),
        new("AI_NightRace", VerbGroup.Brain, "%*s %f", 0x60d930, 1, VerbStatus.Implemented, "night-race mode flag + float"),
        new("AI_smooth", VerbGroup.Brain, "%*s %f", 0x60da14, 0, VerbStatus.Recorded, "no script sends it"),
        new("AI_horn", VerbGroup.Brain, "%*s %d", 0x60da24, 0, VerbStatus.Recorded, "no script sends it"),

        // ---------------------------------------------------------------- camera
        new("look", VerbGroup.Camera, "%*s %d %f,%f,%f %f,%f,%f %f %f,%f,%f", 0x60dec4, 23, VerbStatus.Implemented, "aim at object id + offset in its frame (id of a map = world point)"),
        new("angle", VerbGroup.Camera, "%*s %f %f %f %f", 0x60df98, 21, VerbStatus.Implemented, "orbit angles around the look target (roles GUESSED)"),
        new("render", VerbGroup.Camera, "%*s %d %d %d %d %d", 0x60de34, 19, VerbStatus.Implemented, "render into viewport id with flags (car copy %d %d %d = interior camera)"),
        new("move", VerbGroup.Camera, "%*s %d %f,%f,%f %f %f,%f,%f", 0x60def4, 18, VerbStatus.Implemented, "anchor to object id + offset, stiffness, optional camera offset (cursor copy %f,%f)"),
        new("dist", VerbGroup.Camera, "%*s %f %f", 0x60df18, 15, VerbStatus.Implemented, "min/max distance to the look target"),
        new("simulate", VerbGroup.Camera, "%*s %d", 0x60e008, 15, VerbStatus.Implemented, "1 = camera body simulated (free flight), 0 = rigid follow (GUESSED)"),
        new("smooth", VerbGroup.Camera, "%*s %f %f", 0x60df2c, 10, VerbStatus.Implemented, "position / orientation smoothing"),
        new("zoom", VerbGroup.Camera, "%*s %f %f", 0x60df68, 8, VerbStatus.Implemented, "field of view (degrees) and rate"),
        new("force", VerbGroup.Camera, "%*s %f %f %f", 0x60df50, 8, VerbStatus.Implemented, "spring parameters of the follow camera (roles GUESSED)"),
        new("torque", VerbGroup.Camera, "%*s %f", 0x60df40, 8, VerbStatus.Implemented, "orientation stiffness"),
        new("hide", VerbGroup.Camera, "%*s %d", 0x60de7c, 6, VerbStatus.Implemented, "stop rendering into viewport id (car copy %d %d)"),
        new("roll", VerbGroup.Camera, "%*s %f", 0x60dfb0, 5, VerbStatus.Implemented, "roll angle (radians)"),
        new("height", VerbGroup.Camera, "%*s %f", 0x60dfc0, 1, VerbStatus.Implemented, "height above the anchor"),
        new("autozoom", VerbGroup.Camera, "%*s %d %f %f %f", 0x60df80, 1, VerbStatus.Implemented, "distance-driven zoom (roles GUESSED)"),
        new("pitch", VerbGroup.Camera, "%*s %f", 0x60dfd0, 0, VerbStatus.Implemented, "no script sends it"),
        new("maxpitch", VerbGroup.Camera, "%*s %f", 0x60dfe4, 0, VerbStatus.Implemented, "no script sends it"),
        new("follow", VerbGroup.Camera, "%*s %d", 0x60dff4, 0, VerbStatus.Recorded, "no script sends it"),
        new("status", VerbGroup.Camera, "%*s %d", 0x60dcbc, 0, VerbStatus.Recorded, "no script sends it"),
        new("activate", VerbGroup.Camera, "%*s %d", 0x60de90, 2, VerbStatus.Recorded, "camera copy; scripts send it to the cursor (cursor %d %d, car %d %d %d)"),
        new("deactivate", VerbGroup.Camera, null, 0x60dea0, 1, VerbStatus.Recorded, "MouseCursor sends it to its controlled camera"),
        new("fog", VerbGroup.Camera, "%*s %d &f &f", 0x60de64, 0, VerbStatus.Recorded, "exe format typo '&f' (also ground copy 0x610fb0): only the int parses"),

        // ---------------------------------------------------------------- car
        new("setsteer", VerbGroup.Car, "%*s %f", 0x61064c, 35, VerbStatus.Implemented, "steering override"),
        new("idle", VerbGroup.Car, null, 0x610638, 34, VerbStatus.Implemented, "hold the car with the engine idling (GUESSED)"),
        new("start", VerbGroup.Car, null, 0x610610, 33, VerbStatus.Implemented, "release the car for driving"),
        new("reset", VerbGroup.Car, null, 0x610628, 32, VerbStatus.Implemented, "zero velocities"),
        new("stop", VerbGroup.Car, null, 0x610600, 31, VerbStatus.Implemented, "hold the car (no drive input)"),
        new("filter", VerbGroup.Car, "%*s %d %d", 0x6108c4, 17, VerbStatus.Recorded, "input/render filter channel = value (roles unknown; part copy 0x6125c4)"),
        new("brake", VerbGroup.Car, null, 0x610608, 13, VerbStatus.Implemented, "brake to standstill and hold"),
        new("suspend", VerbGroup.Car, null, 0x610618, 10, VerbStatus.Implemented, "physics sleep until wakeup"),
        new("reload", VerbGroup.Car, null, 0x610630, 10, VerbStatus.Recorded, "reload car state"),
        new("corpse", VerbGroup.Car, "%*s %d %d", 0x610510, 6, VerbStatus.Recorded, "driver render instance in the seat (second int)"),
        new("texture", VerbGroup.Car, "%*s %d %d", 0x6106f0, 5, VerbStatus.Recorded, "paint texture/colour (renderer)"),
        new("sethorn", VerbGroup.Car, "%*s %d", 0x6106a0, 5, VerbStatus.Implemented, "horn on/off; Vehicle.getHorn"),
        new("wakeup", VerbGroup.Car, null, 0x610620, 4, VerbStatus.Implemented, "ends suspend"),
        new("difflock", VerbGroup.Car, "%*s %f", 0x610574, 2, VerbStatus.Recorded, "assist setting for physics"),
        new("transmission", VerbGroup.Car, "%*s %d", 0x61053c, 1, VerbStatus.Recorded, "assist setting for physics"),
        new("steerhelp", VerbGroup.Car, "%*s %f", 0x610588, 1, VerbStatus.Recorded, "assist setting for physics"),
        new("asr", VerbGroup.Car, "%*s %f", 0x610554, 1, VerbStatus.Recorded, "assist setting for physics"),
        new("abs", VerbGroup.Car, "%*s %f", 0x610548, 1, VerbStatus.Recorded, "assist setting for physics"),
        new("cruise", VerbGroup.Car, "%*s %f", 0x610598, 1, VerbStatus.Recorded, "cruise control setting"),
        new("damage_multiplier", VerbGroup.Car, "%*s %f", 0x6105b4, 1, VerbStatus.Recorded, "damage scale"),
        new("flap_toggle", VerbGroup.Car, null, 0, 1, VerbStatus.Recorded, "no exe string next to a parser (Mechanic sends it)"),
        new("esp", VerbGroup.Car, "%*s %f", 0x610560, 0, VerbStatus.Recorded, "no script sends it"),
        new("next", VerbGroup.Car, "%*s %d", 0x6104f0, 0, VerbStatus.Recorded, "interior camera next"),
        new("prev", VerbGroup.Car, "%*s %d", 0x610500, 0, VerbStatus.Recorded, "interior camera previous"),
        new("pickup", VerbGroup.Car, "%*s %d %d", 0x6105d4, 0, VerbStatus.Recorded, "no script sends it"),
        new("setpitch", VerbGroup.Car, "%*s %f", 0x610660, 0, VerbStatus.Recorded, "no script sends it"),
        new("addroll", VerbGroup.Car, "%*s %f", 0x610670, 0, VerbStatus.Recorded, "no script sends it"),
        new("setvel", VerbGroup.Car, "%*s %f,%f,%f,%f,%f,%f", 0x610680, 0, VerbStatus.Implemented, "set linear (+ angular) velocity"),
        new("autopilot_on", VerbGroup.Car, null, 0x6108d0, 0, VerbStatus.Recorded, "no script sends it"),
        new("autopilot_off", VerbGroup.Car, null, 0x6108e0, 0, VerbStatus.Recorded, "no script sends it"),
        new("ai_cheat", VerbGroup.Car, "%*s %f %f %f %f %f %f %f", 0x6108fc, 0, VerbStatus.Recorded, "no script sends it"),
        new("cancel", VerbGroup.Car, null, 0x6106d8, 0, VerbStatus.Recorded, "no script sends it"),
        new("mesh", VerbGroup.Car, "%*s %d", 0x610704, 0, VerbStatus.Recorded, "no script sends it"),

        // ---------------------------------------------------------------- cursor
        new("lock", VerbGroup.Cursor, null, 0x610d54, 4, VerbStatus.Implemented, "cursor locked to the screen centre"),
        new("unlock", VerbGroup.Cursor, null, 0x610d5c, 4, VerbStatus.Implemented, "cursor free"),
        new("cursor", VerbGroup.Cursor, "%*s %d %d %d", 0x610ce0, 1, VerbStatus.Recorded, "viewport, camera, cursor object"),
        new("mode", VerbGroup.Cursor, "%*s %d %1s %d", 0x610cf8, 1, VerbStatus.Recorded, "pointer mode"),
        new("enable", VerbGroup.Cursor, null, 0x610d3c, 1, VerbStatus.Implemented, "cursor shown"),
        new("disable", VerbGroup.Cursor, null, 0x610d44, 1, VerbStatus.Implemented, "cursor hidden"),
        new("sens", VerbGroup.Cursor, null, 0x610d64, 1, VerbStatus.Recorded, "re-reads Config.mouseSensitivity"),
        new("relmove", VerbGroup.Cursor, "%*s %f,%f", 0x610d88, 0, VerbStatus.Recorded, "no script sends it"),
        new("color", VerbGroup.Cursor, "%*s %d", 0x610d10, 0, VerbStatus.Recorded, "no script sends it"),
        new("standby", VerbGroup.Cursor, null, 0x610d4c, 0, VerbStatus.Recorded, "no script sends it"),

        // ---------------------------------------------------------------- ground
        new("obs_add", VerbGroup.Ground, "%*s %d", 0x610fc8, 4, VerbStatus.Implemented, "observer object (streaming / traffic centre)"),
        new("obs_rem", VerbGroup.Ground, "%*s %d", 0x610fd8, 3, VerbStatus.Implemented, "removes an observer"),

        // ---------------------------------------------------------------- part structure (Slrr.Engine.Parts)
        new("install", VerbGroup.Part, "%*s %d %d %d %d %d %f %f %f", 0x6124b4, 5, VerbStatus.NotOwned, "Slrr.Engine.Parts"),
        new("remove", VerbGroup.Part, "%*s %d %d", 0x6124d8, 2, VerbStatus.NotOwned, "Slrr.Engine.Parts"),
        new("add_rollbar", VerbGroup.Part, "%*s %d %d %f %f", 0x610808, 2, VerbStatus.NotOwned, "car structure verb (Parts/physics)"),
        new("repair", VerbGroup.Part, "%*s %d", 0x6124f4, 1, VerbStatus.NotOwned, "Slrr.Engine.Parts"),
        new("rem_rollbar", VerbGroup.Part, "%*s %d %d %f %f", 0x610824, 0, VerbStatus.NotOwned, "car structure verb"),
        new("add_part", VerbGroup.Part, "%*s %d", 0x610770, 0, VerbStatus.NotOwned, "car structure verb"),
        new("rem_part", VerbGroup.Part, "%*s %d", 0x610784, 0, VerbStatus.NotOwned, "car structure verb"),
        new("add_light", VerbGroup.Part, "%*s %d %d", 0x6107a8, 0, VerbStatus.NotOwned, "car structure verb"),
        new("rem_light", VerbGroup.Part, "%*s %d %d", 0x6107c0, 0, VerbStatus.NotOwned, "car structure verb"),
        new("add_wing", VerbGroup.Part, "%*s %d %d", 0x6107d8, 0, VerbStatus.NotOwned, "car structure verb"),
        new("rem_wing", VerbGroup.Part, "%*s %d %d", 0x6107f0, 0, VerbStatus.NotOwned, "car structure verb"),
        new("add_linked", VerbGroup.Part, "%*s %d %d %d %d", 0x610840, 0, VerbStatus.NotOwned, "car structure verb"),
        new("rem_linked", VerbGroup.Part, "%*s %d %d %d %d", 0x61086c, 0, VerbStatus.NotOwned, "car structure verb"),
        new("add_mslot", VerbGroup.Part, "%*s %d %d", 0x610898, 0, VerbStatus.NotOwned, "car structure verb"),
        new("rem_mslot", VerbGroup.Part, "%*s %d %d", 0x6108b0, 0, VerbStatus.NotOwned, "car structure verb"),
        new("decal", VerbGroup.Part, "%*s %d,%d %f,%f,%f %f,%f,%f %f %f %d %d", 0x61253c, 0, VerbStatus.NotOwned, "part paint verb"),
        new("paint", VerbGroup.Part, "%*s %f %f %d %d %f", 0x61256c, 0, VerbStatus.NotOwned, "part paint verb"),
        new("colorscheme", VerbGroup.Part, "%*s %d", 0x61258c, 0, VerbStatus.NotOwned, "part paint verb"),
    ];

    /// <summary>Formats of verbs that several kinds parse differently (MEASURED: second string of the same verb).</summary>
    public static readonly IReadOnlyDictionary<(VerbGroup, string), string> KindFormats = new Dictionary<(VerbGroup, string), string>
    {
        [(VerbGroup.Car, "render")] = "%*s %d %d %d",            // 0x61047c
        [(VerbGroup.Car, "hide")] = "%*s %d %d",                 // 0x6104ac
        [(VerbGroup.Car, "activate")] = "%*s %d %d %d",          // 0x6104c4
        [(VerbGroup.Car, "deactivate")] = "%*s %d",              // 0x6104e0
        [(VerbGroup.Car, "osd")] = "%*s %d %d",                  // 0x610520
        [(VerbGroup.Car, "team")] = "%*s %d",                    // 0x6105c4
        [(VerbGroup.Cursor, "activate")] = "%*s %d %d",          // 0x610d24
        [(VerbGroup.Cursor, "move")] = "%*s %f,%f",              // 0x610da4
        [(VerbGroup.Ground, "fog")] = "%*s %d &f &f",            // 0x610fb0
    };

    public static IReadOnlyList<VerbSpec> Verbs => All;

    /// <summary>The spec of <paramref name="verb"/> (exact case) in its primary group, or null.</summary>
    public static VerbSpec? Find(string verb) => Array.Find(All, v => v.Verb == verb);

    /// <summary>The sscanf format a native kind applies to a verb.</summary>
    public static string? FormatFor(VerbGroup group, string verb) =>
        KindFormats.TryGetValue((group, verb), out var f) ? f : Find(verb)?.Format;

    /// <summary>Call-site coverage: sites of verbs this project implements or records, of all literal-verb sites.</summary>
    public static (int Implemented, int Recorded, int NotOwned, int Total) Coverage()
    {
        int impl = 0, rec = 0, other = 0;
        foreach (var v in All)
        {
            switch (v.Status)
            {
                case VerbStatus.Implemented:
                    impl += v.ScriptSites;
                    break;
                case VerbStatus.Recorded:
                    rec += v.ScriptSites;
                    break;
                default:
                    other += v.ScriptSites;
                    break;
            }
        }

        return (impl, rec, other, TotalVerbSites);
    }
}

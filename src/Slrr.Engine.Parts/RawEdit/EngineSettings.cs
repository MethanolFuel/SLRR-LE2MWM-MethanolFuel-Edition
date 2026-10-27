namespace Slrr.Engine.Parts.RawEdit;

/// <summary>
/// Typed replacement for the absolute addresses that <c>java.lang.RawEdit</c> pokes in <c>Slrr_GI.exe</c>
/// (docs/research/rawedit.md section 4). Every address is a named global tuning scalar; the engine subsystems read these
/// properties, only <see cref="RawAddressMap"/> knows addresses.
/// </summary>
public sealed class EngineSettings
{
    private EngineSettings(bool vanilla)
    {
        Graphics.ApplyPreset(vanilla);
        ResourceBudget.ApplyPreset(vanilla);
        Damage.ApplyPreset(vanilla);
        Wear.ApplyPreset(vanilla);
        Ai.ApplyPreset(vanilla);
        Physics.ApplyPreset(vanilla);
        Drivetrain.ApplyPreset(vanilla);
        Clock.ApplyPreset(vanilla);
    }

    public GraphicsSettings Graphics { get; } = new();

    public ResourceBudgetSettings ResourceBudget { get; } = new();

    public DamageSettings Damage { get; } = new();

    public WearSettings Wear { get; } = new();

    public AiSettings Ai { get; } = new();

    public PhysicsSettings Physics { get; } = new();

    public DrivetrainSettings Drivetrain { get; } = new();

    public ClockSettings Clock { get; } = new();

    /// <summary>
    /// The values patched into <c>Slrr_GI.exe</c> (the "GI default" column, measured from the file). This is the
    /// preset the shipped MethanolFuel scripts were written against.
    /// </summary>
    public static EngineSettings MethanolFuel() => new(vanilla: false);

    /// <summary>
    /// <c>StreetLegal_Redline.exe</c> file values for the 25 config-backed globals (measured). The 39 mod-created
    /// variables did not exist in Redline; they take the GI default (guess: the mod's neutral value).
    /// </summary>
    public static EngineSettings Vanilla() => new(vanilla: true);
}

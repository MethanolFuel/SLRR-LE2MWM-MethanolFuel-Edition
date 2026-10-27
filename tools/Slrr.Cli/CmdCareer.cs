#if SLRR_CAREER
namespace Slrr.Cli;

/// <summary>
/// <c>career</c>: the career-side race flows (src/Slrr.Career) on the shipped scripts: ROC, City day race, City night
/// race, collision damage → garage repair quote. Compiled when SLRR_CAREER is defined (the lead adds the Slrr.Career
/// reference and the define to Slrr.Cli.csproj, see docs/gaps/career.md); until then the career test project compiles it.
/// </summary>
public static class CmdCareer
{
    public const string Usage = Slrr.Career.CareerCommand.Usage;

    public static int Run(string[] args, TextWriter o, TextWriter e) => Slrr.Career.CareerCommand.Run(args, o, e);
}
#endif

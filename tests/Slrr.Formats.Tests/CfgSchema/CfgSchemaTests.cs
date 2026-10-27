using System.Text;
using Slrr.Cli;
using Slrr.Core;
using Slrr.Formats.Cfg;
using Slrr.Formats.CfgSchema;
using Xunit;

namespace Slrr.Formats.Tests.CfgSchema;

public class CfgSchemaTests
{
    private static List<Diagnostic> Check(string text) =>
        CfgDocument.Parse(Encoding.Latin1.GetBytes(text + "\neof\n"), "t.cfg").Directives
            .SelectMany(l => CfgArgSchemas.Default.Check(l, "t.cfg")).ToList();

    [Theory]
    [InlineData("0x00000D39", "hex")]
    [InlineData("-12", "int")]
    [InlineData("1.5e3", "float")]
    [InlineData(".5", "float")]
    [InlineData("0,1.8,0", "vector")]
    [InlineData("500:0.0", "pair")]
    [InlineData(@"cars\x\a.scx", "path")]
    [InlineData("sphere", "word")]
    public void ClassifiesTokens(string token, string type) => Assert.Equal(type, CfgArgTypes.Classify(token));

    [Fact]
    public void EmbeddedSchemaLoads()
    {
        var s = CfgArgSchemas.Default;
        Assert.True(s.Keywords.Count >= 80);
        var attach = s.Get("attach")!;
        Assert.Equal((2, 2), (attach.Min, attach.Max));
    }

    [Fact]
    public void AcceptsWellFormedLinesAndIgnoresUnknownKeywords()
    {
        Assert.Empty(Check("attach 0x0000023F 5\nlod 1 2\ncategory 3\nnosuchkeyword 1 2 3"));
    }

    [Fact]
    public void FlagsWrongCountAndType()
    {
        Assert.Contains(Check("attach 0x1"), d => d.Code == "CFG300");
        Assert.Contains(Check("attach 0x1 2 3"), d => d.Code == "CFG300");
        Assert.Contains(Check("lod 1 sphere"), d => d.Code == "CFG301");
        Assert.Empty(Check("#attach"));
    }

    [Fact]
    public void ShippedContentHasOnlyFewSchemaViolations()
    {
        var root = GameRoot.Find();
        if (root is null)
        {
            return;
        }

        var fs = new GameFileSystem(root);
        var bad = new List<Diagnostic>();
        var lines = 0;
        foreach (var f in fs.Enumerate(".cfg"))
        {
            foreach (var l in CfgDocument.Load(f).Directives)
            {
                lines++;
                bad.AddRange(CfgArgSchemas.Default.Check(l, f));
            }
        }

        Assert.True(lines > 20000);
        // schema is derived from this corpus with a 0.2% outlier trim: what remains are the trimmed defects
        Assert.True(bad.Count < lines / 500, $"{bad.Count} violations in {lines} lines");
        var sw = new StringWriter();
        Assert.Equal(0, CmdCfgSchema.Run([root], sw, sw));
        Assert.Contains("violations", sw.ToString());
        var dump = Environment.GetEnvironmentVariable("SLRR_SCHEMA_DUMP");
        if (!string.IsNullOrEmpty(dump))
        {
            File.WriteAllText(dump, sw.ToString());
        }
    }
}

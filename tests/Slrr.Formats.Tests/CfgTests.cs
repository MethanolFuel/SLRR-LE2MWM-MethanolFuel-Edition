using System.Text;
using Slrr.Core;
using Slrr.Formats.Cfg;
using Slrr.Formats.Validation;
using Xunit;

namespace Slrr.Formats.Tests;

public class CfgTests
{
    private static CfgDocument Parse(string text) => CfgDocument.Parse(Encoding.Latin1.GetBytes(text), "t.cfg");

    [Fact]
    public void ParsesDirectivesCommentsAndDisabledLines()
    {
        var d = Parse("render\t0x00000D39 ; body\nlinked\t301 501\n#click 0x3C\n// note\nstockpart 0x000000D7 // \"FL seat\" //\neof\n");
        Assert.False(d.HasErrors);
        Assert.True(d.HasEof);
        var directives = d.Directives.ToList();
        Assert.Equal(["render", "linked", "stockpart"], directives.Select(l => l.Keyword));
        Assert.Equal(["301", "501"], directives[1].Args);
        Assert.Equal("; body", directives[0].Comment);
        Assert.Equal(CfgLineKind.Disabled, d.Lines[2].Kind);
        Assert.Equal(CfgLineKind.Comment, d.Lines[3].Kind);
    }

    [Fact]
    public void NestedCompoundsTrackDepth()
    {
        var d = Parse("compound\ncompound\nchassis\nendcompound\tauto\nendcompound auto\neof\n");
        Assert.False(d.HasErrors);
        Assert.Equal(2, d.Lines.Single(l => l.Keyword == "chassis").Depth);
    }

    [Theory]
    [InlineData("endcompound\neof\n", "CFG010")]
    [InlineData("compound\nchassis\neof\n", "CFG011")]
    [InlineData("[STATE\neof\n", "CFG002")]
    [InlineData("render 0x1\u0001\neof\n", "CFG001")]
    public void StructuralProblemsAreErrors(string text, string code)
    {
        var d = Parse(text);
        Assert.Contains(d.Diagnostics, x => x.Code == code && x.Severity == Severity.Error);
    }

    [Fact]
    public void SoftProblemsAreWarnings()
    {
        var d = Parse("stcokpart 0x1\nrender 0x0000000??\n");
        Assert.False(d.HasErrors);
        Assert.Contains(d.Diagnostics, x => x.Code == "CFG100");
        Assert.Contains(d.Diagnostics, x => x.Code == "CFG101");
        Assert.Contains(d.Diagnostics, x => x.Code == "CFG013");
    }

    [Fact]
    public void TextAfterEofIsIgnored()
    {
        var d = Parse("eof\nendcompound\n");
        Assert.False(d.HasErrors);
        Assert.Equal(CfgLineKind.AfterEof, d.Lines[1].Kind);
    }

    [Fact]
    public void ParticleSectionsParse()
    {
        var d = Parse("[STATE]\nsource_size\tpoint\t0.30 0.00 0.00\n[ACTION]\nmove m0\t\t\t1 1\neof\n");
        Assert.False(d.HasErrors);
        Assert.Equal(2, d.Lines.Count(l => l.Kind == CfgLineKind.Section));
    }

    [Fact]
    public void EveryShippedCfgParsesWithoutErrors()
    {
        var root = GameRoot.Find();
        if (root is null)
        {
            return;
        }

        var files = new GameFileSystem(root).Enumerate(".cfg").ToList();
        Assert.Equal(1182, files.Count);
        var withErrors = files.Select(f => CfgDocument.Load(f)).Where(d => d.HasErrors).Select(d => d.Path).ToList();
        Assert.Empty(withErrors);
    }

    [Fact]
    public void ValidatorFindsNoErrorsInShippedContent()
    {
        var root = GameRoot.Find();
        if (root is null)
        {
            return;
        }

        var v = new ContentValidator(new GameFileSystem(root));
        v.ValidateAll();
        Assert.DoesNotContain(v.Diagnostics, d => d.Severity == Severity.Error);
        Assert.True(v.FileRefsChecked > 1000);
    }
}

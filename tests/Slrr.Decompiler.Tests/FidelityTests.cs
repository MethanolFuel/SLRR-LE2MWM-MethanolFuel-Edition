using Xunit;
using Xunit.Abstractions;

namespace Slrr.Decompiler.Tests;

public class FidelityTests(ITestOutputHelper output)
{
    [Fact]
    public void NormalisationToleratesKnownLossyDetails()
    {
        const string original = """
            package java.game;
            import java.io.*;
            // comment
            public class A extends B
            {
                static final int X = 0xFFC0C0C0, Y;
                public float deg2rad( float deg ) { return(deg * 3.141 / 180); }
                public void m( int count )
                {
                    int i, j = 2;
                    for( i=0; i<count; i++ ) if( i == j ) { { j++; } } else foo();
                    return;
                }
            }
            """;
        const string decompiled = """
            public class A extends B
            {
                final static int X = 0xFFC0C0C0;
                final static int Y;
                public float deg2rad( float f1 ) { return f1 * 3.141 / 180; }
                public void m( int i1 )
                {
                    int i2;
                    int i3 = 2;
                    for( i2 = 0; i2 < i1; i2++ )
                    {
                        if( i2 == i3 )
                        {
                            i3++;
                        }
                        else
                        {
                            foo();
                        }
                    }
                }
            }
            """;
        var a = SourceStructure.Parse(original).Single();
        var b = SourceStructure.Parse(decompiled).Single();
        Assert.Equal(a.Members.Select(m => m.Key), b.Members.Select(m => m.Key));
        foreach (var (x, y) in a.Members.Zip(b.Members))
        {
            var px = x.Tokens.Where(t => t is not ("(" or ")"));
            var py = y.Tokens.Where(t => t is not ("(" or ")"));
            Assert.Equal(string.Join(' ', px), string.Join(' ', py));
        }
    }

    [Fact]
    public void LcsCountsCommonTokens()
    {
        Assert.Equal(3, FidelityChecker.Lcs(["a", "b", "c", "d"], ["a", "c", "d", "e"]));
        Assert.Equal(0, FidelityChecker.Lcs([], ["x"]));
    }

    [Fact]
    public void CorpusDecompilesWithoutErrorsOrGotos()
    {
        var root = GameRoot.Find();
        if (root is null)
        {
            output.WriteLine("game content not found; skipped");
            return;
        }

        var report = CorpusDecompiler.Run(root);
        output.WriteLine(report.Summary());
        Assert.Equal(1692, report.Files.Count);
        Assert.Equal(1760, report.Classes);
        Assert.Equal(report.Classes, report.CleanClasses);
        Assert.Equal(0, report.Gotos);
        Assert.Equal(report.TotalBodies, report.Bodies + report.NativeBodies);
        Assert.Empty(report.Unresolved);
    }

    [Fact]
    public void ShippedSourcesRoundTrip()
    {
        var root = GameRoot.Find();
        if (root is null)
        {
            output.WriteLine("game content not found; skipped");
            return;
        }

        var report = FidelityChecker.Run(root);
        output.WriteLine(report.Summary());
        Assert.True(report.Pairs.Count >= 130, $"only {report.Pairs.Count} pairs");
        Assert.True(report.Matched >= 2500, $"only {report.Matched} matched members");

        // The remaining differences are edits made to the .java files after compilation (docs/decompiler.md).
        Assert.True(report.ExactNoParens >= report.Matched * 0.98, report.Summary());
        Assert.True(report.Similarity >= 0.99, report.Summary());
    }

    [Fact]
    public void CliDecompilesOneFile()
    {
        var root = GameRoot.Find();
        if (root is null)
        {
            output.WriteLine("game content not found; skipped");
            return;
        }

        var o = new StringWriter();
        var e = new StringWriter();
        var rc = Slrr.Cli.CmdDecompile.Run([Path.Combine(root, "parts", "scripts", "bodypart", "Mirror.class")], o, e);
        Assert.Equal(0, rc);
        Assert.Contains("public class Mirror extends BodyPart", o.ToString());
        Assert.Contains("public class SetAgain extends Thread", o.ToString());
        Assert.Equal(string.Empty, e.ToString());
        Assert.Equal(2, Slrr.Cli.CmdDecompile.Run([], o, e));
    }
}

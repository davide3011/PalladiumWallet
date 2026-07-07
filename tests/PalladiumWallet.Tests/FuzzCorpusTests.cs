using PalladiumWallet.Fuzz;

namespace PalladiumWallet.Tests;

/// <summary>
/// Replays the fuzzing seed corpus (tests/PalladiumWallet.Fuzz/Corpus, copied
/// next to the test binary) through every fuzz target on each test run: the
/// corpus includes the regression inputs for crashes found by past campaigns,
/// so a fixed contract violation can never silently come back. The real
/// coverage-guided campaigns run separately via tests/PalladiumWallet.Fuzz/fuzz.sh.
/// </summary>
public class FuzzCorpusTests
{
    public static TheoryData<string> Targets()
    {
        var data = new TheoryData<string>();
        foreach (var name in FuzzTargets.All.Keys)
            data.Add(name);
        return data;
    }

    [Theory]
    [MemberData(nameof(Targets))]
    public void Il_corpus_del_target_rispetta_il_contratto_del_parser(string target)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Corpus", target);
        Assert.True(Directory.Exists(dir), $"seed corpus missing for '{target}' at {dir}");

        foreach (var file in Directory.EnumerateFiles(dir))
        {
            // Any escaping exception is a contract violation: the target itself
            // swallows the exception types documented as the parser's failure mode.
            FuzzTargets.Run(target, File.ReadAllBytes(file));
        }
    }
}

using System.Reflection;
using NetArchTest.Rules;

namespace BalanceLab.Architecture.Tests;

/// <summary>
/// Reguly z planu: silnik nie zna zadnej gry, gry znaja tylko silnik, boty dzialaja
/// na kontrakcie, nie na konkretnej grze, a deterministyczny rdzen losuje wylacznie przez Pcg32.
/// Projekty sa wykrywane z katalogu src/, wiec nowa gra trafia pod reguly sama.
/// </summary>
public sealed class ArchitectureTests
{
    private static readonly string[] SourceProjects = FindSourceProjects();

    public static TheoryData<string> Games => [.. SourceProjects.Where(p => p.StartsWith("BalanceLab.Games.", StringComparison.Ordinal))];

    /// <summary>Kod, ktorego wynik musi zalezec tylko od ziarna: silnik, gry, boty (pozniej symulator).</summary>
    public static TheoryData<string> DeterministicCore =>
        [.. SourceProjects.Where(p => p is "BalanceLab.Engine" or "BalanceLab.Bots" or "BalanceLab.Simulation"
            || p.StartsWith("BalanceLab.Games.", StringComparison.Ordinal))];

    [Fact]
    public void Every_source_project_is_checked_by_these_tests()
    {
        // Nowy projekt w src/ bez referencji tutaj po cichu ominalby wszystkie reguly.
        var missing = SourceProjects.Where(p => !File.Exists(Path.Combine(AppContext.BaseDirectory, p + ".dll")));

        Assert.Empty(missing);
        Assert.Contains("BalanceLab.Games.ConnectFour", SourceProjects);
    }

    [Fact]
    public void Engine_knows_no_game_bot_or_infrastructure()
    {
        AssertNoDependency("BalanceLab.Engine", "BalanceLab.Games", "BalanceLab.Bots", "BalanceLab.Simulation",
            "BalanceLab.Persistence", "BalanceLab.Api", "Microsoft.AspNetCore", "Microsoft.EntityFrameworkCore");
    }

    [Theory]
    [MemberData(nameof(Games))]
    public void Games_know_only_the_engine(string game)
    {
        AssertNoDependency(game, "BalanceLab.Bots", "BalanceLab.Simulation", "BalanceLab.Persistence",
            "BalanceLab.Api", "Microsoft.AspNetCore", "Microsoft.EntityFrameworkCore");
    }

    [Fact]
    public void Bots_work_on_the_contract_not_on_a_concrete_game()
    {
        AssertNoDependency("BalanceLab.Bots", "BalanceLab.Games", "BalanceLab.Persistence");
    }

    [Fact]
    public void Simulator_runs_any_game_with_any_player()
    {
        // Gry i boty sklada dopiero katalog; symulator zna tylko kontrakt silnika.
        AssertNoDependency("BalanceLab.Simulation", "BalanceLab.Games", "BalanceLab.Bots", "BalanceLab.Cli", "BalanceLab.Reporting", "BalanceLab.Persistence");
    }

    [Fact]
    public void Report_shows_simulation_results_without_knowing_games_or_bots()
    {
        // Raport HTML dziala dla kazdej gry: zna tylko liczby z symulatora, nie zasady ani boty.
        AssertNoDependency("BalanceLab.Reporting", "BalanceLab.Games", "BalanceLab.Bots", "BalanceLab.Cli", "BalanceLab.Persistence");
    }

    [Fact]
    public void Journal_stores_any_game_through_the_engine_contract()
    {
        // Dziennik dostaje gre z kodekami od wolajacego; nie zna zasad zadnej gry ani botow.
        AssertNoDependency("BalanceLab.Persistence", "BalanceLab.Games", "BalanceLab.Bots", "BalanceLab.Simulation", "BalanceLab.Reporting", "BalanceLab.Cli");
    }

    [Fact]
    public void Catalog_composes_games_and_bots_without_storage_or_network()
    {
        // Katalog sklada gry i boty dla CLI i serwera; o zapisie i sieci decyduje dopiero wolajacy.
        AssertNoDependency("BalanceLab.Catalog", "BalanceLab.Persistence", "BalanceLab.Cli", "BalanceLab.Reporting",
            "BalanceLab.Api", "BalanceLab.Multiplayer", "Microsoft.AspNetCore", "Microsoft.EntityFrameworkCore");
    }

    [Theory]
    [MemberData(nameof(DeterministicCore))]
    public void Randomness_comes_only_from_the_engine_generator(string project)
    {
        // Random, zegar, Guid i HashCode (losowany przy starcie procesu) psuja determinizm:
        // partia musi dac sie odtworzyc z ziarna na kazdej maszynie.
        AssertNoDependency(project, "System.Random", "System.Security.Cryptography.RandomNumberGenerator",
            "System.DateTime", "System.DateTimeOffset", "System.Guid", "System.HashCode", "System.Environment");
    }

    [Theory]
    [MemberData(nameof(DeterministicCore))]
    public void Only_the_optional_mcts_time_budget_reads_the_clock(string project)
    {
        // Jedyny swiadomy wyjatek: MctsOptions.TimeLimit (MCTS i ISMCTS). Kto go uzywa, rezygnuje z powtarzalnosci.
        var result = Types.InAssembly(Assembly.Load(project))
            .That().DoNotHaveNameStartingWith("MctsBot").And().DoNotHaveNameStartingWith("IsmctsBot")
            .ShouldNot().HaveDependencyOn("System.Diagnostics.Stopwatch")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    private static void AssertNoDependency(string project, params string[] forbidden)
    {
        var result = Types.InAssembly(Assembly.Load(project)).ShouldNot().HaveDependencyOnAny(forbidden).GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    private static string[] FindSourceProjects()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BalanceLab.slnx")))
        {
            dir = dir.Parent;
        }

        var src = Path.Combine(dir?.FullName ?? throw new InvalidOperationException("Nie znaleziono BalanceLab.slnx."), "src");
        return [.. Directory.GetFiles(src, "*.csproj", SearchOption.AllDirectories).Select(Path.GetFileNameWithoutExtension).Order()!];
    }
}

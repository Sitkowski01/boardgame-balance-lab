using BalanceLab.Experiments;
using BalanceLab.Multiplayer;

namespace BalanceLab.Demo.Tests;

/// <summary>
/// Demo bez przerw: boty od razu i szybkie, zeby testy nie czekaly na zegar. Czas na ruch czlowieka jest dlugi:
/// partia, ktora konczy sie w testach, nie mogla isc autopilotem (ten wchodzi dopiero po tym czasie).
/// </summary>
internal static class TestOptions
{
    public static RoomOptions Rooms { get; } = DemoOptions.Rooms with { BotDelay = TimeSpan.Zero, FillBot = "random", AutopilotBot = "random", MoveTimeout = TimeSpan.FromHours(1) };

    public static ExperimentOptions Lab { get; } = DemoOptions.Lab;
}

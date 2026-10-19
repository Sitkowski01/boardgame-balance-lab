using BalanceLab.Engine;
using BalanceLab.Simulation;
using Microsoft.Extensions.Logging;

namespace BalanceLab.Multiplayer;

/// <summary>Boty przy stole i za ludzi: liczenie poza kolejka, odwolywanie i ponawianie zapisu.</summary>
internal sealed partial class Room<TState, TMove, TGame>
{
    /// <summary>Numer pozycji, dla ktorej bot juz liczy ruch (zeby nie liczyl go dwa razy).</summary>
    private int _botTurn = -1;

    /// <summary>Rosnie przy odwolaniu bota: ruch policzony dla starszego pokolenia przepada.</summary>
    private int _botGeneration;

    /// <summary>Nieudane zapisy ruchow botow z rzedu; po <see cref="RoomOptions.MaxRecordFailures"/> partia jest porzucana.</summary>
    private int _recordFailures;

    /// <summary>
    /// Bot liczy poza kolejka (MCTS trwa), na widoku swojego miejsca — nie widzi cudzych kart, tak jak w symulatorze.
    /// Gracz jest tworzony na kazdy ruch z ziarna partii i numeru ruchu: dwa liczenia nigdy nie dziela jednej instancji.
    /// </summary>
    private void ScheduleBot(int seat, TimeSpan delay)
    {
        if (_botTurn == _moves)
        {
            return;
        }

        var (number, generation) = (_botTurn = _moves, _botGeneration);
        var player = _seats[seat].Bot!.Create(Tournament.SeedFor(_seed, number, seat + 1));
        var view = _rules.Determinize(_state, seat, Match.ViewSeed(_seed, number));
        // Przerwa liczy sie od teraz, a nie od chwili, gdy watek puli zacznie prace.
        var wait = delay > TimeSpan.Zero ? Task.Delay(delay, _clock, _queue.Closing) : Task.CompletedTask;
        _ = Task.Run(async () =>
        {
            try
            {
                await wait;
                var move = player.ChooseMove(_rules, view);
                _queue.Fire(() => BotMoveAsync(seat, number, generation, move));
            }
            catch (OperationCanceledException)
            {
                // Pokoj zamkniety w trakcie przerwy.
            }
            catch (Exception ex)
            {
                // Blad bota nie moze zatrzymac partii: w kolejce zagra pierwszy legalny ruch.
                LogBotFailed(_logger, ex, Id, number + 1);
                _queue.Fire(() => FirstLegalMoveAsync(seat, number, generation));
            }
        });
    }

    /// <summary>Ruch bota trafia do pozycji, dla ktorej go liczyl; jesli czlowiek zdazyl pierwszy albo bota odwolano, przepada.</summary>
    private async Task BotMoveAsync(int seat, int number, int generation, TMove move)
    {
        if (!Current(number, generation))
        {
            return;
        }

        try
        {
            await CommitAsync(seat, move, byBot: true);
        }
        catch (RoomException ex) when (ex.InnerException is IllegalMoveException)
        {
            // Bot zagral nielegalnie (blad bota albo determinizacji): partia idzie dalej pierwszym legalnym ruchem.
            LogBotFailed(_logger, ex, Id, number + 1);
            await FirstLegalMoveAsync(seat, number, generation);
        }
        catch (RoomException)
        {
            RetryOrAbandon(seat);
        }
    }

    private async Task FirstLegalMoveAsync(int seat, int number, int generation)
    {
        if (!Current(number, generation))
        {
            return;
        }

        try
        {
            await CommitAsync(seat, _rules.LegalMoves(_state)[0], byBot: true);
        }
        catch (RoomException)
        {
            // Pierwszy legalny ruch jest legalny, wiec to blad zapisu.
            RetryOrAbandon(seat);
        }
    }

    private bool Current(int number, int generation) =>
        _status == RoomStatus.Playing && _moves == number && _botGeneration == generation;

    /// <summary>Zapis sie nie udal (blad jest w logu): ta sama pozycja za chwile, a po kilku porazkach z rzedu koniec partii.</summary>
    private void RetryOrAbandon(int seat)
    {
        if (++_recordFailures >= _options.MaxRecordFailures)
        {
            _status = RoomStatus.Abandoned;
            LogAbandoned(_logger, Id, _recordFailures);
            PublishAll();
            return;
        }

        CancelBot();
        ScheduleBot(seat, _options.RetryDelay);
    }

    private void CancelBot()
    {
        _botGeneration++;
        _botTurn = -1;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Pokoj {Room}: bot nie wybral poprawnie ruchu nr {Number}, gra pierwszy legalny")]
    private static partial void LogBotFailed(ILogger logger, Exception exception, Guid room, int number);

    [LoggerMessage(Level = LogLevel.Error, Message = "Pokoj {Room}: partia porzucona po {Failures} nieudanych zapisach z rzedu")]
    private static partial void LogAbandoned(ILogger logger, Guid room, int failures);
}

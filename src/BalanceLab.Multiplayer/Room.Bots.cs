using BalanceLab.Engine;
using BalanceLab.Simulation;
using Microsoft.Extensions.Logging;

namespace BalanceLab.Multiplayer;

/// <summary>Boty przy stole i za ludzi: liczenie poza kolejka, odwolywanie i ponawianie zapisu.</summary>
internal sealed partial class Room<TState, TMove, TGame>
{
    /// <summary>Numer pozycji, dla ktorej bot juz liczy ruch (zeby nie liczyl go dwa razy).</summary>
    private int _botTurn = -1;

    /// <summary>Biezacy plan bota; odwolanie podmienia go na nowy, a ruch policzony dla starego przepada.</summary>
    private object _botPlan = new();

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

        var (number, plan) = (_botTurn = _moves, _botPlan);
        var player = _seats[seat].Bot!.Create(Tournament.SeedFor(_seed, number, seat + 1));
        var view = _rules.Determinize(_state, seat, Match.ViewSeed(_seed, number));
        // Przerwa liczy sie od teraz, a nie od chwili, gdy watek puli zacznie prace (zerowa konczy sie od razu).
        // Token brany tu, w kolejce: po zamknieciu pokoju jego zrodlo jest juz zwolnione.
        var closing = _queue.Closing;
        var wait = Task.Delay(delay, _clock, closing);
        _ = Task.Run(async () =>
        {
            try
            {
                await wait;
                TMove move;
                await _searches.WaitAsync(closing);
                try
                {
                    move = player.ChooseMove(_rules, view);
                }
                finally
                {
                    _searches.Release();
                }

                _queue.Fire(() => BotMoveAsync(seat, number, plan, move));
            }
            catch (OperationCanceledException)
            {
                // Pokoj zamkniety w trakcie przerwy.
            }
            catch (Exception ex)
            {
                // Blad bota nie moze zatrzymac partii: w kolejce zagra pierwszy legalny ruch.
                LogBotFailed(_logger, ex, Id, number + 1);
                _queue.Fire(() => FirstLegalMoveAsync(seat, number, plan));
            }
        });
    }

    /// <summary>Ruch bota trafia do pozycji, dla ktorej go liczyl; jesli czlowiek zdazyl pierwszy albo bota odwolano, przepada.</summary>
    private async Task BotMoveAsync(int seat, int number, object plan, TMove move)
    {
        if (!Current(number, plan))
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
            await FirstLegalMoveAsync(seat, number, plan);
        }
        catch (RoomException)
        {
            await RetryOrAbandonAsync(seat);
        }
    }

    private async Task FirstLegalMoveAsync(int seat, int number, object plan)
    {
        if (!Current(number, plan))
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
            await RetryOrAbandonAsync(seat);
        }
    }

    /// <summary>
    /// Ruch bota jest aktualny, gdy pozycja i plan sie nie zmienily. Status sprawdzac nie trzeba: koniec partii
    /// zmienia pozycje, a porzucenie odwoluje plan.
    /// </summary>
    private bool Current(int number, object plan) => _moves == number && ReferenceEquals(_botPlan, plan);

    /// <summary>Zapis sie nie udal (blad jest w logu): ta sama pozycja za chwile, a po kilku porazkach z rzedu koniec partii.</summary>
    private async Task RetryOrAbandonAsync(int seat)
    {
        if (++_recordFailures >= _options.MaxRecordFailures)
        {
            StopClock();
            CancelBot();
            _status = RoomStatus.Abandoned;
            LogAbandoned(_logger, Id, _recordFailures);
            PublishAll();
            await AbandonInJournalAsync();
            return;
        }

        CancelBot();
        ScheduleBot(seat, _options.RetryDelay);
    }

    /// <summary>Proba zamkniecia partii w dzienniku; baza moze lezec (stad porzucenie), wiec blad tylko w logu.</summary>
    private async Task AbandonInJournalAsync()
    {
        try
        {
            using var timeout = RecordTimeout();
            await _recorder.AbandonAsync(_match!.Value, timeout.Token);
        }
        catch (Exception ex)
        {
            LogRecordFailed(_logger, ex, Id, _moves);
        }
    }

    private void CancelBot()
    {
        _botPlan = new object();
        _botTurn = -1;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Pokoj {Room}: bot nie wybral poprawnie ruchu nr {Number}, gra pierwszy legalny")]
    private static partial void LogBotFailed(ILogger logger, Exception exception, Guid room, int number);

    [LoggerMessage(Level = LogLevel.Error, Message = "Pokoj {Room}: partia porzucona po {Failures} nieudanych zapisach z rzedu")]
    private static partial void LogAbandoned(ILogger logger, Guid room, int failures);
}

using BalanceLab.Engine;
using Microsoft.Extensions.Logging;

namespace BalanceLab.Multiplayer;

/// <summary>Partia: start, ruchy sprawdzane przez zasady, kolejne tury i czas na ruch.</summary>
internal sealed partial class Room<TState, TMove, TGame>
{
    private DateTimeOffset? _deadline;
    private ITimer? _timer;

    /// <summary>Czas czlowieka na ten ruch minal; jego ruch juz nie przejdzie, gra bot.</summary>
    private bool _timedOut;

    public Task StartAsync(string token) => _queue.Post(async () =>
    {
        EnsureHost(token);
        Ensure(RoomStatus.Waiting, "Partia już się zaczęła.");
        var setup = new GameSetup(_seats.Length, _seed);
        // Najpierw zapis w dzienniku: gdy sie nie uda, pokoj zostaje taki, jak byl.
        try
        {
            using var timeout = RecordTimeout();
            _match = await _recorder.StartAsync(setup, [.. _seats.Select(s => s.Kind == SeatKind.Empty ? _fill.Name : s.Name!)], _variant, timeout.Token);
        }
        catch (Exception ex)
        {
            LogRecordFailed(_logger, ex, Id, 0);
            throw new RoomException("Nie udało się zapisać partii - spróbuj jeszcze raz.", ex);
        }

        foreach (var seat in _seats)
        {
            (seat.Kind, seat.Name, seat.Bot) = seat.Kind switch
            {
                SeatKind.Empty => (SeatKind.Bot, _fill.Name, _fill),
                SeatKind.Human => (SeatKind.Human, seat.Name, _autopilot),
                _ => (seat.Kind, seat.Name, seat.Bot),
            };
        }

        _state = _rules.Setup(setup);
        _status = RoomStatus.Playing;
        // Kto wyszedl przed startem, gra dalej jako rozlaczony (za niego zagra bot) — miejsca juz sie nie zwalnia.
        for (var seat = 0; seat < _seats.Length; seat++)
        {
            StopLeaving(seat);
        }

        BeginTurn();
    });

    public Task MoveAsync(string token, int number, string move) => _queue.Post(async () =>
    {
        var seat = SeatOf(token);
        Ensure(RoomStatus.Playing, "Partia się nie toczy.");
        if (number != _moves)
        {
            throw new RoomException($"Ruch do nieaktualnej pozycji: partia ma już {_moves} ruchów, a ruch zakładał {number}.");
        }

        if (seat != _rules.CurrentPlayer(_state))
        {
            throw new RoomException("Nie twoja kolej.");
        }

        if (_timedOut)
        {
            throw new RoomException("Czas na ruch minął - za ciebie gra bot.");
        }

        TMove decoded;
        try
        {
            decoded = _rules.DecodeMove(move ?? throw new FormatException("Brak ruchu."));
        }
        catch (FormatException ex)
        {
            throw new RoomException($"Nieznany ruch '{move}'.", ex);
        }

        await CommitAsync(seat, decoded, byBot: false);
    });

    /// <summary>
    /// Ruch sprawdzony przez zasady trafia najpierw do dziennika, a dopiero potem zmienia stan pokoju
    /// i idzie do graczy — nikt nie zobaczy ruchu, ktorego nie ma w zapisie.
    /// </summary>
    private async Task CommitAsync(int seat, TMove move, bool byBot)
    {
        TState next;
        try
        {
            next = _rules.Apply(_state, move);
        }
        catch (IllegalMoveException ex)
        {
            throw new RoomException($"Ruch '{_rules.EncodeMove(move)}' jest nielegalny.", ex);
        }

        Exception? failure = null;
        try
        {
            using var timeout = RecordTimeout();
            await _recorder.AppendAsync(_match!.Value, _moves, move, timeout.Token);
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        if (failure is not null && !await WrittenAnywayAsync(move))
        {
            LogRecordFailed(_logger, failure, Id, _moves + 1);
            throw new RoomException("Nie udało się zapisać ruchu - spróbuj jeszcze raz.", failure);
        }

        _state = next;
        _moves++;
        _recordFailures = 0;
        var played = new MovePlayed(Id, _moves, seat, _rules.EncodeMove(move), byBot);
        Send(n => n.MovePlayedAsync(played));
        BeginTurn();
    }

    /// <summary>Nowa pozycja: koniec partii albo ruch kolejnego gracza (bot liczy, czlowiekowi biegnie czas).</summary>
    private void BeginTurn()
    {
        StopClock();
        _timedOut = false;
        if (_rules.IsTerminal(_state))
        {
            _status = RoomStatus.Finished;
            _scores = _rules.Scores(_state);
            PublishAll();
            return;
        }

        var seat = _rules.CurrentPlayer(_state);
        if (_seats[seat].Kind == SeatKind.Bot)
        {
            ScheduleBot(seat, _options.BotDelay);
        }
        else
        {
            var number = _moves;
            _deadline = _clock.GetUtcNow() + _options.MoveTimeout;
            _timer = _clock.CreateTimer(_ => _queue.Fire(() => Timeout(number)), null, _options.MoveTimeout, System.Threading.Timeout.InfiniteTimeSpan);
            if (_seats[seat].Connections.Count == 0)
            {
                ScheduleBot(seat, _options.BotDelay);
            }
        }

        PublishAll();
    }

    private void Timeout(int number)
    {
        if (_status != RoomStatus.Playing || _moves != number)
        {
            return;
        }

        StopClock();
        _timedOut = true;
        ScheduleBot(_rules.CurrentPlayer(_state), _options.BotDelay);
        // Widoki tez: klient gracza musi przestac pokazywac ruchy, ktorych juz nie moze zagrac.
        PublishAll();
    }

    /// <summary>
    /// Blad zapisu nie znaczy, ze ruchu nie ma: polaczenie moglo pasc juz po zatwierdzeniu. Wtedy pokoj musi go
    /// przyjac — inaczej mialby o ruch mniej niz dziennik i kazdy kolejny zapis konczylby sie konfliktem.
    /// </summary>
    private async Task<bool> WrittenAnywayAsync(TMove move)
    {
        try
        {
            using var timeout = RecordTimeout();
            return await _recorder.ContainsAsync(_match!.Value, _moves, move, timeout.Token);
        }
        catch (Exception ex)
        {
            LogRecordFailed(_logger, ex, Id, _moves + 1);
            return false;
        }
    }

    private void StopClock()
    {
        _timer?.Dispose();
        (_timer, _deadline) = (null, null);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Pokoj {Room}: nie udalo sie zapisac ruchu nr {Number}")]
    private static partial void LogRecordFailed(ILogger logger, Exception exception, Guid room, int number);
}

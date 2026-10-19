using BalanceLab.Engine;
using Microsoft.Extensions.Logging;

namespace BalanceLab.Multiplayer;

/// <summary>Partia: start, ruchy sprawdzane przez zasady, kolejne tury i czas na ruch.</summary>
internal sealed partial class Room<TState, TMove, TGame>
{
    public Task StartAsync(string token) => _queue.Post(async () =>
    {
        EnsureHost(token);
        Ensure(RoomStatus.Waiting, "Partia juz sie zaczela.");
        var setup = new GameSetup(_seats.Length, _seed);
        // Najpierw zapis w dzienniku: gdy sie nie uda, pokoj zostaje taki, jak byl.
        try
        {
            _match = await _recorder.StartAsync(setup, [.. _seats.Select(s => s.Kind == SeatKind.Empty ? _fill.Name : s.Name!)], _variant);
        }
        catch (Exception ex)
        {
            LogRecordFailed(_logger, ex, Id, 0);
            throw new RoomException("Nie udalo sie zapisac partii - sprobuj jeszcze raz.", ex);
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
        BeginTurn();
        return true;
    });

    public Task MoveAsync(string token, int number, string move) => _queue.Post(async () =>
    {
        var seat = SeatOf(token);
        Ensure(RoomStatus.Playing, "Partia sie nie toczy.");
        if (number != _moves)
        {
            throw new RoomException($"Ruch do nieaktualnej pozycji: partia ma juz {_moves} ruchow, a ruch zakladal {number}.");
        }

        if (seat != _rules.CurrentPlayer(_state))
        {
            throw new RoomException("Nie twoja kolej.");
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
        return true;
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

        try
        {
            await _recorder.AppendAsync(_match!.Value, _moves, move);
        }
        catch (Exception ex)
        {
            LogRecordFailed(_logger, ex, Id, _moves + 1);
            throw new RoomException("Nie udalo sie zapisac ruchu - sprobuj jeszcze raz.", ex);
        }

        _state = next;
        _moves++;
        var played = new MovePlayed(Id, _moves, seat, _rules.EncodeMove(move), byBot);
        Send(n => n.MovePlayedAsync(played));
        BeginTurn();
    }

    /// <summary>Nowa pozycja: koniec partii albo ruch kolejnego gracza.</summary>
    private void BeginTurn()
    {
        if (_rules.IsTerminal(_state))
        {
            _status = RoomStatus.Finished;
            _scores = _rules.Scores(_state);
        }

        PublishAll();
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Pokoj {Room}: nie udalo sie zapisac ruchu nr {Number}")]
    private static partial void LogRecordFailed(ILogger logger, Exception exception, Guid room, int number);
}

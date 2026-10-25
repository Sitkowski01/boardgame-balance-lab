using System.Collections.Immutable;

namespace BalanceLab.Multiplayer;

/// <summary>Co kto widzi: stan pokoju dla wszystkich i widok gry osobno dla kazdego miejsca.</summary>
internal sealed partial class Room<TState, TMove, TGame>
{
    private void Send(Func<IRoomNotifier, Task> send) => _queue.Send(() => send(_notifier));

    /// <param name="active">Czy to ruch w pokoju; zmiana z samego uplywu czasu nie odsuwa sprzatania lobby.</param>
    private void PublishRoom(bool active = true)
    {
        _version++;
        var info = _info = BuildInfo();
        if (active)
        {
            Interlocked.Exchange(ref _lastActivity, _clock.GetUtcNow().UtcTicks);
        }

        Send(n => n.RoomChangedAsync(info));
    }

    /// <summary>Stan pokoju dla wszystkich i widok gry dla kazdego miejsca z czlowiekiem oraz dla widzow.</summary>
    private void PublishAll()
    {
        PublishRoom();
        for (var seat = 0; seat < _seats.Length; seat++)
        {
            if (_seats[seat].Kind == SeatKind.Human)
            {
                var view = ViewFor(seat);
                Send(n => n.ViewChangedAsync(Id, view.Seat, view));
            }
        }

        var spectators = ViewFor(null);
        Send(n => n.ViewChangedAsync(Id, null, spectators));
    }

    /// <summary>Legalne ruchy dostaje tylko czlowiek na ruchu, i tylko dopoki nie minal mu czas.</summary>
    private TableView ViewFor(int? seat)
    {
        var moves = _status == RoomStatus.Playing && seat == _rules.CurrentPlayer(_state) && !_timedOut
            ? _rules.LegalMoves(_state).Select(_rules.EncodeMove).ToImmutableArray()
            : [];
        return new TableView(Id, seat, _version, _moves, _rules.View(_state, seat), moves, _deadline);
    }

    private RoomInfo BuildInfo()
    {
        int? toMove = _status == RoomStatus.Playing ? _rules.CurrentPlayer(_state) : null;
        var seats = _seats.Select((s, i) => new SeatInfo(
            i,
            s.Kind,
            s.Name,
            s.Connections.Count > 0,
            Autopilot: i == toMove && s.Kind == SeatKind.Human && _botTurn == _moves));
        return new RoomInfo(Id, _version, _definition.Key, _rules.Name, _variant, _status, _host, [.. seats], _moves, toMove, _deadline, _match, _scores, _createdAt);
    }
}

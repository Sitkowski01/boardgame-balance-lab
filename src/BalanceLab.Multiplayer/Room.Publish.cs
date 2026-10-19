namespace BalanceLab.Multiplayer;

/// <summary>Co kto widzi: stan pokoju dla wszystkich i widok gry osobno dla kazdego miejsca.</summary>
internal sealed partial class Room<TState, TMove, TGame>
{
    private void Send(Func<IRoomNotifier, Task> send) => _queue.Send(() => send(_notifier));

    private void PublishRoom()
    {
        _version++;
        var info = _info = BuildInfo();
        Interlocked.Exchange(ref _lastActivity, _clock.GetUtcNow().UtcTicks);
        Send(n => n.RoomChangedAsync(info));
    }

    private RoomInfo BuildInfo()
    {
        int? toMove = _status == RoomStatus.Playing ? _rules.CurrentPlayer(_state) : null;
        var seats = _seats.Select((s, i) => new SeatInfo(
            i,
            s.Kind,
            s.Name,
            s.Connections.Count > 0,
            Autopilot: false));
        return new RoomInfo(Id, _version, _definition.Key, _rules.Name, _variant, _status, _host, [.. seats], 0, toMove, null, null, _scores, _createdAt);
    }
}

using System.Security.Cryptography;

namespace BalanceLab.Multiplayer;

/// <summary>Miejsca przy stole: dolaczanie, powrot, boty i rozlaczenia.</summary>
internal sealed partial class Room<TState, TMove, TGame>
{
    public Task<SeatTicket> JoinAsync(string name, string connection) => _queue.Post(() =>
    {
        var player = PlayerName.Validate(name, _options.MaxNameLength);
        Ensure(RoomStatus.Waiting, "Partia juz trwa - mozesz ja ogladac.");
        var seat = Array.FindIndex(_seats, s => s.Kind == SeatKind.Empty);
        if (seat < 0)
        {
            throw new RoomException("Brak wolnych miejsc.");
        }

        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        _seats[seat] = new Seat { Kind = SeatKind.Human, Name = player, Token = token };
        _seats[seat].Connections.Add(connection);
        _host = _host < 0 ? seat : _host;
        PublishRoom();
        return new SeatTicket(Id, seat, token);
    });
}

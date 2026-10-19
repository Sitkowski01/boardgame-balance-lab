using System.Security.Cryptography;
using BalanceLab.Simulation;

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

    public Task<SeatTicket> RejoinAsync(string token, string connection) => _queue.Post(() =>
    {
        var seat = SeatOf(token);
        _seats[seat].Connections.Add(connection);
        PublishRoom();

        return new SeatTicket(Id, seat, token);
    });

    public Task AddBotAsync(string token, int seat, string bot) => _queue.Post(() =>
    {
        EnsureHost(token);
        Ensure(RoomStatus.Waiting, "Boty dodaje sie przed startem partii.");
        if (seat < 0 || seat >= _seats.Length)
        {
            throw new RoomException($"Nie ma miejsca {seat}; stol ma {_seats.Length} miejsc.");
        }

        if (_seats[seat].Kind == SeatKind.Human)
        {
            throw new RoomException($"Miejsce {seat} zajmuje gracz.");
        }

        if (string.IsNullOrWhiteSpace(bot) || bot.Length > _options.MaxNameLength)
        {
            throw new RoomException($"Opis bota: od 1 do {_options.MaxNameLength} znakow (np. mcts:300).");
        }

        Contestant<TState, TMove> contestant;
        try
        {
            contestant = _definition.Bot(bot, bot);
        }
        catch (FormatException ex)
        {
            throw new RoomException(ex.Message, ex);
        }

        _seats[seat] = new Seat { Kind = SeatKind.Bot, Name = contestant.Name, Bot = contestant };
        PublishRoom();
        return true;
    });

    public Task DisconnectAsync(string connection) => _queue.Post(() =>
    {
        var left = Array.FindIndex(_seats, s => s.Connections.Contains(connection));
        if (left < 0)
        {
            return false;
        }

        _seats[left].Connections.Remove(connection);
        PublishRoom();
        return true;
    });
}

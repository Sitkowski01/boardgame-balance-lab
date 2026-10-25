using System.Security.Cryptography;
using BalanceLab.Simulation;

namespace BalanceLab.Multiplayer;

/// <summary>Miejsca przy stole: dolaczanie, powrot, boty i rozlaczenia.</summary>
internal sealed partial class Room<TState, TMove, TGame>
{
    public Task<SeatTicket> JoinAsync(string name, string connection) => _queue.Post(() =>
    {
        var player = PlayerName.Validate(name, _options.MaxNameLength);
        Ensure(RoomStatus.Waiting, "Partia już trwa - możesz ją oglądać.");
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
        StopLeaving(seat);
        // Wrocil na swoim ruchu, zanim minal mu czas: bot, ktory gral za rozlaczonego, juz nie zagra.
        if (_status == RoomStatus.Playing && seat == _rules.CurrentPlayer(_state) && _botTurn == _moves && !_timedOut)
        {
            CancelBot();
            PublishAll();
        }
        else
        {
            PublishRoom();
        }

        return new SeatTicket(Id, seat, token);
    });

    public Task<TableView?> ViewAsync(string token) => _queue.Post(() =>
    {
        var seat = SeatOf(token);
        return _status == RoomStatus.Waiting ? null : ViewFor(seat);
    });

    public Task<TableView?> SpectateAsync() => _queue.Post(() => _status == RoomStatus.Waiting ? null : ViewFor(null));

    public Task AddBotAsync(string token, int seat, string bot) => _queue.Post(() =>
    {
        EnsureHost(token);
        Ensure(RoomStatus.Waiting, "Boty dodaje się przed startem partii.");
        if (seat < 0 || seat >= _seats.Length)
        {
            throw new RoomException($"Nie ma miejsca {seat}; stół ma {_seats.Length} miejsc.");
        }

        if (_seats[seat].Kind == SeatKind.Human)
        {
            throw new RoomException($"Miejsce {seat} zajmuje gracz.");
        }

        if (string.IsNullOrWhiteSpace(bot) || bot.Length > _options.MaxNameLength)
        {
            throw new RoomException($"Opis bota: od 1 do {_options.MaxNameLength} znaków (np. mcts:300).");
        }

        Contestant<TState, TMove> contestant;
        try
        {
            // Limit iteracji: bot z lobby liczy na serwerze, wiec gracz nie moze zamowic nieskonczonego MCTS.
            contestant = _definition.Bot(bot, bot, _options.MaxBotIterations);
        }
        catch (FormatException ex)
        {
            throw new RoomException(ex.Message, ex);
        }

        _seats[seat] = new Seat { Kind = SeatKind.Bot, Name = contestant.Name, Bot = contestant };
        PublishRoom();
    });

    public Task DisconnectAsync(string connection) => _queue.Post(() =>
    {
        var left = Array.FindIndex(_seats, s => s.Connections.Contains(connection));
        if (left < 0)
        {
            return;
        }

        _seats[left].Connections.Remove(connection);
        // Rozlaczony na swoim ruchu: po chwili gra za niego bot, zeby partia nie stala. Wroci wczesniej — gra sam.
        if (_status == RoomStatus.Playing && left == _rules.CurrentPlayer(_state) && _seats[left].Connections.Count == 0)
        {
            ScheduleBot(left, _options.DisconnectGrace);
        }
        else if (_status == RoomStatus.Waiting && _seats[left].Connections.Count == 0)
        {
            ReleaseLater(left);
        }

        PublishRoom();
    });

    /// <summary>Kto wyszedl z czekajacego pokoju i nie wrocil przez <see cref="RoomOptions.WaitingSeatHold"/>, zwalnia miejsce.</summary>
    /// <remarks>
    /// Jeden odliczany czas na miejsce: powrot go odwoluje, a kolejne wyjscie zaczyna liczyc od nowa — miejsce
    /// zwalnia dopiero nieprzerwana nieobecnosc. Kontynuacja synchronicznie wrzuca zwolnienie do kolejki pokoju,
    /// wiec po uplywie czasu (takze na podstawionym zegarze) jest ono w kolejce przed nastepna prosba.
    /// </remarks>
    private void ReleaseLater(int seat)
    {
        StopLeaving(seat);
        var leaving = _seats[seat].Leaving = CancellationTokenSource.CreateLinkedTokenSource(_queue.Closing);
        _ = Task.Delay(_options.WaitingSeatHold, _clock, leaving.Token).ContinueWith(
            _ => _queue.Fire(() => Release(seat, leaving)),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnRanToCompletion | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void StopLeaving(int seat)
    {
        _seats[seat].Leaving?.Cancel();
        _seats[seat].Leaving?.Dispose();
        _seats[seat].Leaving = null;
    }

    private void Release(int seat, CancellationTokenSource leaving)
    {
        // Partia ruszyla albo czas odwolano (powrot, kolejne wyjscie) w chwili, gdy zwolnienie bylo juz w drodze.
        if (_status != RoomStatus.Waiting || !ReferenceEquals(_seats[seat].Leaving, leaving))
        {
            return;
        }

        StopLeaving(seat);
        _seats[seat] = new Seat();
        // Gospodarz wyszedl: start i boty przechodza na nastepnego czlowieka (albo na pierwszego, ktory usiadzie).
        _host = _host == seat ? Array.FindIndex(_seats, s => s.Kind == SeatKind.Human) : _host;

        PublishRoom(active: false);
    }
}

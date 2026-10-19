import { useEffect, useState } from "react";
import { hubMessage, type Connect } from "../api/connection";
import { api } from "../api/rest";
import type { GameInfo, RoomInfo } from "../api/schemas";
import type { Tickets } from "../table/tickets";
import { CreateRoom } from "./CreateRoom";
import { sortRooms, upsertRoom } from "./lobby";
import { RoomRow } from "./RoomRow";

export interface LobbyPageProps {
  connect: Connect;
  tickets: Tickets;
  navigate: (path: string) => void;
  loadGames?: () => Promise<GameInfo[]>;
}

// Stala funkcja, nie nowa przy kazdym renderze: inaczej efekt z lista gier laczylby sie od nowa bez konca.
const defaultGames = (): Promise<GameInfo[]> => api.games();

/**
 * Lobby: pokoje na zywo (WatchLobby) i zakladanie nowego. Pokoj zaklada osobne, krotkie polaczenie:
 * bilet gospodarza trafia do tej karty przegladarki, a stol siada z nim od nowa (jak po odswiezeniu).
 */
export function LobbyPage({ connect, tickets, navigate, loadGames = defaultGames }: LobbyPageProps) {
  const [rooms, setRooms] = useState<RoomInfo[]>([]);
  const [games, setGames] = useState<GameInfo[]>([]);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    // Zmiana stanu po odmontowaniu React po prostu pomija; pilnowac trzeba tylko polaczenia, ktore otworzy
    // sie juz po wyjsciu z lobby — inaczej zostaloby otwarte.
    let left = false;
    let close: (() => Promise<void>) | null = null;
    loadGames().then(setGames, (e: unknown) => {
      setError(hubMessage(e));
    });
    const failed = (e: unknown): void => {
      setError(hubMessage(e));
    };
    connect({
      lobby: (room) => {
        setRooms((list) => upsertRoom(list, room));
      },
    })
      .then(async (hub) => {
        close = () => hub.close();
        if (left) {
          await hub.close();
          return;
        }

        const list = await hub.watchLobby();
        setRooms((known) => list.reduce(upsertRoom, known));
      })
      .catch(failed);
    return () => {
      left = true;
      void close?.();
    };
  }, [connect, loadGames]);

  const create = async (game: string, seats: number, variant: string | null, name: string): Promise<void> => {
    try {
      setError(null);
      const hub = await connect({});
      try {
        const ticket = await hub.createRoom({ game, seats, variant, name });
        tickets.save(ticket);
        navigate(`/rooms/${ticket.room}`);
      } finally {
        await hub.close();
      }
    } catch (e) {
      setError(hubMessage(e));
    }
  };

  return (
    <main className="lobby">
      <h1>Balance Lab</h1>
      <p className="lead">Zagraj z ludźmi i botami w gry, których balans sprawdza laboratorium.</p>
      {error !== null && (
        <p role="alert" className="banner error">
          {error}
        </p>
      )}
      <CreateRoom games={games} onCreate={create} />
      <section aria-label="Pokoje">
        <h2>Pokoje</h2>
        {rooms.length === 0 ? (
          <p className="muted">Nie ma jeszcze żadnego pokoju — załóż pierwszy.</p>
        ) : (
          <ul className="rooms">
            {sortRooms(rooms).map((room) => (
              <RoomRow key={room.id} room={room} navigate={navigate} />
            ))}
          </ul>
        )}
      </section>
    </main>
  );
}

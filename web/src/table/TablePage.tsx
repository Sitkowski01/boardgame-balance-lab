import type { Connect } from "../api/connection";
import { describe, GameBoard } from "../games";
import { Link } from "../Link";
import { freeSeats, statusLabel } from "../lobby/lobby";
import { Clock } from "./Clock";
import { JoinForm } from "./JoinForm";
import { Result } from "./Result";
import { Seats } from "./Seats";
import { myRole } from "./table";
import type { Tickets } from "./tickets";
import { useTable, type Connection } from "./useTable";

export interface TablePageProps {
  room: string;
  connect: Connect;
  tickets: Tickets;
  navigate: (path: string) => void;
  /** Zegar (testy podstawiaja wlasny). */
  now?: () => number;
}

const linkLabel: Record<Connection, string> = {
  connecting: "Łączenie…",
  online: "",
  reconnecting: "Połączenie zerwane — łączę ponownie…",
  closed: "Brak połączenia z serwerem. Odśwież stronę.",
};

export function TablePage({ room: id, connect, tickets, navigate, now = Date.now }: TablePageProps) {
  const { table, link, error, pending, join, addBot, start, move, clearError } = useTable(id, connect, tickets);
  const { room, view, moves } = table;
  const role = myRole(table);
  const names = room?.seats.map((s) => s.name ?? `Miejsce ${String(s.seat + 1)}`) ?? [];

  return (
    <main className="table-page">
      {linkLabel[link] !== "" && <p className="banner">{linkLabel[link]}</p>}
      {error !== null && (
        <p role="alert" className="banner error">
          {error}
          <button type="button" onClick={clearError}>
            OK
          </button>
        </p>
      )}
      {room === null ? (
        <p>Wczytywanie stołu…</p>
      ) : (
        <>
          <header className="table-head">
            <h1>{room.gameName}</h1>
            <span className={`status ${room.status}`}>{statusLabel[room.status]}</span>
            {room.status === "Playing" && <Clock deadline={room.deadline} now={now} />}
            {role.seat === null && <span className="badge">oglądasz</span>}
          </header>
          <Seats room={room} host={role.host} onAddBot={addBot} />
          {room.status === "Waiting" && role.seat === null && freeSeats(room) > 0 && <JoinForm onJoin={join} />}
          {room.status === "Waiting" && role.host && (
            <button type="button" className="primary" onClick={() => void start()}>
              Zacznij partię
            </button>
          )}
          {room.status === "Waiting" && !role.host && role.seat !== null && <p>Czekamy, aż gospodarz zacznie partię.</p>}
          {role.myTurn && !pending && <p className="your-turn">Twój ruch!</p>}
          {view !== null && (
            <GameBoard
              game={room.game}
              variant={room.variant}
              view={view.view}
              legalMoves={pending ? [] : view.legalMoves}
              seat={role.seat}
              names={names}
              onMove={(m) => void move(m)}
            />
          )}
          {(room.status === "Finished" || room.status === "Abandoned") && <Result room={room} names={names} navigate={navigate} />}
          {moves.length > 0 && (
            <section aria-label="Ostatnie ruchy" className="moves">
              <h2>Ostatnie ruchy</h2>
              <ol>
                {moves.map((m) => (
                  <li key={m.number} value={m.number}>
                    {names[m.seat]}
                    {m.byBot && room.seats[m.seat]?.kind === "Human" ? " (bot za gracza)" : ""}: {describe(room.game, m.move)}
                  </li>
                ))}
              </ol>
            </section>
          )}
        </>
      )}
      <p>
        <Link to="/" navigate={navigate}>
          ← Lobby
        </Link>
      </p>
    </main>
  );
}

import type { Connect } from "../api/connection";
import { GameBoard, useDescribe } from "../games";
import { Link } from "../Link";
import { GameArt } from "../lobby/GameArt";
import { gameTitle, seatName } from "../names";
import { CopyLink } from "./CopyLink";
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
  const names = room?.seats.map((s) => seatName(s) ?? `Miejsce ${String(s.seat + 1)}`) ?? [];
  const describe = useDescribe(room?.game ?? null, room?.variant ?? null);

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
            <GameArt game={room.game} />
            <div>
              <h1>{gameTitle(room.gameName)}</h1>
              <div className="table-meta">
                <span className={`status ${room.status}`}>{statusLabel[room.status]}</span>
                {room.variant !== null && <span className="badge accent">wariant: {room.variant}</span>}
                {role.seat === null && <span className="badge">oglądasz</span>}
              </div>
            </div>
            {room.status === "Playing" && <Clock deadline={room.deadline} now={now} />}
          </header>
          {room.status === "Waiting" ? (
            <section className="waiting panel" aria-label="Poczekalnia">
              <div className="waiting-head">
                <div>
                  <h2>Poczekalnia</h2>
                  <p className="muted">
                    {role.host
                      ? "Posadź boty na wolnych miejscach albo wyślij link znajomym — partia rusza, gdy klikniesz „Zacznij partię”."
                      : "Stół czeka na graczy. Usiądź na wolnym miejscu albo oglądaj, aż gospodarz zacznie."}
                  </p>
                </div>
                <CopyLink />
              </div>
              <Seats room={room} names={names} host={role.host} onAddBot={addBot} />
              {role.seat === null && freeSeats(room) > 0 && <JoinForm onJoin={join} />}
              {role.host && (
                <button type="button" className="primary start" onClick={() => void start()}>
                  Zacznij partię
                </button>
              )}
              {!role.host && role.seat !== null && <p className="note">Czekamy, aż gospodarz zacznie partię.</p>}
            </section>
          ) : (
            <div className="table-layout">
              <div className="board-area">
                {role.myTurn && !pending && <p className="your-turn">Twój ruch!</p>}
                {(room.status === "Finished" || room.status === "Abandoned") && <Result room={room} names={names} navigate={navigate} />}
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
              </div>
              <aside className="table-side">
                <Seats room={room} names={names} host={role.host} onAddBot={addBot} />
                {moves.length > 0 && (
                  <section aria-label="Ostatnie ruchy" className="moves panel">
                    <h2>Ostatnie ruchy</h2>
                    <ol>
                      {moves.map((m) => (
                        <li key={m.number} value={m.number} className={`seat-${String(m.seat)}`}>
                          <strong>{names[m.seat]}</strong>
                          {m.byBot && room.seats[m.seat]?.kind === "Human" ? " (bot za gracza)" : ""}: {describe(m.move)}
                        </li>
                      ))}
                    </ol>
                  </section>
                )}
              </aside>
            </div>
          )}
        </>
      )}
      <p className="back">
        <Link to="/" navigate={navigate}>
          ← Lobby
        </Link>
      </p>
    </main>
  );
}

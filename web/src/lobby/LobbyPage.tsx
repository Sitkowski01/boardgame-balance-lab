import { useEffect, useState } from "react";
import { hubMessage, type Connect, type GameServer } from "../api/connection";
import { api } from "../api/rest";
import { Link } from "../Link";
import { gamesFact } from "../names";
import type { GameInfo, RoomInfo } from "../api/schemas";
import type { Tickets } from "../table/tickets";
import { CreateRoom } from "./CreateRoom";
import { isLive, RecentFinished, refreshRooms, sortRooms, upsertRoom } from "./lobby";
import { RoomRow } from "./RoomRow";

export interface LobbyPageProps {
  connect: Connect;
  tickets: Tickets;
  navigate: (path: string) => void;
  loadGames?: () => Promise<GameInfo[]>;
}

// Stala funkcja, nie nowa przy kazdym renderze: inaczej efekt z lista gier laczylby sie od nowa bez konca.
const defaultGames = (): Promise<GameInfo[]> => api.games();

/** Stan polaczenia z lista pokoi: po zerwaniu lista stoi, wiec gracz musi o tym wiedziec. */
type LobbyLink = "online" | "reconnecting" | "closed";

const linkLabel: Record<LobbyLink, string> = {
  online: "",
  reconnecting: "Połączenie zerwane — łączę ponownie, lista pokojów może być nieaktualna…",
  closed: "Brak połączenia z serwerem — lista pokojów się nie odświeża. Odśwież stronę.",
};

/**
 * Lobby: pokoje na zywo (WatchLobby) i zakladanie nowego. Pokoj zaklada osobne, krotkie polaczenie:
 * bilet gospodarza trafia do tej karty przegladarki, a stol siada z nim od nowa (jak po odswiezeniu).
 * Po zerwaniu sieci nowe polaczenie nie jest w grupie lobby, wiec lista jest pobierana i zapisywana od nowa.
 */
export function LobbyPage({ connect, tickets, navigate, loadGames = defaultGames }: LobbyPageProps) {
  const [rooms, setRooms] = useState<RoomInfo[]>([]);
  const [games, setGames] = useState<GameInfo[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [link, setLink] = useState<LobbyLink>("online");
  const [allFinished, setAllFinished] = useState(false);

  useEffect(() => {
    // Zmiana stanu po odmontowaniu React po prostu pomija; pilnowac trzeba tylko polaczenia, ktore otworzy
    // sie juz po wyjsciu z lobby — inaczej zostaloby otwarte.
    let left = false;
    let close: (() => Promise<void>) | null = null;
    let server: GameServer | null = null;
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
      invalid: setError,
      reconnecting: () => {
        setLink("reconnecting");
      },
      reconnected: () => {
        setLink("online");
        server
          ?.watchLobby()
          .then((list) => {
            setRooms((known) => refreshRooms(known, list));
          })
          .catch(failed);
      },
      closed: () => {
        setLink("closed");
      },
    })
      .then(async (hub) => {
        close = () => hub.close();
        server = hub;
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
    // Drugiego pokoju przy podwojnym kliku pilnuje formularz (CreateRoom): do odpowiedzi przycisk jest wylaczony.
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

  const sorted = sortRooms(rooms);
  const live = sorted.filter(isLive);
  const done = sorted.filter((room) => !isLive(room));

  return (
    <main className="lobby">
      <section className="hero">
        <div className="hero-text">
          <p className="eyebrow">Laboratorium balansu gier planszowych</p>
          <h1>Balance Lab</h1>
          <p className="lead">
            Zagraj z ludźmi i botami w gry, których balans sprawdza{" "}
            <Link to="/lab" navigate={navigate}>
              laboratorium
            </Link>
            . Boty MCTS rozgrywają tysiące partii i pokazują, która karta, kafelek czy miejsce przy stole daje za dużo.
          </p>
          <ul className="hero-facts" aria-label="W skrócie">
            {games.length > 0 && <li>{gamesFact(games)}</li>}
            <li>boty od losowego po ISMCTS</li>
            <li>powtórka każdej partii</li>
          </ul>
        </div>
        <HeroArt />
      </section>
      {linkLabel[link] !== "" && (
        <p role="status" className="banner">
          {linkLabel[link]}
        </p>
      )}
      {error !== null && (
        <p role="alert" className="banner error">
          {error}
        </p>
      )}
      <h2>Nowy stół</h2>
      <CreateRoom games={games} onCreate={create} />
      <section aria-label="Pokoje" className="rooms-section">
        <h2>Otwarte stoły</h2>
        {live.length === 0 ? (
          <p className="empty-rooms">
            {rooms.length === 0
              ? "Nie ma jeszcze żadnego pokoju — załóż pierwszy."
              : "Żaden stół nie czeka teraz na graczy — załóż własny."}
          </p>
        ) : (
          <ul className="rooms">
            {live.map((room) => (
              <RoomRow key={room.id} room={room} navigate={navigate} />
            ))}
          </ul>
        )}
        {done.length > 0 && (
          <>
            <h2>Ostatnie partie</h2>
            <ul className="rooms finished">
              {(allFinished ? done : done.slice(0, RecentFinished)).map((room) => (
                <RoomRow key={room.id} room={room} navigate={navigate} />
              ))}
            </ul>
            {!allFinished && done.length > RecentFinished && (
              <button
                type="button"
                className="more"
                onClick={() => {
                  setAllFinished(true);
                }}
              >
                Pokaż wszystkie ({done.length})
              </button>
            )}
          </>
        )}
      </section>
      <section className="lab-teaser panel" aria-label="Laboratorium">
        <div>
          <h2>Co jeśli karta kosztuje o jeden mniej?</h2>
          <p className="muted">
            W laboratorium zamawiasz turniej, ligę albo porównanie wariantów zasad. Wynik to ranking Elo, macierz „kto bije kogo”, przewaga
            pierwszego gracza i karty, które wygrywają za często — każda liczba z 95% przedziałem.
          </p>
        </div>
        <Link to="/lab" navigate={navigate}>
          Otwórz laboratorium →
        </Link>
      </section>
    </main>
  );
}

/** Ruch kursora nad ilustracja jako -0,5..0,5 w zmiennych CSS; warstwy przesuwaja sie o rozne odleglosci (paralaksa). */
function tilt(e: React.PointerEvent<SVGSVGElement>): void {
  const box = e.currentTarget.getBoundingClientRect();
  e.currentTarget.style.setProperty("--px", ((e.clientX - box.left) / box.width - 0.5).toFixed(3));
  e.currentTarget.style.setProperty("--py", ((e.clientY - box.top) / box.height - 0.5).toFixed(3));
}

function untilt(e: React.PointerEvent<SVGSVGElement>): void {
  e.currentTarget.style.removeProperty("--px");
  e.currentTarget.style.removeProperty("--py");
}

/** Ozdoba wstepu: elementy trzech gier na zielonym suknie stolu; karta i zetony lekko unosza sie nad suknem. */
function HeroArt() {
  return (
    <svg className="hero-art" viewBox="0 0 320 220" aria-hidden="true" onPointerMove={tilt} onPointerLeave={untilt}>
      <rect x="6" y="10" width="308" height="200" rx="28" fill="var(--felt)" />
      <rect x="6" y="10" width="308" height="200" rx="28" fill="url(#hero-sheen)" />
      <defs>
        <radialGradient id="hero-sheen" cx="30%" cy="20%" r="90%">
          <stop offset="0" stopColor="#ffffff" stopOpacity="0.16" />
          <stop offset="1" stopColor="#000000" stopOpacity="0.18" />
        </radialGradient>
      </defs>
      <g className="layer near">
        <g className="float">
          <g transform="rotate(-9 92 104)">
            <rect x="52" y="48" width="80" height="112" rx="10" fill="#fffaf0" stroke="#d1a43c" strokeWidth="3" />
            <text x="64" y="76" fontSize="22" fontWeight="700" fill="#2b2b2b" fontFamily="Inter Variable, sans-serif">
              4
            </text>
            <path d="M92 84l16 12-16 28-16-28z" fill="#2f6fd1" />
            <path d="M92 84l16 12h-32z" fill="#78a6ee" />
            <circle cx="68" cy="144" r="7" fill="#2e9a4f" />
            <circle cx="86" cy="144" r="7" fill="#c9353b" />
            <circle cx="104" cy="144" r="7" fill="#2b2b2b" />
          </g>
        </g>
      </g>
      <g className="layer far">
        {[
          [212, 52, "#1f9e94"],
          [236, 52, "#d4952a"],
          [260, 52, "#b3243b"],
          [212, 76, "#4a4c50"],
          [236, 76, "#1f9e94"],
          [260, 76, "#d4952a"],
          [212, 100, "#efe6cf"],
          [236, 100, "#4a4c50"],
          [260, 100, "#1f9e94"],
        ].map(([x, y, fill], i) => (
          <rect key={i} x={x} y={y} width="21" height="21" rx="4" fill={String(fill)} opacity={i % 4 === 3 ? 0.35 : 1} />
        ))}
      </g>
      <g className="layer mid">
        <g className="float late">
          <circle cx="176" cy="168" r="17" fill="#d33c3c" stroke="rgb(0 0 0 / 0.25)" />
          <circle cx="176" cy="168" r="11" fill="none" stroke="#ffd0d0" strokeWidth="2" strokeDasharray="4 3" />
          <circle cx="214" cy="160" r="17" fill="#e8c23a" stroke="rgb(0 0 0 / 0.25)" />
          <circle cx="214" cy="160" r="11" fill="none" stroke="#fff2c0" strokeWidth="2" strokeDasharray="4 3" />
          <circle cx="252" cy="170" r="17" fill="#f5f3ee" stroke="rgb(0 0 0 / 0.25)" />
          <circle cx="252" cy="170" r="11" fill="none" stroke="#b9b4a8" strokeWidth="2" strokeDasharray="4 3" />
        </g>
      </g>
    </svg>
  );
}

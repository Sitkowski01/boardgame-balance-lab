import { useEffect, useMemo, useState } from "react";
import { api } from "../api/rest";
import type { MatchReplay, ReplayFrame } from "../api/schemas";
import { GameBoard, useDescribe } from "../games";
import { Link } from "../Link";
import { GameArt } from "../lobby/GameArt";
import { gameTitle, playerName } from "../names";
import { winners } from "../table/table";
import { Controls } from "./Controls";
import { clampPosition, frameCache } from "./frames";

export interface ReplayPageProps {
  match: string;
  navigate: (path: string) => void;
  loadMatch?: (id: string) => Promise<MatchReplay>;
  loadFrame?: (id: string, moves: number) => Promise<ReplayFrame>;
  /** Co ile milisekund odtwarzanie przechodzi do nastepnego ruchu. */
  pace?: number;
  /** Ile milisekund suwak musi stac, zanim pojdzie pytanie o klatke (przeciaganie nie zasypuje serwera). */
  settle?: number;
}

const defaultMatch = (id: string): Promise<MatchReplay> => api.match(id);
const defaultFrame = (id: string, moves: number): Promise<ReplayFrame> => api.frame(id, moves);
const message = (e: unknown): string => (e instanceof Error ? e.message : String(e));

/** Powtorka z dziennika: suwak po ruchach, plansza w kazdej pozycji liczona przez serwer (widok widza). */
export function ReplayPage({
  match: id,
  navigate,
  loadMatch = defaultMatch,
  loadFrame = defaultFrame,
  pace = 800,
  settle = 150,
}: ReplayPageProps) {
  const [match, setMatch] = useState<MatchReplay | null>(null);
  const [frame, setFrame] = useState<ReplayFrame | null>(null);
  const [position, setPosition] = useState(0);
  const [playing, setPlaying] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [frameError, setFrameError] = useState<string | null>(null);
  const frames = useMemo(() => frameCache((moves) => loadFrame(id, moves)), [id, loadFrame]);
  const last = match?.moves.length ?? 0;
  const describe = useDescribe(match?.game ?? null, match?.variant ?? null);

  // Strona jest kluczowana numerem partii (App), wiec odpowiedz zawsze dotyczy tej partii.
  useEffect(() => {
    loadMatch(id).then(setMatch, (e: unknown) => {
      setError(message(e));
    });
  }, [id, loadMatch]);

  useEffect(() => {
    if (match === null) {
      return undefined;
    }

    // Odpowiedz dla pozycji, z ktorej suwak juz zjechal, nie moze nadpisac nowszej klatki.
    let current = true;
    const timer = setTimeout(() => {
      frames.get(position).then(
        (loaded) => {
          if (current) {
            setFrame(loaded);
            setFrameError(null);
            frames.prefetch(position, last);
          }
        },
        (e: unknown) => {
          if (current) setFrameError(message(e));
        },
      );
    }, settle);
    return () => {
      current = false;
      clearTimeout(timer);
    };
  }, [frames, match, position, last, settle]);

  // Na ostatnim ruchu przycisk od razu pokazuje "Odtwarzaj", zanim stan playing zdazy sie zmienic.
  const running = playing && position < last;
  useEffect(() => {
    if (!running) {
      return undefined;
    }

    const timer = setTimeout(() => {
      // Ostatni krok konczy odtwarzanie: cofniecie suwaka potem nie rusza go samo od nowa.
      if (position + 1 >= last) setPlaying(false);
      setPosition(clampPosition(position + 1, last));
    }, pace);
    return () => {
      clearTimeout(timer);
    };
  }, [running, last, pace, position]);

  const go = (to: number): void => {
    setPosition(clampPosition(to, last));
  };

  if (error !== null) {
    return (
      <main>
        <p role="alert">{error}</p>
        <p>
          <Link to="/" navigate={navigate}>
            ← Lobby
          </Link>
        </p>
      </main>
    );
  }

  if (match === null) {
    return (
      <main>
        <p className="muted">Wczytywanie partii…</p>
      </main>
    );
  }

  const best = winners(match.scores);
  const players = match.players.map(playerName);
  return (
    <main className="replay">
      <header className="table-head">
        <GameArt game={match.game} />
        <div>
          <h1>Powtórka: {gameTitle(match.gameName)}</h1>
          <div className="table-meta">
            <span className="muted">{players.join(" vs ")}</span>
            {match.status === "InProgress" && <span className="badge">partia trwa</span>}
            {match.verified && <span className="badge accent">sprawdzona z dziennika</span>}
          </div>
        </div>
      </header>
      {!match.verified && match.problem !== null && <p className="banner error">{match.problem}</p>}
      {frameError !== null && (
        <p role="alert" className="banner error">
          Nie udało się pobrać pozycji: {frameError}
        </p>
      )}
      <div className="table-layout">
        <div className="board-area">
          <div className="panel">
            <Controls position={position} last={last} running={running} onGo={go} onPlay={setPlaying} />
          </div>
          {position === last && best.length > 0 && <p className="result-winner">Wygrywa: {best.map((s) => players[s]).join(", ")}</p>}
          {frame === null ? (
            <p className="muted">Wczytywanie pozycji…</p>
          ) : (
            <GameBoard
              game={match.game}
              variant={match.variant}
              view={frame.view}
              legalMoves={[]}
              seat={null}
              names={players}
              onMove={() => undefined}
            />
          )}
        </div>
        <aside className="table-side">
          <section className="moves panel" aria-label="Lista ruchów">
            <h2>Ruchy</h2>
            <ol aria-label="Ruchy partii">
              {match.moves.map((move, i) => (
                <li key={i} className={i + 1 === position ? "current" : ""}>
                  <button
                    type="button"
                    onClick={() => {
                      go(i + 1);
                    }}
                  >
                    {describe(move)}
                  </button>
                </li>
              ))}
            </ol>
          </section>
        </aside>
      </div>
      <p className="back">
        <Link to="/" navigate={navigate}>
          ← Lobby
        </Link>
      </p>
    </main>
  );
}

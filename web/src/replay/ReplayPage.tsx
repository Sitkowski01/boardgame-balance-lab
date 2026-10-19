import { useEffect, useMemo, useState } from "react";
import { api } from "../api/rest";
import type { MatchReplay, ReplayFrame } from "../api/schemas";
import { describe, GameBoard } from "../games";
import { Link } from "../Link";
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
    return <p>Wczytywanie partii…</p>;
  }

  const best = winners(match.scores);
  return (
    <main className="replay">
      <header className="table-head">
        <h1>Powtórka: {match.gameName}</h1>
        <span className="muted">{match.players.join(" vs ")}</span>
        {match.status === "InProgress" && <span className="badge">partia trwa</span>}
      </header>
      {!match.verified && match.problem !== null && <p className="banner error">{match.problem}</p>}
      {frameError !== null && (
        <p role="alert" className="banner error">
          Nie udało się pobrać pozycji: {frameError}
        </p>
      )}
      <Controls position={position} last={last} running={running} onGo={go} onPlay={setPlaying} />
      {frame !== null && (
        <GameBoard
          game={match.game}
          variant={match.variant}
          view={frame.view}
          legalMoves={[]}
          seat={null}
          names={match.players}
          onMove={() => undefined}
        />
      )}
      {position === last && best.length > 0 && <p>Wygrywa: {best.map((s) => match.players[s]).join(", ")}</p>}
      <ol className="moves" aria-label="Ruchy partii">
        {match.moves.map((move, i) => (
          <li key={i} className={i + 1 === position ? "current" : ""}>
            <button
              type="button"
              onClick={() => {
                go(i + 1);
              }}
            >
              {describe(match.game, move)}
            </button>
          </li>
        ))}
      </ol>
      <p>
        <Link to="/" navigate={navigate}>
          ← Lobby
        </Link>
      </p>
    </main>
  );
}

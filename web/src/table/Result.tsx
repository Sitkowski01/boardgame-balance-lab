import { useState } from "react";
import { hubMessage } from "../api/connection";
import type { RoomInfo } from "../api/schemas";
import { Link } from "../Link";
import { Confetti } from "./Confetti";
import { winners } from "./table";

export interface ResultProps {
  room: RoomInfo;
  names: readonly string[];
  navigate: (path: string) => void;
  /** Partia skonczyla sie na oczach gracza (a nie byla skonczona przy wejsciu): wtedy krotkie konfetti. */
  celebrate?: boolean | undefined;
  /** Rewanz od razu przy stole (tylko dla gracza, nie widza): nowy stol z ta sama gra i botami. */
  onRematch?: (() => Promise<void>) | undefined;
}

/** Koniec partii (albo jej przerwanie): zwyciezcy, wyniki wszystkich, rewanz i link do powtorki z dziennika. */
export function Result({ room, names, navigate, celebrate = false, onRematch }: ResultProps) {
  const [busy, setBusy] = useState(false);
  const [problem, setProblem] = useState<string | null>(null);
  const best = winners(room.scores);
  const finished = room.status === "Finished";
  return (
    <section className={`result panel${finished ? "" : " abandoned"}`} aria-label="Wynik">
      {celebrate && finished && <Confetti />}
      <span className="result-mark" aria-hidden="true">
        {finished ? "🏆" : "⏸"}
      </span>
      <div className="result-body">
        <h2>{finished ? "Koniec partii" : "Partia przerwana"}</h2>
        {best.length > 0 && <p className="result-winner">Wygrywa: {best.map((s) => names[s]).join(", ")}</p>}
        {room.scores.length > 0 && (
          <ol className="result-scores" aria-label="Wyniki">
            {room.scores.map((score, seat) => (
              <li key={seat} className={best.includes(seat) ? "best" : ""}>
                {names[seat]}: {score}
              </li>
            ))}
          </ol>
        )}
      </div>
      <div className="result-actions">
        {onRematch !== undefined && (
          <button
            type="button"
            className="primary"
            disabled={busy}
            onClick={() => {
              // Blokada do odpowiedzi serwera: podwojny klik zalozylby dwa stoly.
              setBusy(true);
              setProblem(null);
              onRematch().catch((error: unknown) => {
                setProblem(hubMessage(error));
                setBusy(false);
              });
            }}
          >
            {busy ? "Zakładam stół…" : "Zagraj jeszcze raz"}
          </button>
        )}
        {room.matchId !== null && (
          <Link to={`/matches/${room.matchId}`} navigate={navigate}>
            Obejrzyj powtórkę
          </Link>
        )}
      </div>
      {problem !== null && (
        <p className="result-problem" role="alert">
          {problem}
        </p>
      )}
    </section>
  );
}

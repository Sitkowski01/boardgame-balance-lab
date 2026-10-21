import type { RoomInfo } from "../api/schemas";
import { Link } from "../Link";
import { winners } from "./table";

/** Koniec partii (albo jej przerwanie): zwyciezcy, wyniki wszystkich i link do powtorki z dziennika. */
export function Result({ room, names, navigate }: { room: RoomInfo; names: readonly string[]; navigate: (path: string) => void }) {
  const best = winners(room.scores);
  const finished = room.status === "Finished";
  return (
    <section className={`result panel${finished ? "" : " abandoned"}`} aria-label="Wynik">
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
      {room.matchId !== null && (
        <Link to={`/matches/${room.matchId}`} navigate={navigate}>
          Obejrzyj powtórkę
        </Link>
      )}
    </section>
  );
}

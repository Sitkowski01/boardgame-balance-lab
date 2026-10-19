import type { RoomInfo } from "../api/schemas";
import { Link } from "../Link";
import { winners } from "./table";

/** Koniec partii (albo jej przerwanie): zwyciezcy i link do powtorki z dziennika. */
export function Result({ room, names, navigate }: { room: RoomInfo; names: readonly string[]; navigate: (path: string) => void }) {
  const best = winners(room.scores);
  return (
    <section className="result" aria-label="Wynik">
      <h2>{room.status === "Finished" ? "Koniec partii" : "Partia przerwana"}</h2>
      {best.length > 0 && <p>Wygrywa: {best.map((s) => names[s]).join(", ")}</p>}
      {room.matchId !== null && (
        <Link to={`/matches/${room.matchId}`} navigate={navigate}>
          Obejrzyj powtórkę
        </Link>
      )}
    </section>
  );
}

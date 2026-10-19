import type { RoomInfo } from "../api/schemas";
import { Link } from "../Link";
import { freeSeats, statusLabel } from "./lobby";

/** Pokoj na liscie: kto gra i jak wejsc — usiasc, ogladac albo obejrzec powtorke. */
export function RoomRow({ room, navigate }: { room: RoomInfo; navigate: (path: string) => void }) {
  const players = room.seats.filter((s) => s.kind !== "Empty").map((s) => s.name);
  const table = `/rooms/${room.id}`;
  return (
    <li aria-label={`Pokój ${room.gameName}`}>
      <strong>{room.gameName}</strong>
      {room.variant !== null && <span className="muted"> ({room.variant})</span>}
      <span className={`status ${room.status}`}>{statusLabel[room.status]}</span>
      <span className="muted">
        {players.join(", ")} · {room.seats.length - freeSeats(room)}/{room.seats.length}
      </span>
      {room.status === "Waiting" && freeSeats(room) > 0 ? (
        <Link to={table} navigate={navigate}>
          Dołącz
        </Link>
      ) : (
        room.status === "Playing" && (
          <Link to={table} navigate={navigate}>
            Oglądaj
          </Link>
        )
      )}
      {(room.status === "Finished" || room.status === "Abandoned") && room.matchId !== null && (
        <Link to={`/matches/${room.matchId}`} navigate={navigate}>
          Powtórka
        </Link>
      )}
    </li>
  );
}

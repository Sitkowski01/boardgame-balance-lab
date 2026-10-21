import type { RoomInfo } from "../api/schemas";
import { Link } from "../Link";
import { gameTitle, seatName } from "../names";
import { GameArt } from "./GameArt";
import { freeSeats, statusLabel } from "./lobby";

/** Pokoj na liscie: kto gra i jak wejsc — usiasc, ogladac albo obejrzec powtorke. */
export function RoomRow({ room, navigate }: { room: RoomInfo; navigate: (path: string) => void }) {
  const players = room.seats.filter((s) => s.kind !== "Empty").map(seatName);
  const table = `/rooms/${room.id}`;
  const title = gameTitle(room.gameName);
  return (
    <li aria-label={`Pokój ${title}`} className={`room ${room.status}`}>
      <GameArt game={room.game} />
      <div className="room-body">
        <div className="room-title">
          <strong>{title}</strong>
          <span className={`status ${room.status}`}>{statusLabel[room.status]}</span>
        </div>
        {room.variant !== null && <span className="room-variant">wariant: {room.variant}</span>}
        <span className="muted room-players">
          {players.join(", ")} · {room.seats.length - freeSeats(room)}/{room.seats.length}
        </span>
      </div>
      <div className="room-action">
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
      </div>
    </li>
  );
}

import { useState } from "react";
import type { RoomInfo, SeatInfo } from "../api/schemas";
import { botsFor } from "../games";

/** Miejsca przy stole; gospodarz przed startem sadza boty na wolnych. */
export function Seats({ room, host, onAddBot }: { room: RoomInfo; host: boolean; onAddBot: (seat: number, bot: string) => Promise<void> }) {
  return (
    <ol className="seats" aria-label="Miejsca">
      {room.seats.map((seat) => (
        <li key={seat.seat} className={room.toMove === seat.seat ? "to-move" : ""}>
          <SeatLabel seat={seat} host={room.host === seat.seat} />
          {host && room.status === "Waiting" && seat.kind === "Empty" && <BotPicker game={room.game} seat={seat.seat} onAdd={onAddBot} />}
        </li>
      ))}
    </ol>
  );
}

function SeatLabel({ seat, host }: { seat: SeatInfo; host: boolean }) {
  if (seat.kind === "Empty") {
    return <span className="muted">wolne miejsce</span>;
  }

  return (
    <span>
      <strong>{seat.name}</strong>
      {seat.kind === "Bot" && <span className="badge">bot</span>}
      {host && <span className="badge">gospodarz</span>}
      {seat.kind === "Human" && !seat.connected && <span className="badge warn">rozłączony</span>}
      {seat.autopilot && <span className="badge warn">gra za niego bot</span>}
    </span>
  );
}

function BotPicker({ game, seat, onAdd }: { game: string; seat: number; onAdd: (seat: number, bot: string) => Promise<void> }) {
  const bots = botsFor(game);
  const [spec, setSpec] = useState(bots[0]?.spec ?? "random");
  return (
    <span className="bot-picker">
      <select
        aria-label={`Bot na miejsce ${String(seat + 1)}`}
        value={spec}
        onChange={(e) => {
          setSpec(e.target.value);
        }}
      >
        {bots.map((b) => (
          <option key={b.spec} value={b.spec}>
            {b.label}
          </option>
        ))}
      </select>
      <button type="button" onClick={() => void onAdd(seat, spec)}>
        Dodaj bota
      </button>
    </span>
  );
}

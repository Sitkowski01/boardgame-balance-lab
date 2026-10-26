import { useState } from "react";
import type { RoomInfo, SeatInfo } from "../api/schemas";
import { botsFor } from "../games";
import { botStyle } from "../names";
import { SeatStatus } from "../SeatStatus";

export interface SeatsProps {
  room: RoomInfo;
  /** Nazwy miejsc do pokazania (boty po polsku). */
  names: readonly string[];
  host: boolean;
  onAddBot: (seat: number, bot: string) => Promise<void>;
}

/** Miejsca przy stole; gospodarz przed startem sadza boty na wolnych. */
export function Seats({ room, names, host, onAddBot }: SeatsProps) {
  return (
    <ol className="seats" aria-label="Miejsca">
      {room.seats.map((seat) => (
        <li
          key={seat.seat}
          className={`seat seat-${String(seat.seat)}${room.toMove === seat.seat ? " to-move" : ""}${seat.kind === "Empty" ? " empty" : ""}`}
        >
          <Avatar seat={seat} name={names[seat.seat] ?? ""} />
          <SeatLabel seat={seat} name={names[seat.seat] ?? ""} host={room.host === seat.seat} toMove={room.toMove === seat.seat} />
          {host && room.status === "Waiting" && seat.kind === "Empty" && <BotPicker game={room.game} seat={seat.seat} onAdd={onAddBot} />}
        </li>
      ))}
    </ol>
  );
}

function Avatar({ seat, name }: { seat: SeatInfo; name: string }) {
  return (
    <span className="avatar" aria-hidden="true">
      {seat.kind === "Empty" ? "+" : seat.kind === "Bot" ? "⚙" : (Array.from(name.trim())[0] ?? "?").toUpperCase()}
    </span>
  );
}

function SeatLabel({ seat, name, host, toMove }: { seat: SeatInfo; name: string; host: boolean; toMove: boolean }) {
  if (seat.kind === "Empty") {
    return <span className="muted seat-name">wolne miejsce</span>;
  }

  return (
    <span className="seat-name">
      <strong>{name}</strong>
      {/* Kolor ramki mowi to samo, ale nie kazdy go widzi (np. Czworki nie maja innego znaku kolejki). */}
      {toMove && <span className="badge accent">na ruchu</span>}
      {host && <span className="badge">gospodarz</span>}
      <SeatStatus seat={seat} />
      {seat.kind === "Bot" && seat.name !== null && botStyle(seat.name) !== null && (
        <span className="seat-note">{botStyle(seat.name)}</span>
      )}
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

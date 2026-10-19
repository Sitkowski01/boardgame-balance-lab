import { tileName } from "./model";

export interface TileProps {
  tile: number;
  count?: number;
  /** Puste pole sciany: kolor wzoru, przygaszony. */
  faint?: boolean;
}

/** Kafelek; z liczba, gdy jest ich kilka, a jako puste pole sciany przygaszony. */
export function Tile({ tile, count, faint = false }: TileProps) {
  const name = tileName(tile);
  return (
    <span
      className={`tile tile-${String(tile)}${faint ? " faint" : ""}`}
      role="img"
      aria-label={count === undefined ? name : `${name}: ${String(count)}`}
    >
      {count !== undefined && count > 1 ? count : ""}
    </span>
  );
}

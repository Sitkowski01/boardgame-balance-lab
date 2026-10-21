import { tileName } from "./model";

export interface TileProps {
  tile: number;
  count?: number;
  /** Puste pole sciany: kolor wzoru, przygaszony. */
  faint?: boolean;
  /** Kafelek w grupie, ktora ma juz wlasna nazwe (np. "ochra: 2" na podlodze) — bez nazwy dla czytnikow. */
  decorative?: boolean;
}

/** Kafelek; z liczba, gdy jest ich kilka, a jako puste pole sciany przygaszony. */
export function Tile({ tile, count, faint = false, decorative = false }: TileProps) {
  const name = tileName(tile);
  const className = `tile tile-${String(tile)}${faint ? " faint" : ""}`;
  if (decorative) {
    return <span className={className} aria-hidden="true" />;
  }

  return (
    <span className={className} role="img" aria-label={count === undefined ? name : `${name}: ${String(count)}`}>
      {count !== undefined && count > 1 && <span className="tile-count">{count}</span>}
    </span>
  );
}

import { floorCount, floorPenalty, tileName, WallSize, type MozaikaPlayerView, type MozaikaSheet } from "./model";
import type { Line } from "./moves";
import { Tile } from "./Tile";

export interface PlayerBoardProps {
  player: MozaikaPlayerView;
  name: string;
  me: boolean;
  toMove: boolean;
  sheet: MozaikaSheet;
  /** Ruchy dla wybranych kafelkow po rzedzie docelowym; pusta mapa, gdy nic nie wybrano albo to cudza plansza. */
  placements: ReadonlyMap<Line, string>;
  onMove: (move: string) => void;
}

/** Plansza gracza: rzedy wzoru (od 1 do 5 pol), sciana z wzorem kolorow i podloga z karami. */
export function PlayerBoard({ player, name, me, toMove, sheet, placements, onMove }: PlayerBoardProps) {
  const wall = new Set(player.wall);
  const place = (line: Line, label: string, name: string) => {
    const move = placements.get(line);
    return move === undefined ? null : (
      <button
        type="button"
        className="place"
        aria-label={name}
        onClick={() => {
          onMove(move);
        }}
      >
        {label}
      </button>
    );
  };

  return (
    <article className={`mozaika-player${me ? " me" : ""}${toMove ? " to-move" : ""}`} aria-label={`Gracz ${name}`}>
      <header>
        <strong>{name}</strong>
        {me && <span className="badge">ty</span>}
        {toMove && <span className="badge">na ruchu</span>}
        <span className="points" aria-label="Punkty">
          {player.score} pkt
        </span>
      </header>
      <div className="mozaika-boards">
        <div className="lines" role="list" aria-label="Rzędy wzoru">
          {player.lines.map((line, row) => (
            <div key={row} className="line" role="listitem" aria-label={lineLabel(row, line.color, line.count)}>
              {place(row, "Tutaj", `Połóż na rząd ${String(row + 1)}`)}
              {Array.from({ length: row + 1 }, (_, slot) =>
                line.color !== null && slot < line.count ? <Tile key={slot} tile={line.color} /> : <span key={slot} className="slot" />,
              )}
            </div>
          ))}
        </div>
        <div className="wall" role="grid" aria-label="Ściana">
          {sheet.wall.map((colors, row) => (
            <div key={row} role="row" className="wall-row">
              {colors.map((tile, column) => (
                <span key={column} role="gridcell" aria-selected={wall.has(row * WallSize + column)}>
                  <Tile tile={tile} faint={!wall.has(row * WallSize + column)} />
                </span>
              ))}
            </div>
          ))}
        </div>
      </div>
      <div className="floor" aria-label="Podłoga">
        {place("floor", "Na podłogę", "Połóż na podłogę")}
        {player.floor.map((n, tile) => (n > 0 ? <Tile key={tile} tile={tile} count={n} /> : null))}
        {player.marker && (
          <span className="marker" role="img" aria-label="Znacznik pierwszego gracza">
            1
          </span>
        )}
        {floorCount(player) > 0 && <span className="muted">−{floorPenalty(player, sheet)} pkt</span>}
      </div>
    </article>
  );
}

function lineLabel(row: number, color: number | null, count: number): string {
  const contents = color === null ? "pusty" : `${tileName(color)} ${String(count)}/${String(row + 1)}`;
  return `Rząd ${String(row + 1)}: ${contents}`;
}

import { useState } from "react";
import { tileName, total, type MozaikaSheet, type MozaikaView } from "./model";
import { canTake, placements, type Source } from "./moves";
import { PlayerBoard } from "./PlayerBoard";
import { Tile } from "./Tile";
import "./mozaika.css";

export interface MozaikaBoardProps {
  view: MozaikaView;
  sheet: MozaikaSheet;
  /** Legalne ruchy odbiorcy (puste, gdy nie jego kolej albo w powtorce). */
  legalMoves: readonly string[];
  /** Miejsce odbiorcy; null = widz. */
  seat: number | null;
  names: readonly string[];
  onMove: (move: string) => void;
}

interface Picked {
  source: Source;
  color: number;
}

/**
 * Stol Mozaiki. Ruch to dwa klikniecia: kolor w warsztacie albo na srodku, potem rzad na wlasnej planszy
 * (albo podloga). Kafelki da sie wybrac i polozyc tylko tam, gdzie prowadzi to do legalnego ruchu serwera.
 */
export function MozaikaBoard({ view, sheet, legalMoves, seat, names, onMove }: MozaikaBoardProps) {
  // Wybor nalezy do jednej listy legalnych ruchow: nowy widok (np. bot zagral za gracza po czasie) zaczyna od nowa.
  const [state, setState] = useState<{ moves: readonly string[]; picked: Picked | null }>({ moves: legalMoves, picked: null });
  const picked = state.moves === legalMoves ? state.picked : null;
  const pick = (next: Picked | null): void => {
    setState({ moves: legalMoves, picked: next });
  };
  const targets = picked === null ? new Map() : placements(picked.source, picked.color, legalMoves);
  const play = (move: string): void => {
    pick(null);
    onMove(move);
  };

  const sourceTiles = (source: Source, tiles: readonly number[], label: string) => (
    <div className={`source${source === "center" ? " center" : ""}`} role="group" aria-label={label}>
      {tiles.map((count, color) => {
        if (count === 0) return null;
        const selected = picked?.source === source && picked.color === color;
        return (
          <button
            key={color}
            type="button"
            className={selected ? "picked" : ""}
            aria-pressed={selected}
            aria-label={`${label}: ${tileName(color)} (${String(count)})`}
            disabled={!canTake(source, color, legalMoves)}
            onClick={() => {
              pick(selected ? null : { source, color });
            }}
          >
            {source === "center" ? (
              <Tile tile={color} count={count} />
            ) : (
              Array.from({ length: count }, (_, i) => <Tile key={i} tile={color} decorative />)
            )}
          </button>
        );
      })}
      {source === "center" && view.centerMarker && (
        <span className="marker" role="img" aria-label="Znacznik pierwszego gracza">
          1
        </span>
      )}
    </div>
  );

  return (
    <div className="mozaika">
      <section aria-label="Warsztaty" className="table">
        {view.factories.map((tiles, f) => (
          <div key={f} className="factory">
            {sourceTiles(f, tiles, `Warsztat ${String(f + 1)}`)}
          </div>
        ))}
        {sourceTiles("center", view.center, "Środek")}
      </section>
      <p className="note" aria-label="Runda">
        Runda {view.round} · w worku {total(view.bag)} · w pudełku {total(view.box)}
        {picked !== null && ` · wybrano: ${tileName(picked.color)} — wskaż rząd na swojej planszy`}
      </p>
      <section aria-label="Gracze" className="players">
        {view.players.map((player, index) => (
          <PlayerBoard
            key={index}
            player={player}
            name={names[index] ?? `Gracz ${String(index + 1)}`}
            me={index === seat}
            toMove={!view.over && index === view.toMove}
            sheet={sheet}
            placements={index === seat ? targets : new Map()}
            onMove={play}
          />
        ))}
      </section>
      <p className="note">
        Premie na koniec: rząd +{sheet.rowBonus}, kolumna +{sheet.columnBonus}, kolor +{sheet.colorBonus}.
      </p>
    </div>
  );
}

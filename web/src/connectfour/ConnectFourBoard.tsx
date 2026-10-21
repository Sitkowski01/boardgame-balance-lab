import { useState } from "react";
import { newDiscs, sameRows, winningCells, type ConnectFourView } from "./model";
import "./connectfour.css";

export interface ConnectFourBoardProps {
  view: ConnectFourView;
  legalMoves: readonly string[];
  names: readonly string[];
  onMove: (move: string) => void;
}

type Rows = ConnectFourView["rows"];

/**
 * Plansza Czworek: cala kolumna jest przyciskiem, aktywnym tylko dla legalnego ruchu. Nowy krazek spada
 * na miejsce i ma obwodke (ostatni ruch), a po wygranej swieci sie zwycieska czworka.
 */
export function ConnectFourBoard({ view, legalMoves, names, onMove }: ConnectFourBoardProps) {
  const columns = view.rows[0]?.length ?? 0;
  // Ostatni ruch liczony wzgledem poprzedniej pozycji (wzorzec "poprzednie propsy" z dokumentacji Reacta).
  // Porownanie po zawartosci: ten sam widok przyslany ponownie (render, ponowne polaczenie) zostawia obwodke.
  const [seen, setSeen] = useState<{ rows: Rows; fresh: Set<number> }>({ rows: view.rows, fresh: new Set() });
  let fresh = seen.fresh;
  if (!sameRows(seen.rows, view.rows)) {
    fresh = newDiscs(seen.rows, view.rows);
    setSeen({ rows: view.rows, fresh });
  }
  const win = view.over && view.winner !== null ? winningCells(view.rows) : new Set<number>();
  const grid = { gridTemplateColumns: `repeat(${String(columns)}, 1fr)` };
  const name = (player: number): string => names[player] ?? `gracz ${String(player + 1)}`;

  return (
    <div className={`connectfour turn-p${String(view.toMove)}`}>
      <div className="c4-frame">
        <div className="grid" role="group" aria-label="Plansza" style={grid}>
          {view.rows.flatMap((row, r) =>
            row.map((cell, c) => {
              const index = r * columns + c;
              const classes = [
                "cell",
                cell === null ? "" : `p${String(cell)}`,
                fresh.has(index) ? "last" : "",
                win.has(index) ? "win" : "",
              ];
              return (
                <div
                  key={`${String(r)}-${String(c)}`}
                  role="img"
                  className={classes.filter((x) => x !== "").join(" ")}
                  style={fresh.has(index) ? { ["--drop" as string]: String(r + 1) } : undefined}
                  aria-label={cell === null ? "puste" : name(cell)}
                />
              );
            }),
          )}
        </div>
        <div className="drops" style={grid}>
          {Array.from({ length: columns }, (_, column) => (
            <button
              key={column}
              type="button"
              disabled={!legalMoves.includes(String(column))}
              onClick={() => {
                onMove(String(column));
              }}
            >
              <span className="visually-hidden">Kolumna {column + 1}</span>
            </button>
          ))}
        </div>
      </div>
      <p className="c4-legend">
        <span className="disc p0" aria-hidden="true" /> {name(0)}
        <span className="disc p1" aria-hidden="true" /> {name(1)}
      </p>
      {view.over && <p className="note">{view.winner === null ? "Remis." : `Wygrywa ${name(view.winner)}.`}</p>}
    </div>
  );
}

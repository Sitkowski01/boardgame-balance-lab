import type { ConnectFourView } from "./model";
import "./connectfour.css";

export interface ConnectFourBoardProps {
  view: ConnectFourView;
  legalMoves: readonly string[];
  names: readonly string[];
  onMove: (move: string) => void;
}

/** Plansza Czworek: przycisk nad kazda kolumna, aktywny tylko dla legalnego ruchu. */
export function ConnectFourBoard({ view, legalMoves, names, onMove }: ConnectFourBoardProps) {
  const columns = view.rows[0]?.length ?? 0;
  return (
    <div className="connectfour">
      <div className="drops" style={{ gridTemplateColumns: `repeat(${String(columns)}, 1fr)` }}>
        {Array.from({ length: columns }, (_, column) => (
          <button
            key={column}
            type="button"
            disabled={!legalMoves.includes(String(column))}
            onClick={() => {
              onMove(String(column));
            }}
          >
            Kolumna {column + 1}
          </button>
        ))}
      </div>
      <div className="grid" role="group" aria-label="Plansza" style={{ gridTemplateColumns: `repeat(${String(columns)}, 1fr)` }}>
        {view.rows.flatMap((row, r) =>
          row.map((cell, c) => (
            <div
              key={`${String(r)}-${String(c)}`}
              role="img"
              className={`cell${cell === null ? "" : ` p${String(cell)}`}`}
              aria-label={cell === null ? "puste" : (names[cell] ?? `gracz ${String(cell + 1)}`)}
            />
          )),
        )}
      </div>
      {view.over && (
        <p className="note">{view.winner === null ? "Remis." : `Wygrywa ${names[view.winner] ?? `gracz ${String(view.winner + 1)}`}.`}</p>
      )}
    </div>
  );
}

import { Bank } from "./Bank";
import { Card } from "./Card";
import { Cost } from "./Gem";
import { marketRows, type KlejnotySheet, type KlejnotyView } from "./model";
import { PlayerPanel } from "./PlayerPanel";
import "./klejnoty.css";

export interface KlejnotyBoardProps {
  view: KlejnotyView;
  sheet: KlejnotySheet;
  /** Legalne ruchy odbiorcy (puste, gdy nie jego kolej albo w powtorce). */
  legalMoves: readonly string[];
  /** Miejsce odbiorcy; null = widz. */
  seat: number | null;
  names: readonly string[];
  onMove: (move: string) => void;
}

/**
 * Plansza Klejnotow. Wszystko, co mozna kliknac, wynika z listy legalnych ruchow: przycisk "Kup" jest
 * tylko przy karcie, na ktora gracza stac, a zeton da sie wybrac tylko, jesli prowadzi do legalnego ruchu.
 */
export function KlejnotyBoard({ view, sheet, legalMoves, seat, names, onMove }: KlejnotyBoardProps) {
  return (
    <div className="klejnoty">
      <section aria-label="Patroni" className="patrons">
        {view.patrons.map((id) => {
          const patron = sheet.patrons[id];
          return patron === undefined ? null : (
            <div key={id} className="patron" aria-label={`Patron #${String(id)}`}>
              <span className="patron-crown" aria-hidden="true">
                ♛
              </span>
              <strong>{patron.points}</strong>
              <Cost counts={patron.requirement} />
            </div>
          );
        })}
      </section>

      <section aria-label="Rynek" className="market">
        {marketRows(view.market)
          .map((row, index) => ({ row, tier: index + 1 }))
          .reverse()
          .map(({ row, tier }) => {
            const deck = legalMoves.includes(`deck:${String(tier)}`) ? `deck:${String(tier)}` : null;
            return (
              <div key={tier} className="market-row">
                <div className={`deck tier-${String(tier)}`} aria-label={`Talia ${String(tier)}`}>
                  <span className="deck-tier">{"I".repeat(tier)}</span>
                  <span className="deck-count">{view.deckSizes[tier - 1]} kart</span>
                  {deck !== null && (
                    <button
                      type="button"
                      onClick={() => {
                        onMove(deck);
                      }}
                    >
                      Rezerwuj w ciemno
                    </button>
                  )}
                </div>
                {row.map((id, slot) =>
                  id === null ? (
                    <div key={slot} className="card empty" aria-label="Pusty slot" />
                  ) : (
                    <Card key={id} card={sheet.cards[id]} id={id} legalMoves={legalMoves} onMove={onMove} />
                  ),
                )}
              </div>
            );
          })}
      </section>

      <Bank bank={view.bank} legalMoves={legalMoves} onMove={onMove} />

      <section aria-label="Gracze" className="players">
        {view.players.map((player, index) => (
          <PlayerPanel
            key={index}
            player={player}
            name={names[index] ?? `Gracz ${String(index + 1)}`}
            me={index === seat}
            toMove={!view.over && index === view.toMove}
            sheet={sheet}
            legalMoves={index === seat ? legalMoves : []}
            onMove={onMove}
          />
        ))}
      </section>
      {view.finalRound && !view.over && <p className="note">Ostatnia runda — ktoś ma już {sheet.winningPoints} punktów.</p>}
    </div>
  );
}

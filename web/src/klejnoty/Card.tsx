import { Cost, GemIcon } from "./Gem";
import { gemName, type CardSheet } from "./model";
import { cardMoves } from "./moves";

export interface CardProps {
  /** Opis karty z tabeli zasad; brak opisu = sama ramka z numerem. */
  card: CardSheet | undefined;
  id: number;
  legalMoves: readonly string[];
  onMove: (move: string) => void;
}

/** Karta na rynku albo w rezerwie: punkty, rabat, koszt i przyciski tylko dla legalnych ruchow. */
export function Card({ card, id, legalMoves, onMove }: CardProps) {
  const { buy, reserve } = cardMoves(id, legalMoves);
  return (
    <div className={`card tier-${String(card?.tier ?? 0)}`} aria-label={`Karta #${String(id)}`}>
      {card !== undefined && (
        <>
          <div className="card-head">
            <strong>{card.points > 0 ? card.points : ""}</strong>
            <GemIcon gem={card.bonus} label={`rabat: ${gemName(card.bonus)}`} />
          </div>
          <Cost counts={card.cost} />
        </>
      )}
      <div className="card-actions">
        {buy !== null && (
          <button
            type="button"
            className="buy"
            aria-label={`Kup kartę #${String(id)}`}
            onClick={() => {
              onMove(buy);
            }}
          >
            Kup
          </button>
        )}
        {reserve !== null && (
          <button
            type="button"
            aria-label={`Rezerwuj kartę #${String(id)}`}
            onClick={() => {
              onMove(reserve);
            }}
          >
            Rezerwuj
          </button>
        )}
      </div>
    </div>
  );
}

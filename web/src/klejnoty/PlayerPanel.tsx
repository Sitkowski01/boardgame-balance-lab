import { Bump } from "../Bump";
import { Card } from "./Card";
import { Gem } from "./Gem";
import { gemName, totalTokens, type KlejnotySheet, type PlayerView } from "./model";

export interface PlayerPanelProps {
  player: PlayerView;
  name: string;
  me: boolean;
  toMove: boolean;
  sheet: KlejnotySheet;
  /** Legalne ruchy, jesli to panel odbiorcy (kupno z rezerwy); u innych graczy puste. */
  legalMoves: readonly string[];
  onMove: (move: string) => void;
}

/** Stan gracza: punkty, zetony, rabaty z kupionych kart i rezerwa (cudze karty wziete w ciemno — tylko rewers). */
export function PlayerPanel({ player, name, me, toMove, sheet, legalMoves, onMove }: PlayerPanelProps) {
  return (
    <article className={`player${me ? " me" : ""}${toMove ? " to-move" : ""}`} aria-label={`Gracz ${name}`}>
      <header>
        <strong>{name}</strong>
        {me && <span className="badge">ty</span>}
        {toMove && <span className="badge">na ruchu</span>}
        <span className="points" aria-label="Punkty">
          <Bump value={player.points}>{player.points}</Bump> pkt
        </span>
      </header>
      <div className="row tokens" aria-label="Żetony">
        {player.tokens.map((n, gem) => (n > 0 ? <Gem key={gem} gem={gem} count={n} /> : null))}
        <span className="muted">
          <Bump value={totalTokens(player)}>żetony: {totalTokens(player)}/10</Bump>
        </span>
      </div>
      <div className="row bonuses" aria-label="Rabaty">
        {player.bonuses.map((n, gem) =>
          n > 0 ? <Gem key={gem} gem={gem} count={n} label={`rabat ${gemName(gem)}: ${String(n)}`} /> : null,
        )}
        <span className="muted">
          <Bump value={player.bought.length}>kart: {player.bought.length}</Bump>
        </span>
        {player.patrons.length > 0 && <span className="muted">patroni: {player.patrons.length}</span>}
      </div>
      {player.reserved.length > 0 && (
        <div className="reserved" aria-label="Rezerwa">
          {player.reserved.map((r, i) =>
            r.card === null ? (
              <div key={-1 - i} className={`card back tier-${String(r.tier)}`} aria-label={`Zakryta karta poziomu ${String(r.tier)}`}>
                {"I".repeat(r.tier)}
              </div>
            ) : (
              <Card key={r.card} card={sheet.cards[r.card]} id={r.card} legalMoves={legalMoves} onMove={onMove} />
            ),
          )}
        </div>
      )}
    </article>
  );
}

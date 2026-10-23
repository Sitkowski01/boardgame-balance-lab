import { playerName } from "../../names";
import { plural } from "../../plural";
import { pairScore, percent, type LeagueReport } from "../model";

/** Kolor komorki: szary przy 50%, niebieski ku wygranej wiersza, czerwony ku przegranej (skala rozbiezna). */
export function cellColor(score: number): string {
  const strength = Math.min(1, Math.abs(score - 0.5) * 2);
  const pole = score >= 0.5 ? "var(--win)" : "var(--loss)";
  return `color-mix(in oklab, ${pole} ${String(Math.round(strength * 100))}%, var(--neutral))`;
}

/**
 * Kolor tekstu pod kontrast AA: blisko polowy — kolor tekstu motywu (komorka prawie neutralna), na czerwieni zawsze
 * ciemny (biel na #e34948 to ok. 3,9:1), na niebieskim bialy dopiero przy prawie pelnym kolorze.
 */
export function inkOn(score: number): "theme" | "dark" | "light" {
  const strength = Math.abs(score - 0.5) * 2;
  if (strength < 0.35) return "theme";
  return score > 0.5 && strength > 0.85 ? "light" : "dark";
}

/**
 * "Kto bije kogo": wiersz przeciw kolumnie, sredni wynik wiersza (remis = pol). Wartosc jest wpisana w kazda
 * komorke, wiec kolor tylko pomaga ja szybko znalezc.
 */
export function PairMatrix({ report }: { report: LeagueReport }) {
  const names = report.ratings.map((r) => r.name);
  return (
    <figure className="chart">
      <figcaption>
        <strong>Kto bije kogo</strong>
        <span>
          Średni wynik bota z wiersza przeciw botowi z kolumny ({report.gamesPerPair}{" "}
          {plural(report.gamesPerPair, "partia", "partie", "partii")} na parę, remis = pół).
        </span>
      </figcaption>
      <div className="matrix-scroll">
        <table className="matrix" aria-label="Kto bije kogo">
          <thead>
            <tr>
              <th scope="col">
                <span className="visually-hidden">wiersz przeciw kolumnie</span>
              </th>
              {names.map((n) => (
                <th key={n} scope="col">
                  {playerName(n)}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {names.map((row) => (
              <tr key={row}>
                <th scope="row">{playerName(row)}</th>
                {names.map((column) => {
                  const score = row === column ? null : pairScore(report, row, column);
                  return score === null ? (
                    <td key={column} className="empty">
                      {row === column ? "—" : "brak partii"}
                    </td>
                  ) : (
                    <td
                      key={column}
                      className={`cell ink-${inkOn(score)}`}
                      style={{ background: cellColor(score) }}
                      title={`${playerName(row)} przeciw ${playerName(column)}`}
                    >
                      {percent(score)}
                    </td>
                  );
                })}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <p className="legend">
        <span className="key-swatch loss" /> wiersz przegrywa · <span className="key-swatch neutral" /> po równo ·{" "}
        <span className="key-swatch win" /> wiersz wygrywa
      </p>
    </figure>
  );
}

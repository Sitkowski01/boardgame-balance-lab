import { useRef, useState } from "react";
import { itemName } from "../../names";
import { useWidth } from "./useWidth";
import { edge, percent, points, verdictMark, verdictWord, type ItemStats, type TournamentReport } from "../model";

const M = { left: 64, right: 16, top: 12, bottom: 44 };

/** Krok podzialki w punktach odsetka: okragly, zeby os miala 2-4 kreski w kazda strone. */
export function niceStep(reach: number): number {
  return [0.01, 0.02, 0.05, 0.1, 0.2, 0.5].find((step) => reach / step <= 4) ?? 0.5;
}

/**
 * Elementy gry (karty, pola sciany): w poziomie, w jakiej czesci partii ktos je mial, w pionie — o ile czesciej
 * wygrywa wlasciciel niz przy przecietnym elemencie grupy. Ocena (▲ za mocna, ▼ za slaba, ○ martwa) to ksztalt
 * i kolor naraz, a pod wykresem zawsze jest opis wskazanego elementu i tabela odstajacych.
 */
export function ItemScatter({ report }: { report: TournamentReport }) {
  const [focus, setFocus] = useState<ItemStats | null>(null);
  const frame = useRef<HTMLDivElement>(null);
  const W = Math.max(280, useWidth(frame, 960));
  // Na waskim ekranie wykres wyzszy niz szeroki, zeby punkty sie nie zlewaly.
  const H = W < 560 ? 340 : 400;
  const owned = report.items.filter((i) => i.ownerWins.trials > 0);
  const widest = Math.max(0.05, ...owned.map((i) => Math.abs(edge(report, i))));
  const step = niceStep(widest);
  const reach = Math.ceil((widest * 1.05) / step) * step;
  const ticks = Array.from({ length: Math.round((2 * reach) / step) + 1 }, (_, k) => -reach + k * step);
  const x = (taken: number): number => M.left + taken * (W - M.left - M.right);
  const y = (value: number): number => M.top + ((reach - value) / (2 * reach)) * (H - M.top - M.bottom);
  const flagged = report.items
    .filter((i) => i.verdict !== "None")
    .sort((a, b) => order(a) - order(b) || Math.abs(edge(report, b)) - Math.abs(edge(report, a)));
  const noun = report.itemNoun;
  const describe = (item: ItemStats): string =>
    `${itemName(item.name)}: w ${percent(item.taken.rate)} partii, właściciel wygrywa ${percent(item.ownerWins.rate)} (${points(edge(report, item))} wobec grupy „${item.group}”)${item.verdict === "None" ? "" : ` — ${verdictWord(item.verdict, noun)}`}`;

  return (
    <figure className="chart">
      <figcaption>
        <strong>{capitalize(noun.plural)}: martwe i za mocne</strong>
        <span>
          Każdy punkt to {noun.singular}. To korelacja, nie dowód: silny gracz zdobywa też dobre {noun.plural} — wynik wskazuje, co
          sprawdzić wariantem.
        </span>
      </figcaption>
      <div ref={frame} className="scatter-frame">
        <svg
          viewBox={`0 0 ${String(W)} ${String(H)}`}
          className="scatter"
          role="group"
          aria-label={`${capitalize(noun.plural)}: wykres punktowy`}
        >
          {[0, 0.25, 0.5, 0.75, 1].map((t) => (
            <g key={t}>
              <line className="grid" x1={x(t)} x2={x(t)} y1={M.top} y2={H - M.bottom} />
              <text className="tick" x={x(t)} y={H - M.bottom + 16} textAnchor="middle">
                {percent(t).replace(",0", "")}
              </text>
            </g>
          ))}
          {ticks.map((v) => (
            <g key={v.toFixed(4)}>
              <line className={Math.abs(v) < 1e-9 ? "axis" : "grid"} x1={M.left} x2={W - M.right} y1={y(v)} y2={y(v)} />
              <text className="tick" x={M.left - 8} y={y(v) + 4} textAnchor="end">
                {Math.abs(v) < 1e-9 ? "0" : `${v > 0 ? "+" : "−"}${String(Math.round(Math.abs(v) * 100))}`}
              </text>
            </g>
          ))}
          <text className="axis-title" transform={`translate(16 ${String((M.top + H - M.bottom) / 2)}) rotate(-90)`} textAnchor="middle">
            przewaga właściciela (pp)
          </text>
          <text className="axis-title" x={(M.left + W - M.right) / 2} y={H - 4} textAnchor="middle">
            udział w partiach
          </text>
          {owned.map((item) => (
            <g
              key={item.id}
              className={`dot ${item.verdict.toLowerCase()}${focus?.id === item.id ? " focused" : ""}`}
              role="img"
              aria-label={describe(item)}
              tabIndex={0}
              onPointerEnter={() => {
                setFocus(item);
              }}
              onFocus={() => {
                setFocus(item);
              }}
              onBlur={() => {
                // Opis znika tylko, jesli fokus wyszedl z tego punktu, a nie gdy wskaznik pokazal juz inny.
                setFocus((shown) => (shown?.id === item.id ? null : shown));
              }}
            >
              <circle className="hit" cx={x(item.taken.rate)} cy={y(edge(report, item))} r={12} />
              {item.verdict === "Strong" || item.verdict === "Weak" ? (
                <path className="mark" d={triangle(x(item.taken.rate), y(edge(report, item)), item.verdict === "Strong")} />
              ) : (
                <circle className="mark" cx={x(item.taken.rate)} cy={y(edge(report, item))} r={4.5} />
              )}
            </g>
          ))}
        </svg>
      </div>
      <p className="readout" aria-live="polite">
        {focus === null ? "Wskaż punkt, żeby zobaczyć szczegóły." : describe(focus)}
      </p>
      <p className="legend">
        <span className="key-dot strong">▲</span> {noun.strong} · <span className="key-dot weak">▼</span> {noun.weak} ·{" "}
        <span className="key-dot dead">○</span> {noun.dead} (w &lt; 5% partii) · <span className="key-dot none">●</span> bez zastrzeżeń
      </p>
      <details className="table-view">
        <summary>Tabela wszystkich ({report.items.length})</summary>
        <table aria-label={`${capitalize(noun.plural)}: tabela`}>
          <thead>
            <tr>
              <th scope="col">{capitalize(noun.singular)}</th>
              <th scope="col">Grupa</th>
              <th scope="col">W partiach</th>
              <th scope="col">Właściciel wygrywa</th>
            </tr>
          </thead>
          <tbody>
            {report.items.map((item) => (
              <tr key={item.id}>
                <td>{itemName(item.name)}</td>
                <td>{item.group}</td>
                <td>{percent(item.taken.rate)}</td>
                <td>
                  {item.ownerWins.trials === 0
                    ? "brak danych"
                    : `${percent(item.ownerWins.rate)} (${percent(item.ownerWins.low)}–${percent(item.ownerWins.high)})`}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </details>
      {flagged.length === 0 ? (
        <p className="note">Nic nie odstaje przy tej liczbie partii.</p>
      ) : (
        <table className="flagged" aria-label={`Odstające: ${noun.plural}`}>
          <thead>
            <tr>
              <th scope="col">{capitalize(noun.singular)}</th>
              <th scope="col">Ocena</th>
              <th scope="col">W partiach</th>
              <th scope="col">Właściciel wygrywa</th>
            </tr>
          </thead>
          <tbody>
            {flagged.slice(0, 12).map((item) => (
              <tr key={item.id}>
                <td>{itemName(item.name)}</td>
                <td>
                  <span className={`tag ${item.verdict.toLowerCase()}`}>
                    {verdictMark(item.verdict)} {verdictWord(item.verdict, noun)}
                  </span>
                </td>
                <td>{percent(item.taken.rate)}</td>
                <td>
                  {percent(item.ownerWins.rate)}{" "}
                  <span className="range">
                    ({percent(item.ownerWins.low)}–{percent(item.ownerWins.high)})
                  </span>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </figure>
  );
}

const order = (item: ItemStats): number => ["Strong", "Weak", "Dead"].indexOf(item.verdict);

const capitalize = (text: string): string => text.charAt(0).toUpperCase() + text.slice(1);

/** Trojkat wierzcholkiem w gore (za mocna) albo w dol (za slaba), wpisany w kolo o promieniu 6. */
function triangle(cx: number, cy: number, up: boolean): string {
  const apex = up ? cy - 6 : cy + 6;
  const base = up ? cy + 3.6 : cy - 3.6;
  return `M${String(cx)} ${String(apex)}L${String(cx + 5.6)} ${String(base)}L${String(cx - 5.6)} ${String(base)}Z`;
}

import { percent } from "../model";

export interface IntervalRow {
  key: string;
  label: string;
  rate: number;
  low: number;
  high: number;
  /** Np. "z 400 partii" — do podpowiedzi i tabeli. */
  detail: string;
}

export interface IntervalBarsProps {
  title: string;
  caption: string;
  rows: readonly IntervalRow[];
  /** Linia odniesienia, np. uczciwy udzial 1/n. */
  reference?: { value: number; label: string };
}

/** Pozycja na osi 0-100% jako procent szerokosci toru. */
const at = (rate: number): string => `${String(Math.min(100, Math.max(0, rate * 100)))}%`;

/** Czy caly przedzial lezy po jednej stronie linii odniesienia — wtedy roznica nie jest przypadkiem. */
export function sideOf(row: IntervalRow, reference: number | undefined): "above" | "below" | null {
  if (reference === undefined) return null;
  if (row.low > reference) return "above";
  return row.high < reference ? "below" : null;
}

/**
 * Odsetki z 95% przedzialami: cienki slupek od zera, wasy przedzialu i linia odniesienia. Wiersz, ktorego
 * przedzial nie obejmuje odniesienia, ma znak ▲/▼ przy etykiecie — nie tylko inny kolor.
 */
export function IntervalBars({ title, caption, rows, reference }: IntervalBarsProps) {
  return (
    <figure className="chart">
      <figcaption>
        <strong>{title}</strong>
        <span>{caption}</span>
      </figcaption>
      <div className="bars" role="list" aria-label={title}>
        {rows.map((row) => {
          const side = sideOf(row, reference?.value);
          const tip = `${row.label}: ${percent(row.rate)} (95%: ${percent(row.low)}–${percent(row.high)}) ${row.detail}`;
          return (
            <div key={row.key} className="bar-row" role="listitem" aria-label={tip}>
              <span className="bar-label">
                {row.label}
                {side !== null && (
                  <span
                    className={`flag ${side}`}
                    title={side === "above" ? "istotnie powyżej odniesienia" : "istotnie poniżej odniesienia"}
                  >
                    {side === "above" ? " ▲" : " ▼"}
                  </span>
                )}
              </span>
              <span className="track" data-tip={tip} tabIndex={0}>
                <span className="fill" style={{ width: at(row.rate) }} />
                <span className="whisker" style={{ left: at(row.low), width: `calc(${at(row.high)} - ${at(row.low)})` }} />
                {reference !== undefined && <span className="reference" style={{ left: at(reference.value) }} />}
              </span>
              <span className="bar-value">{percent(row.rate)}</span>
            </div>
          );
        })}
      </div>
      {reference !== undefined && (
        <p className="legend">
          <span className="key-reference" /> {reference.label} · <span className="key-whisker" /> 95% przedział · ▲▼ cały przedział po
          jednej stronie linii
        </p>
      )}
      <details className="table-view">
        <summary>Tabela</summary>
        <table aria-label={`${title}: tabela`}>
          <thead>
            <tr>
              <th scope="col">Kto</th>
              <th scope="col">Odsetek</th>
              <th scope="col">95% przedział</th>
              <th scope="col">Dane</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={row.key}>
                <td>{row.label}</td>
                <td>{percent(row.rate)}</td>
                <td>
                  {percent(row.low)}–{percent(row.high)}
                </td>
                <td>{row.detail}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </details>
    </figure>
  );
}

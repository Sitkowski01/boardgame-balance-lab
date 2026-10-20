export interface DiffRow {
  key: string;
  label: string;
  delta: number;
  low: number;
  high: number;
  /** Przedzial nie obejmuje zera. */
  significant: boolean;
  /** Np. "52.0% → 55.1%" — do podpowiedzi i tabeli. */
  detail: string;
}

export interface DiffBarsProps {
  title: string;
  caption: string;
  rows: readonly DiffRow[];
  /** Zapis wartosci, np. punkty procentowe albo Elo. */
  format: (value: number) => string;
  /** Pol szerokosci osi w jednostkach wartosci; domyslnie najwiekszy kraniec przedzialu z zapasem. */
  reach?: number;
  /** Bez przedzialow (np. ranking Elo): sam slupek, bez wasow i bez legendy istotnosci. */
  intervals?: boolean;
}

/**
 * Roznice od zera w obie strony: slupek od osi srodkowej do wartosci, wasy przedzialu. Istotna roznica ma
 * pelny kolor i znak "istotna"; nieistotna jest przygaszona — wtedy zmiana mogla sie po prostu wylosowac.
 */
export function DiffBars({ title, caption, rows, format, reach, intervals = true }: DiffBarsProps) {
  const half = reach ?? Math.max(1e-9, ...rows.flatMap((r) => [Math.abs(r.low), Math.abs(r.high)])) * 1.1;
  const at = (value: number): number => 50 + (Math.max(-half, Math.min(half, value)) / half) * 50;
  return (
    <figure className="chart">
      <figcaption>
        <strong>{title}</strong>
        <span>{caption}</span>
      </figcaption>
      <div className="bars" role="list" aria-label={title}>
        {rows.map((row) => {
          const [from, to] = row.delta >= 0 ? [50, at(row.delta)] : [at(row.delta), 50];
          const tip = intervals
            ? `${row.label}: ${format(row.delta)} (95%: ${format(row.low)} do ${format(row.high)})${row.significant ? ", istotna" : ""} · ${row.detail}`
            : `${row.label}: ${format(row.delta)} · ${row.detail}`;
          return (
            <div
              key={row.key}
              className={`bar-row${!intervals || row.significant ? " significant" : " faint"}`}
              role="listitem"
              aria-label={tip}
            >
              <span className="bar-label">
                {row.label}
                {row.significant && <span className="flag above"> ●</span>}
              </span>
              <span className="track diverging" data-tip={tip} tabIndex={0}>
                <span
                  className={`fill ${row.delta >= 0 ? "up" : "down"}`}
                  style={{ left: `${String(from)}%`, width: `${String(to - from)}%` }}
                />
                {intervals && (
                  <span className="whisker" style={{ left: `${String(at(row.low))}%`, width: `${String(at(row.high) - at(row.low))}%` }} />
                )}
                <span className="reference zero" style={{ left: "50%" }} />
              </span>
              <span className="bar-value">{format(row.delta)}</span>
            </div>
          );
        })}
      </div>
      {intervals && (
        <p className="legend">
          <span className="key-reference" /> zero (bez zmiany) · <span className="key-whisker" /> 95% przedział · ● istotna: przedział nie
          obejmuje zera
        </p>
      )}
      <details className="table-view">
        <summary>Tabela</summary>
        <table aria-label={`${title}: tabela`}>
          <thead>
            <tr>
              <th scope="col">Co</th>
              <th scope="col">{intervals ? "Różnica" : "Wartość"}</th>
              {intervals && <th scope="col">95% przedział</th>}
              {intervals && <th scope="col">Istotna</th>}
              <th scope="col">Dane</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={row.key}>
                <td>{row.label}</td>
                <td>{format(row.delta)}</td>
                {intervals && (
                  <td>
                    {format(row.low)} do {format(row.high)}
                  </td>
                )}
                {intervals && <td>{row.significant ? "tak" : "nie"}</td>}
                <td>{row.detail}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </details>
    </figure>
  );
}

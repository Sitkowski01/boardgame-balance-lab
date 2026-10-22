import { DiffBars } from "./charts/DiffBars";
import { IntervalBars, type IntervalRow } from "./charts/IntervalBars";
import { ItemScatter } from "./charts/ItemScatter";
import { PairMatrix } from "./charts/PairMatrix";
import { plural } from "../plural";
import {
  decimal,
  percent,
  points,
  type ComparisonReport,
  type ExperimentResult,
  type LeagueReport,
  type Proportion,
  type TournamentReport,
} from "./model";

/** Wyniki eksperymentu: inny zestaw wykresow dla turnieju, ligi i porownania "co jesli". */
export function Results({ result }: { result: ExperimentResult }) {
  switch (result.kind) {
    case "Tournament":
      return <TournamentResults report={result.report} />;
    case "League":
      return <LeagueResults report={result.report} />;
    case "Comparison":
      return <ComparisonResults report={result.report} />;
  }
}

const row = (key: string, label: string, p: Proportion): IntervalRow => ({
  key,
  label,
  rate: p.rate,
  low: p.low,
  high: p.high,
  detail: `z ${String(p.trials)} partii`,
});

function Tiles({ tiles }: { tiles: readonly { label: string; value: string; note?: string | undefined }[] }) {
  return (
    <div className="stats">
      {tiles.map((t) => (
        <div key={t.label} className="stat">
          <span className="stat-label">{t.label}</span>
          <span className="stat-value">{t.value}</span>
          {t.note !== undefined && <span className="stat-note">{t.note}</span>}
        </div>
      ))}
    </div>
  );
}

export function TournamentResults({ report }: { report: TournamentReport }) {
  const fair = 1 / Math.max(1, report.seats.length);
  return (
    <section className="results" aria-label="Wyniki turnieju">
      <Tiles
        tiles={[
          { label: "Ukończone partie", value: `${String(report.completed)} z ${String(report.games)}`, note: `ziarno ${report.seed}` },
          { label: "Remisy", value: percent(report.completed === 0 ? 0 : report.draws / report.completed) },
          ...(report.length === null
            ? []
            : [
                {
                  label: "Długość partii",
                  value: `${decimal(report.length.mean, 1)} ruchu`,
                  note: `mediana ${String(report.length.median)}, 90% poniżej ${String(report.length.p90)}`,
                },
              ]),
        ]}
      />
      <IntervalBars
        title="Przewaga miejsca przy stole"
        caption="Jak często wygrywa gracz z danego miejsca. Boty siadają po kolei na każdym miejscu, więc różnica to przewaga samej kolejności."
        rows={report.seats.map((s) => row(`seat${String(s.seat)}`, `miejsce ${String(s.seat + 1)}`, s.wins))}
        reference={{ value: fair, label: `uczciwy udział ${percent(fair)}` }}
      />
      <IntervalBars
        title="Boty"
        caption="Odsetek wygranych każdego bota z 95% przedziałem."
        rows={report.contestants.map((c) => row(c.name, c.name, c.wins))}
        reference={{ value: fair, label: `uczciwy udział ${percent(fair)}` }}
      />
      {report.items.length > 0 && <ItemScatter report={report} />}
    </section>
  );
}

export function LeagueResults({ report }: { report: LeagueReport }) {
  return (
    <section className="results" aria-label="Wyniki ligi">
      <Tiles
        tiles={[
          {
            label: "Najlepszy",
            value: report.ratings[0]?.name ?? "—",
            note: report.ratings[0] === undefined ? undefined : `Elo ${report.ratings[0].elo.toFixed(0)}`,
          },
          {
            label: "Pary",
            value: String(report.pairs.length),
            note: `${String(report.gamesPerPair)} ${plural(report.gamesPerPair, "partia", "partie", "partii")} na parę`,
          },
        ]}
      />
      <DiffBars
        title="Ranking Elo"
        caption="Siła względem średniej 1500 (model Bradleya-Terry'ego ze wszystkich partii). 200 punktów różnicy to ok. 76% wygranych."
        rows={report.ratings.map((r) => ({
          key: r.name,
          label: r.name,
          delta: r.elo - 1500,
          low: r.elo - 1500,
          high: r.elo - 1500,
          significant: false,
          detail: `Elo ${r.elo.toFixed(0)}, średni wynik ${percent(r.meanScore)}`,
        }))}
        format={(v) => `${v > 0 ? "+" : v < 0 ? "−" : "±"}${Math.abs(v).toFixed(0)}`}
        intervals={false}
      />
      <PairMatrix report={report} />
    </section>
  );
}

export function ComparisonResults({ report }: { report: ComparisonReport }) {
  // Tylko zmiana wygranych wlasciciela — to ja rysuje wykres; zmiane samego udzialu w partiach mowi podpowiedz.
  const changed = report.items
    .filter((i) => i.ownerWins.significant)
    .sort(
      (a, b) =>
        Math.max(Math.abs(b.taken.delta), Math.abs(b.ownerWins.delta)) - Math.max(Math.abs(a.taken.delta), Math.abs(a.ownerWins.delta)),
    );
  const noun = report.variant.itemNoun;
  const pp = (v: number): string => points(v);
  return (
    <section className="results" aria-label="Wyniki porównania">
      <Tiles
        tiles={[
          { label: "Oryginał", value: report.baseline.game, note: `${String(report.baseline.completed)} partii` },
          { label: "Wariant", value: report.variant.game, note: `${String(report.variant.completed)} partii z tych samych ziaren` },
          ...(report.length === null
            ? []
            : [
                {
                  label: "Długość partii",
                  value: `${report.length.delta >= 0 ? "+" : "−"}${decimal(Math.abs(report.length.delta), 1)} ruchu`,
                  note: report.length.significant ? "istotna zmiana" : "w granicach przypadku",
                },
              ]),
        ]}
      />
      <DiffBars
        title="Boty: zmiana odsetka wygranych"
        caption="Wariant minus oryginał. Te same rozdania po obu stronach, więc różnica bierze się ze zmiany zasad."
        rows={report.contestants.map((c) => ({
          key: c.name,
          label: c.name,
          ...c.wins,
          detail: `${percent(c.wins.baseline)} → ${percent(c.wins.variant)}`,
        }))}
        format={pp}
      />
      <DiffBars
        title="Miejsca przy stole: zmiana przewagi"
        caption="Czy wariant wyrównuje (albo psuje) szanse kolejnych miejsc."
        rows={report.seats.map((s) => ({
          key: s.name,
          label: s.name,
          ...s.wins,
          detail: `${percent(s.wins.baseline)} → ${percent(s.wins.variant)}`,
        }))}
        format={pp}
      />
      {report.items.length > 0 &&
        (changed.length === 0 ? (
          <p className="note">Żadne {noun.plural} nie zmieniły się istotnie.</p>
        ) : (
          <DiffBars
            title={`${noun.plural.charAt(0).toUpperCase()}${noun.plural.slice(1)}, które zmieniły się istotnie`}
            caption={`Zmiana wygranych właściciela; ${String(changed.length)} z ${String(report.items.length)}${changed.length > 15 ? ", poniżej 15 największych" : ""}.`}
            rows={changed.slice(0, 15).map((i) => ({
              key: String(i.id),
              label: i.name,
              ...i.ownerWins,
              detail: `udział w partiach ${pp(i.taken.delta)}, właściciel wygrywa ${percent(i.ownerWins.baseline)} → ${percent(i.ownerWins.variant)}`,
            }))}
            format={pp}
          />
        ))}
    </section>
  );
}

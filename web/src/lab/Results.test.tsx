import { fireEvent, render, screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { comparison, d, fields, league, tournament } from "../test/labReports";
import { sideOf } from "./charts/IntervalBars";
import { niceStep } from "./charts/ItemScatter";
import { cellColor, inkOn } from "./charts/PairMatrix";
import { Results } from "./Results";

const list = (name: string): HTMLElement => screen.getByRole("list", { name });

describe("Results", () => {
  it("shows a tournament: tiles, seats against the fair share, bots and items", () => {
    render(<Results result={{ kind: "Tournament", report: tournament() }} />);

    expect(screen.getByText("ukończone: 98 z 100")).toBeInTheDocument();
    expect(screen.getByText("62,4 ruchu")).toBeInTheDocument();
    expect(screen.getByText("mediana 61, 90% poniżej 80")).toBeInTheDocument();
    const seats = within(list("Przewaga miejsca przy stole")).getAllByRole("listitem");
    expect(seats.map((s) => s.getAttribute("aria-label"))).toEqual([
      "miejsce 1: 56,1% (95%: 46,0%–65,0%) z 98 partii",
      "miejsce 2: 39,8% (95%: 31,0%–49,0%) z 98 partii",
    ]);
    expect(screen.getAllByText("uczciwy udział 50,0%", { exact: false })).toHaveLength(2);
    // Boty: greedy caly przedzial ponad 50%, random caly ponizej.
    expect(within(list("Boty")).getByText("▲", { exact: false })).toBeInTheDocument();
    expect(within(list("Boty")).getByText("▼", { exact: false })).toBeInTheDocument();
    expect(screen.getByRole("group", { name: "Karty: wykres punktowy" })).toBeInTheDocument();
    expect(screen.getByText("Karty: martwe i za mocne")).toBeInTheDocument();
  });

  it("names items the way the game does and lists the flagged ones strongest first", () => {
    render(<Results result={{ kind: "Tournament", report: tournament({ itemNoun: fields }) }} />);

    expect(screen.getByText("Pola sciany: martwe i za mocne")).toBeInTheDocument();
    const rows = within(screen.getByRole("table", { name: "Odstające: pola sciany" }))
      .getAllByRole("row")
      .slice(1);
    expect(rows.map((r) => r.textContent)).toEqual([
      expect.stringContaining("#0 mocna▲ za mocne"),
      expect.stringContaining("#2 slaba▼ za slabe"),
      expect.stringContaining("#1 martwa○ martwe"),
    ]);
  });

  it("shows the hovered item below the chart, nobody-owned items stay off the chart", () => {
    render(<Results result={{ kind: "Tournament", report: tournament() }} />);

    expect(screen.getByText("Wskaż punkt, żeby zobaczyć szczegóły.")).toBeInTheDocument();
    const dots = screen.getAllByRole("img", { name: /partii, właściciel/u });
    expect(dots).toHaveLength(4); // "#4 nikt" bez wlascicieli
    fireEvent.pointerEnter(dots[0] ?? document.body);
    expect(
      screen.getByText(/^#0 mocna: w 50,0% partii, właściciel wygrywa 80,0% \(\+40,0 pp wobec grupy „poziom 1”\) — za mocna$/u),
    ).toBeInTheDocument();
    fireEvent.pointerEnter(dots[3] ?? document.body);
    expect(screen.getByText(/^#3 zwykla: .*grupy „poziom 2”\)$/u)).toBeInTheDocument();
  });

  it("skips items, length and draws it does not have", () => {
    render(<Results result={{ kind: "Tournament", report: tournament({ items: [], length: null, completed: 0, draws: 0 }) }} />);

    expect(screen.queryByText(/martwe i za mocne/u)).not.toBeInTheDocument();
    expect(screen.queryByText("Długość partii")).not.toBeInTheDocument();
    expect(screen.getByText("0,0%")).toBeInTheDocument();
  });

  it("says when nothing stands out", () => {
    render(
      <Results
        result={{ kind: "Tournament", report: tournament({ items: tournament().items.map((i) => ({ ...i, verdict: "None" as const })) }) }}
      />,
    );

    expect(screen.getByText("Nic nie odstaje przy tej liczbie partii.")).toBeInTheDocument();
  });

  it("shows a league: Elo against 1500 and who beats whom", () => {
    render(<Results result={{ kind: "League", report: league() }} />);

    expect(screen.getByText("Zachłanny", { selector: ".stat-value" })).toBeInTheDocument();
    expect(screen.getByText("Elo 1720")).toBeInTheDocument();
    expect(
      within(list("Ranking Elo"))
        .getAllByRole("listitem")
        .map((r) => r.getAttribute("aria-label")),
    ).toEqual([
      "Zachłanny: +220 · Elo 1720, średni wynik 80,0%",
      "ISMCTS 50: −20 · Elo 1480, średni wynik 45,0%",
      "Losowy: −200 · Elo 1300, średni wynik 20,0%",
    ]);
    const matrix = screen.getByRole("table", { name: "Kto bije kogo" });
    expect(within(matrix).getByText("95,0%")).toHaveAttribute("title", "Zachłanny przeciw Losowy");
    expect(within(matrix).getByText("95,0%")).toHaveClass("ink-light");
    expect(within(matrix).getByText("5,0%")).toBeInTheDocument();
    expect(within(matrix).getAllByText("brak partii")).toHaveLength(2);
    expect(within(matrix).getAllByText("—")).toHaveLength(3);
  });

  it("shows a comparison: what changed and whether it is more than chance", () => {
    render(<Results result={{ kind: "Comparison", report: comparison() }} />);

    expect(screen.getByText("Klejnoty (win=13)")).toBeInTheDocument();
    expect(screen.getByText("−6,5 ruchu")).toBeInTheDocument();
    expect(screen.getByText("istotna zmiana")).toBeInTheDocument();
    const bots = within(list("Boty: zmiana odsetka wygranych")).getAllByRole("listitem");
    expect(bots[0]).toHaveClass("significant");
    expect(bots[1]).toHaveClass("faint");
    expect(bots[0]).toHaveAttribute("aria-label", "Zachłanny: +6,0 pp (95%: +2,0 pp do +10,0 pp), istotna · 70,0% → 76,0%");
    expect(screen.getByText("Karty, które zmieniły się istotnie")).toBeInTheDocument();
    expect(within(list("Karty, które zmieniły się istotnie")).getAllByRole("listitem")).toHaveLength(1);
  });

  it("says when no item changed and when length is within chance", () => {
    const report = comparison();
    const [, usual] = report.items;
    if (usual === undefined || report.length === null) throw new Error("dane testu");
    render(
      <Results
        result={{
          kind: "Comparison",
          report: {
            ...report,
            items: [usual],
            length: { ...report.length, low: -8, high: 1, significant: false, delta: 0.5 },
          },
        }}
      />,
    );

    expect(screen.getByText("Żadne karty nie zmieniły się istotnie.")).toBeInTheDocument();
    expect(screen.getByText("+0,5 ruchu")).toBeInTheDocument();
    expect(screen.getByText("w granicach przypadku")).toBeInTheDocument();
  });

  it("lists every item in a table, also those nobody owned", () => {
    render(<Results result={{ kind: "Tournament", report: tournament() }} />);

    const table = screen.getByRole("table", { name: "Karty: tabela" });
    expect(within(table).getAllByRole("row")).toHaveLength(6);
    expect(within(table).getByText("brak danych")).toBeInTheDocument();
    expect(screen.getByText("Tabela wszystkich (5)")).toBeInTheDocument();
  });

  it("keeps every value in a table view", () => {
    render(<Results result={{ kind: "Comparison", report: comparison({ length: null, items: [] }) }} />);

    expect(screen.getAllByText("Tabela")).toHaveLength(2);
    expect(screen.queryByText("Długość partii")).not.toBeInTheDocument();
    expect(screen.getAllByRole("row").some((r) => r.textContent === "miejsce 1−4,0 pp−8,0 pp do −1,0 pptak50,0% → 46,0%")).toBe(true);
  });

  it("sums up each kind of experiment in tiles", () => {
    const tiles = (): (string | null)[] => [...document.querySelectorAll(".stat")].map((t) => t.textContent);

    const { unmount } = render(<Results result={{ kind: "Tournament", report: tournament() }} />);
    expect(tiles()).toEqual([
      "Najczęściej wygrywaZachłanny71,4% partii",
      "Remisy4,1%ukończone: 98 z 100",
      "Długość partii62,4 ruchumediana 61, 90% poniżej 80",
    ]);
    unmount();
    const ranking = render(<Results result={{ kind: "League", report: league() }} />);
    expect(tiles()).toEqual(["NajlepszyZachłannyElo 1720", "Pary340 partii na parę"]);
    ranking.unmount();
    const empty = render(<Results result={{ kind: "League", report: league({ ratings: [], pairs: [], gamesPerPair: 2 }) }} />);
    expect(tiles()).toEqual(["Najlepszy—", "Pary02 partie na parę"]);
    empty.unmount();
    render(<Results result={{ kind: "Comparison", report: comparison() }} />);
    expect(tiles()).toEqual([
      "OryginałKlejnoty98 partii",
      "WariantKlejnoty (win=13)98 partii z tych samych ziaren",
      "Długość partii−6,5 ruchuistotna zmiana",
    ]);
  });

  it("writes an Elo of exactly 1500 as ±0 and never marks Elo bars as significant", () => {
    const report = league({ ratings: [{ name: "sredni", elo: 1500, meanScore: 0.5 }] });
    render(<Results result={{ kind: "League", report }} />);

    const bar = within(list("Ranking Elo")).getByRole("listitem");
    expect(bar).toHaveAttribute("aria-label", "sredni: ±0 · Elo 1500, średni wynik 50,0%");
    expect(within(bar).queryByText("●")).toBeNull();
  });

  it("shows an unchanged game length as +0.0", () => {
    const report = comparison();
    if (report.length === null) throw new Error("dane testu");
    render(<Results result={{ kind: "Comparison", report: { ...report, length: { ...report.length, delta: 0 } } }} />);

    expect(screen.getByText("+0,0 ruchu")).toBeInTheDocument();
  });

  it("lists items that changed by the biggest change first, the fifteen biggest at most", () => {
    const changed = (id: number, taken: number, wins: number) => ({
      id,
      name: `#${String(id)}`,
      taken: d(taken, taken - 0.01, taken + 0.01),
      ownerWins: d(wins, wins - 0.01, wins + 0.01),
    });
    const three = [
      changed(1, 0.01, 0.05),
      changed(2, 0.3, -0.06),
      changed(3, 0, 0.12),
      { ...changed(4, 0.5, 0.5), ownerWins: d(0.5, -0.1, 1) },
    ];
    const { unmount } = render(<Results result={{ kind: "Comparison", report: comparison({ items: three }) }} />);

    const rows = within(list("Karty, które zmieniły się istotnie")).getAllByRole("listitem");
    expect(rows.map((r) => r.getAttribute("aria-label"))).toEqual([
      "#2: −6,0 pp (95%: −7,0 pp do −5,0 pp), istotna · udział w partiach +30,0 pp, właściciel wygrywa 50,0% → 44,0%",
      "#3: +12,0 pp (95%: +11,0 pp do +13,0 pp), istotna · udział w partiach ±0,0 pp, właściciel wygrywa 50,0% → 62,0%",
      "#1: +5,0 pp (95%: +4,0 pp do +6,0 pp), istotna · udział w partiach +1,0 pp, właściciel wygrywa 50,0% → 55,0%",
    ]);
    expect(screen.getByText("Zmiana wygranych właściciela; 3 z 4.")).toBeInTheDocument();
    unmount();

    const fifteen = Array.from({ length: 15 }, (_, i) => changed(i, 0, 0.02 + i / 100));
    const at15 = render(<Results result={{ kind: "Comparison", report: comparison({ items: fifteen }) }} />);
    expect(screen.getByText("Zmiana wygranych właściciela; 15 z 15.")).toBeInTheDocument();
    at15.unmount();
    render(<Results result={{ kind: "Comparison", report: comparison({ items: [...fifteen, changed(15, 0, 0.2)] }) }} />);
    expect(screen.getByText("Zmiana wygranych właściciela; 16 z 16, poniżej 15 największych.")).toBeInTheDocument();
    expect(within(list("Karty, które zmieniły się istotnie")).getAllByRole("listitem")).toHaveLength(15);
    expect(screen.queryByRole("listitem", { name: /^#0:/u })).toBeNull();
  });
});

describe("chart helpers", () => {
  it("picks a round tick step for the item chart", () => {
    expect([0.03, 0.06, 0.15, 0.37, 0.8, 3].map(niceStep)).toEqual([0.01, 0.02, 0.05, 0.1, 0.2, 0.5]);
  });

  it("flags a row only when the whole interval is on one side", () => {
    const row = { key: "a", label: "a", rate: 0.6, low: 0.52, high: 0.68, detail: "" };

    expect(sideOf(row, 0.5)).toBe("above");
    expect(sideOf({ ...row, low: 0.3, high: 0.45 }, 0.5)).toBe("below");
    expect(sideOf({ ...row, low: 0.5 }, 0.5)).toBeNull();
    expect(sideOf(row, undefined)).toBeNull();
  });

  it("colors league cells from loss through gray to win and picks readable ink", () => {
    expect(cellColor(0.5)).toBe("color-mix(in oklab, var(--win) 0%, var(--neutral))");
    expect(cellColor(1)).toBe("color-mix(in oklab, var(--win) 100%, var(--neutral))");
    expect(cellColor(0.2)).toBe("color-mix(in oklab, var(--loss) 60%, var(--neutral))");
    // Biel tylko na prawie pelnym niebieskim; na czerwieni zawsze ciemny tekst; blisko polowy — tekst motywu.
    expect([inkOn(0.95), inkOn(0.9), inkOn(0.75), inkOn(0.6), inkOn(0.4), inkOn(0.25), inkOn(0)]).toEqual([
      "light",
      "dark",
      "dark",
      "theme",
      "theme",
      "dark",
      "dark",
    ]);
  });
});

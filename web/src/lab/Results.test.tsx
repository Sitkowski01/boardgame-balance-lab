import { fireEvent, render, screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { comparison, fields, league, tournament } from "../test/labReports";
import { sideOf } from "./charts/IntervalBars";
import { niceStep } from "./charts/ItemScatter";
import { cellColor, inkOn } from "./charts/PairMatrix";
import { Results } from "./Results";

const list = (name: string): HTMLElement => screen.getByRole("list", { name });

describe("Results", () => {
  it("shows a tournament: tiles, seats against the fair share, bots and items", () => {
    render(<Results result={{ kind: "Tournament", report: tournament() }} />);

    expect(screen.getByText("98 z 100")).toBeInTheDocument();
    expect(screen.getByText("ziarno 7")).toBeInTheDocument();
    expect(screen.getByText("62.4 ruchu")).toBeInTheDocument();
    expect(screen.getByText("mediana 61, 90% poniżej 80")).toBeInTheDocument();
    const seats = within(list("Przewaga miejsca przy stole")).getAllByRole("listitem");
    expect(seats.map((s) => s.getAttribute("aria-label"))).toEqual([
      "miejsce 1: 56.1% (95%: 46.0%–65.0%) z 98 partii",
      "miejsce 2: 39.8% (95%: 31.0%–49.0%) z 98 partii",
    ]);
    expect(screen.getAllByText("uczciwy udział 50.0%", { exact: false })).toHaveLength(2);
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
      screen.getByText(/^#0 mocna: w 50.0% partii, właściciel wygrywa 80.0% \(\+40.0 pp wobec grupy „poziom 1”\) — za mocna$/u),
    ).toBeInTheDocument();
    fireEvent.pointerEnter(dots[3] ?? document.body);
    expect(screen.getByText(/^#3 zwykla: .*grupy „poziom 2”\)$/u)).toBeInTheDocument();
  });

  it("skips items, length and draws it does not have", () => {
    render(<Results result={{ kind: "Tournament", report: tournament({ items: [], length: null, completed: 0, draws: 0 }) }} />);

    expect(screen.queryByText(/martwe i za mocne/u)).not.toBeInTheDocument();
    expect(screen.queryByText("Długość partii")).not.toBeInTheDocument();
    expect(screen.getByText("0.0%")).toBeInTheDocument();
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

    expect(screen.getByText("greedy", { selector: ".stat-value" })).toBeInTheDocument();
    expect(screen.getByText("Elo 1720")).toBeInTheDocument();
    expect(
      within(list("Ranking Elo"))
        .getAllByRole("listitem")
        .map((r) => r.getAttribute("aria-label")),
    ).toEqual([
      "greedy: +220 · Elo 1720, średni wynik 80.0%",
      "ismcts:50: −20 · Elo 1480, średni wynik 45.0%",
      "random: −200 · Elo 1300, średni wynik 20.0%",
    ]);
    const matrix = screen.getByRole("table", { name: "Kto bije kogo" });
    expect(within(matrix).getByText("95.0%")).toHaveAttribute("title", "greedy przeciw random");
    expect(within(matrix).getByText("95.0%")).toHaveClass("ink-light");
    expect(within(matrix).getByText("5.0%")).toBeInTheDocument();
    expect(within(matrix).getAllByText("brak partii")).toHaveLength(2);
    expect(within(matrix).getAllByText("—")).toHaveLength(3);
  });

  it("shows a comparison: what changed and whether it is more than chance", () => {
    render(<Results result={{ kind: "Comparison", report: comparison() }} />);

    expect(screen.getByText("Klejnoty (win=13)")).toBeInTheDocument();
    expect(screen.getByText("−6.5 ruchu")).toBeInTheDocument();
    expect(screen.getByText("istotna zmiana")).toBeInTheDocument();
    const bots = within(list("Boty: zmiana odsetka wygranych")).getAllByRole("listitem");
    expect(bots[0]).toHaveClass("significant");
    expect(bots[1]).toHaveClass("faint");
    expect(bots[0]).toHaveAttribute("aria-label", "greedy: +6.0 pp (95%: +2.0 pp do +10.0 pp), istotna · 70.0% → 76.0%");
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
    expect(screen.getByText("+0.5 ruchu")).toBeInTheDocument();
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
    expect(screen.getAllByRole("row").some((r) => r.textContent === "miejsce 1−4.0 pp−8.0 pp do −1.0 pptak50.0% → 46.0%")).toBe(true);
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

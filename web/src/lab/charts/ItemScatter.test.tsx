import { act, fireEvent, render, screen, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { item, p, tournament } from "../../test/labReports";
import { ItemScatter } from "./ItemScatter";

afterEach(() => {
  vi.unstubAllGlobals();
});

const numbers = (text: string | null | undefined): number[] => [...(text ?? "").matchAll(/-?\d+(\.\d+)?/gu)].map((m) => Number(m[0]));

function expectClose(actual: number[], expected: number[]) {
  expect(actual).toHaveLength(expected.length);
  actual.forEach((value, i) => {
    expect(value).toBeCloseTo(expected[i] ?? NaN, 6);
  });
}

const dot = (name: string): SVGGElement => {
  const found = screen.getAllByRole("img").find((d) => d.getAttribute("aria-label")?.startsWith(`${name}:`));
  if (found === undefined) throw new Error(name);
  return found as unknown as SVGGElement;
};

/** Szerokosc 960, wysokosc 400; os pionowa od -50 do +50 pp (najdalej #0: +40 pp, krok 10 pp). */
const x = (taken: number): number => 64 + taken * 880;
const y = (value: number): number => 12 + (0.5 - value) * 344;

describe("ItemScatter", () => {
  it("puts every owned item where its share of games and its edge over the group say", () => {
    const { container } = render(<ItemScatter report={tournament()} />);

    const svg = container.querySelector("svg");
    expect(svg).toHaveAttribute("viewBox", "0 0 960 400");
    // #0 za mocna: trojkat wierzcholkiem w gore.
    expectClose(numbers(dot("#0 mocna").querySelector("path.mark")?.getAttribute("d")), [
      x(0.5),
      y(0.4) - 6,
      x(0.5) + 5.6,
      y(0.4) + 3.6,
      x(0.5) - 5.6,
      y(0.4) + 3.6,
    ]);
    // #2 za slaba (grupa 45%, wlasciciel 10%): wierzcholkiem w dol.
    expectClose(numbers(dot("#2 slaba").querySelector("path.mark")?.getAttribute("d")), [
      x(0.5),
      y(-0.35) + 6,
      x(0.5) + 5.6,
      y(-0.35) - 3.6,
      x(0.5) - 5.6,
      y(-0.35) - 3.6,
    ]);
    // Martwa i zwykla: kolko.
    const dead = dot("#1 martwa").querySelector("circle.mark");
    expectClose(
      [Number(dead?.getAttribute("cx")), Number(dead?.getAttribute("cy")), Number(dead?.getAttribute("r"))],
      [x(0.02), y(0.1), 4.5],
    );
    const hit = dot("#3 zwykla").querySelector("circle.hit");
    expectClose([Number(hit?.getAttribute("cx")), Number(hit?.getAttribute("cy")), Number(hit?.getAttribute("r"))], [x(0.5), y(-0.05), 12]);
    expect(dot("#3 zwykla").querySelector("path")).toBeNull();
    expect(dot("#0 mocna")).toHaveClass("dot strong");
    expect(dot("#1 martwa").getAttribute("class")).toBe("dot dead");
  });

  it("labels both axes with round steps and draws the zero line stronger", () => {
    const { container } = render(<ItemScatter report={tournament()} />);

    const ticks = [...container.querySelectorAll("text.tick")];
    expect(ticks.map((t) => t.textContent)).toEqual([
      "0%",
      "25%",
      "50%",
      "75%",
      "100%",
      "−50",
      "−40",
      "−30",
      "−20",
      "−10",
      "0",
      "+10",
      "+20",
      "+30",
      "+40",
      "+50",
    ]);
    expectClose(
      ticks.slice(0, 5).map((t) => Number(t.getAttribute("x"))),
      [0, 0.25, 0.5, 0.75, 1].map(x),
    );
    expect(ticks.slice(0, 5).map((t) => t.getAttribute("y"))).toEqual(Array(5).fill("372"));
    expect(ticks[5]).toHaveAttribute("x", "56");
    expectClose([Number(ticks[5]?.getAttribute("y")), Number(ticks[15]?.getAttribute("y"))], [y(-0.5) + 4, y(0.5) + 4]);
    const lines = [...container.querySelectorAll("line")];
    const vertical = lines[1];
    expectClose([Number(vertical?.getAttribute("x1")), Number(vertical?.getAttribute("x2"))], [x(0.25), x(0.25)]);
    expect([vertical?.getAttribute("y1"), vertical?.getAttribute("y2")]).toEqual(["12", "356"]);
    const axis = container.querySelectorAll("line.axis");
    expect(axis).toHaveLength(1);
    expect([axis[0]?.getAttribute("x1"), axis[0]?.getAttribute("x2"), axis[0]?.getAttribute("y1")]).toEqual(["64", "944", "184"]);
    const titles = container.querySelectorAll("text.axis-title");
    expect(titles[0]).toHaveAttribute("transform", "translate(16 184) rotate(-90)");
    expect([titles[1]?.getAttribute("x"), titles[1]?.getAttribute("y")]).toEqual(["504", "396"]);
  });

  it("keeps a small edge readable: the axis reaches at least five points", () => {
    const report = tournament({
      items: [item({ id: 0, name: "#0", ownerWins: p(21, 50) })],
      itemGroups: [{ group: "poziom 1", ownerWins: p(40, 100) }],
    });
    const { container } = render(<ItemScatter report={report} />);

    expect([...container.querySelectorAll("text.tick")].slice(5).map((t) => t.textContent)).toEqual([
      "−6",
      "−4",
      "−2",
      "0",
      "+2",
      "+4",
      "+6",
    ]);
  });

  it("on a narrow screen is taller than wide and never narrower than 280", () => {
    let report: ResizeObserverCallback = () => undefined;
    vi.stubGlobal(
      "ResizeObserver",
      class {
        constructor(callback: ResizeObserverCallback) {
          report = callback;
        }

        observe = vi.fn();

        disconnect = vi.fn();
      },
    );
    const { container } = render(<ItemScatter report={tournament()} />);
    const resize = (width: number) => {
      act(() => {
        report([{ contentRect: { width } } as ResizeObserverEntry], {} as ResizeObserver);
      });
    };

    resize(400);
    expect(container.querySelector("svg")).toHaveAttribute("viewBox", "0 0 400 340");
    resize(200);
    expect(container.querySelector("svg")).toHaveAttribute("viewBox", "0 0 280 340");
    resize(560);
    expect(container.querySelector("svg")).toHaveAttribute("viewBox", "0 0 560 400");
  });

  it("describes the pointed item below the chart and highlights it", () => {
    render(<ItemScatter report={tournament()} />);

    fireEvent.pointerEnter(dot("#2 slaba"));

    expect(dot("#2 slaba").getAttribute("class")).toBe("dot weak focused");
    expect(dot("#0 mocna").getAttribute("class")).toBe("dot strong");
    expect(screen.getByText(/^#2 slaba: w 50,0% partii/u)).toHaveClass("readout");
    expect(screen.getByRole("group", { name: "Karty: wykres punktowy" })).toBeInTheDocument();
  });

  it("lists all items in a table, with no data for an item nobody owned", () => {
    render(<ItemScatter report={tournament()} />);

    expect(screen.getByText("Tabela wszystkich (5)")).toBeInTheDocument();
    const rows = within(screen.getByRole("table", { name: "Karty: tabela" })).getAllByRole("row");
    expect(rows.map((r) => r.textContent)).toEqual([
      "KartaGrupaW partiachWłaściciel wygrywa",
      "#0 mocnapoziom 150,0%80,0% (67,0%–89,0%)",
      "#1 martwapoziom 12,0%50,0% (45,0%–55,0%)",
      "#2 slabapoziom 250,0%10,0% (4,0%–21,0%)",
      "#3 zwyklapoziom 250,0%40,0% (35,0%–45,0%)",
      "#4 niktpoziom 10,0%brak danych",
    ]);
    expect(screen.getByText("za mocna", { exact: false, selector: ".legend" })).toHaveTextContent(
      "▲ za mocna · ▼ za slaba · ○ martwa (w < 5% partii) · ● bez zastrzeżeń",
    );
  });

  it("shows at most twelve flagged items, strongest edge first within a verdict", () => {
    const many = Array.from({ length: 14 }, (_, i) => item({ id: i, name: `#${String(i)}`, ownerWins: p(21 + i, 50), verdict: "Strong" }));
    render(<ItemScatter report={tournament({ items: many })} />);

    const rows = within(screen.getByRole("table", { name: "Odstające: karty" }))
      .getAllByRole("row")
      .slice(1);
    expect(rows).toHaveLength(12);
    expect(rows.map((r) => r.firstChild?.textContent).slice(0, 3)).toEqual(["#13", "#12", "#11"]);
    expect(rows[0]).toHaveTextContent("#13▲ za mocna50,0%68,0% (63,0%–73,0%)");
    expect(within(rows[0] as HTMLElement).getByText("▲ za mocna")).toHaveClass("tag strong");
  });
});

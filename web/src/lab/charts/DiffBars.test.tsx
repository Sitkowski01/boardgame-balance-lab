import { render, screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { DiffBars, type DiffRow } from "./DiffBars";

const row = (key: string, delta: number, low: number, high: number): DiffRow => ({
  key,
  label: key,
  delta,
  low,
  high,
  significant: low > 0 || high < 0,
  detail: `dane ${key}`,
});

const rows = [
  row("w gore", 0.06, 0.02, 0.1),
  row("w dol", -0.04, -0.08, -0.01),
  row("bez zmian", 0, -0.05, 0.05),
  row("za daleko", 0.3, -0.3, 0.5),
];
const pp = (value: number): string => `${(value * 100).toFixed(0)} pp`;

function bar(label: string): HTMLElement {
  const item = screen.getAllByRole("listitem").find((i) => i.getAttribute("aria-label")?.startsWith(`${label}:`));
  if (item === undefined) throw new Error(label);
  return item;
}

const style = (element: HTMLElement, selector: string): [number, number] => {
  const found = element.querySelector<HTMLElement>(selector);
  return [parseFloat(found?.style.left ?? "NaN"), parseFloat(found?.style.width ?? "NaN")];
};

describe("DiffBars", () => {
  it("draws each bar from the middle line to its value and the whisker over its interval", () => {
    render(<DiffBars title="Boty" caption="opis" rows={rows} format={pp} reach={0.1} />);

    expect(style(bar("w gore"), ".fill")).toEqual([50, 30]);
    expect(style(bar("w gore"), ".whisker")).toEqual([60, 40]);
    expect(bar("w gore").querySelector(".fill")).toHaveClass("up");
    expect(style(bar("w dol"), ".fill")).toEqual([30, 20]);
    expect(style(bar("w dol"), ".whisker")).toEqual([10, 35]);
    expect(bar("w dol").querySelector(".fill")).toHaveClass("down");
    expect(style(bar("bez zmian"), ".fill")).toEqual([50, 0]);
    expect(bar("bez zmian").querySelector(".fill")).toHaveClass("up");
    // Poza zasiegiem osi slupek i wasy koncza sie na krawedzi.
    expect(style(bar("za daleko"), ".fill")).toEqual([50, 50]);
    expect(style(bar("za daleko"), ".whisker")).toEqual([0, 100]);
    expect(bar("w gore").querySelector<HTMLElement>(".reference")?.style.left).toBe("50%");
  });

  it("without a given reach fits the widest interval end with a margin", () => {
    render(<DiffBars title="Boty" caption="opis" rows={[row("a", 0.055, 0.022, 0.1), row("b", -0.05, -0.1, -0.01)]} format={pp} />);

    // Pol osi = 0.1 * 1.1, wiec 0.055 to cwierc szerokosci od srodka.
    const [left, width] = style(bar("a"), ".fill");
    expect(left).toBe(50);
    expect(width).toBeCloseTo(25, 9);
    expect(style(bar("a"), ".whisker")[0]).toBeCloseTo(60, 9);
  });

  it("dims bars whose interval crosses zero and marks the significant ones", () => {
    render(<DiffBars title="Boty" caption="opis" rows={rows} format={pp} reach={0.1} />);

    expect(bar("w gore")).toHaveClass("bar-row significant");
    expect(bar("bez zmian")).toHaveClass("bar-row faint");
    expect(bar("bez zmian")).not.toHaveClass("significant");
    expect(within(bar("w gore")).getByText("●")).toHaveClass("flag above");
    expect(within(bar("bez zmian")).queryByText("●")).toBeNull();
    expect(bar("w gore")).toHaveAttribute("aria-label", "w gore: 6 pp (95%: 2 pp do 10 pp), istotna · dane w gore");
    expect(bar("bez zmian")).toHaveAttribute("aria-label", "bez zmian: 0 pp (95%: -5 pp do 5 pp) · dane bez zmian");
    expect(bar("w gore").querySelector(".track")).toHaveAttribute("data-tip", "w gore: 6 pp (95%: 2 pp do 10 pp), istotna · dane w gore");
    expect(within(bar("w gore")).getByText("6 pp", { selector: ".bar-value" })).toBeInTheDocument();
    expect(screen.getByText(/95% przedział · ● istotna/u)).toHaveClass("legend");
  });

  it("repeats the rows in a table with the interval and whether it is significant", () => {
    render(<DiffBars title="Boty" caption="opis" rows={rows.slice(0, 3)} format={pp} reach={0.1} />);

    const table = screen.getByRole("table", { name: "Boty: tabela" });
    expect(
      within(table)
        .getAllByRole("row")
        .map((r) => r.textContent),
    ).toEqual([
      "CoRóżnica95% przedziałIstotnaDane",
      "w gore6 pp2 pp do 10 pptakdane w gore",
      "w dol-4 pp-8 pp do -1 pptakdane w dol",
      "bez zmian0 pp-5 pp do 5 ppniedane bez zmian",
    ]);
    expect(screen.getByRole("list", { name: "Boty" })).toBeInTheDocument();
    expect(screen.getByText("opis")).toBeInTheDocument();
  });

  it("without intervals shows plain bars: no whiskers, no legend, no interval columns", () => {
    const { container } = render(<DiffBars title="Elo" caption="opis" rows={rows.slice(0, 3)} format={pp} reach={0.1} intervals={false} />);

    expect(container.querySelector(".whisker")).toBeNull();
    expect(container.querySelector(".legend")).toBeNull();
    expect(bar("bez zmian")).toHaveClass("bar-row significant");
    expect(bar("w gore")).toHaveAttribute("aria-label", "w gore: 6 pp · dane w gore");
    expect(
      within(screen.getByRole("table", { name: "Elo: tabela" }))
        .getAllByRole("row")
        .map((r) => r.textContent)
        .slice(0, 2),
    ).toEqual(["CoWartośćDane", "w gore6 ppdane w gore"]);
  });
});

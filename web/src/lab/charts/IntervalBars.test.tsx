import { render, screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { IntervalBars, sideOf, type IntervalRow } from "./IntervalBars";

const row = (patch: Partial<IntervalRow>): IntervalRow => ({
  key: "a",
  label: "a",
  rate: 0.5,
  low: 0.45,
  high: 0.55,
  detail: "z 10",
  ...patch,
});

const rows = [
  row({ key: "up", label: "wyzej", rate: 0.62, low: 0.6, high: 0.7 }),
  row({ key: "down", label: "nizej", rate: 0.3, low: 0.2, high: 0.4 }),
  row({ key: "mid", label: "srodek", rate: 0.5, low: 0.4, high: 0.6 }),
  row({ key: "out", label: "poza", rate: 1.2, low: -0.1, high: 1.5 }),
];

function draw(extra: { reference?: { value: number; label: string } } = {}) {
  const { container } = render(<IntervalBars title="Miejsca" caption="opis" rows={rows} {...extra} />);
  const track = (label: string): HTMLElement => {
    const item = screen.getAllByRole("listitem").find((i) => i.getAttribute("aria-label")?.startsWith(`${label}:`));
    if (item === undefined) throw new Error(label);
    return item;
  };
  return { container, track };
}

describe("IntervalBars", () => {
  it("places the bar, whisker and reference along a 0-100% track, clamped at both ends", () => {
    const { track } = draw({ reference: { value: 0.5, label: "uczciwy udział" } });

    const up = track("wyzej");
    expect(up.querySelector<HTMLElement>(".fill")?.style.width).toBe("62%");
    expect(up.querySelector<HTMLElement>(".whisker")?.style.left).toBe("60%");
    expect(up.querySelector<HTMLElement>(".whisker")?.style.width).toBe("calc(10%)");
    expect(up.querySelector<HTMLElement>(".reference")?.style.left).toBe("50%");
    const out = track("poza");
    expect(out.querySelector<HTMLElement>(".fill")?.style.width).toBe("100%");
    expect(out.querySelector<HTMLElement>(".whisker")?.style.left).toBe("0%");
    expect(out.querySelector<HTMLElement>(".whisker")?.style.width).toBe("calc(100%)");
  });

  it("marks a whole interval above or below the reference with a sign and a title", () => {
    const { track } = draw({ reference: { value: 0.5, label: "uczciwy udział" } });

    const above = track("wyzej").querySelector(".flag");
    expect(above?.className).toBe("flag above");
    expect(above?.getAttribute("title")).toBe("istotnie powyżej odniesienia");
    expect(above?.textContent).toBe(" ▲");
    const below = track("nizej").querySelector(".flag");
    expect(below?.className).toBe("flag below");
    expect(below?.getAttribute("title")).toBe("istotnie poniżej odniesienia");
    expect(below?.textContent).toBe(" ▼");
    expect(track("srodek").querySelector(".flag")).toBeNull();
    expect(screen.getByText(/uczciwy udział · /u)).toHaveClass("legend");
  });

  it("without a reference draws no line, no flags and no legend", () => {
    const { container } = draw();

    expect(container.querySelector(".reference")).toBeNull();
    expect(container.querySelector(".flag")).toBeNull();
    expect(container.querySelector(".legend")).toBeNull();
  });

  it("repeats every row in a table with the interval and the data", () => {
    draw();

    const table = screen.getByRole("table", { name: "Miejsca: tabela" });
    expect(
      within(table)
        .getAllByRole("row")
        .map((r) => r.textContent),
    ).toEqual([
      "KtoOdsetek95% przedziałDane",
      "wyzej62.0%60.0%–70.0%z 10",
      "nizej30.0%20.0%–40.0%z 10",
      "srodek50.0%40.0%–60.0%z 10",
      "poza120.0%-10.0%–150.0%z 10",
    ]);
    expect(screen.getByRole("list", { name: "Miejsca" })).toBeInTheDocument();
    expect(screen.getByText("wyzej", { selector: ".bar-label" }).nextSibling).toHaveAttribute(
      "data-tip",
      "wyzej: 62.0% (95%: 60.0%–70.0%) z 10",
    );
  });

  it("does not flag an interval that only touches the reference", () => {
    expect(sideOf(row({ low: 0.4, high: 0.5 }), 0.5)).toBeNull();
    expect(sideOf(row({ low: 0.4, high: 0.49 }), 0.5)).toBe("below");
  });
});

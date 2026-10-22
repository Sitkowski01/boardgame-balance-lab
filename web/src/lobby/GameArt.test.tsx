import { render } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { GameArt } from "./GameArt";

describe("GameArt", () => {
  it("draws pieces of each game and hides the drawing from screen readers", () => {
    const art = (game: string) => render(<GameArt game={game} />).container.querySelector("svg");

    const klejnoty = art("klejnoty");
    expect(klejnoty).toHaveAttribute("aria-hidden", "true");
    expect(klejnoty).toHaveClass("game-art", "art-klejnoty");
    expect(klejnoty?.querySelectorAll("circle")).toHaveLength(6);

    const mozaika = art("mozaika");
    expect(mozaika?.querySelectorAll("rect")).toHaveLength(13);
    expect(Array.from(mozaika?.querySelectorAll("rect[opacity]") ?? []).filter((r) => r.getAttribute("opacity") === "0.3")).toHaveLength(3);

    const connect = art("connectfour");
    const discs = Array.from(connect?.querySelectorAll("circle") ?? []).map((c) => c.getAttribute("fill"));
    expect(discs).toHaveLength(20);
    expect(discs.filter((f) => f === "#d33c3c")).toHaveLength(4);
    expect(discs.filter((f) => f === "#e8c23a")).toHaveLength(4);
    expect(connect?.querySelector("circle")).toHaveAttribute("cx", "30");
    expect(connect?.querySelectorAll("circle")[19]).toHaveAttribute("cy", "62");
  });
});

describe("GameArt details", () => {
  const svg = (game: string): SVGSVGElement => {
    const found = render(<GameArt game={game} />).container.querySelector("svg");
    if (found === null) throw new Error(game);
    return found;
  };
  const attrs = (elements: Iterable<Element>, names: string[]): string[] =>
    Array.from(elements).map((e) => names.map((n) => e.getAttribute(n)).join(" "));

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("lays out the mosaic wall in rows and columns of its colors", () => {
    const wall = Array.from(svg("mozaika").querySelectorAll("rect")).slice(4);

    expect(attrs(wall, ["x", "y", "fill", "opacity"])).toEqual([
      "70 15 #1f9e94 1",
      "84 15 #d4952a 1",
      "98 15 #b3243b 0.3",
      "70 29 #4a4c50 1",
      "84 29 #1f9e94 0.3",
      "98 29 #d4952a 1",
      "70 43 #efe6cf 0.3",
      "84 43 #4a4c50 1",
      "98 43 #1f9e94 1",
    ]);
  });

  it("puts red, yellow and empty discs into the Connect Four grid", () => {
    const discs = attrs(svg("connectfour").querySelectorAll("circle"), ["cx", "cy", "fill"]);

    expect(discs.slice(0, 5)).toEqual([
      "30 17 rgb(255 255 255 / 0.9)",
      "45 17 rgb(255 255 255 / 0.9)",
      "60 17 rgb(255 255 255 / 0.9)",
      "75 17 rgb(255 255 255 / 0.9)",
      "90 17 rgb(255 255 255 / 0.9)",
    ]);
    expect(discs.slice(5, 10)).toEqual([
      "30 32 rgb(255 255 255 / 0.9)",
      "45 32 rgb(255 255 255 / 0.9)",
      "60 32 #e8c23a",
      "75 32 rgb(255 255 255 / 0.9)",
      "90 32 rgb(255 255 255 / 0.9)",
    ]);
    expect(discs.slice(10, 15)).toEqual([
      "30 47 rgb(255 255 255 / 0.9)",
      "45 47 #d33c3c",
      "60 47 #e8c23a",
      "75 47 rgb(255 255 255 / 0.9)",
      "90 47 rgb(255 255 255 / 0.9)",
    ]);
    expect(discs.slice(15)).toEqual(["30 62 #d33c3c", "45 62 #e8c23a", "60 62 #d33c3c", "75 62 #d33c3c", "90 62 #e8c23a"]);
  });

  it("gives every drawn piece its own key", () => {
    const error = vi.spyOn(console, "error").mockImplementation(() => undefined);

    for (const game of ["klejnoty", "mozaika", "connectfour"]) svg(game);

    expect(error).not.toHaveBeenCalled();
  });
});

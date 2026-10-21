import { render } from "@testing-library/react";
import { describe, expect, it } from "vitest";
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

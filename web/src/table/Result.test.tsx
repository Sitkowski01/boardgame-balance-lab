import { render, screen, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { matchId, room } from "../test/fixtures";
import { Result } from "./Result";

describe("Result", () => {
  it("names the winner, lists every score and links the replay", () => {
    render(<Result room={room({ status: "Finished", scores: [3, 7, 7], matchId })} names={["Ala", "Ola", "Ela"]} navigate={vi.fn()} />);

    const result = screen.getByRole("region", { name: "Wynik" });
    expect(result).toHaveClass("result", "panel");
    expect(result).not.toHaveClass("abandoned");
    expect(within(result).getByRole("heading", { name: "Koniec partii" })).toBeInTheDocument();
    expect(within(result).getByText("Wygrywa: Ola, Ela")).toBeInTheDocument();
    const scores = within(screen.getByRole("list", { name: "Wyniki" })).getAllByRole("listitem");
    expect(scores.map((li) => [li.textContent, li.className])).toEqual([
      ["Ala: 3", ""],
      ["Ola: 7", "best"],
      ["Ela: 7", "best"],
    ]);
    expect(within(result).getByRole("link", { name: "Obejrzyj powtórkę" })).toHaveAttribute("href", `/matches/${matchId}`);
    expect(result).toHaveTextContent("🏆");
  });

  it("says the game was stopped and shows no scores when there are none", () => {
    render(<Result room={room({ status: "Abandoned", scores: [], matchId: null })} names={[]} navigate={vi.fn()} />);

    const result = screen.getByRole("region", { name: "Wynik" });
    expect(result).toHaveClass("abandoned");
    expect(within(result).getByRole("heading", { name: "Partia przerwana" })).toBeInTheDocument();
    expect(screen.queryByRole("list", { name: "Wyniki" })).not.toBeInTheDocument();
    expect(screen.queryByRole("link")).not.toBeInTheDocument();
    expect(result).toHaveTextContent("⏸");
  });
});

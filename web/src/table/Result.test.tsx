import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
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

  it("offers a rematch only when asked, blocks a double click and shows why it failed", async () => {
    const user = userEvent.setup();
    let refuse: (error: Error) => void = () => undefined;
    const onRematch = vi.fn(() => new Promise<void>((_, reject) => (refuse = reject)));
    const finished = room({ status: "Finished", scores: [1, 0], matchId });
    const { rerender } = render(<Result room={finished} names={["Ala", "Ola"]} navigate={vi.fn()} />);
    expect(screen.queryByRole("button", { name: "Zagraj jeszcze raz" })).toBeNull();

    rerender(<Result room={finished} names={["Ala", "Ola"]} navigate={vi.fn()} onRematch={onRematch} />);
    await user.click(screen.getByRole("button", { name: "Zagraj jeszcze raz" }));
    expect(screen.getByRole("button", { name: "Zakładam stół…" })).toBeDisabled();
    expect(onRematch).toHaveBeenCalledOnce();

    refuse(new Error("An unexpected error occurred invoking 'CreateRoom' on the server. HubException: Za dużo stołów."));
    expect(await screen.findByRole("alert")).toHaveTextContent("Za dużo stołów.");
    expect(screen.getByRole("button", { name: "Zagraj jeszcze raz" })).toBeEnabled();
  });

  it("celebrates only a game that finished before the player's eyes", () => {
    vi.stubGlobal("matchMedia", () => ({ matches: false }));
    vi.spyOn(HTMLCanvasElement.prototype, "getContext").mockReturnValue({} as CanvasRenderingContext2D);
    const show = (status: "Finished" | "Abandoned", celebrate?: boolean) =>
      render(<Result room={room({ status, scores: [1, 0], matchId })} names={["Ala", "Ola"]} navigate={vi.fn()} celebrate={celebrate} />);

    const cases = [show("Finished", true), show("Finished"), show("Abandoned", true)];
    expect(cases.map((c) => c.container.querySelector("canvas.confetti") !== null)).toEqual([true, false, false]);
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
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

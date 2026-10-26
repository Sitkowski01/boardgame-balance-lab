import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { klejnoty, openingMoves, player, sheet } from "../test/boards";
import { seat } from "../test/fixtures";
import { KlejnotyBoard, type KlejnotyBoardProps } from "./KlejnotyBoard";

function board(patch: Partial<KlejnotyBoardProps> = {}) {
  const onMove = vi.fn<(move: string) => void>();
  render(<KlejnotyBoard view={klejnoty()} sheet={sheet()} legalMoves={[]} seat={0} names={["Ala", "Ola"]} onMove={onMove} {...patch} />);
  return { onMove, user: userEvent.setup() };
}

const bank = (color: string): HTMLElement => screen.getByRole("button", { name: new RegExp(`^Bank: ${color}`, "u") });

describe("KlejnotyBoard", () => {
  it("marks bots and disconnected players in their panels at a live table", () => {
    board({ seats: [seat(0, { name: "Ala", connected: false }), seat(1, { kind: "Bot", name: "greedy", connected: false })] });

    expect(within(screen.getByLabelText("Gracz Ala")).getByText("rozłączony")).toHaveClass("badge", "warn");
    expect(within(screen.getByLabelText("Gracz Ola")).getByText("bot")).toHaveClass("badge");
    expect(within(screen.getByLabelText("Gracz Ola")).queryByText("rozłączony")).not.toBeInTheDocument();
  });

  it("shows the market from the top tier, decks, patrons and players", () => {
    board({ view: klejnoty({ players: [player({ points: 4 }), player()] }) });

    const rows = within(screen.getByRole("region", { name: "Rynek" })).getAllByLabelText(/^Talia/u);
    expect(rows.map((r) => r.getAttribute("aria-label"))).toEqual(["Talia 3", "Talia 2", "Talia 1"]);
    expect(screen.getByLabelText("Talia 3")).toHaveTextContent("16 kart");
    expect(screen.getByLabelText("Karta #41")).toHaveTextContent("1");
    expect(within(screen.getByLabelText("Karta #41")).getByLabelText("rabat: niebieski")).toBeInTheDocument();
    expect(within(screen.getByLabelText("Karta #41")).getByLabelText("zielony: 3")).toBeInTheDocument();
    expect(within(screen.getByRole("region", { name: "Patroni" })).getAllByLabelText(/^Patron/u)).toHaveLength(3);
    expect(screen.queryByText(/patroni już odwiedzili/u)).not.toBeInTheDocument();
    expect(within(screen.getByLabelText("Gracz Ala")).getByLabelText("Punkty")).toHaveTextContent("4 pkt");
    expect(within(screen.getByLabelText("Gracz Ala")).getByText("ty")).toBeInTheDocument();
    expect(within(screen.getByLabelText("Gracz Ala")).getByText("na ruchu")).toBeInTheDocument();
    expect(within(screen.getByLabelText("Gracz Ola")).queryByText("ty")).not.toBeInTheDocument();
  });

  it("takes three different tokens picked in the bank", async () => {
    const { onMove, user } = board({ legalMoves: openingMoves() });

    await user.click(bank("zielony"));
    await user.click(bank("biały"));
    expect(screen.getByRole("button", { name: "Weź żetony" })).toBeDisabled();
    await user.click(bank("niebieski"));
    expect(bank("niebieski")).toHaveAttribute("aria-pressed", "true");
    await user.click(screen.getByRole("button", { name: "Weź żetony" }));

    expect(onMove).toHaveBeenCalledWith("take:WUG");
    expect(bank("niebieski")).toHaveAttribute("aria-pressed", "false");
  });

  it("takes two of a color with a second click and lets the choice be cleared", async () => {
    const { onMove, user } = board({ legalMoves: openingMoves() });

    await user.click(bank("czerwony"));
    await user.click(bank("czerwony"));
    expect(within(screen.getByLabelText("Wybrane żetony")).getAllByLabelText("czerwony")).toHaveLength(2);
    await user.click(screen.getByRole("button", { name: "Wyczyść" }));
    expect(screen.queryByRole("button", { name: "Wyczyść" })).not.toBeInTheDocument();
    await user.click(bank("czerwony"));
    await user.click(bank("czerwony"));
    await user.click(screen.getByRole("button", { name: "Weź żetony" }));

    expect(onMove).toHaveBeenCalledWith("take2:RR");
  });

  it("locks the bank when no move uses it and never offers gold", () => {
    board({ legalMoves: ["take:WUG"] });

    expect(bank("biały")).toBeEnabled();
    expect(bank("czarny")).toBeDisabled();
    expect(bank("złoto")).toBeDisabled();
  });

  it("locks an empty pile", () => {
    board({ view: klejnoty({ bank: [0, 4, 4, 4, 4, 5] }), legalMoves: openingMoves() });

    expect(bank("biały")).toBeDisabled();
  });

  it("offers buying and reserving only where legal", async () => {
    const { onMove, user } = board({ legalMoves: ["buy:41", "reserve:0", "deck:3"] });

    expect(within(screen.getByLabelText("Karta #42")).queryByRole("button")).not.toBeInTheDocument();
    await user.click(within(screen.getByLabelText("Karta #41")).getByRole("button", { name: "Kup kartę #41" }));
    await user.click(within(screen.getByLabelText("Karta #0")).getByRole("button", { name: "Rezerwuj kartę #0" }));
    // Przycisk mowi, z ktorej talii, a na ekranie zostaje krotkie "Rezerwuj w ciemno".
    const blind = within(screen.getByLabelText("Talia 3")).getByRole("button", { name: "Rezerwuj w ciemno z talii III" });
    expect(blind).toHaveTextContent(/^Rezerwuj w ciemno$/u);
    await user.click(blind);
    expect(within(screen.getByLabelText("Talia 2")).queryByRole("button")).not.toBeInTheDocument();

    expect(onMove.mock.calls.map(([m]) => m)).toEqual(["buy:41", "reserve:0", "deck:3"]);
  });

  it("shows own reserved cards, hides others' blind ones and buys from the reserve", async () => {
    const view = klejnoty({
      players: [player({ reserved: [{ card: 5, tier: 1, hidden: true }] }), player({ reserved: [{ card: null, tier: 2, hidden: true }] })],
    });
    const { onMove, user } = board({ view, legalMoves: ["buy-reserved:5"] });

    await user.click(within(screen.getByLabelText("Karta #5")).getByRole("button", { name: "Kup kartę #5" }));

    expect(onMove).toHaveBeenCalledWith("buy-reserved:5");
    expect(within(screen.getByLabelText("Gracz Ola")).getByLabelText("Zakryta karta poziomu 2")).toBeInTheDocument();
  });

  it("asks to give back a token over the limit and scrolls the request into view", async () => {
    // jsdom nie ma ukladu strony: przewijanie i preferencje ruchu podstawiamy.
    const scroll = vi.fn();
    Element.prototype.scrollIntoView = scroll;
    for (const reduced of [false, true]) {
      vi.stubGlobal("matchMedia", () => ({ matches: reduced }));
      const view = klejnoty({ players: [player({ tokens: [3, 3, 3, 2, 0, 0] }), player()] });
      const shown = render(
        <KlejnotyBoard view={view} sheet={sheet()} legalMoves={["return:W"]} seat={0} names={["Ala", "Ola"]} onMove={vi.fn()} />,
      );
      expect(scroll).toHaveBeenLastCalledWith({ block: "nearest", behavior: reduced ? "auto" : "smooth" });
      shown.unmount();
    }

    scroll.mockClear();
    const view = klejnoty({ players: [player({ tokens: [3, 3, 3, 2, 0, 0] }), player()] });
    const { onMove, user } = board({ view, legalMoves: ["return:W", "return:R"] });
    expect(scroll).toHaveBeenCalledOnce();

    const returns = screen.getByRole("group", { name: "Oddaj żeton" });
    expect(within(returns).getAllByRole("button")).toHaveLength(2);
    expect(within(screen.getByLabelText("Gracz Ala")).getByText("żetony: 11/10")).toBeInTheDocument();
    await user.click(within(returns).getByRole("button", { name: "Oddaj: czerwony" }));

    expect(onMove).toHaveBeenCalledWith("return:R");
    vi.unstubAllGlobals();
  });

  it("passes when nothing else is possible", async () => {
    const { onMove, user } = board({ legalMoves: ["pass"] });

    await user.click(screen.getByRole("button", { name: "Pasuj" }));

    expect(onMove).toHaveBeenCalledWith("pass");
    expect(screen.queryByRole("group", { name: "Oddaj żeton" })).not.toBeInTheDocument();
  });

  it("shows empty slots, the final round and bought cards and patrons", () => {
    const market = klejnoty().market.map((id, i) => (i === 11 ? null : id));
    const players = [player({ bought: [1, 2], patrons: [4], bonuses: [1, 0, 0, 0, 1] }), player()];
    board({ view: klejnoty({ market, players, finalRound: true }), seat: null, names: [] });

    expect(screen.getByLabelText("Pusty slot")).toBeInTheDocument();
    expect(screen.getByText("Ostatnia runda — ktoś ma już 15 punktów.")).toBeInTheDocument();
    const first = screen.getByLabelText("Gracz Gracz 1");
    expect(within(first).getByText("kart: 2")).toBeInTheDocument();
    expect(within(first).getByText("patroni: 1")).toBeInTheDocument();
    expect(within(first).getByLabelText("rabat biały: 1")).toBeInTheDocument();
    expect(screen.queryByText("ty")).not.toBeInTheDocument();
  });

  it("keeps the patrons place once all of them are gone", () => {
    board({ view: klejnoty({ patrons: [] }) });

    const patrons = screen.getByRole("region", { name: "Patroni" });
    expect(within(patrons).queryAllByLabelText(/^Patron/u)).toHaveLength(0);
    expect(within(patrons).getByText("Wszyscy patroni już odwiedzili graczy.")).toHaveClass("patrons-gone");
  });

  it("does not mark anyone to move after the end", () => {
    board({ view: klejnoty({ over: true, finalRound: true }) });

    expect(screen.queryByText("na ruchu")).not.toBeInTheDocument();
    expect(screen.queryByText(/Ostatnia runda/u)).not.toBeInTheDocument();
  });
});

describe("KlejnotyBoard for every reader", () => {
  it("counts the cards left in each deck in Polish", () => {
    board({ view: klejnoty({ deckSizes: [1, 2, 5] }) });

    expect(within(screen.getByLabelText("Talia 1")).getByText(/kart/u)).toHaveTextContent(/^1 karta$/u);
    expect(within(screen.getByLabelText("Talia 2")).getByText(/kart/u)).toHaveTextContent(/^2 karty$/u);
    expect(within(screen.getByLabelText("Talia 3")).getByText(/kart/u)).toHaveTextContent(/^5 kart$/u);
  });

  it("presents tokens and costs as named images", () => {
    board();

    const cost = within(screen.getByLabelText("Karta #41")).getByRole("img", { name: "zielony: 3" });
    expect(cost).toHaveClass("gem");
    expect(cost).toHaveTextContent("3");
  });

  it("keeps an empty slot and a card with the same number apart", () => {
    const error = vi.spyOn(console, "error").mockImplementation(() => undefined);
    const market = klejnoty().market.map((id, i) => (i === 0 ? 1 : i === 1 || i === 2 ? null : id));

    board({ view: klejnoty({ market }) });

    expect(screen.getByLabelText("Karta #1")).toBeInTheDocument();
    expect(screen.getAllByLabelText("Pusty slot")).toHaveLength(2);
    expect(error).not.toHaveBeenCalled();
    error.mockRestore();
  });
});

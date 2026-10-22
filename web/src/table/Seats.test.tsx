import { cleanup, render, screen, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { RoomInfo } from "../api/schemas";
import { room, seat } from "../test/fixtures";
import { Seats } from "./Seats";

function seats(info: RoomInfo, host: boolean, names: string[] = ["Ala", "Ola", "Bot"]) {
  render(<Seats room={info} names={names} host={host} onAddBot={vi.fn<(seat: number, bot: string) => Promise<void>>()} />);
  return within(screen.getByRole("list", { name: "Miejsca" })).getAllByRole("listitem");
}

describe("Seats", () => {
  it("marks the seat to move with a word and a class, and empty seats with their own class", () => {
    const items = seats(
      room({
        status: "Playing",
        toMove: 1,
        seats: [seat(0, { name: "Ala" }), seat(1, { name: "Ola" }), seat(2, { kind: "Empty", name: null })],
      }),
      false,
    );

    expect(items.map((li) => li.className)).toEqual(["seat seat-0", "seat seat-1 to-move", "seat seat-2 empty"]);
    expect(within(items[1] as HTMLElement).getByText("na ruchu")).toHaveClass("badge", "accent");
    expect(within(items[0] as HTMLElement).queryByText("na ruchu")).not.toBeInTheDocument();
    expect(screen.getAllByText("na ruchu")).toHaveLength(1);
  });

  it("draws an avatar: plus for a free seat, a cog for a bot, the first letter of a person", () => {
    const items = seats(
      room({
        seats: [
          seat(0, { name: " ola" }),
          seat(1, { kind: "Bot", name: "random" }),
          seat(2, { kind: "Empty", name: null }),
          seat(3, { name: "" }),
        ],
      }),
      false,
      [" ola", "Losowy", ""],
    );

    expect(items.map((li) => li.querySelector(".avatar")?.textContent)).toEqual(["O", "⚙", "+", "?"]);
  });

  it("offers a bot only to the host, only before the start and only on a free seat", () => {
    const free = [seat(0, { name: "Ala" }), seat(1, { kind: "Empty", name: null })];

    seats(room({ seats: free }), true);
    expect(screen.getByLabelText("Bot na miejsce 2")).toBeInTheDocument();
    expect(screen.queryByLabelText("Bot na miejsce 1")).not.toBeInTheDocument();
    cleanup();

    seats(room({ seats: free }), false);
    expect(screen.queryByRole("button", { name: "Dodaj bota" })).not.toBeInTheDocument();
    cleanup();

    seats(room({ seats: free, status: "Playing" }), true);
    expect(screen.queryByRole("button", { name: "Dodaj bota" })).not.toBeInTheDocument();
  });
});

import { describe, expect, it } from "vitest";
import { fakeConnect, last } from "../test/fakeServer";
import { room, seat } from "../test/fixtures";
import { rematch } from "./rematch";

const finished = (seats = [seat(0, { name: "Ala" }), seat(1, { kind: "Bot", name: "mcts:500" })]) =>
  room({ status: "Finished", game: "klejnoty", variant: "win=13", seats, scores: [15, 9] });

describe("rematch", () => {
  it("opens a new table with the same game, variant and bots, starts it and closes its own connection", async () => {
    const { connect, links } = fakeConnect((server) => {
      server.createRoom.mockResolvedValue({ room: "nowy", seat: 0, token: "rewanz" });
    });

    const ticket = await rematch(connect, finished(), 0);

    const server = last(links).server;
    expect(ticket).toEqual({ room: "nowy", seat: 0, token: "rewanz" });
    expect(server.createRoom).toHaveBeenCalledWith({ game: "klejnoty", seats: 2, variant: "win=13", name: "Ala" });
    expect(server.addBot.mock.calls).toEqual([[1, "mcts:500"]]);
    expect(server.start).toHaveBeenCalledOnce();
    expect(server.close).toHaveBeenCalledOnce();
  });

  it("keeps the others in order behind the player and waits for people instead of starting", async () => {
    const { connect, links } = fakeConnect();
    const seats = [
      seat(0, { kind: "Bot", name: "greedy" }),
      seat(1, { name: "Ola" }),
      seat(2, { name: "Ela" }),
      seat(3, { kind: "Bot", name: "random" }),
    ];

    await rematch(connect, finished(seats), 2);

    const server = last(links).server;
    expect(server.createRoom).toHaveBeenCalledWith(expect.objectContaining({ seats: 4, name: "Ela" }));
    expect(server.addBot.mock.calls).toEqual([
      [1, "greedy"],
      [3, "random"],
    ]);
    expect(server.start).not.toHaveBeenCalled();
  });

  it("refuses a spectator and closes the connection when the server refuses", async () => {
    const { connect, links } = fakeConnect((server) => {
      server.addBot.mockRejectedValue(new Error("HubException: Stół jest pełny."));
    });

    await expect(rematch(connect, finished(), 1)).rejects.toThrow("tylko gracz przy stole");
    expect(links).toHaveLength(0);

    await expect(rematch(connect, finished(), 0)).rejects.toThrow("Stół jest pełny.");
    expect(last(links).server.start).not.toHaveBeenCalled();
    expect(last(links).server.close).toHaveBeenCalledOnce();
  });
});

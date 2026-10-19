import { describe, expect, it } from "vitest";
import { room, seat } from "../test/fixtures";
import { freeSeats, sortRooms, statusLabel, upsertRoom } from "./lobby";

describe("lobby", () => {
  it("adds new rooms, replaces known ones and skips older messages", () => {
    const a = room({ id: "a", version: 2 });
    const list = upsertRoom([], a);

    expect(list).toEqual([a]);
    expect(upsertRoom(list, room({ id: "a", version: 1 }))).toEqual([a]);
    expect(upsertRoom(list, room({ id: "a", version: 2, moves: 5 }))[0]?.moves).toBe(5);
    expect(upsertRoom(list, room({ id: "b" })).map((r) => r.id)).toEqual(["a", "b"]);
  });

  it("puts open tables first and the newest first within a status", () => {
    const rooms = [
      room({ id: "old-wait", createdAt: "2026-10-19T09:00:00Z" }),
      room({ id: "done", status: "Finished" }),
      room({ id: "gone", status: "Abandoned" }),
      room({ id: "play", status: "Playing" }),
      room({ id: "new-wait", createdAt: "2026-10-19T11:00:00Z" }),
    ];

    expect(sortRooms(rooms).map((r) => r.id)).toEqual(["new-wait", "old-wait", "play", "done", "gone"]);
  });

  it("counts free seats and names every status", () => {
    expect(freeSeats(room())).toBe(1);
    expect(freeSeats(room({ seats: [seat(0), seat(1)] }))).toBe(0);
    expect(Object.values(statusLabel)).toEqual(["czeka na graczy", "w grze", "zakończona", "przerwana"]);
  });

  it("updates one room of many in the lobby", () => {
    const list = [room({ id: "a" }), room({ id: "b" })];

    expect(upsertRoom(list, room({ id: "b", version: 2, moves: 7 })).map((r) => [r.id, r.moves])).toEqual([
      ["a", 0],
      ["b", 7],
    ]);
  });
});

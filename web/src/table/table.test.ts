import fc from "fast-check";
import { describe, expect, it } from "vitest";
import { played, room, roomId, table as tableView } from "../test/fixtures";
import { emptyTable, myRole, RecentMoves, reduceTable, secondsLeft, winners, type Table } from "./table";

describe("reduceTable", () => {
  it("remembers the seat from the ticket", () => {
    expect(reduceTable(emptyTable, { type: "seated", seat: 2 }).seat).toBe(2);
  });

  it("takes a newer or equal room version and skips an older one", () => {
    const start = reduceTable(emptyTable, { type: "room", room: room({ version: 5 }) });

    expect(reduceTable(start, { type: "room", room: room({ version: 4, moves: 9 }) })).toBe(start);
    expect(reduceTable(start, { type: "room", room: room({ version: 5, moves: 1 }) }).room?.moves).toBe(1);
    expect(reduceTable(start, { type: "room", room: room({ version: 6, moves: 2 }) }).room?.moves).toBe(2);
  });

  it("takes a newer or equal view and skips an older one", () => {
    const start = reduceTable(emptyTable, { type: "view", view: tableView(null, { version: 3, moves: 3 }) });

    expect(reduceTable(start, { type: "view", view: tableView(null, { version: 2, moves: 2 }) })).toBe(start);
    expect(reduceTable(start, { type: "view", view: tableView(null, { version: 3, moves: 4 }) }).view?.moves).toBe(4);
  });

  it("keeps the last moves once each, in order", () => {
    let table: Table = emptyTable;
    for (let n = 1; n <= RecentMoves + 3; n++) {
      table = reduceTable(table, { type: "move", move: played(n, "3") });
    }

    const again = reduceTable(table, { type: "move", move: played(RecentMoves, "1") });

    expect(again).toBe(table);
    expect(table.moves.map((m) => m.number)).toEqual(Array.from({ length: RecentMoves }, (_, i) => i + 4));
  });

  it("ends with the newest room whatever order the messages come in", () => {
    fc.assert(
      fc.property(fc.uniqueArray(fc.integer({ min: 0, max: 1000 }), { minLength: 1 }), (versions) => {
        const final = versions.reduce<Table>((t, version) => reduceTable(t, { type: "room", room: room({ version }) }), emptyTable);

        expect(final.room?.version).toBe(Math.max(...versions));
      }),
    );
  });
});

describe("myRole", () => {
  const playing = room({ status: "Playing", toMove: 1, host: 0 });

  it("knows the host and a spectator", () => {
    expect(myRole({ ...emptyTable, seat: 0, room: room() })).toEqual({ seat: 0, host: true, myTurn: false });
    expect(myRole({ ...emptyTable, room: room() })).toEqual({ seat: null, host: false, myTurn: false });
    expect(myRole({ ...emptyTable, seat: 1, room: room() }).host).toBe(false);
  });

  it("says it is my turn only while playing, on my seat, with moves to make", () => {
    const view = tableView(null, { seat: 1, legalMoves: ["0"] });

    expect(myRole({ ...emptyTable, seat: 1, room: playing, view }).myTurn).toBe(true);
    expect(myRole({ ...emptyTable, seat: 0, room: playing, view }).myTurn).toBe(false);
    expect(myRole({ ...emptyTable, seat: 1, room: { ...playing, status: "Finished" }, view }).myTurn).toBe(false);
    expect(myRole({ ...emptyTable, seat: 1, room: playing, view: { ...view, legalMoves: [] } }).myTurn).toBe(false);
    expect(myRole({ ...emptyTable, seat: 1, room: playing }).myTurn).toBe(false);
    expect(myRole({ ...emptyTable, room: playing, view }).myTurn).toBe(false);
  });
});

describe("secondsLeft", () => {
  const now = Date.parse("2026-10-19T10:00:00Z");

  it("rounds up and never goes below zero", () => {
    expect(secondsLeft(null, now)).toBeNull();
    expect(secondsLeft("2026-10-19T10:00:30Z", now)).toBe(30);
    expect(secondsLeft("2026-10-19T10:00:00.200Z", now)).toBe(1);
    expect(secondsLeft("2026-10-19T09:59:00Z", now)).toBe(0);
  });
});

describe("winners", () => {
  it("lists every seat with the best score", () => {
    expect(winners([])).toEqual([]);
    expect(winners([0, 1, 0])).toEqual([1]);
    expect(winners([0.5, 0, 0.5])).toEqual([0, 2]);
  });
});

describe("table edges", () => {
  it("skips a repeated last move and can forget the seat", () => {
    const once = reduceTable(emptyTable, { type: "move", move: played(1, "3") });

    expect(reduceTable(once, { type: "move", move: played(1, "4") })).toBe(once);
    expect(reduceTable({ ...emptyTable, seat: 1 }, { type: "seated", seat: null }).seat).toBeNull();
  });

  it("a spectator is never on the move", () => {
    const view = { room: roomId, seat: null, version: 1, moves: 0, view: null, legalMoves: ["0"], deadline: null };

    expect(myRole({ ...emptyTable, room: room({ status: "Playing", toMove: null }), view }).myTurn).toBe(false);
  });
});

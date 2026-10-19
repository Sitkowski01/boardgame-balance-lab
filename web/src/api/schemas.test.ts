import { describe, expect, it } from "vitest";
import { room, table } from "../test/fixtures";
import { roomInfo, tableView } from "./schemas";

describe("schemas", () => {
  it("accept what the server sends", () => {
    expect(roomInfo.parse(room())).toEqual(room());
    expect(tableView.parse(table({ any: 1 }))).toEqual(table({ any: 1 }));
  });

  it("reject a room of the wrong shape", () => {
    expect(roomInfo.safeParse({ ...room(), status: "Paused" }).success).toBe(false);
    expect(tableView.safeParse({ ...table(null), legalMoves: "0" }).success).toBe(false);
  });
});

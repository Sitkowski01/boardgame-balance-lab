import { describe, expect, it } from "vitest";
import { parseRoute, routePath } from "./route";
import { roomId } from "./test/fixtures";

describe("route", () => {
  it("maps paths to pages and back", () => {
    expect(parseRoute("/")).toEqual({ page: "lobby" });
    expect(parseRoute("")).toEqual({ page: "lobby" });
    expect(parseRoute(`/rooms/${roomId}`)).toEqual({ page: "room", id: roomId });
    expect(parseRoute(`/rooms/${roomId}/`)).toEqual({ page: "room", id: roomId });
    expect(parseRoute(`/matches/${roomId}`)).toEqual({ page: "match", id: roomId });
    expect(parseRoute("/rooms/x")).toEqual({ page: "missing" });
    expect(parseRoute(`/x/rooms/${roomId}`)).toEqual({ page: "missing" });
    expect(parseRoute(`/rooms/${roomId}/x`)).toEqual({ page: "missing" });

    expect(routePath({ page: "room", id: roomId })).toBe(`/rooms/${roomId}`);
    expect(routePath({ page: "match", id: roomId })).toBe(`/matches/${roomId}`);
    expect(routePath({ page: "lobby" })).toBe("/");
    expect(routePath({ page: "missing" })).toBe("/");
  });

  it("accepts a match id in any letter case only as a whole path", () => {
    expect(parseRoute(`/matches/${roomId.toUpperCase()}`)).toEqual({ page: "match", id: roomId.toUpperCase() });
    expect(parseRoute(`/matches/${roomId}x`)).toEqual({ page: "missing" });
  });
});

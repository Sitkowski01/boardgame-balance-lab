import { describe, expect, it } from "vitest";
import { parseRoute, routePath, routeTitle } from "./route";
import { roomId } from "./test/fixtures";

describe("route", () => {
  it("names every page in the browser tab", () => {
    expect(routeTitle({ page: "lobby" })).toBe("Lobby · Balance Lab");
    expect(routeTitle({ page: "room", id: roomId })).toBe("Stół · Balance Lab");
    expect(routeTitle({ page: "match", id: roomId })).toBe("Powtórka · Balance Lab");
    expect(routeTitle({ page: "lab", experiment: null })).toBe("Laboratorium · Balance Lab");
    expect(routeTitle({ page: "missing" })).toBe("Nie ma takiej strony · Balance Lab");
  });

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

  it("opens the lab with or without an experiment", () => {
    expect(parseRoute("/lab")).toEqual({ page: "lab", experiment: null });
    expect(parseRoute("/lab/")).toEqual({ page: "lab", experiment: null });
    expect(parseRoute(`/lab/${roomId}`)).toEqual({ page: "lab", experiment: roomId });
    expect(parseRoute("/lab/x")).toEqual({ page: "missing" });
    expect(parseRoute("/labx")).toEqual({ page: "missing" });
    expect(routePath({ page: "lab", experiment: null })).toBe("/lab");
    expect(routePath({ page: "lab", experiment: roomId })).toBe(`/lab/${roomId}`);
  });

  it("accepts a match id in any letter case only as a whole path", () => {
    expect(parseRoute(`/matches/${roomId.toUpperCase()}`)).toEqual({ page: "match", id: roomId.toUpperCase() });
    expect(parseRoute(`/matches/${roomId}x`)).toEqual({ page: "missing" });
  });
});

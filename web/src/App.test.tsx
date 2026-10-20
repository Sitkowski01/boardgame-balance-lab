import { act, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { App } from "./App";
import { fakeLab } from "./test/fakeLab";
import { fakeConnect, memoryTickets } from "./test/fakeServer";
import { matchId, roomId } from "./test/fixtures";

afterEach(() => {
  window.history.pushState(null, "", "/");
  vi.unstubAllGlobals();
});

function app(path: string) {
  window.history.pushState(null, "", path);
  // Lobby i powtorka pytaja REST; tu wystarczy odpowiedz bez danych.
  vi.stubGlobal(
    "fetch",
    vi.fn(() => Promise.resolve(new Response("[]", { status: 200 }))),
  );
  const { connect } = fakeConnect();
  render(<App connect={connect} tickets={memoryTickets()} lab={fakeLab()} />);
  return userEvent.setup();
}

describe("App", () => {
  it("opens the lobby at the root", async () => {
    app("/");

    expect(await screen.findByRole("heading", { name: "Balance Lab" })).toBeInTheDocument();
  });

  it("opens a table and a replay by address", async () => {
    app(`/rooms/${roomId}`);
    expect(await screen.findByText("Wczytywanie stołu…")).toBeInTheDocument();
  });

  it("goes from the lobby to the lab and back", async () => {
    const user = app("/");

    await user.click(await screen.findByRole("link", { name: "laboratorium" }));

    expect(window.location.pathname).toBe("/lab");
    expect(await screen.findByRole("heading", { name: "Laboratorium balansu" })).toBeInTheDocument();
  });

  it("opens a replay by address", () => {
    app(`/matches/${matchId}`);
    expect(screen.getByText("Wczytywanie partii…")).toBeInTheDocument();
  });

  it("says that a page does not exist and leads back to the lobby", async () => {
    const user = app("/nie-ma");
    expect(screen.getByRole("heading", { name: "Nie ma takiej strony" })).toBeInTheDocument();

    await user.click(screen.getByRole("link", { name: "← Lobby" }));

    expect(window.location.pathname).toBe("/");
    expect(await screen.findByRole("heading", { name: "Balance Lab" })).toBeInTheDocument();
  });

  it("follows the browser back button", async () => {
    app("/");
    window.history.pushState(null, "", "/nie-ma");
    act(() => {
      window.dispatchEvent(new PopStateEvent("popstate"));
    });

    expect(await screen.findByRole("heading", { name: "Nie ma takiej strony" })).toBeInTheDocument();
  });
});

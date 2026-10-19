import { describe, expect, it } from "vitest";
import { hubMessage, isRefusal } from "./connection";

describe("hubMessage", () => {
  it("keeps only the player's message from a hub error", () => {
    expect(hubMessage(new Error("An unexpected error occurred invoking 'MakeMove' on the server. HubException: Nie twoja kolej."))).toBe(
      "Nie twoja kolej.",
    );
    expect(hubMessage(new Error("Brak sieci"))).toBe("Brak sieci");
    expect(hubMessage("tekst")).toBe("tekst");
  });
});

describe("isRefusal", () => {
  it("tells a server refusal from a network failure", () => {
    expect(isRefusal(new Error("An unexpected error occurred invoking 'Rejoin' on the server. HubException: Nieznany bilet."))).toBe(true);
    expect(isRefusal(new Error("WebSocket closed with status code: 1006"))).toBe(false);
    expect(isRefusal("HubException: tekst")).toBe(false);
  });
});

import { describe, expect, it, vi } from "vitest";
import { z } from "zod";
import { hubMessage, isRefusal, receive } from "./connection";

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

describe("receive", () => {
  const schema = z.object({ seat: z.number() });

  it("passes a message that fits the schema to the handler", () => {
    const handler = vi.fn<(value: { seat: number }) => void>();
    const invalid = vi.fn<(problem: string) => void>();

    receive("Room", schema, handler, invalid)({ seat: 2 });

    expect(handler).toHaveBeenCalledWith({ seat: 2 });
    expect(invalid).not.toHaveBeenCalled();
  });

  it("reports a message that does not fit instead of throwing inside the SignalR client", () => {
    const handler = vi.fn<(value: { seat: number }) => void>();
    const invalid = vi.fn<(problem: string) => void>();

    expect(() => {
      receive("View", schema, handler, invalid)({ seat: "dwa" });
    }).not.toThrow();

    expect(handler).not.toHaveBeenCalled();
    expect(invalid).toHaveBeenCalledWith("Serwer przysłał wiadomość View, której ta strona nie rozumie. Odśwież stronę.");
  });

  it("works without listeners", () => {
    expect(() => {
      receive("Move", schema, undefined, undefined)({ seat: 1 });
      receive("Move", schema, undefined, undefined)(null);
    }).not.toThrow();
  });
});

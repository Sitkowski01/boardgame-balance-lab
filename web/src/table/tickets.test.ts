import { describe, expect, it } from "vitest";
import { roomId } from "../test/fixtures";
import { loadName, saveName, storedTickets } from "./tickets";

/** Prosty Storage w pamieci (jsdom ma wlasny, ale tu sprawdzamy tez zepsuty). */
function memory(): Storage {
  const data = new Map<string, string>();
  return {
    get length() {
      return data.size;
    },
    clear: () => {
      data.clear();
    },
    getItem: (k) => data.get(k) ?? null,
    key: (i) => [...data.keys()][i] ?? null,
    removeItem: (k) => {
      data.delete(k);
    },
    setItem: (k, v) => {
      data.set(k, v);
    },
  };
}

const broken = (): Storage => {
  throw new Error("Brak dostepu do pamieci przegladarki.");
};

describe("storedTickets", () => {
  it("saves, finds and forgets a ticket per room", () => {
    const storage = memory();
    const tickets = storedTickets(() => storage);
    const ticket = { room: roomId, seat: 1, token: "abc" };

    expect(tickets.find(roomId)).toBeNull();
    tickets.save(ticket);
    expect(tickets.find(roomId)).toEqual(ticket);
    expect(storage.getItem(`balance-lab:ticket:${roomId}`)).toBe(JSON.stringify(ticket));
    tickets.forget(roomId);
    expect(tickets.find(roomId)).toBeNull();
  });

  it("ignores broken or foreign entries", () => {
    const storage = memory();
    const tickets = storedTickets(() => storage);
    storage.setItem(`balance-lab:ticket:${roomId}`, "{nie json");
    expect(tickets.find(roomId)).toBeNull();
    storage.setItem(`balance-lab:ticket:${roomId}`, JSON.stringify({ room: "inny", seat: 0, token: "x" }));
    expect(tickets.find(roomId)).toBeNull();
    storage.setItem(`balance-lab:ticket:${roomId}`, JSON.stringify({ room: roomId, seat: "zero" }));
    expect(tickets.find(roomId)).toBeNull();
  });

  it("keeps working when the browser refuses storage", () => {
    const tickets = storedTickets(broken);

    expect(() => {
      tickets.save({ room: roomId, seat: 0, token: "t" });
      tickets.forget(roomId);
    }).not.toThrow();
    expect(tickets.find(roomId)).toBeNull();
  });

  it("uses sessionStorage by default", () => {
    sessionStorage.clear();
    storedTickets().save({ room: roomId, seat: 0, token: "t" });

    expect(sessionStorage.getItem(`balance-lab:ticket:${roomId}`)).not.toBeNull();
  });
});

describe("player name", () => {
  it("is remembered and empty when unknown or unavailable", () => {
    const storage = memory();

    expect(loadName(() => storage)).toBe("");
    saveName("Ala", () => storage);
    expect(loadName(() => storage)).toBe("Ala");
    expect(loadName(broken)).toBe("");
    expect(() => {
      saveName("Ala", broken);
    }).not.toThrow();
  });

  it("uses localStorage by default", () => {
    localStorage.clear();
    saveName("Ola");

    expect(loadName()).toBe("Ola");
  });
});

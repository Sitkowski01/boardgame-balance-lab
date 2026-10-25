import { describe, expect, it, vi } from "vitest";
import { workerTransport, type DemoMessage, type DemoRequest, type WorkerPort } from "./transport";

/** Atrapa workera: zapamietuje prosby, a odpowiedzi i zmiany wysyla test. */
function fakeWorker() {
  const sent: DemoRequest[] = [];
  const port: WorkerPort = { postMessage: (message) => sent.push(message), onmessage: null, onerror: null };
  const deliver = (data: DemoMessage): void => {
    port.onmessage?.({ data } as MessageEvent<DemoMessage>);
  };
  const crash = (event: ErrorEvent): void => {
    port.onerror?.(event);
  };
  return { port, sent, deliver, crash };
}

describe("workerTransport", () => {
  it("numbers the requests and resolves each with its own reply", async () => {
    const { port, sent, deliver } = fakeWorker();
    const transport = workerTransport(port);

    const connect = transport.connect();
    const invoke = transport.invoke("demo-1", "MakeMove", [3, "4"]);
    const fetched = transport.fetch("GET", "/api/games", null);

    expect(sent).toEqual([
      { id: 1, op: "connect" },
      { id: 2, op: "invoke", connection: "demo-1", method: "MakeMove", args: '[3,"4"]' },
      { id: 3, op: "fetch", method: "GET", path: "/api/games", body: null },
    ]);
    deliver({ type: "reply", id: 3, result: "gry" });
    deliver({ type: "reply", id: 1, result: "demo-1" });
    deliver({ type: "error", id: 2, error: "zly ruch" });
    await expect(connect).resolves.toBe("demo-1");
    await expect(fetched).resolves.toBe("gry");
    await expect(invoke).rejects.toThrow("zly ruch");
  });

  it("ignores a reply nobody waits for", async () => {
    const { port, deliver } = fakeWorker();
    const transport = workerTransport(port);
    const connect = transport.connect();

    deliver({ type: "reply", id: 99, result: "obcy" });
    deliver({ type: "error", id: 98, error: "obcy" });
    deliver({ type: "reply", id: 1, result: "demo-1" });

    await expect(connect).resolves.toBe("demo-1");
  });

  it("hands changes only to the listeners of that connection until they stop listening", () => {
    const { port, deliver } = fakeWorker();
    const transport = workerTransport(port);
    const first = vi.fn();
    const second = vi.fn();
    const other = vi.fn();
    const stopFirst = transport.listen("demo-1", first);
    transport.listen("demo-1", second);
    transport.listen("demo-2", other);

    deliver({ type: "event", connection: "demo-1", kind: "move", json: '{"number":1}' });
    stopFirst();
    deliver({ type: "event", connection: "demo-1", kind: "view", json: "null" });

    expect(first.mock.calls).toEqual([["move", { number: 1 }]]);
    expect(second.mock.calls).toEqual([
      ["move", { number: 1 }],
      ["view", null],
    ]);
    expect(other).not.toHaveBeenCalled();
  });

  it("forgets a connection when its last listener stops", () => {
    const { port, deliver } = fakeWorker();
    const transport = workerTransport(port);
    const handler = vi.fn();
    transport.listen("demo-1", handler)();
    const again = vi.fn();
    transport.listen("demo-1", again);

    deliver({ type: "event", connection: "demo-1", kind: "room", json: "{}" });

    expect(handler).not.toHaveBeenCalled();
    expect(again).toHaveBeenCalledOnce();
  });

  it("tells when the server is ready", async () => {
    const { port, deliver } = fakeWorker();
    const transport = workerTransport(port);

    deliver({ type: "ready" });

    await expect(transport.ready).resolves.toBeUndefined();
  });

  it("tells why the server did not start", async () => {
    const { port, deliver } = fakeWorker();
    const transport = workerTransport(port);

    deliver({ type: "failed", error: "brak WebAssembly" });

    await expect(transport.ready).rejects.toThrow("brak WebAssembly");
  });

  it("fails everyone waiting, and everyone later, when the worker crashes", async () => {
    const { port, sent, crash } = fakeWorker();
    const transport = workerTransport(port);
    const waiting = transport.connect();

    crash(new ErrorEvent("error", { message: "Nie mozna wczytac modulu" }));

    await expect(waiting).rejects.toThrow("Nie mozna wczytac modulu");
    await expect(transport.ready).rejects.toThrow("Nie mozna wczytac modulu");
    await expect(transport.fetch("GET", "/api/games", null)).rejects.toThrow("Nie mozna wczytac modulu");
    expect(sent).toHaveLength(1);
  });

  it("explains a crash that came without a message", async () => {
    const { port, crash } = fakeWorker();
    const transport = workerTransport(port);

    crash(new ErrorEvent("error"));

    await expect(transport.ready).rejects.toThrow("Serwer demo w przeglądarce przestał działać.");
  });
});

import { describe, expect, it, vi } from "vitest";
import { demoFetch } from "./server";
import type { DemoTransport } from "./transport";

/** Atrapa serwera w workerze: odpowiedzi REST ustawia test. */
function fakeTransport() {
  const transport = {
    connect: vi.fn<DemoTransport["connect"]>(),
    fetch: vi.fn<DemoTransport["fetch"]>(() => Promise.resolve(JSON.stringify({ status: 200, body: [] }))),
    invoke: vi.fn<DemoTransport["invoke"]>(),
    listen: vi.fn<DemoTransport["listen"]>(),
  } satisfies DemoTransport;
  return { transport };
}

describe("demoFetch", () => {
  const network = vi.fn<typeof fetch>(() => Promise.resolve(new Response("z sieci")));

  it("answers /api requests from the worker with the status and JSON", async () => {
    const { transport } = fakeTransport();
    transport.fetch.mockResolvedValue(JSON.stringify({ status: 202, body: { id: "e1" } }));
    const fetch = demoFetch(() => transport, network);

    const response = await fetch("/api/experiments?x=1", { method: "POST", body: '{"game":"klejnoty"}' });

    expect(transport.fetch).toHaveBeenCalledWith("POST", "/api/experiments?x=1", '{"game":"klejnoty"}');
    expect(response.status).toBe(202);
    expect(response.headers.get("Content-Type")).toBe("application/json");
    expect(await response.json()).toEqual({ id: "e1" });
    expect(network).not.toHaveBeenCalled();
  });

  it("gives an empty body for a missing resource and GET by default", async () => {
    const { transport } = fakeTransport();
    transport.fetch.mockResolvedValue(JSON.stringify({ status: 404, body: null }));
    const fetch = demoFetch(() => transport, network);

    const response = await fetch(new Request(`${window.location.origin}/api/rooms/x`));

    expect(transport.fetch).toHaveBeenCalledWith("GET", "/api/rooms/x", null);
    expect(response.status).toBe(404);
    expect(await response.text()).toBe("");
  });

  it("takes the method of a Request and picks the worker by the path", async () => {
    const game = fakeTransport().transport;
    const lab = fakeTransport().transport;
    const fetch = demoFetch((path) => (path.startsWith("/api/experiments") ? lab : game), network);

    await fetch(new Request(`${window.location.origin}/api/experiments/1`, { method: "DELETE" }));
    await fetch("/api/games");

    expect(lab.fetch).toHaveBeenCalledWith("DELETE", "/api/experiments/1", null);
    expect(game.fetch).toHaveBeenCalledWith("GET", "/api/games", null);
  });

  it("reads the body of a Request and of a non-text body", async () => {
    const { transport } = fakeTransport();
    const fetch = demoFetch(() => transport, network);

    await fetch(new Request(`${window.location.origin}/api/experiments`, { method: "POST", body: '{"games":5}' }));
    await fetch("/api/experiments", { method: "POST", body: new Blob(['{"games":6}']) });

    expect(transport.fetch.mock.calls).toEqual([
      ["POST", "/api/experiments", '{"games":5}'],
      ["POST", "/api/experiments", '{"games":6}'],
    ]);
  });

  it("leaves another site's /api to the network", async () => {
    const { transport } = fakeTransport();
    const fetch = demoFetch(() => transport, network);

    await fetch("https://inny.example/api/games");

    expect(network).toHaveBeenCalledWith("https://inny.example/api/games", undefined);
    expect(transport.fetch).not.toHaveBeenCalled();
  });

  it("leaves everything outside /api to the network", async () => {
    const { transport } = fakeTransport();
    const fetch = demoFetch(() => transport, network);

    const response = await fetch("/balance-lab/favicon.svg");

    expect(await response.text()).toBe("z sieci");
    expect(network).toHaveBeenCalledWith("/balance-lab/favicon.svg", undefined);
    expect(transport.fetch).not.toHaveBeenCalled();
  });
});

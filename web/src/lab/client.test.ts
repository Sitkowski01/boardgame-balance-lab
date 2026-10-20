import { describe, expect, it, vi } from "vitest";
import { ApiError } from "../api/rest";
import { experiment } from "../test/fakeLab";
import { labClient } from "./client";

const json = (body: unknown, status = 200): Response =>
  new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });

describe("labClient", () => {
  it("sends the order as JSON and reads the accepted experiment", async () => {
    const fetcher = vi.fn<typeof fetch>().mockResolvedValue(json(experiment(), 202));
    const request = experiment().request;

    await expect(labClient(fetcher).submit(request)).resolves.toEqual(experiment());

    expect(fetcher).toHaveBeenCalledWith("/api/experiments", {
      method: "POST",
      headers: { Accept: "application/json", "Content-Type": "application/json" },
      body: JSON.stringify(request),
    });
  });

  it("asks for one, for the recent ones and cancels with DELETE", async () => {
    const fetcher = vi.fn<typeof fetch>((url) => Promise.resolve(json(url === "/api/experiments" ? [experiment()] : experiment())));
    const lab = labClient(fetcher);

    await lab.get("a/b");
    await lab.recent();
    await lab.cancel("x");

    expect(fetcher.mock.calls.map((c) => [c[0], c[1]?.method ?? "GET"])).toEqual([
      ["/api/experiments/a%2Fb", "GET"],
      ["/api/experiments", "GET"],
      ["/api/experiments/x", "DELETE"],
    ]);
    expect(fetcher.mock.calls[2]?.[1]).not.toHaveProperty("body");
  });

  it("turns a refusal into ApiError with the server's reason", async () => {
    const lab = labClient(() => Promise.resolve(json({ title: "Zle zamowienie eksperymentu", detail: "Nieznany bot 'alfa'." }, 400)));

    await expect(lab.submit(experiment().request)).rejects.toEqual(new ApiError("Nieznany bot 'alfa'.", 400));
  });

  it("uses the browser fetch by default", async () => {
    const fetcher = vi.fn<typeof fetch>().mockResolvedValue(json([]));
    vi.stubGlobal("fetch", fetcher);

    await labClient().recent();

    expect(fetcher).toHaveBeenCalledOnce();
    vi.unstubAllGlobals();
  });
});

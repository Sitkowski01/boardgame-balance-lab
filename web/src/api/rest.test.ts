import { describe, expect, it, vi } from "vitest";
import { z } from "zod";
import { api, ApiError, getJson } from "./rest";

const json = (body: unknown, status = 200): Response =>
  new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });

describe("getJson", () => {
  it("returns data checked by the schema", async () => {
    const fetcher = vi.fn<typeof fetch>().mockResolvedValue(json({ a: 1 }));

    await expect(getJson("/x", z.object({ a: z.number() }), fetcher)).resolves.toEqual({ a: 1 });
    expect(fetcher).toHaveBeenCalledWith("/x", { headers: { Accept: "application/json" } });
  });

  it("rejects data that does not match the schema", async () => {
    await expect(getJson("/x", z.object({ a: z.number() }), () => Promise.resolve(json({ a: "1" })))).rejects.toThrow();
  });

  it("turns an error answer into ApiError with the server's message", async () => {
    const detail = getJson("/x", z.unknown(), () => Promise.resolve(json({ title: "T", detail: "Zly wariant." }, 400)));
    await expect(detail).rejects.toEqual(new ApiError("Zly wariant.", 400));
    await expect(detail).rejects.toMatchObject({ status: 400, name: "ApiError" });
    await expect(getJson("/x", z.unknown(), () => Promise.resolve(json({ title: "Tytul" }, 422)))).rejects.toThrow("Tytul");
    await expect(getJson("/x", z.unknown(), () => Promise.resolve(json({}, 500)))).rejects.toThrow("Serwer odpowiedzial 500.");
    await expect(getJson("/x", z.unknown(), () => Promise.resolve(json([1], 500)))).rejects.toThrow("Serwer odpowiedzial 500.");
    await expect(getJson("/x", z.unknown(), () => Promise.resolve(new Response("", { status: 404 })))).rejects.toThrow(
      "Nie ma takiej strony.",
    );
  });
});

describe("api", () => {
  const calls = (): { fetcher: typeof fetch; urls: string[] } => {
    const urls: string[] = [];
    const fetcher: typeof fetch = (url) => {
      urls.push(url instanceof Request ? url.url : url.toString());
      return Promise.resolve(json(null));
    };
    return { fetcher, urls };
  };

  it("builds the server paths", async () => {
    const { fetcher, urls } = calls();
    const any = z.unknown();

    await api.rules("klejnoty", null, any, fetcher);
    await api.rules("klejnoty", "card3.points=4; win=9", any, fetcher);
    await Promise.allSettled([api.games(fetcher), api.room("a/b", fetcher), api.match("m", fetcher), api.frame("m", 7, fetcher)]);

    expect(urls).toEqual([
      "/api/games/klejnoty/rules",
      "/api/games/klejnoty/rules?variant=card3.points%3D4%3B%20win%3D9",
      "/api/games",
      "/api/rooms/a%2Fb",
      "/api/matches/m",
      "/api/matches/m/frames/7",
    ]);
  });
});

import { describe, expect, it } from "vitest";
import { urls } from "./base";

describe("urls", () => {
  it("leaves addresses alone when the app lives at the root", () => {
    const { toUrl, fromUrl } = urls("/");

    expect(toUrl("/rooms/1")).toBe("/rooms/1");
    expect(fromUrl("/rooms/1")).toBe("/rooms/1");
    expect(fromUrl("/")).toBe("/");
  });

  it("puts routes under the base path and takes them back", () => {
    const { toUrl, fromUrl } = urls("/balance-lab/");

    expect(toUrl("/")).toBe("/balance-lab/");
    expect(toUrl("/lab")).toBe("/balance-lab/lab");
    expect(fromUrl("/balance-lab/lab")).toBe("/lab");
    expect(fromUrl("/balance-lab/")).toBe("/");
    expect(fromUrl("/balance-lab")).toBe("/");
  });

  it("does not mistake a longer path for the base", () => {
    const { fromUrl } = urls("/balance-lab/");

    expect(fromUrl("/balance-labs/lab")).toBe("/balance-labs/lab");
    expect(fromUrl("/inne")).toBe("/inne");
  });
});

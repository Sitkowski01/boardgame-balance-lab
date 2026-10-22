import { afterEach, describe, expect, it, vi } from "vitest";
import page from "../index.html?raw";
import { applyTheme, currentTheme, loadTheme, saveTheme } from "./theme";

function memory(initial: Record<string, string> = {}): Storage {
  const data = new Map(Object.entries(initial));
  return {
    get length() {
      return data.size;
    },
    clear: () => {
      data.clear();
    },
    getItem: (key) => data.get(key) ?? null,
    key: () => null,
    removeItem: (key) => data.delete(key),
    setItem: (key, value) => data.set(key, value),
  };
}

const broken = (): Storage => {
  throw new Error("brak dostepu");
};

describe("theme", () => {
  it("remembers the chosen theme and ignores anything else in storage", () => {
    const storage = memory();
    expect(loadTheme(() => storage)).toBeNull();
    saveTheme("dark", () => storage);
    expect(storage.getItem("balance-lab:theme")).toBe("dark");
    expect(loadTheme(() => storage)).toBe("dark");
    expect(loadTheme(() => memory({ "balance-lab:theme": "light" }))).toBe("light");
    expect(loadTheme(() => memory({ "balance-lab:theme": "blue" }))).toBeNull();
  });

  it("works without access to storage", () => {
    expect(loadTheme(broken)).toBeNull();
    expect(() => {
      saveTheme("light", broken);
    }).not.toThrow();
  });

  it("follows the system until someone chooses", () => {
    expect(currentTheme(null, true)).toBe("dark");
    expect(currentTheme(null, false)).toBe("light");
    expect(currentTheme("light", true)).toBe("light");
    expect(currentTheme("dark", false)).toBe("dark");
  });

  it("puts the choice on the root element and takes it off", () => {
    const root = document.createElement("html");
    applyTheme("dark", root);
    expect(root.dataset.theme).toBe("dark");
    applyTheme(null, root);
    expect(root.hasAttribute("data-theme")).toBe(false);
  });
});

describe("theme before the first paint", () => {
  /** Skrypt z <head> w index.html: dziala, zanim React sie wczyta. */
  const early = (): void => {
    const script = /<script>([\s\S]*?)<\/script>/u.exec(page)?.[1];
    if (script === undefined) throw new Error("index.html bez skryptu motywu");
    // Posredni eval: skrypt dziala w zasiegu globalnym, tak jak w przegladarce.
    (0, eval)(script);
  };

  afterEach(() => {
    localStorage.clear();
    delete document.documentElement.dataset.theme;
    vi.restoreAllMocks();
  });

  it("puts the theme saved by the switch on the page before React starts", () => {
    saveTheme("dark");
    early();
    expect(document.documentElement.dataset.theme).toBe("dark");

    saveTheme("light");
    early();
    expect(document.documentElement.dataset.theme).toBe("light");
  });

  it("leaves the system theme alone without a valid choice", () => {
    early();
    expect(document.documentElement.hasAttribute("data-theme")).toBe(false);

    localStorage.setItem("balance-lab:theme", "blue");
    early();
    expect(document.documentElement.hasAttribute("data-theme")).toBe(false);
  });

  it("does not break the page when storage is blocked", () => {
    vi.spyOn(Storage.prototype, "getItem").mockImplementation(() => {
      throw new Error("brak dostepu");
    });

    expect(early).not.toThrow();
    expect(document.documentElement.hasAttribute("data-theme")).toBe(false);
  });
});

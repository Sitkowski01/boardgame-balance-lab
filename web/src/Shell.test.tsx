import { act, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { Repository, Shell } from "./Shell";

afterEach(() => {
  localStorage.clear();
  delete document.documentElement.dataset.theme;
});

function shell(page: "lobby" | "lab", systemDark = false) {
  const navigate = vi.fn<(path: string) => void>();
  render(
    <Shell route={page === "lab" ? { page: "lab", experiment: null } : { page: "lobby" }} navigate={navigate} systemDark={() => systemDark}>
      <main>strona</main>
    </Shell>,
  );
  return { navigate, user: userEvent.setup() };
}

describe("Shell", () => {
  it("frames the page with the brand, the main navigation and the footer", async () => {
    const { navigate, user } = shell("lobby");

    expect(screen.getByText("strona")).toBeInTheDocument();
    const nav = screen.getByRole("navigation", { name: "Główna" });
    expect(screen.getByRole("link", { name: "Graj" }).parentElement).toHaveAttribute("aria-current", "page");
    expect(screen.getByRole("link", { name: "Laboratorium" }).parentElement).not.toHaveAttribute("aria-current");
    expect(nav).toContainElement(screen.getByRole("link", { name: "Laboratorium" }));
    expect(screen.getByRole("link", { name: "Kod na GitHubie" })).toHaveAttribute("href", Repository);

    await user.click(screen.getByRole("link", { name: "Laboratorium" }));
    expect(navigate).toHaveBeenLastCalledWith("/lab");
    await user.click(screen.getByRole("link", { name: "Balance Lab" }));
    expect(navigate).toHaveBeenLastCalledWith("/");
  });

  it("marks the lab as the current section on lab pages", () => {
    shell("lab");
    expect(screen.getByRole("link", { name: "Laboratorium" }).parentElement).toHaveAttribute("aria-current", "page");
    expect(screen.getByRole("link", { name: "Graj" }).parentElement).not.toHaveAttribute("aria-current");
  });

  it("switches between light and dark and remembers the choice", async () => {
    const { user } = shell("lobby");

    await user.click(screen.getByRole("button", { name: "Włącz ciemny motyw" }));
    expect(document.documentElement.dataset.theme).toBe("dark");
    expect(localStorage.getItem("balance-lab:theme")).toBe("dark");

    await user.click(screen.getByRole("button", { name: "Włącz jasny motyw" }));
    expect(document.documentElement.dataset.theme).toBe("light");
    expect(localStorage.getItem("balance-lab:theme")).toBe("light");
  });

  it("starts from the system theme, unless one was chosen before", () => {
    shell("lobby", true);
    expect(screen.getByRole("button", { name: "Włącz jasny motyw" })).toBeInTheDocument();
  });

  it("starts from the remembered theme over the system one", () => {
    localStorage.setItem("balance-lab:theme", "light");
    shell("lobby", true);
    expect(screen.getByRole("button", { name: "Włącz ciemny motyw" })).toBeInTheDocument();
  });
});

describe("Shell and the system theme", () => {
  function query(matches: boolean) {
    const listeners = new Set<(e: MediaQueryListEvent) => void>();
    const list = {
      matches,
      addEventListener: (_: string, l: (e: MediaQueryListEvent) => void) => listeners.add(l),
      removeEventListener: (_: string, l: (e: MediaQueryListEvent) => void) => listeners.delete(l),
    } as unknown as MediaQueryList;
    const change = (dark: boolean): void => {
      for (const l of listeners) l({ matches: dark } as MediaQueryListEvent);
    };
    return { list, listeners, change };
  }

  it("follows the system until someone chooses, then stops listening", async () => {
    const system = query(false);
    const user = userEvent.setup();
    const { unmount } = render(
      <Shell route={{ page: "lobby" }} navigate={vi.fn()} systemDark={() => false} systemQuery={() => system.list}>
        <main />
      </Shell>,
    );
    expect(system.listeners.size).toBe(1);

    act(() => {
      system.change(true);
    });
    expect(screen.getByRole("button", { name: "Włącz jasny motyw" })).toBeInTheDocument();
    act(() => {
      system.change(false);
    });
    expect(screen.getByRole("button", { name: "Włącz ciemny motyw" })).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Włącz ciemny motyw" }));
    expect(system.listeners.size).toBe(0);
    act(() => {
      system.change(false);
    });
    expect(screen.getByRole("button", { name: "Włącz jasny motyw" })).toBeInTheDocument();
    unmount();
  });

  it("does not listen when a theme was chosen before or there is no media query", () => {
    const system = query(true);
    localStorage.setItem("balance-lab:theme", "light");
    render(
      <Shell route={{ page: "lobby" }} navigate={vi.fn()} systemDark={() => true} systemQuery={() => system.list}>
        <main />
      </Shell>,
    );
    expect(system.listeners.size).toBe(0);
    localStorage.clear();
    render(
      <Shell route={{ page: "lobby" }} navigate={vi.fn()} systemQuery={() => null}>
        <main />
      </Shell>,
    );
    expect(screen.getAllByRole("button", { name: /motyw/u })).toHaveLength(2);
  });

  it("removes its listener when the page goes away", () => {
    const system = query(false);
    const { unmount } = render(
      <Shell route={{ page: "lobby" }} navigate={vi.fn()} systemDark={() => false} systemQuery={() => system.list}>
        <main />
      </Shell>,
    );
    unmount();
    expect(system.listeners.size).toBe(0);
  });
});

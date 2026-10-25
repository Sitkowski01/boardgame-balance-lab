import { act, render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { DemoBanner, DemoContext } from "./DemoBanner";

function deferred() {
  let resolve: () => void = () => undefined;
  let reject: (e: Error) => void = () => undefined;
  const ready = new Promise<void>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return { ready, resolve, reject };
}

const banner = (ready: Promise<void>) =>
  render(
    <DemoContext value={{ ready }}>
      <DemoBanner />
    </DemoContext>,
  );

describe("DemoBanner", () => {
  it("is not there outside the demo", () => {
    const { container } = render(<DemoBanner />);

    expect(container).toBeEmptyDOMElement();
  });

  it("says the server is starting and then that it runs in this tab", async () => {
    const start = deferred();
    banner(start.ready);
    expect(screen.getByRole("status")).toHaveTextContent("Uruchamiam serwer gry w Twojej przeglądarce");
    expect(screen.getByRole("status")).not.toHaveTextContent(/działa w tej karcie|Nie udało się/u);

    await act(async () => {
      start.resolve();
      await start.ready;
    });

    expect(screen.getByRole("status")).toHaveTextContent("Serwer gry działa w tej karcie");
    expect(screen.getByRole("status")).not.toHaveTextContent(/Uruchamiam|Nie udało się/u);
    expect(screen.getByRole("status")).toHaveClass("demo-banner", "ready");
  });

  it("tells the visitor when the server did not start", async () => {
    const start = deferred();
    banner(start.ready);

    await act(async () => {
      start.reject(new Error("brak WebAssembly"));
      await start.ready.catch(() => undefined);
    });

    expect(screen.getByRole("status")).toHaveTextContent("Nie udało się uruchomić serwera w przeglądarce");
    expect(screen.getByRole("status")).not.toHaveTextContent(/Uruchamiam|działa w tej karcie/u);
    expect(screen.getByRole("status")).toHaveClass("failed");
  });

  it("follows a new server and ignores what happens to the old one", async () => {
    const old = deferred();
    const view = banner(old.ready);
    const next = deferred();

    view.rerender(
      <DemoContext value={{ ready: next.ready }}>
        <DemoBanner />
      </DemoContext>,
    );
    await act(async () => {
      next.resolve();
      await next.ready;
    });
    await act(async () => {
      old.reject(new Error("stary worker padl"));
      await old.ready.catch(() => undefined);
    });

    expect(screen.getByRole("status")).toHaveTextContent("Serwer gry działa w tej karcie");
  });
});

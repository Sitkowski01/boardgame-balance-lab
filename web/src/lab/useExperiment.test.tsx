import { act, renderHook } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ApiError } from "../api/rest";
import { experiment, fakeLab } from "../test/fakeLab";
import type { ExperimentInfo } from "./model";
import { PollEvery, RetryEvery, useExperiment } from "./useExperiment";

afterEach(() => {
  vi.useRealTimers();
});

/** Odpowiedz serwera, ktora test oddaje, kiedy chce. */
function later() {
  let resolve: (info: ExperimentInfo) => void = () => undefined;
  let reject: (e: unknown) => void = () => undefined;
  const promise = new Promise<ExperimentInfo>((ok, fail) => {
    resolve = ok;
    reject = fail;
  });
  return { promise, resolve, reject };
}

const flush = () =>
  act(async () => {
    await Promise.resolve();
  });

describe("useExperiment", () => {
  it("keeps the newer experiment when an answer for the old one comes late", async () => {
    const client = fakeLab();
    const [a, b] = [later(), later()];
    client.get.mockReturnValueOnce(a.promise).mockReturnValueOnce(b.promise);
    const { result, rerender } = renderHook(({ id }) => useExperiment(client, id), { initialProps: { id: "a" } });

    rerender({ id: "b" });
    b.resolve(experiment({ id: "b", status: "Done" }));
    await flush();
    a.resolve(experiment({ id: "a", status: "Done" }));
    await flush();

    expect(result.current.info?.id).toBe("b");
    expect(client.get.mock.calls.map(([id]) => id)).toEqual(["a", "b"]);
  });

  it("does not show the old experiment's progress next to an error about the new one", async () => {
    const client = fakeLab();
    client.get.mockResolvedValueOnce(experiment({ id: "a", status: "Done" })).mockRejectedValueOnce(new Error("siec"));
    const { result, rerender } = renderHook(({ id }) => useExperiment(client, id), { initialProps: { id: "a" } });
    await flush();
    expect(result.current.info?.id).toBe("a");

    rerender({ id: "b" });
    await flush();

    expect(result.current).toEqual({ info: null, error: "siec Ponawiam…" });
  });

  it("stops asking once it is gone: unmounted, unknown to the server or with a late answer", async () => {
    vi.useFakeTimers();
    const client = fakeLab(experiment({ status: "Running" }));
    const running = renderHook(() => useExperiment(client, "a"));
    await flush();
    running.unmount();
    await act(async () => {
      await vi.advanceTimersByTimeAsync(PollEvery * 3);
    });
    expect(client.get).toHaveBeenCalledTimes(1);

    const gone = fakeLab();
    gone.get.mockRejectedValue(new ApiError("Nie ma takiego eksperymentu.", 404));
    const missing = renderHook(() => useExperiment(gone, "a"));
    await flush();
    await act(async () => {
      await vi.advanceTimersByTimeAsync(RetryEvery * 3);
    });
    expect(missing.result.current.error).toBe("Nie ma takiego eksperymentu.");
    expect(gone.get).toHaveBeenCalledTimes(1);

    const slow = fakeLab();
    const answer = later();
    slow.get.mockReturnValueOnce(answer.promise);
    const left = renderHook(() => useExperiment(slow, "a"));
    left.unmount();
    answer.reject(new Error("siec"));
    await act(async () => {
      await vi.advanceTimersByTimeAsync(RetryEvery * 3);
    });
    expect(slow.get).toHaveBeenCalledTimes(1);
  });
});

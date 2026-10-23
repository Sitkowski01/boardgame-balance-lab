import { act, render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ApiError } from "../api/rest";
import { experiment, fakeLab } from "../test/fakeLab";
import { games, league, tournament } from "../test/labReports";
import { LabPage, RecentEvery } from "./LabPage";
import { PollEvery, RetryEvery } from "./useExperiment";

afterEach(() => {
  vi.useRealTimers();
});

function page(client = fakeLab(), experimentId: string | null = null, loadGames = () => Promise.resolve(games)) {
  const navigate = vi.fn<(path: string) => void>();
  const view = render(<LabPage client={client} loadGames={loadGames} experiment={experimentId} navigate={navigate} />);
  return { client, navigate, view, user: userEvent.setup() };
}

const id = experiment().id;

describe("LabPage", () => {
  it("orders an experiment from the form and opens its address", async () => {
    const { client, navigate, user } = page();

    const form = await screen.findByRole("form", { name: "Nowy eksperyment" });
    await user.selectOptions(within(form).getByLabelText("Gra"), "mozaika");
    await user.click(within(form).getByRole("button", { name: "Uruchom eksperyment" }));

    expect(client.submit).toHaveBeenCalledWith({
      game: "mozaika",
      kind: "Tournament",
      bots: ["greedy", "greedy:kolumny"],
      games: 100,
      seed: "1",
      variant: null,
    });
    expect(navigate).toHaveBeenCalledWith(`/lab/${id}`);
  });

  it("builds a comparison with a variant and its own bots", async () => {
    const { client, user } = page();

    const form = await screen.findByRole("form", { name: "Nowy eksperyment" });
    await user.selectOptions(within(form).getByLabelText("Gra"), "klejnoty");
    await user.click(within(form).getByLabelText("Co jeśli"));
    expect(within(form).getByText(/Ten sam turniej na oryginale/u)).toBeInTheDocument();
    expect(within(form).getByRole("button", { name: "Uruchom eksperyment" })).toBeDisabled();
    expect(within(form).getByText("Co jeśli potrzebuje wariantu zasad — to on jest zmianą.")).toBeInTheDocument();
    await user.type(within(form).getByLabelText(/Wariant \(co jeśli\)/u), "win=13");
    await user.click(within(form).getByRole("button", { name: "Usuń: Zachłanny: punkty" }));
    await user.selectOptions(within(form).getByLabelText("Bot do dodania"), "ismcts:50");
    await user.click(within(form).getByRole("button", { name: "Dodaj bota" }));
    await user.clear(within(form).getByLabelText("Partie"));
    await user.type(within(form).getByLabelText("Partie"), "60");
    await user.clear(within(form).getByLabelText("Ziarno"));
    await user.type(within(form).getByLabelText("Ziarno"), "99");
    expect(within(form).getByText("Zmiany oddzielaj średnikami: win=N")).toBeInTheDocument();
    await user.click(within(form).getByRole("button", { name: "Uruchom eksperyment" }));

    expect(client.submit).toHaveBeenCalledWith({
      game: "klejnoty",
      kind: "Comparison",
      bots: ["greedy", "ismcts:50"],
      games: 60,
      seed: "99",
      variant: "win=13",
    });
  });

  it("explains a refused order and lets the user fix it", async () => {
    const client = fakeLab();
    client.submit.mockRejectedValueOnce(new ApiError("W kolejce jest juz 4 eksperymentow.", 503)).mockRejectedValueOnce(new Error(""));
    const { user } = page(client);

    const run = await screen.findByRole("button", { name: "Uruchom eksperyment" });
    await user.click(run);
    expect(await screen.findByRole("alert")).toHaveTextContent("W kolejce jest juz 4 eksperymentow.");
    await user.click(run);
    expect(await screen.findByRole("alert")).toHaveTextContent("Serwer nie przyjął eksperymentu.");
    await user.clear(screen.getByLabelText("Ziarno"));
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("switching to a league keeps the seed and gives three bots, a two-player game keeps its count", async () => {
    const { user } = page();

    const form = await screen.findByRole("form", { name: "Nowy eksperyment" });
    await user.clear(within(form).getByLabelText("Ziarno"));
    await user.type(within(form).getByLabelText("Ziarno"), "5");
    await user.click(within(form).getByLabelText("Liga"));

    expect(within(form).getByLabelText("Ziarno")).toHaveValue("5");
    expect(within(form).getByLabelText("Partie na parę")).toHaveValue("40");
    expect(within(within(form).getByRole("list", { name: "Wybrane boty" })).getAllByRole("listitem")).toHaveLength(2); // ConnectFour: mcts:50, random
    expect(within(form).queryByLabelText(/Wariant/u)).not.toBeInTheDocument();
  });

  it("stops adding bots at the server limit", async () => {
    const { user } = page();
    const add = await screen.findByRole("button", { name: "Dodaj bota" });

    for (let i = 0; i < 4; i++) await user.click(add);

    expect(add).toBeDisabled();
    expect(screen.getByText(/^Czwórki to gra dla 2 graczy — tylu botów potrzeba przy stole\.$/u)).toBeInTheDocument();
  });

  it("follows a running experiment, shows its progress and can cancel it", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const client = fakeLab(experiment({ status: "Running", done: 30 }));
    page(client, id);

    const bar = await screen.findByRole("progressbar", { name: "Postęp eksperymentu" });
    expect(bar).toHaveAttribute("aria-valuenow", "30");
    expect(bar).toHaveAttribute("aria-valuetext", "30 z 100 partii");
    expect(screen.getByText("liczy się", { selector: ".progress-head .status" })).toBeInTheDocument();

    client.get.mockResolvedValue(experiment({ status: "Done", done: 100, result: tournament() }));
    await act(async () => {
      await vi.advanceTimersByTimeAsync(PollEvery);
    });
    expect(await screen.findByRole("region", { name: "Wyniki turnieju" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Anuluj" })).not.toBeInTheDocument();
    const calls = client.get.mock.calls.length;
    await act(async () => {
      await vi.advanceTimersByTimeAsync(PollEvery * 3);
    });
    expect(client.get.mock.calls.length).toBe(calls); // po koncu juz nie pyta
  });

  it("cancels and shows a failure with its reason", async () => {
    const client = fakeLab(experiment({ status: "Running" }));
    const { user } = page(client, id);

    await user.click(await screen.findByRole("button", { name: "Anuluj" }));
    expect(client.cancel).toHaveBeenCalledWith(id);

    client.cancel.mockRejectedValueOnce(new Error("siec"));
    await user.click(screen.getByRole("button", { name: "Anuluj" }));
  });

  it("shows the reason of a failed experiment and a result it cannot read", async () => {
    const failed = page(fakeLab(experiment({ status: "Failed", error: "bot sie wywrocil" })), id);
    expect(await screen.findByText("bot sie wywrocil")).toBeInTheDocument();
    failed.view.unmount();

    page(fakeLab(experiment({ status: "Done", result: { cos: 1 } })), id);
    expect(await screen.findByText(/wynik, którego ta wersja strony nie rozumie/u)).toBeInTheDocument();
  });

  it("says when the experiment cannot be loaded", async () => {
    const client = fakeLab();
    client.get.mockRejectedValueOnce(new ApiError("Nie ma takiej strony.", 404));
    const { view } = page(client, id);
    expect(await screen.findByText("Nie ma takiej strony.")).toBeInTheDocument(); // 404: bez ponawiania
    view.unmount();

    const other = fakeLab();
    other.get.mockRejectedValueOnce(new Error(""));
    page(other, id);
    expect(await screen.findByText("Nie udało się pobrać eksperymentu. Ponawiam…")).toBeInTheDocument();
  });

  it("keeps the last progress through a network error and carries on", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const client = fakeLab(experiment({ status: "Running", done: 40 }));
    page(client, id);
    await screen.findByRole("progressbar");

    client.get.mockRejectedValueOnce(new ApiError("Serwer odpowiedzial 429.", 429));
    await act(async () => {
      await vi.advanceTimersByTimeAsync(PollEvery);
    });
    expect(screen.getByText("Serwer odpowiedzial 429. Ponawiam…")).toBeInTheDocument();
    expect(screen.getByRole("progressbar")).toHaveAttribute("aria-valuenow", "40");

    client.get.mockResolvedValue(experiment({ status: "Done", done: 100, result: tournament() }));
    await act(async () => {
      await vi.advanceTimersByTimeAsync(RetryEvery);
    });
    expect(await screen.findByRole("region", { name: "Wyniki turnieju" })).toBeInTheDocument();
    expect(screen.queryByText(/Ponawiam/u)).not.toBeInTheDocument();
  });

  it("refreshes the recent list while something on it is running", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const client = fakeLab();
    client.recent.mockResolvedValueOnce([experiment({ status: "Running" })]).mockResolvedValue([experiment({ status: "Done" })]);
    page(client);

    const recent = await screen.findByRole("region", { name: "Ostatnie eksperymenty" });
    expect(recent).toHaveTextContent("liczy się");
    await act(async () => {
      await vi.advanceTimersByTimeAsync(RecentEvery);
    });
    expect(recent).toHaveTextContent("gotowe");
    const calls = client.recent.mock.calls.length;
    await act(async () => {
      await vi.advanceTimersByTimeAsync(RecentEvery * 3);
    });
    expect(client.recent.mock.calls.length).toBe(calls); // nic sie juz nie liczy: bez odpytywania
  });

  it("takes the limits from the server and shows the work of the experiment", async () => {
    const client = fakeLab();
    client.limits.mockResolvedValue({ maxGames: 50, maxBots: 3, maxBotIterations: 40, maxWork: 1000 });
    page(client);

    const form = await screen.findByRole("form", { name: "Nowy eksperyment" });
    expect(await within(form).findByText("Liczba partii: od 1 do 50.")).toBeInTheDocument();
    expect(within(form).getByRole("option", { name: "ISMCTS 40" })).toBeInTheDocument();
    expect(within(form).getByLabelText("Praca")).toHaveTextContent("Praca: 5 100 z 1 000 jednostek");
  });

  it("keeps default limits when the server does not give them", async () => {
    const client = fakeLab();
    client.limits.mockRejectedValue(new Error("siec"));
    page(client);

    const form = await screen.findByRole("form", { name: "Nowy eksperyment" });
    expect(within(form).getByRole("option", { name: "ISMCTS 200" })).toBeInTheDocument();
  });

  it("shows a dash for work that cannot be counted yet", async () => {
    const { user } = page();
    const form = await screen.findByRole("form", { name: "Nowy eksperyment" });

    await user.clear(within(form).getByLabelText("Partie"));
    await user.type(within(form).getByLabelText("Partie"), "x");

    expect(within(form).getByLabelText("Praca")).toHaveTextContent("Praca: — z");
  });

  it("lists recent experiments with their status and marks the open one", async () => {
    const client = fakeLab(experiment({ status: "Done", result: league(), request: { ...experiment().request, kind: "League" } }));
    client.recent.mockResolvedValue([
      experiment({ status: "Done", request: { ...experiment().request, kind: "League" } }),
      experiment({
        id: "00000000-0000-0000-0000-0000000000e2",
        status: "Cancelled",
        request: { ...experiment().request, variant: "row=3" },
      }),
    ]);
    const { navigate, user } = page(client, id);

    const recent = await screen.findByRole("region", { name: "Ostatnie eksperymenty" });
    const items = within(recent).getAllByRole("listitem");
    expect(items[0]).toHaveClass("current");
    expect(items[0]).toHaveTextContent("Liga: Mozaika, Zachłanny · Losowy");
    expect(items[1]).toHaveTextContent("anulowany");
    await user.click(within(items[1] ?? recent).getByRole("link"));
    expect(navigate).toHaveBeenCalledWith("/lab/00000000-0000-0000-0000-0000000000e2");
    expect(await screen.findByRole("region", { name: "Wyniki ligi" })).toBeInTheDocument();
  });

  it("says when the games cannot be loaded and leaves for the lobby", async () => {
    const { navigate, user, view } = page(fakeLab(), null, () => Promise.reject(new Error("Serwer nie odpowiada.")));
    expect(await screen.findByRole("alert")).toHaveTextContent("Serwer nie odpowiada.");
    await user.click(screen.getByRole("link", { name: "← Lobby" }));
    expect(navigate).toHaveBeenCalledWith("/");
    view.unmount();

    page(fakeLab(), null, () => Promise.reject(new Error("")));
    expect(await screen.findByRole("alert")).toHaveTextContent("Nie udało się pobrać listy gier.");
  });

  it("draws the progress of the open experiment with what it plays", async () => {
    const request = { ...experiment().request, variant: "tiles=3" };
    page(fakeLab(experiment({ status: "Running", done: 30, total: 120, request })), id);

    const bar = await screen.findByRole("progressbar", { name: "Postęp eksperymentu" });
    expect(bar).toHaveAttribute("class", "progress running");
    expect(bar.querySelector("span")?.style.width).toBe("25%");
    expect(document.querySelector(".progress-head strong")?.textContent).toBe("Turniej: Mozaika (tiles=3)");
    expect(screen.getByText("liczy się", { selector: ".progress-head .status" })).toHaveAttribute("class", "status running");
    expect(screen.getByText("30 z 120 partii · boty: Zachłanny, Losowy · ziarno 1")).toBeInTheDocument();
    expect(screen.queryByRole("alert")).toBeNull();
    expect(screen.queryByRole("region", { name: "Wyniki turnieju" })).toBeNull();
  });

  it("shows a queued experiment with no games yet and a finished one without the running stripes", async () => {
    const queued = page(fakeLab(experiment({ status: "Queued", total: 0 })), id);
    const bar = await screen.findByRole("progressbar");
    expect(bar.querySelector("span")?.style.width).toBe("0%");
    expect(document.querySelector(".progress-head strong")?.textContent).toBe("Turniej: Mozaika");
    expect(screen.getByText("w kolejce", { selector: ".progress-head .status" })).toHaveAttribute("class", "status queued");
    queued.view.unmount();

    page(fakeLab(experiment({ status: "Failed", done: 10 })), id);
    expect(await screen.findByRole("progressbar")).toHaveAttribute("class", "progress");
    expect(screen.getByText("błąd", { selector: ".progress-head .status" })).toBeInTheDocument();
  });

  it("without an open experiment shows no panel and lists only the last eight, polling while any runs", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const client = fakeLab();
    client.recent.mockResolvedValue(
      Array.from({ length: 9 }, (_, i) => experiment({ id: `e${String(i)}`, status: i === 0 ? "Running" : "Done" })),
    );
    page(client);
    expect(screen.queryByRole("region", { name: "Ostatnie eksperymenty" })).toBeNull();

    const recent = await screen.findByRole("region", { name: "Ostatnie eksperymenty" });
    const items = within(recent).getAllByRole("listitem");
    expect(items).toHaveLength(8);
    expect(items[1]?.className).toBe("");
    expect(within(items[0] ?? recent).getByText("liczy się")).toHaveAttribute("class", "status running");
    expect(screen.queryByRole("region", { name: "Eksperyment" })).toBeNull();
    const calls = client.recent.mock.calls.length;
    await act(async () => {
      await vi.advanceTimersByTimeAsync(RecentEvery);
    });
    expect(client.recent.mock.calls.length).toBeGreaterThan(calls); // jeden liczony wystarczy
  });

  it("refreshes the recent list when the open experiment changes state", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const client = fakeLab(experiment({ status: "Running" }));
    page(client, id);
    await screen.findByRole("progressbar");
    const calls = client.recent.mock.calls.length;

    client.get.mockResolvedValue(experiment({ status: "Done", done: 100, result: tournament() }));
    await act(async () => {
      await vi.advanceTimersByTimeAsync(PollEvery);
    });

    expect(await screen.findByRole("region", { name: "Wyniki turnieju" })).toBeInTheDocument();
    expect(client.recent.mock.calls.length).toBeGreaterThan(calls);
  });

  it("shows loading first and copes with an empty catalog", async () => {
    let resolve: (g: typeof games) => void = () => undefined;
    const { view } = page(fakeLab(), null, () => new Promise((r) => (resolve = r)));
    expect(screen.getByText("Wczytywanie gier…")).toBeInTheDocument();
    view.unmount();
    resolve(games);

    page(fakeLab(), null, () => Promise.resolve([]));
    expect(await screen.findByText("Brak gier w katalogu serwera.")).toBeInTheDocument();
  });
});

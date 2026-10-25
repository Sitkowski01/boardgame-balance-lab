/// <reference lib="webworker" />
import type { DemoMessage, DemoRequest } from "./transport";

/**
 * Serwer gry w Web Workerze: .NET w WebAssembly (BalanceLab.Demo.Wasm) z lobby, pokojami, botami i laboratorium.
 * Boty licza tu, wiec strona nie zacina sie, kiedy MCTS mysli. Prosby, ktore przyjda przed startem .NET,
 * czekaja w kolejce i sa obslugiwane po kolei. Workery sa dwa (nazwa "game" i "lab"): eksperyment zajmuje watek
 * az do konca, wiec laboratorium liczy w osobnym, a stol w tym czasie gra dalej.
 */
declare const self: DedicatedWorkerGlobalScope;

interface DemoExports {
  DemoInterop: {
    Start: (role: string, thinkingMs: number) => void;
    Connect: () => string;
    Invoke: (connection: string, method: string, args: string) => Promise<string>;
    Fetch: (method: string, path: string, body: string | null) => string;
  };
}

/** Namysl Ali w pokazowych partiach: ruchy widac po kolei, jak u czlowieka. */
const ShowcaseThinkingMs = 900;

const post = (message: DemoMessage): void => {
  self.postMessage(message);
};

async function boot(): Promise<DemoExports["DemoInterop"]> {
  // Runtime .NET lezy obok strony (publikacja dotnet publish), a nie w paczce Vite — stad import z adresu.
  const url = new URL(`${import.meta.env.BASE_URL}_framework/dotnet.js`, self.location.origin).href;
  const { dotnet } = (await import(/* @vite-ignore */ url)) as {
    dotnet: {
      create: () => Promise<{
        setModuleImports: (name: string, imports: Record<string, unknown>) => void;
        getAssemblyExports: (assembly: string) => Promise<DemoExports>;
        getConfig: () => { mainAssemblyName?: string };
        runMain: () => Promise<number>;
      }>;
    };
  };
  const runtime = await dotnet.create();
  runtime.setModuleImports("demo", {
    emit: (connection: string, kind: string, json: string) => {
      post({ type: "event", connection, kind, json });
    },
  });
  const exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName ?? "BalanceLab.Demo.Wasm");
  await runtime.runMain();
  exports.DemoInterop.Start(self.name === "lab" ? "lab" : "game", ShowcaseThinkingMs);
  return exports.DemoInterop;
}

const server = boot();
server.then(
  () => {
    post({ type: "ready" });
  },
  (e: unknown) => {
    post({ type: "failed", error: e instanceof Error ? e.message : String(e) });
  },
);

self.addEventListener("message", (event: MessageEvent<DemoRequest>) => {
  void handle(event.data);
});

async function handle(request: DemoRequest): Promise<void> {
  try {
    const demo = await server;
    const result =
      request.op === "connect"
        ? demo.Connect()
        : request.op === "invoke"
          ? await demo.Invoke(request.connection, request.method, request.args)
          : demo.Fetch(request.method, request.path, request.body);
    post({ type: "reply", id: request.id, result });
  } catch (e) {
    post({ type: "error", id: request.id, error: e instanceof Error ? e.message : String(e) });
  }
}

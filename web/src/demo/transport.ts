/**
 * Rozmowa z serwerem demo w Web Workerze. Prosby ida z numerem, a odpowiedz wraca z tym samym numerem;
 * zmiany z pokoi (room, view, move, lobby) przychodza same, z identyfikatorem polaczenia.
 */
export type DemoRequest =
  | { id: number; op: "connect" }
  | { id: number; op: "invoke"; connection: string; method: string; args: string }
  | { id: number; op: "fetch"; method: string; path: string; body: string | null };

export type DemoMessage =
  | { type: "ready" }
  | { type: "failed"; error: string }
  | { type: "reply"; id: number; result: string }
  | { type: "error"; id: number; error: string }
  | { type: "event"; connection: string; kind: string; json: string };

/** Serwer demo z punktu widzenia strony: wywolania (tekst JSON tam i z powrotem) i zmiany dla polaczenia. */
export interface DemoTransport {
  connect: () => Promise<string>;
  invoke: (connection: string, method: string, args: readonly unknown[]) => Promise<string>;
  fetch: (method: string, path: string, body: string | null) => Promise<string>;
  /** Zmiany dla polaczenia; zwraca odpiecie. */
  listen: (connection: string, handler: (kind: string, data: unknown) => void) => () => void;
}

/** Konec workera, ktorego uzywa transport (prawdziwy Worker albo atrapa w testach). */
export interface WorkerPort {
  postMessage: (message: DemoRequest) => void;
  onmessage: ((event: MessageEvent<DemoMessage>) => void) | null;
  /** Worker nie wczytal sie albo padl (np. przegladarka bez workerow modulowych). */
  onerror: ((event: ErrorEvent) => void) | null;
}

/**
 * Transport przez worker. Prosby wyslane przed startem .NET czekaja w kolejce workera, wiec strona moze
 * rysowac sie od razu; `ready` mowi tylko, kiedy serwer jest gotowy (albo dlaczego nie wystartowal).
 */
export function workerTransport(worker: WorkerPort): DemoTransport & { ready: Promise<void> } {
  let next = 0;
  const pending = new Map<number, { resolve: (value: string) => void; reject: (error: Error) => void }>();
  const listeners = new Map<string, Set<(kind: string, data: unknown) => void>>();
  let started: { resolve: () => void; reject: (error: Error) => void } = { resolve: () => undefined, reject: () => undefined };
  const ready = new Promise<void>((resolve, reject) => {
    started = { resolve, reject };
  });

  // Worker, ktory padl, nie odpowie juz nikomu: czekajacy dostaja blad od razu, a nie wisza bez konca.
  let broken: Error | null = null;
  worker.onerror = (event) => {
    broken = new Error(event.message !== "" ? event.message : "Serwer demo w przeglądarce przestał działać.");
    started.reject(broken);
    for (const call of pending.values()) call.reject(broken);
    pending.clear();
  };

  worker.onmessage = ({ data }) => {
    switch (data.type) {
      case "ready":
        started.resolve();
        return;
      case "failed":
        started.reject(new Error(data.error));
        return;
      case "event":
        listeners.get(data.connection)?.forEach((handler) => {
          handler(data.kind, JSON.parse(data.json));
        });
        return;
      case "reply":
      case "error": {
        const call = pending.get(data.id);
        pending.delete(data.id);
        if (data.type === "reply") call?.resolve(data.result);
        else call?.reject(new Error(data.error));
      }
    }
  };

  const request = (message: DistributiveOmit<DemoRequest, "id">): Promise<string> =>
    new Promise((resolve, reject) => {
      if (broken !== null) {
        reject(broken);
        return;
      }

      const id = ++next;
      pending.set(id, { resolve, reject });
      worker.postMessage({ ...message, id });
    });

  return {
    ready,
    connect: () => request({ op: "connect" }),
    invoke: (connection, method, args) => request({ op: "invoke", connection, method, args: JSON.stringify(args) }),
    fetch: (method, path, body) => request({ op: "fetch", method, path, body }),
    listen: (connection, handler) => {
      const set = listeners.get(connection) ?? new Set();
      set.add(handler);
      listeners.set(connection, set);
      return () => {
        set.delete(handler);
        if (set.size === 0) listeners.delete(connection);
      };
    },
  };
}

type DistributiveOmit<T, K extends keyof T> = T extends unknown ? Omit<T, K> : never;

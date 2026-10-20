import { z } from "zod";
import { getJson, sendJson } from "../api/rest";
import { labLimits, type LabLimits } from "./form";
import { experimentInfo, type ExperimentInfo, type ExperimentRequest } from "./model";

/** Laboratorium na serwerze: zamowienie, stan z postepem i wynikiem, anulowanie i lista ostatnich. */
export interface LabClient {
  submit: (request: ExperimentRequest) => Promise<ExperimentInfo>;
  get: (id: string) => Promise<ExperimentInfo>;
  cancel: (id: string) => Promise<ExperimentInfo>;
  recent: () => Promise<ExperimentInfo[]>;
  limits: () => Promise<LabLimits>;
}

const path = (id: string): string => `/api/experiments/${encodeURIComponent(id)}`;

export function labClient(fetcher: typeof fetch = (input, init) => fetch(input, init)): LabClient {
  return {
    submit: (request) => sendJson("POST", "/api/experiments", request, experimentInfo, fetcher),
    get: (id) => getJson(path(id), experimentInfo, fetcher),
    cancel: (id) => sendJson("DELETE", path(id), undefined, experimentInfo, fetcher),
    recent: () => getJson("/api/experiments", z.array(experimentInfo), fetcher),
    limits: () => getJson("/api/experiments/limits", labLimits, fetcher),
  };
}

export const API_BASE_URL =
  import.meta.env.VITE_API_BASE_URL ?? "http://127.0.0.1:8000";

export type IngredientConfig = {
  name: string;
  milliliters: number;
};

export type CocktailConfig = {
  id: string;
  name: string;
  subtitle: string;
  image: string;
  theme: string;
  isActive: boolean;
  ledR?: number | null;
  ledG?: number | null;
  ledB?: number | null;
  ingredients: IngredientConfig[];
};

export type ModeConfig = {
  name: string;
  color: string;
  ledColor: string;
  liquidAssignments: string[];
  cocktails: CocktailConfig[];
};

export type MachineConfig = {
  flowRate: {
    millisecondsPerMilliliter: number;
  };
  fillDurationMs: number;
  cleanDurationMs: number;
  liquidAssignments: string[];
  modes: ModeConfig[];
  cocktails: CocktailConfig[];
};

export type MachineStats = {
  cocktailsDispensed: number;
  cleanCycles: number;
  fillCycles: number;
  lastSeenAt: string | null;
};

export type MachineSummary = {
  id: string;
  name: string;
  variant: "mini" | "max";
  status: "online" | "offline" | "idle" | "busy";
  desiredConfigVersion: number;
  appliedConfigVersion: number;
  stats: MachineStats;
};

export type MachineConfigResponse = {
  machine: MachineSummary;
  config: MachineConfig;
  updatedAt: string;
};

async function handleResponse<T>(response: Response): Promise<T> {
  if (!response.ok) {
    const text = await response.text();
    throw new Error(text || `Request failed with status ${response.status}`);
  }

  return response.json() as Promise<T>;
}

export async function fetchMachines(): Promise<MachineSummary[]> {
  const response = await fetch(`${API_BASE_URL}/api/machines`);
  return handleResponse<MachineSummary[]>(response);
}

export async function fetchMachineConfig(
  machineId: string,
): Promise<MachineConfigResponse> {
  const response = await fetch(`${API_BASE_URL}/api/machines/${machineId}/config`);
  return handleResponse<MachineConfigResponse>(response);
}

export async function updateMachineConfig(
  machineId: string,
  config: MachineConfig,
): Promise<MachineConfigResponse> {
  const response = await fetch(`${API_BASE_URL}/api/machines/${machineId}/config`, {
    method: "PUT",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify({ config }),
  });

  return handleResponse<MachineConfigResponse>(response);
}

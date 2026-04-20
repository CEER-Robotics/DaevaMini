import { FormEvent, useEffect, useState } from "react";
import {
  MachineConfig,
  MachineSummary,
  fetchMachineConfig,
  fetchMachines,
  updateMachineConfig,
} from "./api";

function App() {
  const MACHINE_REFRESH_INTERVAL_MS = 5000;
  const [machines, setMachines] = useState<MachineSummary[]>([]);
  const [selectedMachineId, setSelectedMachineId] = useState<string>("");
  const [config, setConfig] = useState<MachineConfig | null>(null);
  const [message, setMessage] = useState<string>("Loading machines...");
  const [isSaving, setIsSaving] = useState(false);

  useEffect(() => {
    void loadMachines();

    const intervalId = window.setInterval(() => {
      void loadMachines({ silent: true });
    }, MACHINE_REFRESH_INTERVAL_MS);

    return () => window.clearInterval(intervalId);
  }, []);

  useEffect(() => {
    if (!selectedMachineId) {
      return;
    }

    void loadMachineConfig(selectedMachineId);
  }, [selectedMachineId]);

  async function loadMachines(options?: { silent?: boolean }) {
    const silent = options?.silent ?? false;

    try {
      const result = await fetchMachines();
      setMachines(result);

      if (result.length > 0) {
        setSelectedMachineId((current) =>
          current && result.some((machine) => machine.id === current)
            ? current
            : result[0].id,
        );
      }

      if (!silent) {
        if (result.length > 0) {
          setMessage("Connected to backend.");
        } else {
          setMessage("No machines yet.");
        }
      }
    } catch (error) {
      if (!silent) {
        setMessage(`Could not load machines: ${String(error)}`);
      }
    }
  }

  async function loadMachineConfig(machineId: string) {
    try {
      const result = await fetchMachineConfig(machineId);
      setConfig(result.config);
      setMessage(`Editing desired config for ${result.machine.name}.`);
    } catch (error) {
      setMessage(`Could not load machine config: ${String(error)}`);
    }
  }

  function updateField<K extends keyof MachineConfig>(field: K, value: MachineConfig[K]) {
    if (!config) {
      return;
    }

    setConfig({
      ...config,
      [field]: value,
    });
  }

  function onAssignmentsChange(value: string) {
    updateField(
      "liquidAssignments",
      value.split("\n").map((entry) => entry.trim()),
    );
  }

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!selectedMachineId || !config) {
      return;
    }

    setIsSaving(true);
    setMessage("Saving desired config...");

    try {
      const result = await updateMachineConfig(selectedMachineId, config);
      setConfig(result.config);
      setMachines((current) =>
        current.map((machine) =>
          machine.id === selectedMachineId ? result.machine : machine,
        ),
      );
      setMessage(
        `Saved. Desired config version is now ${result.machine.desiredConfigVersion}.`,
      );
    } catch (error) {
      setMessage(`Could not save config: ${String(error)}`);
    } finally {
      setIsSaving(false);
    }
  }

  return (
    <div className="page-shell">
      <aside className="sidebar">
        <div>
          <p className="eyebrow">Daeva Control</p>
          <h1>Machines</h1>
        </div>
        <div className="machine-list">
          {machines.map((machine) => (
            <button
              key={machine.id}
              className={machine.id === selectedMachineId ? "machine-card active" : "machine-card"}
              onClick={() => setSelectedMachineId(machine.id)}
              type="button"
            >
              <strong>{machine.name}</strong>
              <span>{machine.variant.toUpperCase()}</span>
              <span>Status: {machine.status}</span>
              <span>Dispensed: {machine.stats.cocktailsDispensed}</span>
            </button>
          ))}
        </div>
      </aside>

      <main className="content">
        <div className="hero">
          <div>
            <p className="eyebrow">Prototype</p>
            <h2>Remote machine settings</h2>
            <p className="status-line">{message}</p>
          </div>
        </div>

        {config ? (
          <form className="config-form" onSubmit={onSubmit}>
            <section className="card-grid">
              <label className="card">
                <span>Flow rate (ms/ml)</span>
                <input
                  type="number"
                  value={config.flowRate.millisecondsPerMilliliter}
                  onChange={(event) =>
                    updateField("flowRate", {
                      millisecondsPerMilliliter: Number(event.target.value),
                    })
                  }
                />
              </label>

              <label className="card">
                <span>Fill duration (ms)</span>
                <input
                  type="number"
                  value={config.fillDurationMs}
                  onChange={(event) =>
                    updateField("fillDurationMs", Number(event.target.value))
                  }
                />
              </label>

              <label className="card">
                <span>Clean duration (ms)</span>
                <input
                  type="number"
                  value={config.cleanDurationMs}
                  onChange={(event) =>
                    updateField("cleanDurationMs", Number(event.target.value))
                  }
                />
              </label>
            </section>

            <label className="stacked-card">
              <span>Liquid assignments</span>
              <textarea
                rows={10}
                value={config.liquidAssignments.join("\n")}
                onChange={(event) => onAssignmentsChange(event.target.value)}
              />
              <small>One slot per line. Empty lines stay empty.</small>
            </label>

            <section className="stats-row">
              <article className="stat-card">
                <span>Cocktails configured</span>
                <strong>{config.cocktails.length}</strong>
              </article>
              <article className="stat-card">
                <span>Modes configured</span>
                <strong>{config.modes.length}</strong>
              </article>
            </section>

            <button className="primary-button" disabled={isSaving} type="submit">
              {isSaving ? "Saving..." : "Save desired config"}
            </button>
          </form>
        ) : (
          <div className="empty-state">Select a machine to start editing.</div>
        )}
      </main>
    </div>
  );
}

export default App;

const byId = (id) => document.getElementById(id);

const output = {
  health: byId("health-output"),
  stats: byId("stats-output"),
  query: byId("query-output"),
  index: byId("index-output"),
  sources: byId("sources-output"),
  danger: byId("danger-output")
};

const print = (target, data) => {
  target.textContent = typeof data === "string" ? data : JSON.stringify(data, null, 2);
};

const callJson = async (path, method = "GET", payload = null) => {
  const request = {
    method,
    headers: { "Content-Type": "application/json" }
  };

  if (payload !== null) {
    request.body = JSON.stringify(payload);
  }

  const response = await fetch(path, request);
  const body = await response.json().catch(() => ({}));
  if (!response.ok) {
    throw new Error(body.error ?? `Request failed: ${response.status}`);
  }

  return body;
};

byId("btn-health").addEventListener("click", async () => {
  try {
    print(output.health, await callJson("/api/health"));
  } catch (error) {
    print(output.health, error.message);
  }
});

byId("btn-stats").addEventListener("click", async () => {
  try {
    print(output.stats, await callJson("/api/stats"));
  } catch (error) {
    print(output.stats, error.message);
  }
});

byId("btn-query").addEventListener("click", async () => {
  try {
    const text = byId("query-text").value.trim();
    const limit = Number(byId("query-limit").value || "5");
    print(output.query, await callJson("/api/query", "POST", { text, limit }));
  } catch (error) {
    print(output.query, error.message);
  }
});

byId("btn-index").addEventListener("click", async () => {
  try {
    const sourcePath = byId("index-path").value.trim();
    print(output.index, await callJson("/api/index", "POST", { sourcePath }));
  } catch (error) {
    print(output.index, error.message);
  }
});

byId("btn-sources").addEventListener("click", async () => {
  try {
    print(output.sources, await callJson("/api/sources?limit=200"));
  } catch (error) {
    print(output.sources, error.message);
  }
});

byId("btn-delete").addEventListener("click", async () => {
  try {
    const sourcePath = byId("delete-path").value.trim();
    print(output.danger, await callJson("/api/source/delete", "POST", { sourcePath }));
  } catch (error) {
    print(output.danger, error.message);
  }
});

byId("btn-purge").addEventListener("click", async () => {
  try {
    const confirmPhrase = byId("purge-confirm").value.trim();
    print(output.danger, await callJson("/api/purge", "POST", { confirmPhrase }));
  } catch (error) {
    print(output.danger, error.message);
  }
});

(async () => {
  try {
    print(output.health, await callJson("/api/health"));
    print(output.stats, await callJson("/api/stats"));
  } catch {
    // Keep startup resilient; explicit button actions provide retriable calls.
  }
})();

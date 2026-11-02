# mqtt-churn

A small .NET tool that answers one question: **when hundreds of MQTT devices drop at once,
how long until the rest of the system knows?**

Each device is a real MQTTnet client with a retained Last Will. Over the series the tool learns
to kill them (cleanly, brutally, or by cutting the cable) and to measure the time from the drop to the
retained `offline` that a subscriber sees. From part 2, a watcher draws every client live in the browser.

It is built in the open, one episode at a time. Each part of the README below matches a git tag,
a video on [Dev Skills Unlock](https://www.youtube.com/@DevSkillsUnlock), and a blog post on
[devskillsunlock.com](https://devskillsunlock.com).

| Part | Tag | What it adds |
|---|---|---|
| 1 | [`b1`](../../tree/b1) | A fleet of N clients, paced connects, a Will per client, connect latency stats |
| 2 | `b2` | A watcher streams the fleet to a live React heatmap over Server-Sent Events; clean exit vs process kill *(in progress)* |
| 3 | `b3` | Cut the cable: Last Will latency, measured inside the network *(next)* |

## Run it

```powershell
podman compose up -d          # or: docker compose up -d   (Mosquitto 2 on :1883)
dotnet run --project src/MqttChurn -c Release -- --clients 1000 --rate 200 --hold 10
dotnet test
```

| Option | Default | Meaning |
|---|---|---|
| `--broker` | `127.0.0.1` | Broker host |
| `--port` | `1883` | Broker port |
| `--clients` | `100` | How many clients to connect |
| `--rate` | `50` | New connections per second (client *i* starts at *i / rate* s) |
| `--keepalive` | `5` | Keep-alive in seconds (drives Will timing in part 2) |
| `--hold` | `10` | Seconds to stay connected before the clean exit |
| `--run-id` | random | Namespace for client ids and topics |
| `--csv` | none | Per-client rows: `client,start_ms,connect_ms,error` |

Topics: `churn/{runId}/{i}/status`, `online` retained on connect, Will `offline` retained.
On exit, every client clears its retained topic (empty retained payload), then sends a normal
DISCONNECT, so no Will fires and the broker is left clean.

## Part 1 — Simulate 1000 MQTT clients in C#

**What changed:** `Options` (hand-rolled flags), `Fleet` (paced connects, Will per client,
retained `online`, clean exit), `Stats` (nearest-rank p50/p95/p99, text histogram, CSV).

**The numbers** (Mosquitto 2.1.2 in Podman 5.8 on Windows 11, MQTTnet 5.2, .NET 10,
Core Ultra 7 265H; full output in [`runs/b1-output.txt`](runs/b1-output.txt)).
"connect+online" is CONNECT plus the QoS 1 publish of the retained `online`.

| Run | Connected | p50 | p95 | p99 |
|---|---|---|---|---|
| 100 @ 50/s, `--broker localhost` | 100/100 | **2097 ms** | 2107 ms | 2150 ms |
| 100 @ 50/s, `127.0.0.1` | 100/100 | 45.8 ms | 50.1 ms | 65.5 ms |
| 1000 @ 200/s, `127.0.0.1` | 1000/1000 | 47.3 ms | 104.5 ms | 226.2 ms |
| 3000 @ 500/s, `127.0.0.1` | **1989/3000** | 50.5 ms | 64.1 ms | 85.3 ms |
| 1000 @ 200/s, inside the compose network | 1000/1000 | 0.9 ms | 3.3 ms | 36.8 ms |
| 5000 @ 500/s, inside the compose network | 5000/5000 | 0.7 ms | 1.9 ms | 27.2 ms |

**What the numbers actually measured (three lessons before any broker comparison):**

1. **`localhost` cost 2 seconds per client.** On Windows `localhost` resolves to `::1` first;
   nothing answers the container port there, and Windows retries a refused connect for ~2 s
   before falling back to IPv4. That is why the default is `127.0.0.1`.
2. **The ~45 ms floor is the harness, not Mosquitto.** CONNECT alone takes ~2–3 ms from Windows;
   the QoS 1 PUBACK round trip takes ~41 ms through Podman's Windows port forwarder.
   The same binary inside the compose network gets the PUBACK in ~0.2 ms.
   Client `NoDelay` is already on and Mosquitto's `set_tcp_nodelay` changed nothing.
3. **The ~2000-connection ceiling is the forwarder too.** From Windows, exactly 1989 clients
   connect and the rest get `Connection closed` with nothing in the broker log
   (its `nofile` limit is 65536). Inside the network, 5000/5000 connect.

So, for real broker numbers, run the fleet next to the broker:

```powershell
dotnet publish src/MqttChurn -c Release -r linux-x64 --self-contained false -o out
podman run --rm --network mqtt-churn_default -v ${PWD}/out:/app -w /app `
  mcr.microsoft.com/dotnet/sdk:10.0 dotnet MqttChurn.dll --broker mosquitto --clients 5000 --rate 500
```

Also: the first run after a cold start can be ~10× slower (JIT, broker warm-up).
Warm repeats of 1000 @ 1000/s gave p50 51.6–55.9 ms. Throw the first run away.

**Next (part 2):** watch the fleet live in the browser (C# Server-Sent Events + React), then kill it.

## Part 2 — Server-Sent Events in C# + React *(in progress)*

**What changed:**

- `src/MqttChurn.Watch` is one more MQTT subscriber (`churn/{runId}/+/status`). It keeps one char
  per client and streams a snapshot every 100 ms with `TypedResults.ServerSentEvents`
  (`event: frame`, ~5 KB of JSON at 5000 clients). Snapshots, not events, so a late or reconnecting
  browser is right from its first frame. It is its own process: killing the fleet can't kill the observer.
- `web/` is Vite + React + TypeScript: `useFrames` (one `EventSource`, closed in the effect cleanup)
  and `<Heatmap>` (one canvas, not 5000 elements). `npm run build` writes into the watcher's
  `wwwroot`, so the page and `/frames` share an origin; `npm run dev` proxies `/frames` to `:5080`.
- `mosquitto/mosquitto.conf` sets `max_queued_messages` (see the lesson below).

Cells: dark = never seen, green = `online`, red = `offline` (the Will fired),
grey = gone (the clean exit cleared its retained status).

**Run it** (Node.js `^20.19` or `>=22.12` builds the UI). For a quick look from the host:

```powershell
npm --prefix web ci; npm --prefix web run build      # React UI -> src/MqttChurn.Watch/wwwroot
dotnet run --project src/MqttChurn.Watch -- --run-id demo --clients 1000      # terminal 1: http://localhost:5080
dotnet run --project src/MqttChurn -c Release -- --clients 1000 --rate 200 --hold 30 --run-id demo   # terminal 2
```

For 5000 clients, run everything next to the broker (Podman's port forwarder stops near 2000
connections, see part 1), then open http://localhost:5080 (`?cols=50` draws a tall grid):

```powershell
dotnet publish src/MqttChurn.Watch -c Release -r linux-x64 --self-contained false -o out/watch
dotnet publish src/MqttChurn -c Release -r linux-x64 --self-contained false -o out/fleet
podman run -d --name churn-watch --network mqtt-churn_default -p 5080:8080 -v ${PWD}/out/watch:/app -w /app `
  mcr.microsoft.com/dotnet/aspnet:10.0 dotnet MqttChurn.Watch.dll --broker mosquitto --run-id demo --clients 5000
podman run -d --name churn-fleet --network mqtt-churn_default -v ${PWD}/out/fleet:/app -w /app `
  mcr.microsoft.com/dotnet/aspnet:10.0 dotnet MqttChurn.dll --broker mosquitto --clients 5000 --rate 500 --hold 600 --run-id demo
podman kill churn-fleet    # process kill: every Will fires. A short --hold ends with the clean exit instead.
```

| Watcher option | Default | Meaning |
|---|---|---|
| `--broker` / `--port` | `127.0.0.1` / `1883` | Broker |
| `--run-id` | `demo` | Must match the fleet's `--run-id` |
| `--clients` | `1000` | Grid size |
| `--urls` | `:5080` with `dotnet run`, `:8080` in the container | ASP.NET Core listen address |

Statuses are retained, so a watcher started late shows the current state at once. Reuse a
`--run-id` and the grid starts with the previous run's last colors.

**The numbers so far** (5000 clients, fleet and watcher inside the compose network, Mosquitto 2.1.2,
one frame per 100 ms):

| What the watcher saw | Default Mosquitto | `max_queued_messages 100000` |
|---|---|---|
| Clean exit: 5000 retained statuses cleared | **1,279** | 5,000 within 100 ms |
| `podman kill`: 5000 Wills | **1,194** | 5,000 within 200 ms |
| A fresh watcher loading 5000 retained `offline` | **1,040** | 5,000 in the first frame |

**Lesson: the broker dropped three out of four deaths, silently.** Mosquitto keeps at most 20 QoS 1
messages in flight per subscriber (`max_inflight_messages`) and queues 1000 more
(`max_queued_messages`); past that it drops. A mass death sends one Will per client to every watcher
at once, and the only trace is one broker log line: `Outgoing messages are being dropped for client …`.
Size the queue above your fleet.

A watcher on the Windows host saw the same kill wave (≤200 ms) as one inside the network, so the
browser loop is fine for watching. Latency numbers (part 3) still come from inside the network.

**Next (part 3):** cut the cable instead. No FIN, so no Will until the keep-alive expires.
How long does the grid stay green?

## Prior art

- [emqtt-bench](https://github.com/emqx/emqtt-bench) — fast Erlang `conn`/`sub`/`pub` benchmarks; no Last Will option.
- [NBomber.MQTT](https://nbomber.com/docs/protocols/mqtt/) — generic load-test steps over MQTTnet (experimental plugin).
- [HiveMQ Swarm](https://docs.hivemq.com/hivemq-swarm/latest/) — commercial scenario runner; the trial is limited to 25 clients.

None of them reports *Will-to-offline latency as seen by a subscriber*, which is the gap this tool fills.

## License

MIT

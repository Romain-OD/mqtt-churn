# mqtt-churn

A small .NET tool that answers one question: **when hundreds of MQTT devices drop at once,
how long until the rest of the system knows?**

Each device is a real MQTTnet client with a retained Last Will. Over the series the tool learns
to kill them (cleanly, brutally, or by cutting the cable) and to measure the time from the drop to the
retained `offline` that a subscriber sees.

It is built in the open, one episode at a time. Each part of the README below matches a git tag,
a video on [Dev Skills Unlock](https://www.youtube.com/@DevSkillsUnlock), and a blog post on
[devskillsunlock.com](https://devskillsunlock.com).

| Part | Tag | What it adds |
|---|---|---|
| 1 | [`b1`](../../tree/b1) | A fleet of N clients, paced connects, a Will per client, connect latency stats |
| 2 | `b2` | Kill modes and Last Will latency *(next)* |

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

**Next (part 2):** kill the fleet three ways and measure how long the Will takes to reach a subscriber.

## Prior art

- [emqtt-bench](https://github.com/emqx/emqtt-bench) — fast Erlang `conn`/`sub`/`pub` benchmarks; no Last Will option.
- [NBomber.MQTT](https://nbomber.com/docs/protocols/mqtt/) — generic load-test steps over MQTTnet (experimental plugin).
- [HiveMQ Swarm](https://docs.hivemq.com/hivemq-swarm/latest/) — commercial scenario runner; the trial is limited to 25 clients.

None of them reports *Will-to-offline latency as seen by a subscriber*, which is the gap this tool fills.

## License

MIT

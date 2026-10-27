# DropPod

*The Chosen never knocked, they dropped in by the hundreds. Better to find out now, if New Eden holds the line.*

A headless Firefall client that floods [PIN](https://github.com/themeldingwars/PIN) and [RIN.WebAPI](https://github.com/themeldingwars/RIN.WebAPI) with fake players, to see how they hold up once a few hundred show up at the same time.

## Building

```
git submodule update --init
dotnet build
dotnet test
```

Keep in mind, that `lib/AeroMessages` has to stay on the same commit as PIN's `Lib/AeroMessages`, otherwise the pods and the server don't agree on what the messages look like.

## Usage

DropPod needs a running server stack, Calldown is the easiest way to get one.

### seed

```
droppod seed --pods 200
```

Creates the accounts `droppod001@droppod.local` to `droppod200@droppod.local`, each with one character (`DropPod001` and so on). They end up in RIN's database, so this only has to be done once per stack, or again after the database got reset. Running it twice doesn't hurt, it only creates what's missing.

### drop

```
droppod drop --pods 10 --behavior wander --zone 448 --seconds 600
```

Logs the pods in, lets them enter the zone and keeps them in the world. Whatever they do, they send what the 1962 client sent: movement and update requests 20 times a second, a status every second and a time sync every 2 seconds.

The pods drop at `--ramp` (5 per second by default). Once every pod is in the world, they hold for `--seconds`, then leave again at the same pace, the last one first. Ctrl+C skips straight to leaving, a second Ctrl+C closes every pod at once.

What the pods do in the world, with `--behavior`:

- `idle`: stands where it spawned.
- `wander`: runs to a random spot within 20 m of its spawn, rests a few seconds, picks the next one. Pods have no collision, so they keep the spawn's height.
- `chat`: stands and talks on Zone chat every 20 seconds or so. PIN sends every message to everyone in the zone, so this one grows with the square of the pods.
- `churn`: stands for 30 to 90 seconds, logs out and comes back 5 to 15 seconds later, over and over.

A character logs in wherever it was last. With `--zone`, a pod in another zone first sends the `zone` admin command, logs in again once PIN moved it, and only then starts its behavior. PIN keeps the zone, so the next run in the same zone logs in directly.

Every 10 seconds it prints how many pods are in which state. At the end there's a line per pod with how long it took to get into the world, the entities it knows of, gaps on the reliable channels, resends and the round trip. The exit code is 1, if a pod failed or the server closed its connection.

With `--record`, every pod's connections get written into `runs/<timestamp>/` as `DropPod001.pcapng` and so on, the handshake included. It opens like any other capture, e.g. in Wireshark, and can be put next to a capture of the real client. Note that this adds up quickly, it's roughly 0.8 MB per pod and 90 seconds.

### invade

```
droppod invade --scenario new-eden-200
```

Drops mixed groups of pods from `invasions/<name>.json` (or any path):

```json
{
  "ramp": "5/s",
  "hold": "10m",
  "groups": [
    { "pods": 150, "behavior": "wander", "zone": 448 },
    { "pods": 30, "behavior": "chat", "zone": 448 },
    { "pods": 20, "behavior": "churn", "zone": 1030 }
  ]
}
```

The groups take the pod numbers in order, `DropPod001` to `DropPod150` wander, `DropPod151` to `DropPod180` chat and so on, so `seed` has to have created enough of them. `operator`, `ramp` and `hold` are optional, `--operator`, `--ramp` and `--seconds` override them.

### Options

- `--operator <url>`: the operator host, what the client has as `OperatorHost` (default `https://localhost:5001`)
- `--pods <n>`: how many pods (default 1)
- `--first <n>`: the number of the first pod, to split the pods over several runs (default 1)
- `--seconds <n>`: how long the pods hold once all of them are in the world (default 30)
- `--ramp <n>/s`: how many pods drop and leave per second, or per minute with `/m` (default `5/s`)
- `--behavior <name>`: `idle` (default), `wander`, `chat` or `churn` (`drop` only)
- `--zone <id>`: move the characters to this zone first (`drop` only)
- `--scenario <name>`: the invasion file (`invade` only)
- `--insecure`: accept any TLS certificate, on by default for `localhost`, since local stacks run on a development certificate
- `--record`: write a pcapng per pod into `runs/<timestamp>/`

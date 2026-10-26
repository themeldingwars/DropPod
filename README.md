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
droppod drop --pods 10 --seconds 600
```

Logs the pods in, lets them enter the zone and keeps them in the world, standing around like an idle player. They send what the 1962 client sent while standing still: movement and update requests 20 times a second, a status every second and a time sync every 2 seconds.

Every 30 seconds it prints how many pods are in which state. At the end there's a line per pod with how long it took to get into the world, the entities it knows of, gaps on the reliable channels, resends and the round trip. The exit code is 1, if a pod failed or the server closed its connection.

With `--record`, every pod's connection gets written into `runs/<timestamp>/` as `DropPod001.pcapng` and so on, the handshake included. It opens like any other capture, e.g. in Wireshark, and can be put next to a capture of the real client. Note that this adds up quickly, it's roughly 0.8 MB per pod and 90 seconds.

### Options

- `--operator <url>`: the operator host, what the client has as `OperatorHost` (default `https://localhost:5001`)
- `--pods <n>`: how many pods (default 1)
- `--first <n>`: the number of the first pod, to split the pods over several runs (default 1)
- `--seconds <n>`: how long `drop` keeps the pods around (default 30)
- `--insecure`: accept any TLS certificate, on by default for `localhost`, since local stacks run on a development certificate
- `--record`: write a pcapng per pod into `runs/<timestamp>/` (`drop` only)

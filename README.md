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

Logs the pods in and connects them to the game server, then prints for every pod what the server sent it. For now the pods don't get further than the Login, so they never show up in the world.

### Options

- `--operator <url>`: the operator host, what the client has as `OperatorHost` (default `https://localhost:5001`)
- `--pods <n>`: how many pods (default 1)
- `--first <n>`: the number of the first pod, to split the pods over several runs (default 1)
- `--seconds <n>`: how long `drop` keeps the pods around (default 30)
- `--insecure`: accept any TLS certificate, on by default for `localhost`, since local stacks run on a development certificate

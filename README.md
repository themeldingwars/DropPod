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

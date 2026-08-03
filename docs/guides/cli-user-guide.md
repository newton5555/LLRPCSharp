# CLI User Guide

`LlrpCli` is the operational front end for `LlrpSdk`. The primary interface is
the Live Shell. It keeps one connection, one local settings draft, and one
managed inventory workflow. A one-shot `inventory` command uses the same SDK
workflow for agents and scripts.

## Start The Live Shell

```powershell
dotnet run --project src/LlrpCli
```

Connect to a reader:

```text
connect 192.0.2.10
status
caps
```

The prompt shows the active reader. `status` shows connection and inventory
state; `caps` shows reader capabilities and RF index tables.

## Managed Settings

Settings commands operate on a local draft until `apply` is executed:

```text
settings edit --from defaults
settings show draft
settings validate
settings save warehouse.json
settings apply --yes
```

Available sources are:

- `defaults`: settings recommended from the connected reader profile;
- `reader`: the reader's current managed settings;
- `generic`: a portable protocol baseline;
- `settings load <file>`: an existing JSON document.

Command behavior:

| Command | Effect |
|---|---|
| `settings show [reader\|draft\|defaults] [--json]` | Read-only display. |
| `settings edit [--from ...]` | Create or edit the local draft. |
| `settings validate [file]` | Validate without writing to the reader. |
| `settings apply [file] --yes` | Apply settings to the reader. |
| `settings load <file>` | Load a local draft. |
| `settings save <file>` | Save a draft or selected settings source. |
| `settings discard` | Remove the local draft. |

`settings apply` deploys managed settings but leaves inventory stopped. The
explicit `--yes` protects the reader from accidental resource replacement.

## Managed Inventory

```text
inventory start
inventory status
inventory stop
```

`inventory start` starts the settings already applied to the reader.
`inventory status` reports the actual managed state, not the local draft.
`inventory stop` stops RF inventory while keeping the managed settings. Use
`resources clear` only when the managed resource ownership should be released.

For foreground monitoring:

```text
inventory start --monitor live
inventory start --monitor frames
inventory start --monitor none
inventory start --monitor live --monitor-duration 30
```

`live` aggregates tag reports, `frames` displays protocol frames, and `none`
starts inventory without a foreground monitor. Ctrl+C or a monitor duration
only exits the monitor; it does not stop inventory.

## Tag Operations

```text
tag read <epc> --bank user --word 0 --count 2
tag write <epc> --bank user --word 0 --data CAFEBABE --yes
tag lock <epc> --target user --privilege lock --yes
tag erase <epc> --bank user --word 0 --count 2 --yes
tag kill <epc> --yes
```

Read operations can run without `--yes`. Write, lock, erase, kill, and
sequences require explicit confirmation. Without confirmation, write commands
show a dry-run plan where supported.

The bank aliases are `reserved`, `epc`, `tid`, and `user`. The full command
parser accepts the command-specific options shown by `help <command>`.

## One-Shot Inventory

Use the root command when an agent or script needs one bounded operation:

```powershell
dotnet run --project src/LlrpCli -- inventory 192.0.2.10 --duration 10 --yes
dotnet run --project src/LlrpCli -- inventory 192.0.2.10 --settings warehouse.json --duration 30 --yes
```

The command connects, loads or creates settings, validates and applies them,
collects reports for the requested duration, then stops and clears the managed
inventory resources. JSON is the default output format for automation; use
`--output table` for interactive output.

The Live Shell and one-shot command share the same settings workflow and SDK;
they are two invocation styles, not two implementations.

## Help And Offline Tools

Use `help` and command completion inside the Live Shell:

```text
help
help settings
help inventory
```

Offline protocol diagnostics do not connect to a reader:

```powershell
dotnet run --project src/LlrpCli -- inspect "043E0000000A01020304"
dotnet run --project src/LlrpCli -- decode "043E0000000A01020304"
dotnet run --project src/LlrpCli -- validate "043E0000000A01020304"
dotnet run --project src/LlrpCli -- encode get-rospecs --message-id 1
```

Raw protocol and expert resource commands are intentionally secondary paths;
use them only when the managed Reader workflow cannot express the operation.

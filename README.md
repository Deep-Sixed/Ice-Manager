# Ice Pulse Test

Minimal Space Engineers client-plugin proof of concept for Pulsar/CoreCLR.

## Test goal

Determine whether an official Keen multiplayer server accepts a normal client inventory request that moves exactly **1 kg of Ice once per second** from a cargo container to one O2/H2 Generator.

The server remains authoritative. A rejected transfer is a valid test result.

## Grid setup

1. Put Ice in a cargo container and add **`[IceSource]`** to that cargo container's name.
2. Add **`[IceTest]`** to exactly one O2/H2 Generator's name.
3. Make sure the cargo container and generator have a conveyor path.
4. For the cleanest measurement, turn the O2/H2 Generator **Off** during the first test so it does not consume Ice while you watch its inventory.
5. Sit in and control a cockpit/control seat on that grid.
6. Enable the **Ice Pulse Test** plugin in Pulsar and join the server.

The plugin sets **Use Conveyor = Off** and **Auto-Refill = Off** on the tagged generator so vanilla pull behavior does not flood the inventory.

## Expected result

If the server accepts the requests, the tagged generator should gain about 1 kg per second while it is not consuming Ice:

- 0 s: 0 kg
- 1 s: 1 kg
- 2 s: 2 kg
- 10 s: 10 kg

The source must contain at least 1 kg; the plugin will not intentionally send a partial pulse.

## Diagnostics

`%APPDATA%\SpaceEngineers\IcePulseTest.log`

Typical messages include:

- `Pulse OK: 1 kg requested...`
- `Pulse rejected/failed by inventory or server.`
- missing `[IceSource]` or `[IceTest]`
- no conveyor path
- no controlled grid

## Scope

This build is intentionally narrow. It uses public Keen ModAPI/Ingame inventory surfaces and does not patch private game code, emulate a server-side Programmable Block, or attempt to bypass server authority.

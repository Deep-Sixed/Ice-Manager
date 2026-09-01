# Ice Pulse Test

Space Engineers client-plugin proof of concept for Pulsar/CoreCLR.

## Current test: 0.4.0

Version 0.4.0 tests whether an official Keen multiplayer server accepts the same public `Sandbox.Game.MyInventory.TransferByUser` path intended for user-initiated inventory transfers.

The previous `IMyInventory.TransferItemTo` test returned success locally but produced no server-replicated inventory change. A manual terminal transfer on the same grid succeeded, so 0.4.0 changes the request path.

## Grid setup

1. Put at least 1 kg of Ice in a cargo container.
2. Put **`[IceSource]`** in that cargo container's **Custom Data**. The block name does not matter.
3. Put **`[IceTest]`** in an O2/H2 Generator's **Custom Data**. The block name does not matter.
4. Make sure the cargo and generator have a conveyor path.
5. Turn the test generator **Off** so it cannot consume Ice during verification.
6. Sit in and control a cockpit/control seat on that grid.
7. Enable **Ice Pulse Test 0.4.0** in Pulsar and join/load the server.

The plugin sets **Use Conveyor = Off** and **Auto-Refill = Off** on the selected target.

## What it does

The plugin performs one request only:

- discovers `[IceSource]` and `[IceTest]` from Custom Data
- selects a tagged source containing at least 1 kg of Ice
- identifies the Ice stack ItemId
- invokes public `Sandbox.Game.MyInventory.TransferByUser` for exactly 1 kg
- checks source and target immediately, after 2 seconds, and after 5 seconds

## Diagnostics

`%APPDATA%\SpaceEngineers\IcePulseTest.log`

The final result should report `SERVER ACCEPTED`, `SERVER NOT CONFIRMED`, `AMBIGUOUS`, or a specific `TransferByUser` invocation error.

## Scope

This is intentionally a one-shot authority diagnostic. The server remains authoritative. Continuous one-per-second automation should only be added after the server-authoritative request path is proven.

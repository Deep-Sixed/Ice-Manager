using System;
using System.Collections.Generic;
using System.IO;
using Sandbox.ModAPI;
using VRage;
using VRage.Plugins;
using IMyCubeBlock = VRage.Game.ModAPI.IMyCubeBlock;
using IMyCubeGrid = VRage.Game.ModAPI.IMyCubeGrid;
using IMyIngameEntity = VRage.Game.ModAPI.Ingame.IMyEntity;
using IMyInventory = VRage.Game.ModAPI.Ingame.IMyInventory;
using MyInventoryItem = VRage.Game.ModAPI.Ingame.MyInventoryItem;
using MyItemType = VRage.Game.ModAPI.Ingame.MyItemType;

namespace IcePulseTest;

/// <summary>
/// Minimal client-side proof of concept for Space Engineers/Pulsar.
/// Once per second, requests an exact 1 kg Ice transfer from one tagged cargo
/// container to one tagged O2/H2 Generator on the grid currently being controlled.
/// Uses public Keen ModAPI/Ingame inventory surfaces and normal server authority.
/// </summary>
public sealed class Plugin : IPlugin
{
    private static readonly MyItemType IceType = MyItemType.MakeOre("Ice");
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);

    private const double PulseKg = 1.0;
    private const string SourceTag = "[IceSource]";
    private const string TargetTag = "[IceTest]";

    private readonly List<MyInventoryItem> _items = new();
    private DateTime _nextUpdateUtc = DateTime.MinValue;
    private string _lastState = string.Empty;
    private string _lastError = string.Empty;

    public void Init(object gameInstance)
    {
        Log("Ice Pulse Test 0.1.0 initialized.");
    }

    public void Dispose()
    {
        Log("Ice Pulse Test disposed.");
    }

    public void Update()
    {
        DateTime now = DateTime.UtcNow;
        if (now < _nextUpdateUtc)
            return;

        _nextUpdateUtc = now + UpdateInterval;

        try
        {
            RunCycle();
            _lastError = string.Empty;
        }
        catch (Exception ex)
        {
            string error = ex.GetType().FullName + ": " + ex.Message;
            if (!string.Equals(error, _lastError, StringComparison.Ordinal))
            {
                _lastError = error;
                Log("ERROR " + error + Environment.NewLine + ex.StackTrace);
            }
        }
    }

    private void RunCycle()
    {
        IMyCubeGrid? grid = GetControlledGrid();
        if (grid == null)
        {
            ReportState("Idle: control a cockpit/seat on the test grid.");
            return;
        }

        Sandbox.ModAPI.IMyCargoContainer? sourceBlock = null;
        foreach (Sandbox.ModAPI.IMyCargoContainer block in grid.GetFatBlocks<Sandbox.ModAPI.IMyCargoContainer>())
        {
            if (CanManage(block) && HasTag(block, SourceTag))
            {
                sourceBlock = block;
                break;
            }
        }

        Sandbox.ModAPI.IMyGasGenerator? targetBlock = null;
        foreach (Sandbox.ModAPI.IMyGasGenerator block in grid.GetFatBlocks<Sandbox.ModAPI.IMyGasGenerator>())
        {
            if (CanManage(block) && HasTag(block, TargetTag))
            {
                targetBlock = block;
                break;
            }
        }

        if (sourceBlock == null)
        {
            ReportState("Idle: no accessible cargo named with " + SourceTag + ".");
            return;
        }

        if (targetBlock == null)
        {
            ReportState("Idle: no accessible O2/H2 Generator named with " + TargetTag + ".");
            return;
        }

        var targetTerminal = (Sandbox.ModAPI.Ingame.IMyGasGenerator)targetBlock;
        targetTerminal.UseConveyorSystem = false;
        targetTerminal.AutoRefill = false;

        IMyInventory? source = ((IMyIngameEntity)sourceBlock).GetInventory(0);
        IMyInventory? target = ((IMyIngameEntity)targetBlock).GetInventory(0);
        if (source == null || target == null)
        {
            ReportState("Idle: source or target inventory unavailable.");
            return;
        }

        if (!source.CanTransferItemTo(target, IceType))
        {
            ReportState("Idle: no conveyor path from " + SourceTag + " to " + TargetTag + ".");
            return;
        }

        _items.Clear();
        source.GetItems(_items);

        for (int i = 0; i < _items.Count; i++)
        {
            MyInventoryItem item = _items[i];
            if (!item.Type.Equals(IceType))
                continue;

            double available = (double)item.Amount;
            if (available < PulseKg)
            {
                ReportState("Idle: source has less than 1 kg of Ice.");
                return;
            }

            double before = (double)target.GetItemAmount(IceType);
            bool moved = source.TransferItemTo(target, item, (MyFixedPoint)PulseKg);
            double after = (double)target.GetItemAmount(IceType);

            if (moved)
                ReportState("Pulse OK: 1 kg requested. Target " + before.ToString("0.###") + " -> " + after.ToString("0.###") + " kg.");
            else
                ReportState("Pulse rejected/failed by inventory or server.");

            return;
        }

        ReportState("Idle: no Ice found in " + SourceTag + ".");
    }

    private static IMyCubeGrid? GetControlledGrid()
    {
        var controlled = MyAPIGateway.Session?.LocalHumanPlayer?.Controller?.ControlledEntity;

        if (controlled is IMyCubeBlock cubeBlock)
            return cubeBlock.CubeGrid;

        if (controlled is IMyCubeGrid grid)
            return grid;

        return null;
    }

    private static bool CanManage(Sandbox.ModAPI.IMyTerminalBlock block)
    {
        return ((Sandbox.ModAPI.Ingame.IMyTerminalBlock)block).HasLocalPlayerAccess();
    }

    private static bool HasTag(Sandbox.ModAPI.IMyTerminalBlock block, string tag)
    {
        string name = ((Sandbox.ModAPI.Ingame.IMyTerminalBlock)block).CustomName ?? string.Empty;
        return name.IndexOf(tag, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void ReportState(string state)
    {
        if (string.Equals(state, _lastState, StringComparison.Ordinal))
            return;

        _lastState = state;
        Log(state);
    }

    private static void Log(string message)
    {
        try
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string directory = Path.Combine(appData, "SpaceEngineers");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "IcePulseTest.log");
            File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message + Environment.NewLine);
        }
        catch
        {
            // Diagnostics must never affect the game.
        }
    }
}

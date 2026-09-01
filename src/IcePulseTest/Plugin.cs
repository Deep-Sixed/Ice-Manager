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
/// One-shot client-side inventory authority diagnostic for Space Engineers/Pulsar.
/// It requests exactly 1 kg of Ice once, then verifies source and target inventories
/// after replication delays so we can distinguish a local API success from a
/// server-authoritative accepted transfer.
/// </summary>
public sealed class Plugin : IPlugin
{
    private static readonly MyItemType IceType = MyItemType.MakeOre("Ice");
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan VerifyDelay2 = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan VerifyDelay5 = TimeSpan.FromSeconds(5);

    private const double PulseKg = 1.0;
    private const string SourceTag = "[IceSource]";
    private const string TargetTag = "[IceTest]";

    private readonly List<MyInventoryItem> _items = new();
    private DateTime _nextPollUtc = DateTime.MinValue;
    private DateTime _requestUtc = DateTime.MinValue;
    private string _lastState = string.Empty;
    private string _lastError = string.Empty;

    private IMyInventory? _pendingSource;
    private IMyInventory? _pendingTarget;
    private double _beforeSourceKg;
    private double _beforeTargetKg;
    private bool _verified2;
    private bool _complete;

    public void Init(object gameInstance)
    {
        Log("Ice Pulse Test 0.3.0 initialized - multi-source authority diagnostic.");
        Log("Tags are read from block NAMES: cargo " + SourceTag + " and generator " + TargetTag + ". Custom Data is not used.");
    }

    public void Dispose()
    {
        Log("Ice Pulse Test disposed.");
    }

    public void Update()
    {
        DateTime now = DateTime.UtcNow;
        if (now < _nextPollUtc)
            return;

        _nextPollUtc = now + PollInterval;

        try
        {
            if (_complete)
                return;

            if (_pendingSource != null && _pendingTarget != null)
            {
                VerifyPending(now);
                return;
            }

            StartOneShot(now);
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

    private void StartOneShot(DateTime now)
    {
        IMyCubeGrid? grid = GetControlledGrid();
        if (grid == null)
        {
            ReportState("Idle: control a cockpit/seat on the test grid.");
            return;
        }

        Sandbox.ModAPI.IMyGasGenerator? targetBlock = null;
        foreach (Sandbox.ModAPI.IMyGasGenerator block in grid.GetFatBlocks<Sandbox.ModAPI.IMyGasGenerator>())
        {
            if (CanManage(block) && HasNameTag(block, TargetTag))
            {
                targetBlock = block;
                break;
            }
        }

        if (targetBlock == null)
        {
            ReportState("Idle: no accessible O2/H2 Generator BLOCK NAME contains " + TargetTag + ".");
            return;
        }

        var targetTerminal = (Sandbox.ModAPI.Ingame.IMyGasGenerator)targetBlock;
        targetTerminal.UseConveyorSystem = false;
        targetTerminal.AutoRefill = false;

        IMyInventory? target = ((IMyIngameEntity)targetBlock).GetInventory(0);
        if (target == null)
        {
            ReportState("Idle: target inventory unavailable.");
            return;
        }

        IMyInventory? source = null;
        MyInventoryItem selectedItem = default;
        string selectedSourceName = string.Empty;
        int taggedSourceCount = 0;
        int taggedSourcesWithIce = 0;

        foreach (Sandbox.ModAPI.IMyCargoContainer block in grid.GetFatBlocks<Sandbox.ModAPI.IMyCargoContainer>())
        {
            if (!CanManage(block) || !HasNameTag(block, SourceTag))
                continue;

            taggedSourceCount++;

            IMyInventory? candidate = ((IMyIngameEntity)block).GetInventory(0);
            if (candidate == null)
                continue;

            _items.Clear();
            candidate.GetItems(_items);

            for (int i = 0; i < _items.Count; i++)
            {
                MyInventoryItem item = _items[i];
                if (!item.Type.Equals(IceType))
                    continue;

                double available = (double)item.Amount;
                if (available > 0)
                    taggedSourcesWithIce++;

                if (available < PulseKg)
                    break;

                if (!candidate.CanTransferItemTo(target, IceType))
                    break;

                source = candidate;
                selectedItem = item;
                selectedSourceName = ((Sandbox.ModAPI.Ingame.IMyTerminalBlock)block).CustomName ?? string.Empty;
                break;
            }

            if (source != null)
                break;
        }

        if (taggedSourceCount == 0)
        {
            ReportState("Idle: no accessible cargo BLOCK NAME contains " + SourceTag + ".");
            return;
        }

        if (source == null)
        {
            if (taggedSourcesWithIce == 0)
                ReportState("Idle: found " + taggedSourceCount + " tagged " + SourceTag + " cargo block(s), but none contains Ice.");
            else
                ReportState("Idle: tagged " + SourceTag + " cargo has Ice, but no source with >=1 kg and a conveyor path to " + TargetTag + " was found.");
            return;
        }

        _beforeSourceKg = (double)source.GetItemAmount(IceType);
        _beforeTargetKg = (double)target.GetItemAmount(IceType);

        Log("TEST BEGIN");
        Log("SELECTED SOURCE " + selectedSourceName);
        Log("BEFORE Source=" + _beforeSourceKg.ToString("0.###") + " kg Target=" + _beforeTargetKg.ToString("0.###") + " kg");

        bool moved = source.TransferItemTo(target, selectedItem, (MyFixedPoint)PulseKg);
        double immediateSource = (double)source.GetItemAmount(IceType);
        double immediateTarget = (double)target.GetItemAmount(IceType);

        Log("REQUEST TransferItemTo(1 kg) returned " + (moved ? "TRUE" : "FALSE"));
        Log("IMMEDIATE Source=" + immediateSource.ToString("0.###") + " kg Target=" + immediateTarget.ToString("0.###") + " kg");

        if (!moved)
        {
            Log("RESULT: transfer request was rejected immediately.");
            _complete = true;
            return;
        }

        _pendingSource = source;
        _pendingTarget = target;
        _requestUtc = now;
        _verified2 = false;
        ReportState("Request returned TRUE; waiting for server replication checks at +2s and +5s.");
    }

    private void VerifyPending(DateTime now)
    {
        if (_pendingSource == null || _pendingTarget == null)
            return;

        TimeSpan elapsed = now - _requestUtc;

        if (!_verified2 && elapsed >= VerifyDelay2)
        {
            double source2 = (double)_pendingSource.GetItemAmount(IceType);
            double target2 = (double)_pendingTarget.GetItemAmount(IceType);
            Log("AFTER +2s Source=" + source2.ToString("0.###") + " kg Target=" + target2.ToString("0.###") + " kg");
            _verified2 = true;
        }

        if (elapsed < VerifyDelay5)
            return;

        double source5 = (double)_pendingSource.GetItemAmount(IceType);
        double target5 = (double)_pendingTarget.GetItemAmount(IceType);
        Log("AFTER +5s Source=" + source5.ToString("0.###") + " kg Target=" + target5.ToString("0.###") + " kg");

        double sourceDelta = source5 - _beforeSourceKg;
        double targetDelta = target5 - _beforeTargetKg;

        if (sourceDelta <= -0.999 && targetDelta >= 0.999)
        {
            Log("RESULT: SERVER ACCEPTED - source lost ~1 kg and target gained ~1 kg.");
        }
        else if (Math.Abs(sourceDelta) < 0.001 && Math.Abs(targetDelta) < 0.001)
        {
            Log("RESULT: SERVER NOT CONFIRMED - inventories returned to/remaining at pre-request values.");
        }
        else
        {
            Log("RESULT: AMBIGUOUS - SourceDelta=" + sourceDelta.ToString("0.###") + " kg TargetDelta=" + targetDelta.ToString("0.###") + " kg.");
        }

        Log("TEST COMPLETE - disable/re-enable or restart Space Engineers to run a fresh one-shot test.");
        _pendingSource = null;
        _pendingTarget = null;
        _complete = true;
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

    private static bool HasNameTag(Sandbox.ModAPI.IMyTerminalBlock block, string tag)
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
            File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message + Environment.NewLine);
        }
        catch
        {
            // Diagnostics must never affect the game.
        }
    }
}

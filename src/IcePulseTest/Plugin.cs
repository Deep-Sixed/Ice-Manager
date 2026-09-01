using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
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
/// One-shot client-side authority diagnostic for Space Engineers/Pulsar.
/// Version 0.4.0 tags blocks through Custom Data and invokes the public
/// Sandbox.Game.MyInventory.TransferByUser path used for user-initiated transfers,
/// then verifies the server-replicated inventory state after +2s and +5s.
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
    private MethodInfo? _transferByUser;

    public void Init(object gameInstance)
    {
        Log("Ice Pulse Test 0.4.0 initialized - Custom Data + TransferByUser diagnostic.");
        Log("Tags are read ONLY from Custom Data: cargo " + SourceTag + " and generator " + TargetTag + ". Block names are unrestricted.");
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
            Exception root = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
            string error = root.GetType().FullName + ": " + root.Message;
            if (!string.Equals(error, _lastError, StringComparison.Ordinal))
            {
                _lastError = error;
                Log("ERROR " + error + Environment.NewLine + root.StackTrace);
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
        int targetCount = 0;
        foreach (Sandbox.ModAPI.IMyGasGenerator block in grid.GetFatBlocks<Sandbox.ModAPI.IMyGasGenerator>())
        {
            if (!CanManage(block) || !HasCustomDataTag(block, TargetTag))
                continue;

            targetCount++;
            if (targetBlock == null)
                targetBlock = block;
        }

        if (targetBlock == null)
        {
            ReportState("Idle: no accessible O2/H2 Generator Custom Data contains " + TargetTag + ".");
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
        int sourceCount = 0;
        int sourcesWithIce = 0;

        foreach (Sandbox.ModAPI.IMyCargoContainer block in grid.GetFatBlocks<Sandbox.ModAPI.IMyCargoContainer>())
        {
            if (!CanManage(block) || !HasCustomDataTag(block, SourceTag))
                continue;

            sourceCount++;
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
                    sourcesWithIce++;

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

        if (sourceCount == 0)
        {
            ReportState("Idle: no accessible cargo container Custom Data contains " + SourceTag + ".");
            return;
        }

        if (source == null)
        {
            if (sourcesWithIce == 0)
                ReportState("Idle: found " + sourceCount + " " + SourceTag + " cargo block(s), but none contains Ice.");
            else
                ReportState("Idle: tagged cargo has Ice, but no source with >=1 kg and a conveyor path to the selected " + TargetTag + " was found.");
            return;
        }

        _beforeSourceKg = (double)source.GetItemAmount(IceType);
        _beforeTargetKg = (double)target.GetItemAmount(IceType);
        string targetName = ((Sandbox.ModAPI.Ingame.IMyTerminalBlock)targetBlock).CustomName ?? string.Empty;

        Log("TEST BEGIN");
        Log("DISCOVERY Sources=" + sourceCount + " Targets=" + targetCount);
        Log("SELECTED SOURCE name='" + selectedSourceName + "' CustomData contains " + SourceTag);
        Log("SELECTED TARGET name='" + targetName + "' CustomData contains " + TargetTag);
        Log("BEFORE Source=" + _beforeSourceKg.ToString("0.###") + " kg Target=" + _beforeTargetKg.ToString("0.###") + " kg ItemId=" + selectedItem.ItemId);

        if (!TryTransferByUser(source, target, selectedItem.ItemId, (MyFixedPoint)PulseKg))
        {
            Log("RESULT: TransferByUser request could not be issued.");
            _complete = true;
            return;
        }

        double immediateSource = (double)source.GetItemAmount(IceType);
        double immediateTarget = (double)target.GetItemAmount(IceType);
        Log("REQUEST TransferByUser(1 kg) invoked successfully.");
        Log("IMMEDIATE Source=" + immediateSource.ToString("0.###") + " kg Target=" + immediateTarget.ToString("0.###") + " kg");

        _pendingSource = source;
        _pendingTarget = target;
        _requestUtc = now;
        _verified2 = false;
        ReportState("TransferByUser invoked; waiting for server replication checks at +2s and +5s.");
    }

    private bool TryTransferByUser(IMyInventory source, IMyInventory target, uint itemId, MyFixedPoint amount)
    {
        MethodInfo? method = _transferByUser ?? ResolveTransferByUser(source);
        if (method == null)
        {
            Log("REQUEST ERROR: public Sandbox.Game.MyInventory.TransferByUser method was not found in loaded game assemblies.");
            return false;
        }

        _transferByUser = method;
        ParameterInfo[] p = method.GetParameters();
        Log("NETWORK PATH " + method.DeclaringType?.FullName + "." + method.Name + " sourceType=" + source.GetType().FullName + " targetType=" + target.GetType().FullName);

        try
        {
            object?[] args = new object?[] { source, target, itemId, -1, amount };
            method.Invoke(null, args);
            return true;
        }
        catch (ArgumentException ex)
        {
            Log("REQUEST ERROR: reflection argument binding failed: " + ex.Message + "; amountParameter=" + p[4].ParameterType.FullName);
            return false;
        }
    }

    private static MethodInfo? ResolveTransferByUser(IMyInventory source)
    {
        Type? type = source.GetType();
        while (type != null)
        {
            if (string.Equals(type.FullName, "Sandbox.Game.MyInventory", StringComparison.Ordinal))
                break;
            type = type.BaseType;
        }

        if (type == null)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType("Sandbox.Game.MyInventory", false, false);
                if (type != null)
                    break;
            }
        }

        if (type == null)
            return null;

        foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (!string.Equals(method.Name, "TransferByUser", StringComparison.Ordinal))
                continue;
            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length == 5 && parameters[2].ParameterType == typeof(uint) && parameters[3].ParameterType == typeof(int))
                return method;
        }

        return null;
    }

    private void VerifyPending(DateTime now)
    {
        if (_pendingSource == null || _pendingTarget == null)
            return;

        TimeSpan elapsed = now - _requestUtc;
        if (!_verified2 && elapsed >= VerifyDelay2)
        {
            LogInventory("AFTER +2s");
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
            Log("RESULT: SERVER ACCEPTED - source lost ~1 kg and target gained ~1 kg via TransferByUser.");
        else if (Math.Abs(sourceDelta) < 0.001 && Math.Abs(targetDelta) < 0.001)
            Log("RESULT: SERVER NOT CONFIRMED - TransferByUser produced no replicated inventory change.");
        else
            Log("RESULT: AMBIGUOUS - SourceDelta=" + sourceDelta.ToString("0.###") + " kg TargetDelta=" + targetDelta.ToString("0.###") + " kg.");

        Log("TEST COMPLETE - disable/re-enable or restart Space Engineers to run a fresh one-shot test.");
        _pendingSource = null;
        _pendingTarget = null;
        _complete = true;
    }

    private void LogInventory(string prefix)
    {
        if (_pendingSource == null || _pendingTarget == null)
            return;
        double s = (double)_pendingSource.GetItemAmount(IceType);
        double t = (double)_pendingTarget.GetItemAmount(IceType);
        Log(prefix + " Source=" + s.ToString("0.###") + " kg Target=" + t.ToString("0.###") + " kg");
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

    private static bool HasCustomDataTag(Sandbox.ModAPI.IMyTerminalBlock block, string tag)
    {
        string data = ((Sandbox.ModAPI.Ingame.IMyTerminalBlock)block).CustomData ?? string.Empty;
        return data.IndexOf(tag, StringComparison.OrdinalIgnoreCase) >= 0;
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

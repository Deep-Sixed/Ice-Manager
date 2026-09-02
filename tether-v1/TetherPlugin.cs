using System;
using System.Reflection;
using HarmonyLib;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Plugins;
using VRageMath;

[assembly: AssemblyTitle("Tether")]
[assembly: AssemblyDescription("Clean-room Space Engineers client plugin for /tether remote-grid construction testing")]
[assembly: AssemblyCompany("Clean-room test build")]
[assembly: AssemblyProduct("Tether")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

namespace CleanRoomTether
{
    internal static class TetherState
    {
        internal static MyCubeGrid Grid;
        internal static long PlacementRedirects;
    }

    [HarmonyPatch(typeof(MyBlockBuilderBase), nameof(MyBlockBuilderBase.FindClosestPlacementObject))]
    internal static class PlacementGridPatch
    {
        private static void Postfix(ref MyCubeGrid closestGrid, ref MyVoxelBase closestVoxelMap)
        {
            MyCubeGrid tether = TetherState.Grid;
            if (tether == null || tether.Closed || tether.MarkedForClose)
                return;

            closestGrid = tether;
            closestVoxelMap = null;
            TetherState.PlacementRedirects++;
        }
    }

    public sealed class TetherPlugin : IPlugin, IDisposable
    {
        private const string Prefix = "Tether";
        private const double TargetRayLengthMeters = 10000.0;
        private const string HarmonyId = "CleanRoomTether.V2";

        private IMyCubeGrid _tetherGrid;
        private bool _chatRegistered;
        private bool _loadedNoticeShown;
        private Harmony _harmony;

        public void Init(object gameInstance)
        {
            _harmony = new Harmony(HarmonyId);
            _harmony.PatchAll(typeof(TetherPlugin).Assembly);
        }

        public void Update()
        {
            TryRegisterChat();

            if (MyAPIGateway.Session == null || MyAPIGateway.Utilities == null)
            {
                ClearStateOnly();
                return;
            }

            if (!_loadedNoticeShown)
            {
                _loadedNoticeShown = true;
                ShowMessage("V2 loaded. Vanilla CubeBuilder target redirection enabled. Aim at a grid and type /tether");
            }

            if (_tetherGrid != null && (_tetherGrid.Closed || _tetherGrid.MarkedForClose))
                ClearTether("target removed");
        }

        public void Dispose()
        {
            try
            {
                if (_chatRegistered && MyAPIGateway.Utilities != null)
                    MyAPIGateway.Utilities.MessageEnteredSender -= OnMessageEntered;
            }
            catch { }

            try
            {
                _harmony?.UnpatchSelf();
            }
            catch { }

            _chatRegistered = false;
            _harmony = null;
            ClearStateOnly();
        }

        private void TryRegisterChat()
        {
            if (_chatRegistered || MyAPIGateway.Utilities == null)
                return;

            try
            {
                MyAPIGateway.Utilities.MessageEnteredSender += OnMessageEntered;
                _chatRegistered = true;
            }
            catch { }
        }

        private void OnMessageEntered(ulong sender, string messageText, ref bool sendToOthers)
        {
            if (string.IsNullOrWhiteSpace(messageText))
                return;

            string text = messageText.Trim();
            if (!text.Equals("/tether", StringComparison.OrdinalIgnoreCase)
                && !text.StartsWith("/tether ", StringComparison.OrdinalIgnoreCase))
                return;

            sendToOthers = false;
            string argument = text.Length > 7 ? text.Substring(7).Trim() : string.Empty;

            if (argument.Length == 0 || argument.Equals("on", StringComparison.OrdinalIgnoreCase))
            {
                AcquireTether();
                return;
            }

            if (argument.Equals("off", StringComparison.OrdinalIgnoreCase)
                || argument.Equals("clear", StringComparison.OrdinalIgnoreCase))
            {
                ClearTether("released");
                return;
            }

            if (argument.Equals("status", StringComparison.OrdinalIgnoreCase))
            {
                ShowStatus();
                return;
            }

            if (argument.Equals("help", StringComparison.OrdinalIgnoreCase))
            {
                ShowMessage("/tether | /tether status | /tether off");
                return;
            }

            ShowMessage("unknown option; use /tether help");
        }

        private void AcquireTether()
        {
            var session = MyAPIGateway.Session;
            var physics = MyAPIGateway.Physics;

            if (session == null || session.Camera == null || physics == null)
            {
                ShowMessage("camera/physics API is not ready");
                return;
            }

            MatrixD camera = session.Camera.WorldMatrix;
            Vector3D from = camera.Translation;
            Vector3D to = from + camera.Forward * TargetRayLengthMeters;

            IHitInfo hit;
            if (!physics.CastRay(from, to, out hit) || hit == null || hit.HitEntity == null)
            {
                ShowMessage("no grid under crosshair");
                return;
            }

            IMyCubeGrid grid = ResolveGrid(hit.HitEntity);
            if (grid == null)
            {
                ShowMessage("target is not a cube grid");
                return;
            }

            MyCubeGrid concreteGrid = grid as MyCubeGrid;
            if (concreteGrid == null)
            {
                ShowMessage("target grid could not be bound to the vanilla CubeBuilder");
                return;
            }

            _tetherGrid = grid;
            TetherState.Grid = concreteGrid;
            TetherState.PlacementRedirects = 0;

            ShowMessage("TETHERED: " + GridLabel(grid) + " | id=" + grid.EntityId);
        }

        private static IMyCubeGrid ResolveGrid(IMyEntity entity)
        {
            IMyEntity current = entity;
            int guard = 0;

            while (current != null && guard++ < 8)
            {
                IMyCubeGrid grid = current as IMyCubeGrid;
                if (grid != null)
                    return grid;

                IMyCubeBlock block = current as IMyCubeBlock;
                if (block != null)
                    return block.CubeGrid;

                current = current.Parent;
            }

            return null;
        }

        private void ClearTether(string reason)
        {
            if (_tetherGrid == null)
            {
                ShowMessage("no active tether");
                return;
            }

            string old = GridLabel(_tetherGrid);
            ClearStateOnly();
            ShowMessage("tether " + reason + ": " + old);
        }

        private void ClearStateOnly()
        {
            _tetherGrid = null;
            TetherState.Grid = null;
            TetherState.PlacementRedirects = 0;
        }

        private void ShowStatus()
        {
            if (_tetherGrid == null)
            {
                ShowMessage("OFF | redirects=" + TetherState.PlacementRedirects);
                return;
            }

            string state = (_tetherGrid.Closed || _tetherGrid.MarkedForClose) ? "INVALID" : "ON";
            ShowMessage(state + " | " + GridLabel(_tetherGrid)
                + " | id=" + _tetherGrid.EntityId
                + " | redirects=" + TetherState.PlacementRedirects);
        }

        private static string GridLabel(IMyCubeGrid grid)
        {
            if (grid == null)
                return "<none>";

            string name = grid.DisplayName;
            if (string.IsNullOrWhiteSpace(name))
                name = "Grid " + grid.EntityId;

            return name;
        }

        private static void ShowMessage(string text)
        {
            if (MyAPIGateway.Utilities != null)
                MyAPIGateway.Utilities.ShowMessage(Prefix, text);
        }
    }
}

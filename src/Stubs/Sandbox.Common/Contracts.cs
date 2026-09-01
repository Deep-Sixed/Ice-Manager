namespace Sandbox.ModAPI
{
    public static class MyAPIGateway
    {
        public static VRage.Game.ModAPI.IMySession? Session { get; }
    }

    public interface IMyTerminalBlock : Sandbox.ModAPI.Ingame.IMyTerminalBlock, VRage.Game.ModAPI.IMyCubeBlock
    {
    }

    public interface IMyFunctionalBlock : IMyTerminalBlock, Sandbox.ModAPI.Ingame.IMyFunctionalBlock
    {
    }

    public interface IMyGasGenerator : IMyFunctionalBlock, Sandbox.ModAPI.Ingame.IMyGasGenerator
    {
        float ProductionCapacityMultiplier { get; set; }
        float PowerConsumptionMultiplier { get; set; }
    }

    public interface IMyCargoContainer : IMyTerminalBlock, Sandbox.ModAPI.Ingame.IMyCargoContainer
    {
    }
}

namespace Sandbox.ModAPI.Ingame
{
    public interface IMyTerminalBlock : VRage.Game.ModAPI.Ingame.IMyCubeBlock
    {
        string CustomName { get; set; }
        string CustomData { get; set; }
        bool HasLocalPlayerAccess();
    }

    public interface IMyFunctionalBlock : IMyTerminalBlock
    {
        bool Enabled { get; set; }
    }

    public interface IMyGasGenerator : IMyFunctionalBlock
    {
        bool AutoRefill { get; set; }
        bool UseConveyorSystem { get; set; }
        bool IsProducing { get; }
    }

    public interface IMyCargoContainer : IMyTerminalBlock
    {
    }
}

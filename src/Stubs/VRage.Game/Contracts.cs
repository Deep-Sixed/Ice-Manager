using System;
using System.Collections.Generic;
using VRage;

namespace VRage.ModAPI
{
    public interface IMyEntity
    {
        long EntityId { get; }
        VRage.Game.ModAPI.Ingame.IMyInventory? GetInventory();
        VRage.Game.ModAPI.Ingame.IMyInventory? GetInventory(int index);
    }
}

namespace VRage.Game.ModAPI.Interfaces
{
    public interface IMyControllableEntity
    {
        VRage.ModAPI.IMyEntity Entity { get; }
    }
}

namespace VRage.Game.ModAPI
{
    public interface IMySession
    {
        IMyPlayer? LocalHumanPlayer { get; }
    }

    public interface IMyPlayer
    {
        IMyEntityController Controller { get; }
        long IdentityId { get; }
    }

    public interface IMyEntityController
    {
        Interfaces.IMyControllableEntity? ControlledEntity { get; }
    }

    public interface IMyCubeBlock : VRage.ModAPI.IMyEntity, Ingame.IMyCubeBlock
    {
        new IMyCubeGrid CubeGrid { get; }
    }

    public interface IMyCubeGrid : VRage.ModAPI.IMyEntity, Ingame.IMyEntity, Ingame.IMyCubeGrid
    {
        IEnumerable<T> GetFatBlocks<T>() where T : class, IMyCubeBlock;
    }
}

namespace VRage.Game.ModAPI.Ingame
{
    public interface IMyEntity
    {
        long EntityId { get; }
        int InventoryCount { get; }
        IMyInventory? GetInventory();
        IMyInventory? GetInventory(int index);
    }

    public interface IMyCubeGrid : IMyEntity
    {
    }

    public interface IMyCubeBlock : IMyEntity
    {
        VRage.Game.ModAPI.IMyCubeGrid CubeGrid { get; }
    }

    public interface IMyInventory
    {
        MyFixedPoint GetItemAmount(MyItemType itemType);
        void GetItems(List<MyInventoryItem> items, Func<MyInventoryItem, bool>? filter = null);
        bool TransferItemTo(IMyInventory dstInventory, MyInventoryItem item, MyFixedPoint? amount = null);
        bool CanTransferItemTo(IMyInventory otherInventory, MyItemType itemType);
    }

    public readonly struct MyInventoryItem
    {
        public readonly uint ItemId;
        public readonly MyFixedPoint Amount;
        public readonly MyItemType Type;
    }

    public readonly struct MyItemType : IEquatable<MyItemType>
    {
        public static MyItemType MakeOre(string subtypeId) => default;
        public bool Equals(MyItemType other) => false;
        public override bool Equals(object? obj) => false;
        public override int GetHashCode() => 0;
    }
}

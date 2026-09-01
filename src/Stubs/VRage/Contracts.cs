namespace VRage.Plugins
{
    public interface IPlugin
    {
        void Init(object gameInstance);
        void Update();
        void Dispose();
    }
}

using BepInEx;

namespace EclipseCrestMod
{
    [BepInPlugin("com.eclipse.test", "Eclipse Test", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        void Awake()
        {
            Logger.LogInfo("[Eclipse] TEST PLUGIN LOADED");
        }
    }
}

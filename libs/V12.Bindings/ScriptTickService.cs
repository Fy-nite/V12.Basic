using V12.Core;
using V12.Core.Core.Interfaces;

namespace V12.Bindings
{
    /// <summary>
    /// Per-frame game service that drives a script gamepak's
    /// <c>OnUpdate(float deltaTime)</c> hook. Shared by
    /// <see cref="ContractGamepack"/> (.ct) and <see cref="ObjektRTGamepak"/>
    /// (.orbt/.oil) so both flows tick identically.
    /// </summary>
    internal sealed class ScriptTickService : IGameService
    {
        private readonly string _gamepackName;
        private readonly Action<float> _tick;

        public ScriptTickService(string gamepackName, Action<float> tick)
        {
            _gamepackName = gamepackName;
            _tick = tick;
        }

        public void Initialize(GameRoot g)
        {
        }

        public void Update(GameRoot gameRoot)
        {
        }

        public void Update(float deltaTime)
        {
            try
            {
                _tick(deltaTime);
            }
            catch (Exception ex)
            {
                GameRoot.Log.Error(ex, "Script gamepak '{Name}' OnUpdate failed", _gamepackName);
            }
        }
    }
}

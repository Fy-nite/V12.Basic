namespace V12.Pak.Tests
{
    /// <summary>A minimal IV12Gamepack used to verify pak DLL loading end to end.</summary>
    public sealed class TestGamePak : IV12Gamepack
    {
        public static bool Initialized { get; private set; }
        public static bool Started { get; private set; }

        public string Name => "TestGamePak";

        public void Initialize()
        {
            Initialized = true;
        }

        public void OnStart()
        {
            Started = true;
        }
    }
}

namespace V12.Pak
{
    /// <summary>
    /// Data model for <c>manifest.json</c> inside a <c>.v12pak</c> archive.
    /// </summary>
    public sealed class V12PakManifest
    {
        /// <summary>Manifest format version. Currently 1.</summary>
        public int Version { get; set; } = 1;

        /// <summary>Engine identifier. Always "v12".</summary>
        public string Engine { get; set; } = "v12";

        /// <summary>Worlds contained in this pak. A pak can hold multiple worlds.</summary>
        public List<V12PakWorldEntry> Worlds { get; set; } = new();

        /// <summary>
        /// Virtual path to archive path mapping. Keys are <c>v12://</c> resolvable
        /// paths (e.g. <c>textures/crate.png</c>), values are tar entry paths
        /// (e.g. <c>assets/textures/crate.png</c>).
        /// </summary>
        public Dictionary<string, string> Assets { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Archive paths to C# DLL gamepaks, loaded via <see cref="GamePak.GamepackLoader"/>.</summary>
        public List<string> Paks { get; set; } = new();

        /// <summary>
        /// Archive paths to Contract (<c>.ct</c>) gamepack entrypoints, compiled at
        /// load time into <see cref="GamePak.IV12Gamepack"/> adapters. Each script
        /// must define a <c>Main</c> function; an optional
        /// <c>OnUpdate(float deltaTime)</c> hook is invoked per frame.
        /// </summary>
        public List<string> Scripts { get; set; } = new();
    }

    public sealed class V12PakWorldEntry
    {
        /// <summary>Human-readable world name. Used as the <c>v12://</c> mount point.</summary>
        public string Name { get; set; } = "";

        /// <summary>Path inside the tar archive to the world's world.xml.</summary>
        public string Entry { get; set; } = "";

        /// <summary>Path inside the tar archive to the world's templates directory.</summary>
        public string TemplatesDir { get; set; } = "";
    }
}

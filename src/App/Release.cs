using System.Reflection;

// Version attributes come from the shared ReleaseAttributes.cs (AppIdentity.Version = Release.Version).
[assembly: AssemblyTitle("Macro Kit")]
[assembly: AssemblyProduct("Macro Kit")]
[assembly: AssemblyDescription("Record and play back mouse and keyboard, expand typed phrases, and run Super Macro scripts in the window you choose.")]

namespace MacroKit.App
{
    /// <summary>The single authoritative version. Keep it equal to the newest heading in CHANGELOG.md.</summary>
    public static class Release
    {
        public const string Version = "1.0.0";
    }
}

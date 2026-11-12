namespace AppShared
{
    // Identity used by the shared sources in C:\dev\App Maintenance\shared\csharp.
    public static class AppIdentity
    {
        public const string Code = "MK";                                   // Configuration Transfer app ID
        public const string ShortName = "Macro Kit";                       // used in user-facing messages
        public const string Version = MacroKit.App.Release.Version;        // the app's single version source
    }
}

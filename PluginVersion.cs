namespace SDRSharp.InmarsatAero
{
    public static class PluginVersion
    {
        // "1.6.1" from AssemblyVersion in Properties/AssemblyInfo.cs
        public static readonly string Text = typeof(PluginVersion).Assembly.GetName().Version.ToString(3);
    }
}

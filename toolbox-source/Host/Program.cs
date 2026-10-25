using System;
using System.Windows;
using PclLocalTools;
internal static class Program {
    [STAThread]
    private static void Main() { new Application().Run(new ToolboxWindow(new LauncherContext())); }
}

using System.Diagnostics;

var psi = new ProcessStartInfo("GMDiffTool.exe")
{
    UseShellExecute = false,
};
psi.ArgumentList.Add("modded/data.win");
psi.ArgumentList.Add("vanilla/data.win");
psi.ArgumentList.Add("out");

using var p = Process.Start(psi)!;
p.WaitForExit();
Environment.Exit(p.ExitCode);
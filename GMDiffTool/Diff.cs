using System.Diagnostics;
using Serilog;
using UndertaleModLib;
// using UndertaleModLib.Models;

// Diff.cs

namespace GMDiffTool;
internal static class MainOperations
{
    public static async Task MainCommand(string name, string reference, string? outputFolder)
    {
        outputFolder ??= Path.Join(Environment.CurrentDirectory, Path.DirectorySeparatorChar.ToString(), "results");

        LoggerConfiguration logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(string.Format("logs/log_{0}.txt", DateTime.Now.ToString("yyyyMMdd_HHmm")));

        Log.Logger = logger.CreateLogger();

        Console.WriteLine($"Exporting differences between {name} and {reference} in {outputFolder}.");

        Stopwatch stopWatch = new();
        stopWatch.Start();

        // FIX: the old code read task.Result after catching the exception. Reading .Result on a
        // faulted task re-throws an AggregateException, so every internal error became an
        // unhandled crash (and the real error only went to the log file, never the console).
        bool success = false;
        try
        {
            success = await FileReader.Diff(name, reference, outputFolder);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Something went wrong");
            Console.Error.WriteLine($"Fatal: {ex.GetType().Name}: {ex.Message}");
        }

        stopWatch.Stop();
        TimeSpan ts = stopWatch.Elapsed;
        string elapsedTime = string.Format("{0:00}:{1:00}:{2:00}.{3:00}", ts.Hours, ts.Minutes, ts.Seconds, ts.Milliseconds / 10);

        if (success)
        {
            Console.WriteLine($"Process failed successfully in {elapsedTime}.");
        }
        else
        {
            Console.WriteLine($"Finished with errors in {elapsedTime}. Everything that could be exported was written (see errors.txt in the output folder).");
            Environment.ExitCode = 1;
        }

        await Log.CloseAndFlushAsync();
    }
}
internal static class FileReader
{
    public static async Task<bool> Diff(string name, string reference, string outputFolder)
    {
        // FIX: validate inputs BEFORE touching the output folder (it used to be wiped first).
        if (!File.Exists(name)) throw new FileNotFoundException($"File {name} does not exist.");
        if (!File.Exists(reference)) throw new FileNotFoundException($"File {reference} does not exist.");

        DirectoryInfo dir = new(outputFolder);
        if (dir.Exists)
        {
            // FIX: the output folder is deleted recursively; refuse obviously dangerous targets
            // (e.g. -o pointing at the game folder).
            string fullOut = dir.FullName.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            foreach (string input in new[] { name, reference })
            {
                string inputDir = Path.GetDirectoryName(Path.GetFullPath(input))!.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (inputDir.StartsWith(fullOut, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Refusing to delete '{dir.FullName}': it contains the input file {input}.");
            }
            if (dir.EnumerateFiles("*.win").Any())
                throw new InvalidOperationException($"Refusing to delete '{dir.FullName}': it contains .win files.");

            dir.Delete(true);
        }
        dir.Create();

        Task<UndertaleData?> taskName = LoadFile(name);
        await taskName;
        Task<UndertaleData?> taskRef = LoadFile(reference);
        await taskRef;

        if (taskName.Result == null || taskRef.Result == null) throw new FormatException($"Cannot load {name} and {reference}.");

        UndertaleData data = taskName.Result;
        UndertaleData dataRef = taskRef.Result;

        // FIX: each category runs in isolation. One failing category used to abort everything
        // after it (a sprite crash meant TexturePageItems was never written).
        List<string> errors = new();
        void Step(string label, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "{Step} failed", label);
                Console.Error.WriteLine($"[{label}] failed: {ex.GetType().Name}: {ex.Message}");
                errors.Add($"{label}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        Step("Codes", () => DiffUtils.DiffCodes(data, dataRef, dir));
        Step("GameObjects", () => DiffUtils.DiffObjects(data, dataRef, dir));
        Step("Rooms", () => DiffUtils.DiffRooms(data, dataRef, dir));
        Step("Sounds", () => DiffUtils.DiffSounds(data, dataRef, dir));
        Step("Sprites", () => DiffUtils.DiffSprites(data, dataRef, dir));
        Step("TexturePageItems", () => DiffUtils.DiffTexturePageItems(data, dataRef, dir));
        Step("OtherResources", () => DiffUtils.DiffOtherResources(data, dataRef, dir));

        if (errors.Count > 0)
            File.WriteAllLines(Path.Join(dir.FullName, "errors.txt"), errors);

        return errors.Count == 0;
    }
    private static UndertaleData? LoadUmt(string filename)
    {
        UndertaleData? data = null;
        using (FileStream stream = new(filename, FileMode.Open, FileAccess.Read))
        {
            data = UndertaleIO.Read(stream);
        }

        Log.Information(string.Format("Successfully load: {0}.", filename));

        return data;
    }
    public static async Task<UndertaleData?> LoadFile(string filename)
    {
        UndertaleData? data = null;
        // task load a data.win with umt
        Task taskLoadDataWinWithUmt = Task.Run(() =>
        {
            try
            {
                data = LoadUmt(filename);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Something went wrong");
                throw;
            }
        });
        // run
        await taskLoadDataWinWithUmt;
        return data;
    }
}
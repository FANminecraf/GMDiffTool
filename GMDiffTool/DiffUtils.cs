using UndertaleModLib;
using UndertaleModLib.Models;
using UndertaleModLib.Decompiler;     // GlobalDecompileContext
using UndertaleModLib.Util;           // TextureWorker, GMImage
using Underanalyzer.Decompiler;       // DecompileContext (replaces the old Decompiler class)
using ImageMagick;
using DiffMatchPatch;
using Polenter.Serialization;
using Newtonsoft.Json;
using Serilog;

// removed: System.Drawing, System.Drawing.Imaging, System.Runtime.InteropServices
namespace GMDiffTool;

// DiffUtils.cs

class UndertaleCodeNameComparer : IEqualityComparer<UndertaleCode>
{
    public bool Equals(UndertaleCode? x, UndertaleCode? y)
    {
        if (x == null || y == null) return false;
        return x.Name.Content == y.Name.Content;
    }

    // If Equals() returns true for a pair of objects
    // then GetHashCode() must return the same value for these objects.

    public int GetHashCode(UndertaleCode obj)
    {
        //Check whether the object is null
        if (obj == null) return 0;
        return obj.Name.Content.GetHashCode();
    }
}
class UndertaleSpriteNameComparer : IEqualityComparer<UndertaleSprite>
{
    public bool Equals(UndertaleSprite? x, UndertaleSprite? y)
    {
        if (x == null || y == null) return false;
        return x.Name.Content == y.Name.Content;
    }

    // If Equals() returns true for a pair of objects
    // then GetHashCode() must return the same value for these objects.

    public int GetHashCode(UndertaleSprite obj)
    {
        //Check whether the object is null
        if (obj == null) return 0;
        return obj.Name.Content.GetHashCode();
    }
}
class UndertaleGameObjectComparer : IEqualityComparer<UndertaleGameObject>
{
    public bool Equals(UndertaleGameObject? x, UndertaleGameObject? y)
    {
        if (x == null || y == null) return false;
        return x.Name.Content == y.Name.Content &&
            (x.Sprite?.Name.Content ?? "") == (y.Sprite?.Name.Content ?? "") &&
            (x.ParentId?.Name.Content ?? "") == (y.ParentId?.Name.Content ?? "") &&
            x.Visible == y.Visible &&
            x.Persistent == y.Persistent &&
            x.Awake == y.Awake &&
            x.CollisionShape == y.CollisionShape &&
            x.Events.Enumerate().SelectMany(x => x.Item2.Select(y => (((EventType)x.Item1).ToString(), (int)y.EventSubtype))).ToList().Count == y.Events.Enumerate().SelectMany(x => x.Item2.Select(y => (((EventType)x.Item1).ToString(), (int)y.EventSubtype))).ToList().Count;
    }

    // If Equals() returns true for a pair of objects
    // then GetHashCode() must return the same value for these objects.

    public int GetHashCode(UndertaleGameObject obj)
    {
        //Check whether the object is null
        if (obj == null) return 0;
        return obj.Name.Content.GetHashCode() ^
            (obj.Sprite?.Name.Content ?? "").GetHashCode() ^
            (obj.ParentId?.Name.Content ?? "").GetHashCode() ^
            obj.Visible.GetHashCode() ^
            obj.Persistent.GetHashCode() ^
            obj.Awake.GetHashCode() ^
            obj.CollisionShape.GetHashCode() ^
            obj.Events.Enumerate().SelectMany(e => e.Item2.Select(s => (((EventType)e.Item1).ToString(), (int)s.EventSubtype))).ToList().Count.GetHashCode();
    }
}
class UndertaleGameObjectNameComparer : IEqualityComparer<UndertaleGameObject>
{
    public bool Equals(UndertaleGameObject? x, UndertaleGameObject? y)
    {
        if (x == null || y == null) return false;
        return x.Name.Content == y.Name.Content;
    }

    // If Equals() returns true for a pair of objects
    // then GetHashCode() must return the same value for these objects.

    public int GetHashCode(UndertaleGameObject obj)
    {
        //Check whether the object is null
        if (obj == null) return 0;
        return obj.Name.Content.GetHashCode();
    }
}
public class GameObjectSummary
{
    public string name { get; set; } = "";
    public string spriteName { get; set; } = "";
    public string parentName { get; set; } = "";
    public bool isVisible { get; set; }
    public bool isPersistent { get; set; }
    public bool isAwake { get; set; }
    public string collisionShapeFlags { get; set; } = "";
    public List<(string, int)> Events { get; set; } = new List<(string, int)>();
    // NEW: which code entry each event runs. Reskin mods usually change these.
    public List<string> eventCodes { get; set; } = new List<string>();
    public int depth { get; set; }
    public bool isSolid { get; set; }
    public bool isManaged { get; set; }
    public bool usesPhysics { get; set; }
}

static public class DiffUtils
{
    public static IEnumerable<(int, T)> Enumerate<T>(this IEnumerable<T> ienumerable)
    {
        int ind = 0;
        foreach (T element in ienumerable)
        {
            yield return (ind++, element);
        }
    }

    private static string DecompileCode(UndertaleData data, GlobalDecompileContext ctx, UndertaleCode code)
    => new DecompileContext(ctx, code, data.ToolInfo.DecompilerSettings).DecompileToString();

    // thanks to Pong to acknowledge me that possibility
    private static unsafe bool UnsafeCompare(byte[] a1, byte[] a2)
    {
        if (a1 == null || a2 == null || a1.Length != a2.Length) return false;
        fixed (byte* p1 = a1, p2 = a2)
        {
            byte* x1 = p1, x2 = p2;
            int len = a1.Length;
            for (int i = 0; i < len / 8; i++, x1 += 8, x2 += 8)
            {
                if (*(long*)x1 != *(long*)x2) return false; // classic type casting in C, testing 8 bits by 8 bits
            }
            if ((len & 4) != 0) // testing last 4 bits
            {
                if (*(int*)x1 != *(int*)x2) return false;
                x1 += 4;
                x2 += 4;
            }
            if ((len & 2) != 0) // 2 bits
            {
                if (*(short*)x1 != *(short*)x2) return false;
                x1 += 2;
                x2 += 2;
            }
            if ((len & 1) != 0 && *x1 != *x2) return false;
            return true;
        }
    }
    private static bool CompareUndertaleCode(MemoryStream ms, SharpSerializer burstSerializer, UndertaleCode code, UndertaleCode codeRef)
    {
        ms.SetLength(0);
        burstSerializer.Serialize(code, ms);
        byte[] bytes = ms.ToArray();
        ms.SetLength(0);

        burstSerializer.Serialize(codeRef, ms);
        byte[] bytesRef = ms.ToArray();
        return UnsafeCompare(bytes, bytesRef);
    }
    public static void ExportDiffs(IEnumerable<string> added, IEnumerable<string> removed, string name, DirectoryInfo outputFolder)
    {
        File.WriteAllLines(Path.Join(outputFolder.FullName, Path.DirectorySeparatorChar.ToString(), $"added{name}.txt"), added);
        File.WriteAllLines(Path.Join(outputFolder.FullName, Path.DirectorySeparatorChar.ToString(), $"removed{name}.txt"), removed);
    }
    private static Dictionary<string, T> ByName<T>(IEnumerable<T> items, Func<T, string> nameOf)
    {
        // First element wins, matching what Intersect()/First() did before. O(n) instead of O(n^2).
        Dictionary<string, T> map = new();
        foreach (T item in items) map.TryAdd(nameOf(item), item);
        return map;
    }
    private static void AddedRemovedCodes(UndertaleData name, UndertaleData reference, DirectoryInfo outputFolder)
    {
        GlobalDecompileContext contextName = new(name);

        DirectoryInfo dirAddedCode = new(Path.Join(outputFolder.FullName, "AddedCodes"));
        dirAddedCode.Create();

        IEnumerable<UndertaleCode> added = name.Code.Except(reference.Code, new UndertaleCodeNameComparer());
        IEnumerable<UndertaleCode> removed = reference.Code.Except(name.Code, new UndertaleCodeNameComparer());
        List<string> failed = new();

        using (StreamWriter sw = new(Path.Join(outputFolder.FullName, "addedCodes.txt")))
        {
            foreach (UndertaleCode code in added)
            {
                sw.WriteLine(code.Name.Content);
                if (code.ParentEntry is not null) continue; // covered by the parent's file
                try
                {
                    File.WriteAllText(Path.Join(dirAddedCode.FullName, $"{code.Name.Content}.gml"),
                        DecompileCode(name, contextName, code));
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Decompile failed for {Code}", code.Name.Content);
                    try
                    {
                        string asm = code.Disassemble(name.Variables, name.CodeLocals?.For(code));
                        File.WriteAllText(Path.Join(dirAddedCode.FullName, $"{code.Name.Content}.asm"), asm);
                    }
                    catch (Exception ex2)
                    {
                        failed.Add($"{code.Name.Content}\t{ex2.GetType().Name}: {ex2.Message}");
                    }
                }
            }
        }
        File.WriteAllLines(Path.Join(outputFolder.FullName, "removedCodes.txt"), removed.Select(x => x.Name.Content));
        if (failed.Count > 0)
            File.WriteAllLines(Path.Join(outputFolder.FullName, "failedCodes.txt"), failed);
    }
    private static void ModifiedCodes(UndertaleData name, UndertaleData reference, DirectoryInfo outputFolder)
    {
        using MemoryStream ms = new();
        SharpSerializer burstSerializer = new(new SharpSerializerBinarySettings(BinarySerializationMode.Burst));

        GlobalDecompileContext contextName = new(name);
        GlobalDecompileContext contextRef = new(reference);

        DirectoryInfo dirModifiedCode = new(Path.Join(outputFolder.FullName, "ModifiedCodes"));
        dirModifiedCode.Create();

        Dictionary<string, UndertaleCode> refByName = ByName(reference.Code, c => c.Name.Content);
        HashSet<string> seen = new();
        List<string> failed = new();
        diff_match_patch dmp = new();

        foreach (UndertaleCode code in name.Code)
        {
            string codeName = code.Name.Content;
            if (!seen.Add(codeName) || !refByName.TryGetValue(codeName, out UndertaleCode? codeRef)) continue;
            if (CompareUndertaleCode(ms, burstSerializer, code, codeRef)) continue;
            if (code.ParentEntry is not null) continue;

            // FIX: these used to be declared outside the loop, so when decompiling failed the
            // PREVIOUS code's text was diffed against this one and a bogus report was written.
            string strName;
            string strRef;
            string extension = "gml";
            try
            {
                strName = DecompileCode(name, contextName, code);
                strRef = DecompileCode(reference, contextRef, codeRef);
            }
            catch (Exception)
            {
                // Nested function blocks are part of their parent's decompiled text; the parent's
                // diff already covers them.
                try
                {
                    strName = code.Disassemble(name.Variables, name.CodeLocals?.For(code));
                    strRef = codeRef.Disassemble(reference.Variables, reference.CodeLocals?.For(codeRef));
                    extension = "asm";
                }
                catch (Exception ex2)
                {
                    Console.WriteLine($@"{codeName}: {ex2.GetType()}: {ex2.Message}");
                    failed.Add($"{codeName}\t{ex2.GetType().Name}: {ex2.Message}");
                    continue;
                }
            }
            List<Diff> diff = dmp.diff_main(strRef, strName);
            if (diff.Count == 0 || (diff.Count == 1 && diff[0].operation == Operation.EQUAL)) continue;
            dmp.diff_cleanupSemantic(diff);

            string report = dmp.diff_prettyHtml(diff);
            File.WriteAllText(Path.Join(dirModifiedCode.FullName, $"{codeName}.html"), report);
            // NEW: also keep the full NEW source, so a converter can apply it (the html is only a diff).
            File.WriteAllText(Path.Join(dirModifiedCode.FullName, $"{codeName}.{extension}"), strName);
        }
        if (failed.Count > 0)
            File.WriteAllLines(Path.Join(outputFolder.FullName, "failedModifiedCodes.txt"), failed);
    }
    public static void DiffCodes(UndertaleData name, UndertaleData reference, DirectoryInfo outputFolder)
    {
        AddedRemovedCodes(name, reference, outputFolder);
        ModifiedCodes(name, reference, outputFolder);
    }

    private static string WrapHtml(string body) =>
    "<!DOCTYPE html><meta charset=\"utf-8\"><body style=\"font-family:Consolas,monospace;white-space:pre-wrap\">" + body + "</body>";

    private static GameObjectSummary BuildSummary(UndertaleGameObject ob)
    {
        return new GameObjectSummary()
        {
            name = ob.Name.Content,
            spriteName = ob.Sprite?.Name.Content ?? "",
            parentName = ob.ParentId?.Name.Content ?? "",
            isVisible = ob.Visible,
            isPersistent = ob.Persistent,
            isAwake = ob.Awake,
            collisionShapeFlags = ob.CollisionShape.ToString(),
            Events = ob.Events.Enumerate().SelectMany(x => x.Item2.Select(y => (((EventType)x.Item1).ToString(), (int)y.EventSubtype))).ToList(),
            eventCodes = ob.Events.Enumerate().SelectMany(x => x.Item2.Select(y =>
                ((EventType)x.Item1).ToString() + ":" + ((int)y.EventSubtype) + "->" +
                string.Join(",", y.Actions.Select(a => a.CodeId?.Name?.Content ?? "")))).ToList(),
            depth = ob.Depth,
            isSolid = ob.Solid,
            isManaged = ob.Managed,
            usesPhysics = ob.UsesPhysics,
        };
    }
    private static void AddedRemovedObjects(UndertaleData name, UndertaleData reference, DirectoryInfo outputFolder)
    {
        DirectoryInfo dirAddedObject = new(Path.Join(outputFolder.FullName, "AddedGameObjects"));
        dirAddedObject.Create();

        IEnumerable<UndertaleGameObject> added = name.GameObjects.Except(reference.GameObjects, new UndertaleGameObjectNameComparer());
        IEnumerable<UndertaleGameObject> removed = reference.GameObjects.Except(name.GameObjects, new UndertaleGameObjectNameComparer());
        using (StreamWriter sw = new(Path.Join(outputFolder.FullName, "addedGameObjects.txt")))
        {
            foreach (UndertaleGameObject ob in added)
            {
                sw.WriteLine(ob.Name.Content);
                File.WriteAllText(Path.Join(dirAddedObject.FullName, $"{ob.Name.Content}.json"),
                    JsonConvert.SerializeObject(BuildSummary(ob), Formatting.Indented)
                );
            }
        }
        File.WriteAllLines(Path.Join(outputFolder.FullName, "removedGameObjects.txt"), removed.Select(x => x.Name.Content));
    }
    private static void ModifiedObjects(UndertaleData name, UndertaleData reference, DirectoryInfo outputFolder)
    {
        diff_match_patch dmp = new();

        DirectoryInfo dirModifiedObject = new(Path.Join(outputFolder.FullName, "ModifiedObjects"));
        dirModifiedObject.Create();

        Dictionary<string, UndertaleGameObject> refByName = ByName(reference.GameObjects, o => o.Name.Content);
        HashSet<string> seen = new();

        foreach (UndertaleGameObject ob in name.GameObjects)
        {
            string obName = ob.Name.Content;
            if (!seen.Add(obName) || !refByName.TryGetValue(obName, out UndertaleGameObject? obRef)) continue;

            string strName = JsonConvert.SerializeObject(BuildSummary(ob), Formatting.Indented);
            // FIX: the reference side used ob.Events (the NEW object's events) instead of
            // obRef.Events, so event changes could never show up in the diff. The old comparer
            // also only compared the NUMBER of events; now the full summaries are compared.
            string strRef = JsonConvert.SerializeObject(BuildSummary(obRef), Formatting.Indented);
            if (strName == strRef) continue;

            List<Diff> diff = dmp.diff_main(strRef, strName);
            if (diff.Count == 0 || (diff.Count == 1 && diff[0].operation == Operation.EQUAL)) continue;

            string report = dmp.diff_prettyHtml(diff);
            File.WriteAllText(Path.Join(dirModifiedObject.FullName, $"{obName}.html"), report);
            File.WriteAllText(Path.Join(dirModifiedObject.FullName, $"{obName}.json"), strName); // NEW: full new state
        }
    }

    private static void ObjectSpriteMap(UndertaleData name, UndertaleData reference, DirectoryInfo outputFolder)
    {
        static string Dash(string? s) => string.IsNullOrEmpty(s) ? "-" : s;
        Dictionary<string, UndertaleGameObject> refByName = ByName(reference.GameObjects, o => o.Name.Content);
        List<string> lines = new();
        foreach (UndertaleGameObject ob in name.GameObjects)
        {
            bool existed = refByName.TryGetValue(ob.Name.Content, out UndertaleGameObject? obRef);
            lines.Add(string.Join("\t",
                ob.Name.Content,
                existed ? "existing" : "added",
                Dash(obRef?.Sprite?.Name.Content),
                Dash(ob.Sprite?.Name.Content),
                Dash(ob.ParentId?.Name.Content)));
        }
        File.WriteAllLines(Path.Join(outputFolder.FullName, "objectSpriteMap.txt"), lines);
    }
    public static void DiffObjects(UndertaleData name, UndertaleData reference, DirectoryInfo outputFolder)
    {
        AddedRemovedObjects(name, reference, outputFolder);
        ModifiedObjects(name, reference, outputFolder);
        ObjectSpriteMap(name, reference, outputFolder);
    }
    public static void DiffRooms(UndertaleData name, UndertaleData reference, DirectoryInfo outputFolder)
    {
        IEnumerable<string> added = name.Rooms.Select(x => x.Name.Content).Except(reference.Rooms.Select(x => x.Name.Content));
        IEnumerable<string> removed = reference.Rooms.Select(x => x.Name.Content).Except(name.Rooms.Select(x => x.Name.Content));
        ExportDiffs(added, removed, "Rooms", outputFolder);
    }
    public static void DiffSounds(UndertaleData name, UndertaleData reference, DirectoryInfo outputFolder)
    {
        IEnumerable<string> added = name.Sounds.Select(x => x.Name.Content).Except(reference.Sounds.Select(x => x.Name.Content));
        IEnumerable<string> removed = reference.Sounds.Select(x => x.Name.Content).Except(name.Sounds.Select(x => x.Name.Content));
        ExportDiffs(added, removed, "Sounds", outputFolder);
    }

    private static void NamedDiff<T>(IEnumerable<T>? now, IEnumerable<T>? was, string label, DirectoryInfo outputFolder) where T : UndertaleNamedResource
    {
        if (now is null || was is null) return;
        IEnumerable<string> added = now.Select(x => x.Name.Content).Except(was.Select(x => x.Name.Content));
        IEnumerable<string> removed = was.Select(x => x.Name.Content).Except(now.Select(x => x.Name.Content));
        ExportDiffs(added, removed, label, outputFolder);
    }
    public static void DiffOtherResources(UndertaleData name, UndertaleData reference, DirectoryInfo outputFolder)
    {
        NamedDiff(name.Fonts, reference.Fonts, "Fonts", outputFolder);
        NamedDiff(name.Backgrounds, reference.Backgrounds, "Backgrounds", outputFolder);
        NamedDiff(name.Scripts, reference.Scripts, "Scripts", outputFolder);
        NamedDiff(name.Shaders, reference.Shaders, "Shaders", outputFolder);
        NamedDiff(name.Paths, reference.Paths, "Paths", outputFolder);
        NamedDiff(name.Timelines, reference.Timelines, "Timelines", outputFolder);
    }

    // ------------------------------------------------------------------
    // Sprites
    // ------------------------------------------------------------------

    /// <summary>
    /// A frame can only be cropped/exported when it has a texture page and a non-empty rectangle.
    /// TextureWorker builds a Bitmap of the bounding size, and System.Drawing throws
    /// "Parameter is not valid" for a 0x0 Bitmap. That exception used to escape DiffSprites,
    /// kill the whole run, and skip every sprite after it.
    /// </summary>
    private static bool IsExportable(UndertaleTexturePageItem? item, out string reason)
    {
        reason = "";
        if (item is null) { reason = "null texture entry"; return false; }
        if (item.TexturePage is null) { reason = "no texture page"; return false; }
        if (item.SourceWidth <= 0 || item.SourceHeight <= 0)
        {
            reason = $"empty source rectangle {item.SourceWidth}x{item.SourceHeight}";
            return false;
        }
        if (item.BoundingWidth <= 0 || item.BoundingHeight <= 0)
        {
            reason = $"empty bounding rectangle {item.BoundingWidth}x{item.BoundingHeight}";
            return false;
        }
        return true;
    }
    private static bool TryExportFrame(TextureWorker worker, UndertaleSprite sprite, int index, string folder, List<string> skipped)
    {
        UndertaleTexturePageItem? item = sprite.Textures[index]?.Texture;
        if (!IsExportable(item, out string reason))
        {
            skipped.Add($"{sprite.Name.Content}\t{index}\t{reason}");
            return false;
        }
        try
        {
            worker.ExportAsPNG(item!, Path.Combine(folder, sprite.Name.Content + "_" + index + ".png"), null, true);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Export failed for {Sprite} frame {Frame}", sprite.Name.Content, index);
            skipped.Add($"{sprite.Name.Content}\t{index}\t{ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Ported from the NET 6.0 version. Comparing multi-megabyte page images once per frame was extremely
    /// slow, so the result is cached per (page, page) pair. In UTMT 0.9.x TextureBlob no longer exists:
    /// the page is a GMImage, and its on-disk bytes (ToSpan) are compared instead. Identical on-disk
    /// bytes always mean identical pixels; differing bytes just fall through to the pixel comparison.
    /// </summary>
    private static bool PagesEqual(UndertaleEmbeddedTexture a, UndertaleEmbeddedTexture b, Dictionary<(UndertaleEmbeddedTexture, UndertaleEmbeddedTexture), bool> cache)
    {
        if (ReferenceEquals(a, b)) return true;
        if (cache.TryGetValue((a, b), out bool equal)) return equal;
        equal = PageImagesEqual(a.TextureData?.Image, b.TextureData?.Image);
        cache[(a, b)] = equal;
        return equal;
    }
    private static bool PageImagesEqual(GMImage? a, GMImage? b)
    {
        if (a is null || b is null)
            return false;

        if (ReferenceEquals(a, b))
            return true;

        if (a.Format != b.Format ||
            a.Width != b.Width ||
            a.Height != b.Height)
        {
            return false;
        }

        // GMImage.ToSpan() has a disposed-MemoryStream failure
        // in its Bz2Qoi branch. Compare decoded pixels instead.
        if (a.Format == GMImage.ImageFormat.Bz2Qoi)
        {
            GMImage rawA = a.ConvertToRawBgra();
            GMImage rawB = b.ConvertToRawBgra();

            return rawA.GetRawImageData()
                .SequenceEqual(rawB.GetRawImageData());
        }

        // Other formats do not use the faulty Bz2Qoi serialization path.
        return a.ToSpan().SequenceEqual(b.ToSpan());
    }

    /// <summary>
    /// Decodes texture pages to raw BGRA once (GMImage, no ImageMagick) and keeps a few of them.
    /// Bounded, because a 2048x2048 page is 16 MB raw and a game can have hundreds of pages.
    /// Pages that are already raw are returned as-is, with no copy.
    /// </summary>
    private sealed class RawPageCache
    {
        private const int MaxPages = 8;

        private readonly Dictionary<UndertaleEmbeddedTexture,
            LinkedListNode<(UndertaleEmbeddedTexture Page, GMImage Image)>> map = new();
        private readonly LinkedList<(UndertaleEmbeddedTexture Page, GMImage Image)> lru = new();

        public GMImage? Get(UndertaleEmbeddedTexture page)
        {
            if (map.TryGetValue(page, out var node))
            {
                lru.Remove(node);
                lru.AddFirst(node);
                return node.Value.Image;
            }

            GMImage? source = page.TextureData?.Image;
            if (source is null)
                return null;

            GMImage image = source.ConvertToRawBgra();
            var added = lru.AddFirst((page, image));
            map[page] = added;

            if (map.Count > MaxPages)
            {
                var oldest = lru.Last!;
                lru.RemoveLast();
                map.Remove(oldest.Value.Page);
            }

            return image;
        }
    }

    /// <summary>
    /// Compares the source rectangles of two frames straight out of the decoded page buffers, row by row.
    /// Geometry is already known to be identical, so equal source pixels give equal exported frames.
    /// Returns null when it cannot decide (caller falls back to the slow ImageMagick path).
    /// Fully transparent pixels compare equal whatever their RGB is: re-encoding a page (QOI/PNG/BZ2)
    /// commonly rewrites the invisible colour of alpha-0 pixels, and that must not count as a change.
    /// </summary>
    private static bool? RawFrameEqual(UndertaleTexturePageItem item, UndertaleTexturePageItem itemRef, RawPageCache newCache, RawPageCache refCache, SpriteStats stats)
    {
        GMImage? a = newCache.Get(item.TexturePage);
        GMImage? b = refCache.Get(itemRef.TexturePage);
        if (a is null || b is null) return null;

        int w = item.SourceWidth, h = item.SourceHeight;
        int ax = item.SourceX, ay = item.SourceY, bx = itemRef.SourceX, by = itemRef.SourceY;
        if (ax < 0 || ay < 0 || bx < 0 || by < 0) return null;
        if (ax + w > a.Width || ay + h > a.Height || bx + w > b.Width || by + h > b.Height) return null;

        // Scaled frames are resampled on export, so stay strict for those.
        bool tolerateTransparent = item.SourceWidth == item.TargetWidth && item.SourceHeight == item.TargetHeight;

        ReadOnlySpan<byte> da = a.GetRawImageData();
        ReadOnlySpan<byte> db = b.GetRawImageData();
        int rowBytes = w * 4;
        bool sawTransparentOnly = false;
        for (int row = 0; row < h; row++)
        {
            ReadOnlySpan<byte> ra = da.Slice(((ay + row) * a.Width + ax) * 4, rowBytes);
            ReadOnlySpan<byte> rb = db.Slice(((by + row) * b.Width + bx) * 4, rowBytes);
            if (ra.SequenceEqual(rb)) continue;
            for (int i = 0; i < rowBytes; i += 4)
            {
                if (ra.Slice(i, 4).SequenceEqual(rb.Slice(i, 4))) continue;
                // BGRA: index 3 is alpha
                if (tolerateTransparent && ra[i + 3] == 0 && rb[i + 3] == 0) { sawTransparentOnly = true; continue; }
                return false;
            }
        }
        if (sawTransparentOnly) stats.TransparentOnlyDiff++;
        return true;
    }
    private static bool PixelsEqual(TextureWorker worker, UndertaleTexturePageItem a, UndertaleTexturePageItem b)
    {
        // Compare the actual frame images produced by UTMT, including target/bounding-box
        // placement. A difference in the surrounding texture atlas is not a sprite change.
        using var imgA = worker.GetTextureFor(a, "new", true);
        using var imgB = worker.GetTextureFor(b, "ref", true);

        if (imgA.Width != imgB.Width || imgA.Height != imgB.Height)
            return false;

        byte[]? pa = imgA.GetPixels().ToByteArray(PixelMapping.RGBA);
        byte[]? pb = imgB.GetPixels().ToByteArray(PixelMapping.RGBA);
        if (pa is null || pb is null || pa.Length != pb.Length)
            return false;

        // RGB values in fully transparent pixels are invisible and may change when
        // texture pages are rebuilt/re-encoded. They must not make a vanilla frame
        // look like a reskin.
        for (int i = 0; i < pa.Length; i += 4)
        {
            if (pa[i + 3] == 0 && pb[i + 3] == 0)
                continue;

            if (pa[i] != pb[i] || pa[i + 1] != pb[i + 1] ||
                pa[i + 2] != pb[i + 2] || pa[i + 3] != pb[i + 3])
                return false;
        }

        return true;
    }
    private sealed class SpriteStats
    {
        public int CommonSprites, SpritesWithMetadataChanges;
        public int NewFrames, FramesCompared, GeometryChanged, SamePageFastPath;
        public int PixelCompared, PixelChanged, CompareErrors, Exported;
        public int RawCompared, SlowFallback, TransparentOnlyDiff;
    }
    private static void CompareField(List<string> fields, string field, object? now, object? was)
    {
        if (!object.Equals(now, was)) fields.Add($"{field}: {was} -> {now}");
    }
    /// <summary>
    /// Returns null when the frame is visually identical, otherwise the reason it counts as modified.
    /// This does NOT depend on names: frames are paired by (sprite name, frame index) and judged by
    /// geometry and real pixels. The old check compared the whole TEXTURE PAGE blob, so a sprite
    /// whose page was repacked or whose rectangle moved was misjudged, and a frame missing from
    /// the reference threw a NullReferenceException.
    /// </summary>
    private static string? FrameChange(
        TextureWorker worker,
        UndertaleTexturePageItem item,
        UndertaleTexturePageItem? itemRef,
        Dictionary<(UndertaleEmbeddedTexture, UndertaleEmbeddedTexture), bool> pageCache,
        RawPageCache newCache,
        RawPageCache refCache,
        SpriteStats stats)
    {
        if (itemRef is null)
        {
            stats.NewFrames++;
            return "new-frame";
        }

        stats.FramesCompared++;

        // Geometry is a reason to compare the rendered frames, NOT proof that the
        // sprite was visually reskinned. UTMT imports/repacking can alter TPAG metadata
        // while leaving the visible sprite unchanged.
        bool geometryEqual =
            item.SourceWidth == itemRef.SourceWidth &&
            item.SourceHeight == itemRef.SourceHeight &&
            item.TargetX == itemRef.TargetX &&
            item.TargetY == itemRef.TargetY &&
            item.TargetWidth == itemRef.TargetWidth &&
            item.TargetHeight == itemRef.TargetHeight &&
            item.BoundingWidth == itemRef.BoundingWidth &&
            item.BoundingHeight == itemRef.BoundingHeight;

        bool itemEmpty = item.SourceWidth <= 0 || item.SourceHeight <= 0;
        bool refEmpty = itemRef.SourceWidth <= 0 || itemRef.SourceHeight <= 0;
        if (itemEmpty || refEmpty)
        {
            if (itemEmpty && refEmpty)
                return null;

            stats.GeometryChanged++;
            return "geometry";
        }

        if (item.TexturePage is null || itemRef.TexturePage is null)
            return (item.TexturePage is null && itemRef.TexturePage is null)
                ? null
                : "page-missing";

        // Safest fast path: metadata matches, the frame occupies the same source
        // rectangle, and the entire encoded texture-page images are byte-identical.
        if (geometryEqual &&
            item.SourceX == itemRef.SourceX &&
            item.SourceY == itemRef.SourceY &&
            PagesEqual(item.TexturePage, itemRef.TexturePage, pageCache))
        {
            stats.SamePageFastPath++;
            return null;
        }

        stats.PixelCompared++;

        // The direct-page-buffer optimization is only valid when rendered geometry
        // matches. A raw mismatch is NOT conclusive: verify it against UTMT's actual
        // extracted frame before deciding to export a PNG.
        if (geometryEqual)
        {
            bool? raw;
            try
            {
                raw = RawFrameEqual(item, itemRef, newCache, refCache, stats);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Raw compare unavailable, using extracted-frame comparison");
                raw = null;
            }

            if (raw == true)
            {
                stats.RawCompared++;
                return null;
            }

            if (raw == false)
                stats.RawCompared++;

            // Count both undecidable and mismatching raw comparisons that need the
            // definitive extracted-frame verification below.
            stats.SlowFallback++;
        }
        else
        {
            // If the TPAG geometry differs, compare the rendered output instead of
            // automatically calling it modified.
            stats.SlowFallback++;
        }

        if (PixelsEqual(worker, item, itemRef))
            return null;

        // Only report a geometry change if it also changes the actual rendered frame.
        if (!geometryEqual)
        {
            stats.GeometryChanged++;
            return "geometry";
        }

        stats.PixelChanged++;
        return "pixels";
    }
    private static void AddedRemovedSprites(UndertaleData name, UndertaleData reference, DirectoryInfo outputFolder, TextureWorker worker, List<string> skipped)
    {
        DirectoryInfo dirAddedSprite = new(Path.Join(outputFolder.FullName, "AddedSprites"));
        dirAddedSprite.Create();

        IEnumerable<UndertaleSprite> added = name.Sprites.Except(reference.Sprites, new UndertaleSpriteNameComparer());
        IEnumerable<UndertaleSprite> removed = reference.Sprites.Except(name.Sprites, new UndertaleSpriteNameComparer());
        using StreamWriter swOrigin = new(Path.Join(outputFolder.FullName, "addedSpritesOrigin.txt"));
        using (StreamWriter sw = new(Path.Join(outputFolder.FullName, "addedSprites.txt")))
        {
            foreach (UndertaleSprite sprite in added)
            {
                sw.WriteLine(sprite.Name.Content);
                for (int i = 0; i < sprite.Textures.Count; i++)
                {
                    TryExportFrame(worker, sprite, i, dirAddedSprite.FullName, skipped);
                }
                // Written even when every frame was skipped, so the sprite itself is still registered.
                swOrigin.WriteLine($"Msl.GetSprite(\"{sprite.Name.Content}\").OriginX = {sprite.OriginX};\nMsl.GetSprite(\"{sprite.Name.Content}\").OriginY = {sprite.OriginY};");
            }
        }
        File.WriteAllLines(Path.Join(outputFolder.FullName, "removedSprites.txt"), removed.Select(x => x.Name.Content));
        File.WriteAllLines(Path.Join(outputFolder.FullName, "vanillaSprites.txt"), reference.Sprites.Select(x => x.Name.Content));
    }
    private static void ModifiedSprites(UndertaleData name, UndertaleData reference, DirectoryInfo outputFolder, TextureWorker worker, List<string> skipped)
    {
        DirectoryInfo dirModifiedSprite = new(Path.Join(outputFolder.FullName, "ModifiedSprites"));
        dirModifiedSprite.Create();

        Dictionary<string, UndertaleSprite> refByName = ByName(reference.Sprites, s => s.Name.Content);
        Dictionary<(UndertaleEmbeddedTexture, UndertaleEmbeddedTexture), bool> pageCache = new();
        RawPageCache newCache = new(), refCache = new();
        HashSet<string> seen = new();
        SpriteStats stats = new();
        List<string> changedLines = new();      // sprite \t frame \t reason
        List<string> metadataLines = new();     // sprite \t field: was -> now
        List<string> originLines = new();
        List<string> frameCountLines = new();

        foreach (UndertaleSprite sprite in name.Sprites)
        {
            string spriteName = sprite.Name.Content;
            if (!seen.Add(spriteName) || !refByName.TryGetValue(spriteName, out UndertaleSprite? spriteRef)) continue;
            stats.CommonSprites++;

            // Sprite-level properties (size, margins, origin, bbox, playback...).
            List<string> fields = new();
            CompareField(fields, "Width", sprite.Width, spriteRef.Width);
            CompareField(fields, "Height", sprite.Height, spriteRef.Height);
            CompareField(fields, "MarginLeft", sprite.MarginLeft, spriteRef.MarginLeft);
            CompareField(fields, "MarginRight", sprite.MarginRight, spriteRef.MarginRight);
            CompareField(fields, "MarginTop", sprite.MarginTop, spriteRef.MarginTop);
            CompareField(fields, "MarginBottom", sprite.MarginBottom, spriteRef.MarginBottom);
            CompareField(fields, "OriginX", sprite.OriginX, spriteRef.OriginX);
            CompareField(fields, "OriginY", sprite.OriginY, spriteRef.OriginY);
            CompareField(fields, "BBoxMode", sprite.BBoxMode, spriteRef.BBoxMode);
            CompareField(fields, "SepMasks", sprite.SepMasks, spriteRef.SepMasks);
            CompareField(fields, "Transparent", sprite.Transparent, spriteRef.Transparent);
            CompareField(fields, "Smooth", sprite.Smooth, spriteRef.Smooth);
            CompareField(fields, "Preload", sprite.Preload, spriteRef.Preload);
            CompareField(fields, "GMS2PlaybackSpeed", sprite.GMS2PlaybackSpeed, spriteRef.GMS2PlaybackSpeed);
            CompareField(fields, "GMS2PlaybackSpeedType", sprite.GMS2PlaybackSpeedType, spriteRef.GMS2PlaybackSpeedType);
            if (fields.Count > 0)
            {
                stats.SpritesWithMetadataChanges++;
                metadataLines.Add($"{spriteName}\t{string.Join("; ", fields)}");
            }
            if (sprite.OriginX != spriteRef.OriginX || sprite.OriginY != spriteRef.OriginY)
            {
                originLines.Add($"Msl.GetSprite(\"{spriteName}\").OriginX = {sprite.OriginX};\nMsl.GetSprite(\"{spriteName}\").OriginY = {sprite.OriginY};");
            }
            if (sprite.Textures.Count != spriteRef.Textures.Count)
            {
                frameCountLines.Add($"{spriteName}\t{spriteRef.Textures.Count}\t{sprite.Textures.Count}");
            }

            for (int i = 0; i < sprite.Textures.Count; i++)
            {
                UndertaleTexturePageItem? item = sprite.Textures[i]?.Texture;
                if (item is null) continue;
                UndertaleTexturePageItem? itemRef = i < spriteRef.Textures.Count ? spriteRef.Textures[i]?.Texture : null;

                string? reason;
                try
                {
                    reason = FrameChange(worker, item, itemRef, pageCache, newCache, refCache, stats);
                }
                catch (Exception ex)
                {
                    // Cannot prove the frame is unchanged: treat it as changed rather than lose it.
                    Log.Warning(ex, "Compare failed for {Sprite} frame {Frame}", spriteName, i);
                    stats.CompareErrors++;
                    reason = "compare-error: " + ex.GetType().Name;
                }
                if (reason is null) continue;

                changedLines.Add($"{spriteName}\t{i}\t{reason}");
                if (TryExportFrame(worker, sprite, i, dirModifiedSprite.FullName, skipped)) stats.Exported++;
            }
        }

        // modifiedSprites.txt is the counterpart of addedSprites.txt: a NAME list of every
        // same-named sprite whose frames changed, so nothing is only visible as loose PNGs.
        File.WriteAllLines(Path.Join(outputFolder.FullName, "modifiedSprites.txt"), changedLines);
        File.WriteAllLines(Path.Join(outputFolder.FullName, "modifiedSpritesMetadata.txt"), metadataLines);
        File.WriteAllLines(Path.Join(outputFolder.FullName, "modifiedSpritesOrigin.txt"), originLines);
        File.WriteAllLines(Path.Join(outputFolder.FullName, "modifiedSpritesFrameCounts.txt"), frameCountLines);

        string[] summary =
        {
            $"commonSprites\t{stats.CommonSprites}",
            $"framesCompared\t{stats.FramesCompared}",
            $"framesNewInMod\t{stats.NewFrames}",
            $"framesGeometryChanged\t{stats.GeometryChanged}",
            $"framesSamePageSameRect(unchanged)\t{stats.SamePageFastPath}",
            $"framesPixelCompared\t{stats.PixelCompared}",
            $"framesRawCompared\t{stats.RawCompared}",
            $"framesSlowFallback\t{stats.SlowFallback}",
            $"framesTransparentOnlyDiff(unchanged)\t{stats.TransparentOnlyDiff}",
            $"framesPixelChanged\t{stats.PixelChanged}",
            $"framesCompareErrors\t{stats.CompareErrors}",
            $"framesExported\t{stats.Exported}",
            $"spritesWithMetadataChanges\t{stats.SpritesWithMetadataChanges}",
        };
        File.WriteAllLines(Path.Join(outputFolder.FullName, "modifiedSpritesSummary.txt"), summary);
        Console.WriteLine("Sprite comparison: " + string.Join(", ", summary.Select(l => l.Replace('\t', '='))));
    }
    public static void DiffSprites(UndertaleData name, UndertaleData reference, DirectoryInfo outputFolder)
    {
        TextureWorker worker = new();
        List<string> skipped = new();
        try
        {
            AddedRemovedSprites(name, reference, outputFolder, worker, skipped);
            ModifiedSprites(name, reference, outputFolder, worker, skipped);
        }
        finally
        {
            // Sprite\tFrame\tReason. Written even when a later step throws.
            File.WriteAllLines(Path.Join(outputFolder.FullName, "skippedSprites.txt"), skipped);
            worker.Dispose(); // replaces TextureWorker.Cleanup() from UTMT 0.8.x; frees cached page images
        }
    }
    public static void DiffTexturePageItems(UndertaleData name, UndertaleData reference, DirectoryInfo outputFolder)
    {
        IEnumerable<string> added = name.TexturePageItems.Select(x => x.Name.Content).Except(reference.TexturePageItems.Select(x => x.Name.Content));
        IEnumerable<string> removed = reference.TexturePageItems.Select(x => x.Name.Content).Except(name.TexturePageItems.Select(x => x.Name.Content));
        ExportDiffs(added, removed, "TexturePageItems", outputFolder);
    }
}
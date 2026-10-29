# GMDiffTool
A fork of [ModShardTool](https://github.com/ModShardTeam/ModShardDiff) that fixes a lot of the issues found within the older version.

# Changes
- Fixed an issue with a 0x0 rectangle/malformed frame or sprite that aborted the entire process.
- Upgraded from `NET 6.0` > `NET 10.0` (requires [NET 10.0+ runtime.](https://dotnet.microsoft.com/en-us/download/dotnet/10.0))
- Upgraded the Library from using `UnderTaleModTool v0.8.0.0` **>** `UnderTaleModTool v0.9.2.0` (latest Library of UndertaleModToolLib).
- Changed from using `UndertaleModLib.Decompiler` (deprecated) **>** `Underanalyzer` for code compilation.

# Additions
- Should be able to detect reskins/visual changes from the vanilla & modified .win, and write them in `Modified Sprites` correctly.
- Added a safety check for *-o* that refuses to delete *-o* if:
  1. either input .win file lives inside it, or
  2. it contains any .win file.
(If either check trips, the tool throws, prints the reason to stderr, and exits non-zero before deleting anything.)

# Files that you will get by default (mostly unchanged)
**Text Data**
- `addedCodes.txt` / `removedCodes.txt` / `failedCodes.txt` / `failedModifiedCodes.txt`
- `addedGameObjects.txt` / `removedGameObjects.txt` / `objectSpriteMap.txt`
- `addedRooms.txt` / `removedRooms.txt`
- `addedSounds.txt` / `removedSounds.txt`
- `addedSprites.txt` / `removedSprites.txt` / `vanillaSprites.txt`
- `modifiedSprites.txt` / `modifiedSpritesMetadata.txt` / `modifiedSpritesOrigin.txt` / `modifiedSpritesFrameCounts.txt` / `modifiedSpritesSummary.txt`
- `addedSpritesOrigin.txt` / `skippedSprites.txt`
- `addedTexturePageItems.txt` / `removedTexturePageItems.txt`
- `addedFonts.txt` / `removedFonts.txt`
- `addedBackgrounds.txt` / `removedBackgrounds.txt`
- `addedScripts.txt` / `removedScripts.txt`
- `addedShaders.txt` / `removedShaders.txt`
- `addedPaths.txt` / `removedPaths.txt`
- `addedTimelines.txt` / `removedTimelines.txt`
- `errors.txt` (only written when at least one step failed)

**Folders**
- `AddedCodes/` || `*.gml` (or `*.asm` on decompile failure)
- `ModifiedCodes/` || `*.html` diff + `*.gml` / `*.asm` full new source
- `AddedGameObjects/` || `*.json`
- `ModifiedObjects/` || `*.html` diff + `*.json` full new state
- `AddedSprites/` || `*.png`
- `ModifiedSprites/` || `*.png` (only frames confirmed changed)

# Console behaviours
- **Success:** prints `Process failed successfully in HH:MM:SS.mm` (kind of misleading, but it means "finished without errors"; might probably make it more understandable).
- Failure: prints `Finished with errors in HH:MM:SS.mm. Everything that could be exported was written (see errors.txt in the output folder).` and sets `Environment.ExitCode = 1`.

# Tested Project
**GMDiffTool** was verified against **Pizza Tower**: a full diff of the modded and
vanilla `data.win` completed in (~2 minutes 24 seconds) when invoked
from a Python script.

# Usage
The tool is a normal CLI: it takes the normal three positional arguments and returns an exit code. Everything below assumes you've installed it, either as a .NET global tool or by calling the .dll directly.

**Arguments**
```GMDiffTool <modded.win> <vanilla.win> [outputFolder]```

**Exit Codes**
0 = every diff step completed without throwing.
1 = at least one step failed; the per-category failures are written to `<outputFolder>/errors.txt`, and every step that could run still produced its output.

For usage in programming languages, check the **Examples** folder.

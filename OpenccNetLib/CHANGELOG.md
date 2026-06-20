# Changelog

All notable changes to this project will be documented in this file.

This project adheres to [Semantic Versioning](https://semver.org/).

---

## [1.6.1] - Unreleased

### Added

- Added optional IDS preservation for `Opencc`.
    - New `IsPreserveIds` property.
    - New `GetPreserveIds()` and `SetPreserveIds(bool)` helper methods.
    - When enabled, complete Unicode IDS expressions such as `⿰氵漢` are preserved during conversion while surrounding
      text is still converted.
    - IDS preservation is disabled by default.

### Changed

- Update dictionary data
- Hardened Office/EPUB conversion against corrupted ZIP/package output by validating rebuilt packages before success.
- Added explicit null/empty input validation for package bytes, paths, formats, and converter instances.
- Preserved underlying ZIP/package exceptions as inner exceptions on conversion failures.
- Made file output writes atomic to avoid partial files on failure.

---

## [1.6.0] - 2026-06-18

### Added

- Added forward regional phrase dictionary slots:
    - `DictSlot.TWVariantsPhrases` / `tw_variants_phrases` / `TWVariantsPhrases.txt`
    - `DictSlot.HKVariantsPhrases` / `hk_variants_phrases` / `HKVariantsPhrases.txt`
- Added direct Hong Kong phrase conversion configs and APIs:
    - `s2hkp` / `OpenccConfig.S2Hkp` / `Opencc.S2Hkp(...)`
    - `hk2sp` / `OpenccConfig.Hk2Sp` / `Opencc.Hk2Sp(...)`
- Added direct Hong Kong phrase dictionary slots:
    - `DictSlot.HKPhrases` / `hk_phrases` / `HKPhrases.txt`
    - `DictSlot.HKPhrasesRev` / `hk_phrases_rev` / `HKPhrasesRev.txt`
- Added `DictSlot.JPSCharactersRev` / `jps_characters_rev` / `JPShinjitaiCharactersRev.txt` for Traditional
  Kyujitai-to-Japanese Shinjitai character conversion.
- Added full loading, metadata normalization, JSON/CBOR/Zstd serialization, and custom dictionary append/override
  support for the new Taiwan, Hong Kong, and Japanese Shinjitai dictionary slots.
- Added DeTofu display-compatibility support for rare non-BMP CJK extension characters that may render as tofu boxes
  or missing glyphs on systems with incomplete font coverage.
- Added public DeTofu APIs:
    - `DeTofuLevel`
    - `DeTofu.ParseLevel(...)`
    - `DeTofu.Convert(...)`
    - `DeTofuMap.Builtin(...)`
    - `DeTofuMap.WithCustomPairs(...)`
    - `DeTofuMap.WithCustomFile(...)`
    - `DeTofuMap.Convert(...)`
    - `Opencc.DeTofu(...)`
    - `Opencc.DeTofuWithCustomFile(...)`
    - `Opencc.DeTofuWithCustomPairs(...)`
- Added support for built-in DeTofu mappings loaded from `dicts/TSCharactersTofu.txt`, with custom fallback files and
  custom pairs applied afterward so later mappings override earlier mappings for the same tofu-risk character.
- Added threshold-based DeTofu extension levels where `ExtB` means Extension B and above, `ExtC` means Extension C and
  above, and `ExtI` means Extension I only.
- Added tests covering strict loading, enum availability, Zstd provider hydration, JSON/CBOR field preservation,
  custom dictionary append/override behavior, direct `s2hkp` / `hk2sp` conversion, JP Shinjitai conversion topology,
  DeTofu fallback behavior, and phrase-before-character conversion ordering.

### Changed

- Updated Taiwan and Hong Kong forward regional variant conversion plans so phrase variant dictionaries are applied
  before character-level variant dictionaries:
    - `tw_variants_phrases` before `tw_variants`
    - `hk_variants_phrases` before `hk_variants`
- Refactored the `s2twp` conversion plan to match upstream OpenCC's two-round chain:
    - round 1: Simplified Chinese to Traditional Chinese, including punctuation when enabled
    - round 2: Taiwan phrase and variant normalization using `tw_phrases`, `tw_variants_phrases`, and `tw_variants`
- Added direct Hong Kong phrase conversion plans:
    - `s2hkp`: round 1 Simplified-to-Traditional, round 2 `hk_phrases`, `hk_variants_phrases`, `hk_variants`
    - `hk2sp`: round 1 `hk_phrases_rev`, `hk_variants_rev_phrases`, `hk_variants_rev`, round 2 Traditional-to-Simplified
- Aligned the Japanese Shinjitai dictionary topology with upstream OpenCC commit `93ee7f7`.
- `JPShinjitaiCharacters.txt` is now the authoritative Japanese Shinjitai character mapping source, and
  `JPShinjitaiCharactersRev.txt` is the generated reverse dictionary used by `t2jp`.
- Updated Japanese conversion plans:
    - `t2jp` now uses only `jps_characters_rev`
    - `jp2t` now uses only `jps_phrases` + `jps_characters`
- Renamed internal conversion-plan union keys:
    - `TwVariantsOnly` → `TwVariantsPair`
    - `HkVariantsOnly` → `HkVariantsPair`
    - JP Shinjitai reverse conversion now uses `JpsCharactersRev`
    - JP Shinjitai phrase/character conversion now uses `JpsPair`
- Regenerated bundled `dictionary_maxlength.zstd`, JSON, and CBOR dictionary artifacts with the new Taiwan, Hong Kong,
  and Japanese Shinjitai slots included.

### Fixed

- Ensured missing `TWVariantsPhrases.txt`, `HKVariantsPhrases.txt`, `HKPhrases.txt`, `HKPhrasesRev.txt`, or
  `JPShinjitaiCharactersRev.txt` fails clearly during strict text dictionary loading.
- Preserved compatibility with older serialized dictionary payloads by normalizing missing new Taiwan and Hong Kong
  phrase-slot fields to empty dictionary slots during JSON/CBOR/Zstd deserialization.

### Removed

- Removed `JPVariants.txt` and `JPVariantsRev.txt` from the active dictionary schema, bundled text dictionaries, and
  Japanese conversion plans.
- Retained the public `DictSlot.JPVariants` and `DictSlot.JPVariantsRev` enum members as obsolete, unsupported
  compatibility sentinels with their original numeric values (16 and 17). All new v1.6.0 slots use values above 17.

### Breaking Changes

- Custom dictionary bundles, JSON packs, CBOR packs, and Zstd packs must include non-empty `jps_characters_rev` data.
- `JPVariants.txt` and `JPVariantsRev.txt` are no longer active dictionary inputs.
- Custom dictionary APIs reject the obsolete `DictSlot.JPVariants` and `DictSlot.JPVariantsRev` members with an
  unknown-slot error; the members remain defined to prevent old numeric values from silently targeting new slots.
- Custom dictionary users must regenerate JSON, CBOR, Zstd, and bundled dictionary artifacts using OpenccNet v1.6.0
  (or later) DictGen to produce the new dictionary-slot schema.
- Existing custom bundles generated by older DictGen versions do not contain the new v1.6.0 slots
  (`TWVariantsPhrases`, `HKVariantsPhrases`, `HKPhrases`, `HKPhrasesRev`, `JpsCharactersRev`) and should be
  regenerated before use with OpenccNetLib v1.6.0 or later.

### Documentation

- Updated README custom dictionary guidance and slot tables to document the new Taiwan, Hong Kong, and Japanese
  Shinjitai slots.
- Documented that regional phrase slots are applied before character/regional variant slots.
- Added README and XML documentation for DeTofu usage, fallback file format
  (`tofu_char<TAB>fallback_char<TAB>extension`), custom override behavior, and the non-destructive preservation contract
  for unmapped characters.
- Added upgrade guidance for custom dictionary and serialized-bundle users affected by the v1.6.0 JP dictionary schema
  change.

---

## [1.5.1] - 2026-05-25

### Added

- Added flexible custom dictionary loading support for `DictionaryLib.FromDicts()` and related APIs.
- Added public `DictSlot` enum for strongly typed custom dictionary slot selection.
- Added append mode (`appends`) for loading custom user/company dictionaries on top of existing OpenCC dictionary slots.
- Added override mode (`overrides`) for fully replacing individual OpenCC dictionary slots with custom dictionary files.
- Added post-load custom dictionary customization through `DictionaryLib.WithCustomDicts()`.
- Added public `CustomDictSpec` and `CustomDictMode` APIs for strongly typed post-load dictionary customization.
- Added support for applying custom dictionary files (`Paths`) and in-memory dictionary pairs (`Pairs`) to existing
  dictionary instances.
- Added support for sequential custom dictionary layering:
    - files applied in array order
    - in-memory pairs applied after files
    - later entries overwrite earlier duplicate keys
- Added support for post-load append and override customization on dictionaries loaded from:
    - default built-in dictionaries
    - Zstd dictionaries
    - CBOR dictionaries
    - JSON dictionaries
    - pure file-based dictionaries created through `FromDicts()`
- Added strict dictionary slot validation to preserve the OpenCC lexicon contract and prevent unsupported custom slots.
- Added custom dictionary tests covering:
    - append mode
    - override mode
    - file + in-memory pair precedence
    - conversion behavior
    - metadata rebuilding
    - invalid slot rejection
    - empty specification rejection
- Added optional `DictionaryMaxlength dictionary = null` parameters to dictionary serialization helpers:
  `SerializeToJson()`, `SerializeToJsonUnescaped()`, `SaveCbor()`, `ToCborBytes()`, and `SaveJsonCompressed()`.

### Changed

- Refactored `DictionaryLib.FromJson()` to preserve original exception types instead of wrapping all failures in
  `InvalidOperationException`.
- Refactored dictionary serialization helpers so callers can serialize an already loaded or customized
  `DictionaryMaxlength` instance without reloading from the default text dictionaries.
- Refactored custom dictionary `overrides` and `appends` keys from string slot names to strongly typed `DictSlot`
  values before `v1.5.1-beta1` publication, so no published API compatibility is broken.
- Refactored custom dictionary infrastructure to reuse the same normalization and metadata rebuilding pipeline across
  both file-level (`FromDicts`) and post-load (`WithCustomDicts`) customization workflows.
- Refactored post-load customization so custom dictionary layering remains fully compatible with future
  `StarterUnion` / `UnionCache` acceleration structures.
- `FromJson()` now behaves consistently with `FromCbor()` for file loading and error handling.
- `FromJson()` and the internal Zstd loader now support both absolute paths and paths relative to
  `AppContext.BaseDirectory`.
- Missing external JSON/CBOR dictionary files can now be cleanly detected via `FileNotFoundException`, allowing
  applications to safely fall back to the embedded default Zstd dictionaries.
- Corrupted or invalid JSON/CBOR payloads now surface their real exceptions directly instead of being silently wrapped,
  making custom dictionary validation and debugging clearer for advanced users.
- Improved XML documentation across `DictionaryLib`, including dictionary metadata, custom dictionary loading,
  post-load customization, serialization overloads, path handling, exception contracts, and normalization behavior.
- Custom dictionary loading now fully complies with the existing OpenCC dictionary slot structure instead of introducing
  generic dynamic dictionary slots, keeping `DictionaryMaxlength`, `DictRefs`, and future acceleration structures
  stable.
- Reused the existing centralized dictionary normalization and metadata rebuilding pipeline to keep appended and
  overridden dictionaries fully compatible with the current `StarterUnion` / `UnionCache` acceleration system.
- Update and optimize dictionary data.

---

## [1.5.0] - 2026-05-07

### Changed

* Update dictionary data.
* `.zsd` is now the **single source of truth** for dictionary data in this version.
* Prebuilt `.cbor` / `.json` dictionary files are no longer included in the NuGet package.
* `.cbor` / `.json` formats are now intended for **advanced and custom dictionary workflows**.
* Users can generate `.cbor` / `.json` via the `openccnet dictgen` CLI, or download them from the `/data/` directory in
  the repository.
* Public APIs for CBOR/JSON dictionary loading remain **unchanged and fully backward compatible**.
* All existing GUI applications continue to work **without any changes or impact**.

### Fixed

* Made `OfficeDocConverter.SupportedFormats` read-only so downstream code cannot mutate the global supported-format
  list at runtime. `IsSupportedFormat()` still performs case-insensitive validation.
* Changed `DictionaryLib.PlanCache` from a mutable public field to a get-only public property backed by an internal
  cache field, preserving read access while preventing external replacement with a null or inconsistent cache.
* Updated async Office/EPUB conversion documentation to accurately describe cancellation behavior. Cancellation is
  honored before the background conversion task starts; once the synchronous conversion is running, it continues to
  completion.
* Added explicit pre-start cancellation checks to `ConvertOfficeBytesAsync()` and `ConvertOfficeFileAsync()` overloads.

### Documentation

* Added missing XML `<param>` and `<returns>` documentation for public conversion helpers such as `S2Tw()`,
  `S2Twp()`, `S2Hk()`, `T2Hk()`, `Jp2T()`, `St()`, and `Ts()`.
* Added XML documentation for `DictionaryMaxlength` and all existing snake_case dictionary properties.
* Clarified that `DictionaryMaxlength` is a mutable DTO for built-in/custom dictionary data, and that its snake_case
  property names are intentional public API for compatibility with the OpenccNet ecosystem.

---

## [1.4.2] - 2026-04-08

### Changed

- Micro-optimization of `DictionaryLib` and `OpenCC::ConvertByUnion` for improved throughput.
- Updated bundled dictionary data.
- No behavior changes; core conversion results remain identical.

* This is the **last version that includes prebuilt dictionary data** (`dictionary_maxlength.cbor` / `.json`) in the
  NuGet package.
* Starting from the next major version, `.zsd` will become the **single source of truth** for dictionary data.
* The `.cbor` / `.json` formats are primarily intended for **advanced and custom dictionary workflows**.
* Users can generate these formats using the `openccnet dictgen` CLI, or download them from the `/data/` directory in
  the repository if needed.
* Public APIs for loading CBOR/JSON dictionaries remain **unchanged and fully backward compatible**.

### Fixed

- Fixed `XLSX` conversion to process worksheet inline strings (`t="inlineStr"`), ensuring correct handling of hybrid
  workbooks containing both `shared strings` and `inline strings`.

### Notes

- This change prepares for a cleaner and more maintainable dictionary pipeline.
- The next major version (planned **1.5.0**) may fully transition to CLI-generated dictionary assets and remove bundled
  data.

---

## [1.4.1] - 2026-01-25

### Changed

- `OpenccConfig` is now the single source of truth for internal `Opencc` configuration
- Added `ToCanonicalName()` for the `OpenccConfig` enum
- Improved and aligned XML documentation with the current dictionary provider and plan cache architecture
- Update OpenCC dictionary to version v1.2.0

## [1.4.0] - 2025-12-16

### 🆕 What's New in v1.4.0

- Added OfficeHelper for Office/Epub document conversion.

- **Added `OfficeFormat` enum**  
  Strongly typed format selection for safer, cleaner API usage.

- **Added enum-based overloads**
    - `ConvertOfficeBytes(byte[], OfficeFormat, …)`
    - `ConvertOfficeBytesAsync(byte[], OfficeFormat, …)`
    - `ConvertOfficeFile(string, string, OfficeFormat, …)`
    - `ConvertOfficeFileAsync(string, string, OfficeFormat, …)`

- **String format overloads retained for compatibility**  
  (`"docx"`, `"xlsx"`, `"epub"`, etc.)  
  No breaking changes.

- **Internal refactor**
    - Core engine now switches on `OfficeFormat`
    - Cleaner logic
    - Better performance
    - Safer against typo bugs
    - Easier to maintain

### Added

- **SerializeToJsonUnescaped()**:  
  New method in `DictionaryLib` that writes JSON with unescaped Unicode characters, producing fully human-readable
  output.  
  Supplementary-plane CJK characters (e.g. Extension B–H) are now properly restored via automatic surrogate-pair
  decoding.  
  The resulting file is emitted as **UTF-8 without BOM** for maximum cross-platform compatibility.

- **DecodeJsonSurrogatePairs()** (internal helper):  
  Detects and converts escaped UTF-16 surrogate pairs (e.g. `\uD841\uDDE3`) back into their real UTF-8 code points.  
  Used internally by `SerializeToJsonUnescaped()`.

- **`--unescape` flag for `dictgen` CLI**:  
  Allows generating JSON dictionaries in unescaped form  
  (`openccnet dictgen -f json --unescape`), improving readability of multilingual lexicons.

### Changed

- **Removed `maxWordLength` parameters across the conversion pipeline**:  
  All dictionary-length logic has been consolidated into `StarterUnion.GlobalCap`,  
  eliminating redundant length parameters from `ConvertByUnion`, `DictRefs.Round`, and `ApplySegmentReplace`.  
  This simplifies the API surface, improves maintainability, and unifies all key-length constraints under the
  `StarterUnion` metadata model.

- **Moved `OpenccConfig` enum to the `OpenccNetLib` namespace (top-level)**:  
  The enum was previously nested inside the `Opencc` class.  
  Moving it to the namespace root makes it a first-class public API type, improving discoverability,
  IntelliSense usability, and alignment with .NET library design guidelines.  
  This change is fully backward compatible because all string-based configuration APIs remain unchanged.

- **Refactored `ApplySegmentReplace` delegate**:  
  The primary overload now uses  
  `Func<string, DictWithMaxLength[], StarterUnion, string>`  
  (no `maxLen` parameter).  
  A legacy overload is retained for compatibility and internally forwards `union.GlobalCap`.

- **Internal documentation and XML comments improved**, especially around:
    - surrogate-pair restoration and UTF-8 encoding rules
    - `StarterUnion` metadata behavior
    - key-length gating, mask ranges (1–64), and handling of long keys (≥64)

- **Optimized FromDicts()** in handling missing or optional dictionary files.

### Notes

- This refactor does **not** change external behavior.  
  All existing conversions produce identical results, with reduced internal complexity and improved performance.
- Custom dictionaries containing long keys (≥64 UTF-16 units), such as poems or 長句文言文, are still fully supported.
- Default behavior (`SerializeToJson`) remains unchanged and continues producing fully escaped JSON for strict parsers.
- `SerializeToJsonUnescaped()` output can still be deserialized safely by `System.Text.Json` and other compliant JSON
  libraries.

### Migration Note (for `SetConfig(OpenccConfig)` users)

The `OpenccConfig` enum has been moved out of the `Opencc` class into the
top-level `OpenccNetLib` namespace.

This affects only code calling:

```csharp
opencc.SetConfig(Opencc.OpenccConfig.S2T);   // Old style
```

The method signature is unchanged, but the enum must now be referenced without
the `Opencc.` prefix:

```csharp
opencc.SetConfig(OpenccConfig.S2T);          // New, recommended
```

No other public APIs are affected.  
The new constructor `Opencc(OpenccConfig)` did not exist in previous versions,
so this relocation does **not** break any existing constructor usage.

---

## [1.3.1] - 2025-10-28

### Summary

Maintenance release identical to **v1.3.0**, focused on improving developer experience.

### Added

- **XML Documentation**:  
  The NuGet package now includes `OpenccNetLib.xml`, enabling full IntelliSense support in Visual Studio, Rider, and VS
  Code.  
  Users can now view `summary`, `param`, and `returns` information when hovering over APIs such as `Opencc.Convert()`.

### Changed

- Updated `.csproj` with `<GenerateDocumentationFile>true</GenerateDocumentationFile>` and cleaned up redundant manual
  packing rules.
- Retained all symbols and source link support for debugging consistency.

### Notes

- Functionality and performance remain unchanged from **v1.3.0**.
- `.snupkg` symbol package still provided for source-level debugging.

---

## [1.3.0] – 2025-10-20

### Added

- **Global static `PlanCache` architecture**
    - Introduced `DictionaryLib.PlanCache`, a global, thread-safe cache of precomputed `DictRefs` and `ConversionPlan`
      instances.
    - Uses a lazy provider delegate `(() => DefaultLib.Value)` to avoid redundant initialization and enable instant
      reuse across all `Opencc` instances.
    - Fully compatible with **.NET Standard 2.0**; avoids modern language features for broad runtime support.

- **Custom dictionary override APIs**
    - Added `DictionaryLib.UseCustomDictionary(DictionaryMaxlength)` — allows applications to preload a custom
      dictionary at startup without altering the default lazy loader.
    - Added `DictionaryLib.UseDictionaryFromPath(string)` and `UseDictionaryFromJsonString(string)` helpers to load user
      dictionaries from CBOR/JSON sources.
    - Added `DictionaryLib.SetDictionaryProvider(Func<DictionaryMaxlength>)` and its non-delegate overload
      `SetDictionaryProvider(DictionaryMaxlength)` for flexible runtime switching.
    - All methods automatically rebuild the `PlanCache` and clear cached plans to ensure consistency.

- **Warmup control for long-running apps**
    - `Opencc.Warmup()` now supports optional pre-execution JIT and plan preloading.
    - GUI and service applications can call it once at startup to reduce first-use latency;  
      console tools (single-shot conversions) remain fast without it.

### Changed

- Replaced per-round `RoundKey` caching with a simplified `UnionKey`-based slot cache for `StarterUnion`.
- `ConversionPlanCache` now uses predefined semantic slots (`UnionKey`) shared across all configurations, improving
  readability, maintainability, and alignment with the OpenccJava architecture.
- Simplified `GetOrAddUnionFor()` and removed lambda captures for **.NET Standard 2.0** compatibility.
- Converted all dictionary collections from `List<DictWithMaxLength>` to fixed `DictWithMaxLength[]` arrays for improved
  cache locality, zero heap resizing, and reduced GC pressure.
- `DictRefs` now stores dictionaries in array form and caches per-round `MaxLength` values for faster filtering.
- **Astral-safe starter gating:** implemented `StarterUnion.GetAt(c0, c1, hasSecond, …)` which
    - Detects surrogate pairs and reports `starterUnits` (1 for BMP, 2 for valid pairs).
    - Clears `len == 1` bits in the length mask for non-BMP starters.
    - Exposes `hasStarter`, `cap`, `mask`, and `minLen` in one inlined call.
- **Dense-table union from pre-computed masks:**
    - Added `BuildFromStarterMasks()` to construct `StarterUnion` directly from each dictionary’s `StarterLenMask`
      instead of rescanning all keys, cutting dictionary-build time by **~10 ms** for large lexicons.
    - Each starter bucket uses the first UTF-16 unit (`c0`) as index; surrogate pairs share their high-surrogate bucket
      (safe for current OpenCC data where astral keys are single scalars).
- Hot loop tightened:
    - Switched from `Get(char, …)` to `GetAt(…)` and clamped the search lower bound to  
      `max(minLen, starterUnits)` so astral starters never probe `len == 1`.
    - Added pre-computed `IsHs[]` / `IsLs[]` lookup tables (U+D800–DBFF / U+DC00–DFFF) to remove per-iteration range
      checks.
- Introduced **per-dictionary length-mask gating** (`SupportsLength`) for `DictWithMaxLength`, enabling fast O(1)
  filtering of irrelevant key lengths and reducing allocations.
- Added automatic `RebuildAllLengthMetadata()` after CBOR deserialization (`FromCbor`) to restore  
  `MinLength`, `MaxLength`, and `LengthMask`.
- Rewrote `ConvertBy()` for clarity and C# 7.3 safety:
    - Removed C# 8 syntax (`??=`, static locals).
    - Simplified logic used by `zhoCheck()` to handle only fixed-length (1 or 2) lookups.
- Retained original bit-level `IsDelimiter()` logic for optimal .NET Standard 2.0 performance.
- Simplified bit-scan helpers: replaced `BitOperations` dependency with compact inlined loops  
  (`LowestLen()` / `HighestLen()`) — faster for small masks and fully portable to older runtimes.
- Simplified initialization logic: `Opencc` now references the global `DictionaryLib.PlanCache` instead of per-instance
  caches, eliminating redundant plan construction.
- `InitializeLazyLoaders()` retained for internal use; now invoked only by `Warmup()` or explicit custom dictionary
  setup.
- Updated `Warmup()` XML documentation to clarify that CLI tools do **not** need it.
- Minor tuning to XML docs throughout for clarity, cross-references, and C# 7.3 compliance.

### Fixed

- Corrected an issue in `ConvertByUnion()` where longer phrase matches were truncated (`len` incorrectly taken from
  `step`), restoring full conversions such as “切换 → 切換” and “转换 → 轉換”.
- Verified astral / non-BMP starter behavior and confirmed parity with the Rust `opencc-fmmseg` implementation.
- Fixed a packaging issue in v1.2.0 where the third-party `dicts\LICENSE` file was copied as a **folder** instead of a
  file,
  causing MSBuild copy errors (`MSB3024`) in some user builds.
    - Starting with **v1.2.1**, the file is now named **`LICENSE.txt`**, ensuring a clean and unambiguous layout.
    - The change is fully backward compatible:  
      existing `dicts\LICENSE\` folders from older builds can safely coexist with the new `dicts\LICENSE.txt` file.  
      Users may delete the old folder manually or simply run `dotnet clean` / `dotnet restore` to refresh their outputs.
    - The new layout guarantees correct packaging and publishing for all future builds.
- Ensured that plan cache reuse persists across batch conversions in `OpenccNetLibGui`;  
  subsequent conversions in the same session now execute at full speed with zero re-initialization cost.
- Corrected edge cases where custom dictionary reloads did not clear plan unions.

### Performance

- **🏁 Major Speedup (>50%) on S2T Conversions**
    - Pre-chunked `SplitRanges` drastically reduced thread scheduling and work-stealing overhead.
    - Large inputs are now split into balanced `Chunk` batches (128–512 ranges) for highly efficient parallel execution.
    - `Parallel.For` workers now process contiguous slices with near-perfect load balancing and hot cache locality.
- **💾 Slightly higher memory footprint (+3 MB, ~3–4%)**
    - Per-chunk `StringBuilder` buffers are short-lived (Gen 0) and safely amortized.
    - The trade-off yields significantly higher throughput and no Gen 2 GC pressure.
- **🚀 Benchmark Results (S2T):**
    - 1M characters: **21 ms** (≈ **47 M chars/s**, ~95 MB/s) on Intel i5-13400.
    - Throughput improvement over v1.1.x: **+52%** faster end-to-end conversion.
- **⚙️ Additional optimizations**
    - Smarter parallel thresholds (`textLength > 100k` or `splitRanges.Count > 1000`).
    - Global `StringBuilder` reuse (+6.8% capacity headroom) for .NET Standard 2.0 efficiency.
    - Per-thread builders pre-sized via `ch.EstChars`, reducing dynamic growth and GC activity.
- **Persistent global cache**: once loaded, dictionary and plan data remain hot-resident across all conversions.
- **Instant reuse** in GUI and service scenarios — no rebuilds between text-box conversions or batch runs.
- **Consistent sub-40 ms conversions** for 3 M-character texts on Intel i5-13400 (after first warmup).
- **Zero GC churn** in steady state; all per-round objects are reused.

### Deprecated

- `StarterUnion.Get(char, out cap, out mask)` and  
  `Get(char, out cap, out mask, out minLen)` are marked **[Obsolete]**.  
  They remain thin shims for compatibility; conversion loops now use `GetAt()`.
- Per-instance `_planCache` fields inside `Opencc` are now obsolete.  
  Use the global `DictionaryLib.PlanCache` for all conversions.

### Docs

- Updated XML documentation across `ConversionPlanCache`, `DictRefs`, `DictWithMaxLength`, and `StarterUnion` to
  describe:
    - The new array-based caching model.
    - Astral-safe `GetAt()` behavior and surrogate-pair gating.
    - `BuildFromStarterMasks()` usage and performance advantages.
    - The loop-based `LowestLen()` / `HighestLen()` bit-scan helpers.
    - Per-dictionary length-mask (`SupportsLength`) behavior.
    - Simplified, C# 7.3-compatible `ConvertBy()` implementation.
- Added full XML documentation for:
    - `Chunk` struct and `BuildChunks()` helper, describing its load-balancing strategy.
    - `SegmentReplace()` method, explaining pre-chunked parallel processing and `.NET Standard 2.0` `StringBuilder`
      reuse.
- Added/updated XML documentation for:
    - `DictionaryLib.PlanCache`
    - `SetDictionaryProvider()` overloads
    - `UseCustomDictionary()` and related helpers
    - `Opencc.Warmup()` behavior and GUI vs. CLI recommendations
- All public API comments standardized for **.NET Standard 2.0** syntax (no `init`, pattern matching, or modern nullable
  references).

---

This version marks **the completion of the caching redesign** — a stable, high-performance foundation where `PlanCache`,
dictionary providers, and warmup behavior are unified, thread-safe, and runtime-configurable.

---

## [1.2.0] - 2025-09-30

### Added

- `StarterUnion` now precomputes per-starter **minLen**, in addition to cap and mask,
  enabling faster clamping of candidate lengths.
- Plan/union caching (`ConversionPlanCache`) for reduced allocations across repeated conversions.

### Changed

- Optimized `zhoCheck()` with max char scan length.
- Updated bundled dictionaries.
- Corrected longest-match logic: single-grapheme fast path is only taken when
  no longer candidate exists.
- Conversion is now surrogate-aware and astral-safe in `.NET Standard 2.0`.
- **Delimiter handling**: replaced `HashSet<char>` with a precomputed bitset table,
  yielding constant-time O(1) lookups and faster segmentation in hot loops.

### Removed

- Legacy `StarterCapTextElem` and `BuildStarterIndex()` path (redundant since union-based lookup).
- Redundant dictionary hydration steps during load, lowering initialization overhead.
- `DelimiterMode.Minimal` and `DelimiterMode.Normal`, leaving only the `Full` mode used in practice.

### Performance

- Significant allocation and throughput improvements:
    - Up to ~4.3× faster on short inputs compared to v1.0.3.
    - ~70% less allocation across all input sizes.
    - Large-text segmentation (e.g. 3M chars) now ~76–120 ms vs 120–150 ms in prior versions.
- Avalonia GUI memory footprint reduced (~250 MB vs ~400 MB in prior versions).

---

## [1.1.0] — 2025-08-18

### 🚀 Performance

- Massive throughput & allocation reductions in `ConvertBy()` compared to 1.0.3:
    - 100 chars: **10.79 µs → 2.51 µs** (**4.30× faster**), **21.20 KB → 5.08 KB** (**−76.0%**)
    - 1,000 chars: **169.58 µs → 44.25 µs** (**3.83× faster**), **229.43 KB → 68.32 KB** (**−70.2%**)
    - 10,000 chars: **472.20 µs → 282.25 µs** (**1.67× faster**), **1.99 MB → 659.89 KB** (**−67.6%**)
    - 100,000 chars: **8.57 ms → 6.19 ms** (**1.38× faster**), **21.72 MB → ~6.80 MB** (**−68.7%**)
    - 1,000,000 chars: **88.61 ms → 56.29 ms** (**1.57× faster**), **225.30 MB → ~68.4 MB** (**−69.7%**)

### ✨ Highlights

- **Non-BMP aware**: indexing & lookup now handle astral characters (surrogate pairs, emoji) correctly.
- **Starter caps**: O(1) per-starter **UTF-16 cap array** + **64-bit length bitmap** to skip impossible probe lengths.
- **Single-grapheme fast path**: probes 1- or 2-unit keys first (covers `st_characters` / `ts_characters`) with at most
  one tiny allocation.
- **.NET Standard 2.0 safe**: surrogate-aware emit path (no `Append(ReadOnlySpan<char>)`).

### 🛠 Changed

- Upgraded dependency: **OpenccNetLib** → `v1.0.4-preview`.
- Dictionary index now persists a sparse **`StarterCapTextElem`** (`Dictionary<string, byte>`, key = first text element)
  and hydrates runtime arrays on load.

### 🧰 Fixed

- `zhoCheck()` correctly handles **non-BMP** characters.
- `OfficeDocConvert()` adds **Zip Slip** protection for safe extraction of Office/EPUB archives.

### 🔄 Compatibility

- **No breaking API changes.**
- Existing serialized dictionaries still work; if `StarterCapTextElem` is missing it is derived from `Dict` at build
  time.
- `MaxLength` remains in **UTF-16 units**.

### 📈 Benchmark Notes

- BenchmarkDotNet 0.15.2, .NET 9.0.8, Windows 11, X64 RyuJIT AVX2.
- IterationCount=10, WarmupCount=1.

### 🧪 Technical Notes — Why the Speed Jump Was So Big

This release’s performance leap comes from **multi-layered optimizations**, not just allocation trimming:

1. **Lookup Layer**

    - Added **StarterCaps** array and **64-bit probe masks** per starter char to skip impossible lengths instantly.
    - Astral-aware indexing means surrogate pairs and emoji are matched in a single logical lookup.
    - Shared `StarterUnion` objects across plans with identical round layouts.

2. **Matching Loop**

    - **Fast path** for the most common keys (1–2 UTF-16 units) removes almost all redundant probing for
      `st_characters` /
      `ts_characters`.
    - Eliminated substring allocations during probes; everything operates directly on char spans.

3. **Dictionary Plan Caching**

    - `ConversionPlanCache` + `RoundKey` hashing ensures a `(config, punctuation)` plan is built once and reused,
      instead of
      rebuilding `DictRefs` and unions on every call.

4. **Runtime Memory Behavior**

    - Buffer reuse and fewer intermediate collections keep memory stable.
    - GUI idle footprint dropped from ~400 MB to ~250 MB due to fewer temp allocations in repeated conversions.

5. **Compatibility & Safety**

    - Kept `.NET Standard 2.0` surrogate-safe emit path for broad compatibility.
    - Strengthened archive handling with **Zip Slip** prevention without hurting throughput.

---

## [1.0.3] - 2025-07-29

### Changed

#### OpenccNetLib v1.0.3

A major performance-focused and developer-experience update for the OpenCC-based Chinese text conversion library.

✅ **Highlights**:

- 🧵 New parallel segment processing engine for large-scale text conversion
- ⚡ Optimized `StringBuilder` usage with smart preallocation (up to 20% faster in benchmarks)
- ➗ Inclusive splitting improves dictionary lookup performance with fewer overhead calls
- 🧠 Memory allocation and GC pressure now scale linearly and predictably
- 🔁 Unified string joining logic — no more `string.Concat` bottlenecks
- 🔥 Consistently fast warm and cold starts in both CLI and GUI environments

🔧 **API Enhancements**:

- Added `SetConfig(OpenccConfig)` enum-based overload for safer, IntelliSense-friendly config switching
- Added `TryParseConfig(string, out OpenccConfig)` for converting from string to typed enum
- Existing public `Config` property preserved for backward compatibility (validated with fallback and `GetLastError()`)
- Improved internal `GetDictRefs()` logic with structured round grouping

📦 Compatible with **.NET Standard 2.0+**

🧪 Benchmark Results:

- 1M character input processed in ~88 ms with 225 MB allocated — ~3% faster than v1.0.2

🔗 Project: https://github.com/laisuk/OpenccNet

---

## [1.0.2] - 2025-07-08

### OpenccNetLib v1.0.2

High-performance .NET Standard library for OpenCC-style Chinese conversion, featuring dictionary-based segment
replacement and optimized for both CLI and GUI use.

### What's New in 1.0.2:

- Improved performance with thread-local `StringBuilder` caching
- Reduced memory usage via `ArrayPool<char>` for dictionary keys
- Adaptive parallelization for large text inputs
- Preloaded dictionary round-lists for faster first-time conversion
- Enhanced UTF-8 range heuristics for Chinese code detection
- Internal refactoring for GC and allocation efficiency

📦 Compatible with .NET Standard 2.0+

---

## [1.0.1] - 2025-06-18

### Changed

- Added functions to get, set, and validate conversion `Config`

---

## [1.0.0] - 2025-05-25

### Added

- First official release of `OpenccNetLib` to NuGet

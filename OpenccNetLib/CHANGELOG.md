# Changelog

All notable changes to this project will be documented in this file.

This project adheres to [Semantic Versioning](https://semver.org/).

---

## [1.4.0] - 2025-11-20

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

- **Fast path** for the most common keys (1–2 UTF-16 units) removes almost all redundant probing for `st_characters` /
  `ts_characters`.
- Eliminated substring allocations during probes; everything operates directly on char spans.

3. **Dictionary Plan Caching**

- `ConversionPlanCache` + `RoundKey` hashing ensures a `(config, punctuation)` plan is built once and reused, instead of
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

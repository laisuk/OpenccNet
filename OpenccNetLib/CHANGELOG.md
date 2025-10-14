# Changelog

All notable changes to this project will be documented in this file.

This project adheres to [Semantic Versioning](https://semver.org/).

---

## [1.2.1-Preview] – 2025-10-14

### Changed

- Replaced per-round `RoundKey` caching with a simplified `UnionKey`-based slot cache for `StarterUnion`.
- `ConversionPlanCache` now uses predefined semantic slots (`UnionKey`) shared across all configurations, improving
  readability, maintainability, and alignment with the OpenccJava architecture.
- Simplified `GetOrAddUnionFor()` and removed lambda captures for **.NET Standard 2.0** compatibility.
- Converted all dictionary collections from `List<DictWithMaxLength>` to fixed `DictWithMaxLength[]` arrays for improved
  cache locality, zero heap resizing, and reduced GC pressure.
- `DictRefs` now stores dictionaries in array form and caches per-round `MaxLength` values for faster filtering.
- **Astral-safe starter gating:** introduced `StarterUnion.GetAt(c0, c1, hasSecond, …)` which
    - Detects surrogate pairs and returns `starterUnits` (1 for BMP, 2 for valid pairs).
    - Clears `len == 1` bits in the length mask for non-BMP starters.
    - Exposes `hasStarter`, `cap`, `mask`, and `minLen` in one inlined call.
- Hot loop tightened:
    - Switched from `Get(char, …)` to `GetAt(…)` and clamped the search lower bound to `max(minLen, starterUnits)` so
      astral starters never probe `len == 1`.
    - Added pre-computed `IsHs[]` / `IsLs[]` lookup tables (U+D800–DBFF / U+DC00–DFFF) to remove per-iteration range
      checks.
- Introduced **per-dictionary length-mask gating** (`SupportsLength`) for `DictWithMaxLength`, allowing fast O(1)
  filtering of irrelevant key lengths and reducing allocations.
- Added automatic `RebuildAllLengthMetadata()` call after CBOR deserialization (`FromCbor`) to ensure all `MinLength`,
  `MaxLength`, and `LengthMask` values are correctly restored.
- Rewrote `ConvertBy()` for clarity and .NET Standard 2.0 safety:
    - Removed C# 8 syntax (`??=`, static local functions).
    - Simplified logic used by `zhoCheck()` to handle only fixed-length (1 or 2) dictionary lookups.
- Retained the original **bit-level `IsDelimiter()`** logic for optimal performance on .NET Standard 2.0, reverting from
  Codex’s suggested nested while-loop implementation.

### Fixed

- Corrected an issue in `ConvertByUnion()` where longer phrase matches were truncated (`len` was incorrectly taken from
  `step`), restoring full conversions such as “切换 → 切換” and “转换 → 轉換”.
- Verified astral / non-BMP starter behavior and confirmed parity with opencc-fmmseg (Rust) implementation.

### Performance

- Overall **5 – 12 ms faster** on 3 M-character conversions due to:
    - Array-based dictionary layout.
    - Union-slot caching and per-dictionary bitmask gating.
    - Eliminated transient string allocations during dictionary probing.

### Deprecated

- `StarterUnion.Get(char, out cap, out mask)` and  
  `Get(char, out cap, out mask, out minLen)` are now marked **[Obsolete]**.  
  They remain thin shims for non-critical callers; conversion loops use `GetAt()` internally.

### Docs

- Updated XML documentation across `ConversionPlanCache`, `DictRefs`, `DictWithMaxLength`, and `StarterUnion` to
  describe:
    - The new array-based caching model.
    - The astral-safe `GetAt()` API.
    - Per-dictionary length-mask (`SupportsLength`) behavior.
    - The simplified, C# 7.3-compatible `ConvertBy()` implementation.

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
- Preloaded dictionary roundlists for faster first-time conversion
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

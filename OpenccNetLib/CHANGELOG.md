# Changelog

All notable changes to this project will be documented in this file.

This project adheres to [Semantic Versioning](https://semver.org/).

---

## [1.0.4-preview] — 2025-08-10

### 🚀 Performance

- Massive throughput & allocation reductions in `ConvertBy()` compared to 1.0.3:
    - 100 chars: **10.79 µs → 2.51 µs** (**4.30× faster**), **21.20 KB → 5.08 KB** (**−76%**)
    - 1,000 chars: **169.58 µs → 44.25 µs** (**3.83×**), **229.43 KB → 68.32 KB** (**−70%**)
    - 10,000 chars: **472.20 µs → 282.25 µs** (**1.67×**), **1.99 MB → 659.89 KB** (**−67.6%**)
    - 100,000 chars: **8.57 ms → 6.19 ms** (**1.38×**), **21.72 MB → ~6.80 MB** (**−68.7%**)
    - 1,000,000 chars: **88.61 ms → 56.29 ms** (**1.57×**), **225.30 MB → ~68.4 MB** (**−69.7%**)

### ✨ Highlights

- **Non-BMP aware**: indexing & lookup now handle astral characters (surrogate pairs, emoji) correctly.
- **Starter caps**: O(1) per-starter **UTF-16 cap array** + **64‑bit length bitmap** to skip impossible probe lengths.
- **Single‑grapheme fast path**: probes 1‑ or 2‑unit keys first (covers `st_characters` / `ts_characters`) with at most
  one tiny allocation.
- **.NET Standard 2.0 safe**: surrogate‑aware emit path (no `Append(ReadOnlySpan<char>)`).

### 🛠 Changed

- Upgraded dependency: **OpenccNetLib** → `v1.0.4-preview`.
- Dictionary index now persists a sparse **`StarterCapTextElem`** (`Dictionary<string, byte>`, key = first text element)
  and hydrates runtime arrays on load.

### 🧰 Fixed

- `zhoCheck()` correctly handles **non‑BMP** characters.
- `OfficeDocConvert()` adds **Zip Slip** protection for safe extraction of Office/EPUB archives.

### 🔄 Compatibility

- **No breaking API changes.**
- Existing serialized dictionaries still work; if `StarterCapTextElem` is missing it is derived from `Dict` at build
  time.
- `MaxLength` remains in **UTF‑16 units**.

### 📈 Benchmark Notes

- BenchmarkDotNet 0.15.2, .NET 9.0.8, Windows 11, X64 RyuJIT AVX2.
- IterationCount=10, WarmupCount=1.

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

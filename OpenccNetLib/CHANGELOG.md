# Changelog

All notable changes to this project will be documented in this file.

This project adheres to [Semantic Versioning](https://semver.org/).

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

# Changelog

All notable changes to this project will be documented in this file.

This project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html) and uses
the [Keep a Changelog](https://keepachangelog.com/en/1.0.0/) format.

---

## [1.0.3] - 2025-07-25

### Performance

- ✨ **Segment replacement now uses preallocated `StringBuilder`**, replacing `string.Concat()` across the board.
- ✨ **Inclusive splitting strategy** improves dictionary lookup efficiency by reducing redundant calls.
- ✨ **Parallel processing enabled for large inputs** (≥16 segments and ≥2000 chars) — boosting conversion speed by up to **20%**.
- ♻️ GC pressure scales linearly with input — verified with BenchmarkDotNet.
- 📈 Benchmarks confirm consistent performance:
    - 1M characters converted in **~88 ms** with **~225 MB** allocation
    - Outperforms v1.0.2 by ~3–5% on large workloads

### Changed

- Refactored `GetDictRefs()` to use `ConcurrentDictionary<string, DictRefs>` for on-demand caching of dictionary
  reference sequences.
- Replaced raw string keys with strongly typed `OpenCCConfig` enum for better safety and clarity.
- Punctuation-based dictionary inclusion is now determined by a `bool punctuation` flag and handled directly in round 1
  or 2 as appropriate.

### Removed

- Removed legacy `Lazy<RoundList>` and static config-based dispatch logic in favor of centralized `DictRefs` cache.

### Performance (Earlier)

- Conversion throughput improved significantly thanks to caching of parsed `DictRefs`.
- Benchmarks show **stable linear scaling** across all input sizes with minimal jitter.

#### 🧪 Benchmark Summary (BM_Convert_Sized)

| Size      | Mean      | StdDev   | Allocated |
|-----------|-----------|----------|-----------|
| 100       | 11.22 µs  | 0.034 µs | 22.88 KB  |
| 1,000     | 175.01 µs | 0.48 µs  | 227.83 KB |
| 10,000    | 509.09 µs | 16.10 µs | 1.94 MB   |
| 100,000   | 9.74 ms   | 0.40 ms  | 21.47 MB  |
| 1,000,000 | 89.26 ms  | 2.96 ms  | 221.78 MB |

> 💡 Throughput holds consistently with predictable memory allocation and no major GC overhead until 1M input size. Gen2
> activity only appears at very large scale, indicating efficient memory reuse.

---

## [1.0.2] – 2025-07-07

### Changed

- Optimized dictionary segment replacement using thread-local StringBuilder caching.
- Improved memory efficiency by using ArrayPool<char> for dictionary key generation.
- Enhanced Chinese text code detection with refined UTF-8 byte-range heuristics.
- Fixed a minor issue in dictionary candidate evaluation logic.
- Tuned the parallel segment conversion threshold for better scalability on multi-core systems.
- Added a warm-up pipeline to eliminate lazy-loading overhead during the first conversion.
- Preloaded roundlist caches for faster and more consistent conversion performance.
- Improved CLI and GUI responsiveness by reducing first-call latency.

---

## [1.0.1] – 2025-06-20

### Added

- Add functions to `get`, `set` and `validate` conversion Config.

---

## [1.0.0] – 2025-06-02

### Added

- Initial release of `OpenccNetLib` on Nuget.
    - A fast and efficient .NET library for converting Chinese text.  
      Offering support for `Simplified ↔ Traditional, Taiwan, Hong Kong, and Japanese Kanji variants`.  
      Built with inspiration from `OpenCC`, this library is designed to integrate seamlessly into modern .NET projects  
      with a focus on `performance and minimal memory usage`.
- Supported standard OpenCC configs:
    - `s2t`, `s2tw`, `s2twp`, `s2hk`, `t2s`, `tw2s`, `tw2sp`, `hk2s`, `jp2t`, `t2jp`
- Support using `custom dictionary`.

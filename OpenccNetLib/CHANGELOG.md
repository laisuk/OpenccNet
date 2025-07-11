﻿# Changelog

All notable changes to this project will be documented in this file.

This project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html) and uses the [Keep a Changelog](https://keepachangelog.com/en/1.0.0/) format.

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

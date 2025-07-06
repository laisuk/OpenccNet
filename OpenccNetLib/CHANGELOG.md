# Changelog

All notable changes to this project will be documented in this file.

This project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html) and uses the [Keep a Changelog](https://keepachangelog.com/en/1.0.0/) format.

---

## [1.0.2] – 2025-07-06
### Changed
- Optimized dictionary cache.
- Optimized Chinese text code detection.
- Dictionary candidates minor fix.

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

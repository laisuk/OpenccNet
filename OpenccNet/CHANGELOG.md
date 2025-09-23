# Changelog

All notable changes to this project will be documented in this file.

This project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html) and uses the [Keep a Changelog](https://keepachangelog.com/en/1.0.0/) format.

---

## [1.1.0] - 2025-08-18

### Changed

- Update `OpenccNetLib` to v1.1.0

---

## [1.0.3] - 2025-07-29
### Added
- Add builder factory for OfficeConverter
- Add `IsValidOfficeFormat()` validation method
- Add support for old Epub format that uses HTML conversion

---

## [1.0.2] – 2025-07-10
### Added
- Added support for getting, setting, and validating OpenCC conversion configurations.
- Added structured document conversion for Office-style formats (`.docx`, `.xlsx`, `.pptx`, `.odt`, `.ods`, `.odp`, `.epub`).
- Added `--format`, `--keep-font`, and `--auto-ext` options to `convert` command.

### Changed
- Optimized dictionary
- Migrated CLI to `System.CommandLine` beta 5 for long-term stability and API consistency.
- Refactored option declaration and handler wiring to follow new beta 5 conventions.
- Improved CLI argument validation and user feedback consistency.

---

## [1.0.1] – 2025-06-16
### Added
- Initial release of `OpenccNet` CLI tool.
- a fast, Unicode-aware, OpenCC-powered document converter.  
  It supports conversion of plain text and from Simplified to Traditional Chinese and vice versa.
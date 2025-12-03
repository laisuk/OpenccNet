# Changelog

All notable changes to this project will be documented in this file.

This project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html) and uses
the [Keep a Changelog](https://keepachangelog.com/en/1.0.0/) format.

---

## [1.4.0] - 2025-12-03

### Added

- **New `pdf` subcommand** (`openccnet pdf ...`)  
  Enables direct PDF-to-text conversion using PdfPig (pure C#, no native PDFium needed).  
  Supports:
    - `--input/-i`     PDF file path
    - `--output/-o`    UTF-8 text output
    - `--config/-c`    Opencc conversion mode (s2t, t2s, s2tw, etc.)
    - `--punct/-p`     Punctuation conversion
    - `--header/-H`    Insert page headers (`=== [Page x/y] ===`)
    - `--reflow/-r`    CJK paragraph reflow logic
    - `--compact`      Compact reflow mode  
      Includes a cross-platform, single-line dynamic progress bar for smooth UX.

- **`--unescape` flag for `dictgen` CLI**  
  Allows generating JSON dictionaries in *unescaped* form  
  (`openccnet dictgen -f json --unescape`)  
  making multilingual lexicons easier to inspect and edit.

### Changed

- Updated to **System.CommandLine 2.0.0** (final stable release).  
  Refactored all subcommands (`convert`, `office`, `dictgen`, `pdf`) to use the new API  
  for cleaner structure, better validation, and improved long-term extensibility.

### Notes

- PDF extraction backend uses **PdfPig**, ensuring full cross-platform compatibility  
  without requiring native dependencies.
- CLI architecture continues to use a modular subcommand pattern,  
  making new feature integration simple and maintainable.

---

## [1.3.1] - 2025-11-01

### Changed

- Update `OpenccNetLib` to version 1.3.1

---

## [1.3.0] - 2025-10-20

### Changed

- Update `OpenccNetLib` to version 1.3.0

---

## [1.2.0] - 2025-10-01

### Changed

- Update `OpenccNetLib` to version 1.2.0

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
- Added structured document conversion for Office-style formats (`.docx`, `.xlsx`, `.pptx`, `.odt`, `.ods`, `.odp`,
  `.epub`).
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
# Changelog

All notable changes to this project will be documented in this file.

This project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html) and uses
the [Keep a Changelog](https://keepachangelog.com/en/1.0.0/) format.

---

## [1.7.0] - Unreleased

### Changed

- Update DeTofu data table.
- Adapted custom dict token to newly added instance custom dictionary.

---

## [1.6.2] - 2026-08-15

### Changed

- CLI: Added an experimental Kangxi Radical normalization step for the `pdf` command to repair PdfPig extraction that
  emits Kangxi Radical code points instead of ordinary CJK ideographs. Normalization is applied immediately after PDF
  extraction and before CJK paragraph reflow, improving list, heading, and paragraph detection while remaining isolated
  from the core `OpenccNetLib` Unicode compatibility normalization.
- CLI: `-D` / `--custom-dict` now delegates the shared `<slot>:<append|override>:<path>` token grammar to
  `OpenccNetLib.CustomDictSpec.Parse(...)`, including strict slot validation and colon-preserving paths.
- CLI: Centralized custom dictionary parsing, file validation, configuration help, slot help, path validation, output
  resolution, and error reporting into shared `CliUtils` helpers, reducing duplicated logic across commands.
- CLI: Refactored `convert`, `office`, and `pdf` commands to reuse the shared custom dictionary pipeline and validation
  helpers while preserving existing command behavior.
- CLI: Replaced the duplicated temporary-directory Office/EPUB conversion pipeline with direct delegation to
  `OpenccNetLib.OfficeDocConverter`; supported-format validation now comes from the library, and CLI output inherits the
  library's streaming file conversion, temporary-package validation, and safe output-publication behavior.
- CLI: Simplified the `office` command by removing the internal `OfficeConverterBuilder` layer and invoking
  `OfficeConverter` directly, reducing command orchestration complexity while preserving existing behavior.
- CLI: Refactored the `pdf` command into a clearer processing pipeline with dedicated helpers for validation,
  extraction, Kangxi Radical normalization, reflow, conversion, and output, while keeping the extraction →
  normalization → reflow → conversion workflow easy to follow. Extraction-only mode now skips OpenCC initialization
  entirely.

---

## [1.6.1] - 2026-07-15

### Add

- CLI: added `t2hkp` and `hk2tp` as supported direct Hong Kong phrase conversion configurations.
- CLI: convert/office/pdf/dictgen - added feature `--custom-dict` to enable custom conversion dictionary slot.
- OpenccNetLib: added CJK Compatibility Ideograph normalization through `CompatIdeographs` and
  `Opencc.NormalizeCompat(...)` for optional pre-processing before conversion.

### Changed

- CLI: Optimized `OpenccNet office` subcommand.
- Reflow: Allow commas in title headings when they appear within the first 20 characters.
- Reflow: Handle standalone dialog closer line and simple list starter in reflow finalizer.
- Update `OpenccNetLib` to `v1.6.1`.

---

## [1.6.0] - 2026-06-18

### Added

- Added optional DeTofu display-compatibility fallback support for rare non-BMP CJK extension characters.
- Added `DeTofuLevel`, `DeTofuMap`, `OpenCC.DeTofu(...)`, and `OpenCC.DeTofuWithCustomFile(...)`.
- Added support for loading custom DeTofu fallback mappings from UTF-8 text files.
- Added support for post-load DeTofu customization through custom fallback pairs and custom fallback files.
- Added CLI support for `--detofu` and `--detofu-file`.
- Added XML documentation, examples, and contract documentation for DeTofu APIs.

### Changed

- Update OpenccNetLib to v1.6.0.
- Aligned Japanese Shinjitai dictionary bundles with upstream OpenCC commit `93ee7f7`.

### Breaking Changes

- Custom dictionary bundles must include the new `JPShinjitaiCharactersRev.txt` / `jps_characters_rev` slot.
  `JPVariants.txt` and `JPVariantsRev.txt` are no longer part of the active JP conversion schema.

### Notes

- DeTofu is a display-compatibility pass and does not modify OpenCC conversion dictionaries, phrase matching, regional
  variant selection, script detection, or punctuation conversion.
- Custom fallback files and custom fallback pairs override built-in mappings when the same tofu-risk character is
  provided.
- Characters without a built-in or custom fallback mapping are preserved unchanged, even when they belong to an enabled
  CJK extension block.

---

## [1.5.1] - 2026-05-25

### Changed

- Subcommand `convert` now preserves original stdout newline behavior during piped or redirected output.
- Improved CLI stream handling by separating conversion payload (`stdout`) from prompts and status messages (`stderr`).
- Interactive stdin prompt is now shown only for terminal sessions and suppressed for redirected/piped input.
- Enhanced `dictgen --base-dir` handling to resolve relative paths from `AppContext.BaseDirectory`, accept absolute
  paths, validate the source directory before generation, and report missing dictionary files cleanly.
- `dictgen` now loads the source dictionaries once and passes the resulting `DictionaryMaxlength` instance into the
  selected output writer, ensuring `zstd`, `cbor`, `json`, and `json --unescape` outputs are generated from the same
  resolved dictionary set.
- Update OpenccNetLib to v1.5.1

---

[1.5.0] - 2026-05-07

### Changed

- Update OpenccNetLib to v1.5.0

---

## [1.4.2] - 2026-04-08

### Changed

- Update dictionary data
- Optimized `ReflowHelper`
- Update `OpenccNetLib` to v1.4.2

### Fixed

- Fixed XLSX conversion to also process worksheet inline strings (`t="inlineStr"`), preventing missed text conversion in
  hybrid workbooks that contain both `shared strings` and `inline strings`.

---

## [1.4.1] - 2026-01-25

### Changed

- Optimized CJK Reflow in CLI `OpenccNet pdf`
- Update `OpenccNetLib` to v1.4.1

---

## [1.4.0] - 2025-12-16

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

- Added quiet mode in OpenccNet CLI `--quiet/-q`

### Changed

- Updated to **System.CommandLine 2.0.1** (final stable release).  
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

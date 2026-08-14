# OpenccNet

[![NuGet](https://img.shields.io/nuget/v/OpenccNetLib.svg)](https://www.nuget.org/packages/OpenccNetLib/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/OpenccNetLib.svg?label=downloads&color=blue)](https://www.nuget.org/packages/OpenccNetLib/)
[![Latest Downloads](https://img.shields.io/github/downloads/laisuk/OpenccNet/latest/total.svg)](https://github.com/laisuk/OpenccNet/releases/latest)
[![License](https://img.shields.io/github/license/laisuk/OpenccNet.svg)](https://github.com/laisuk/OpenccNet/blob/master/LICENSE)
[![Release](https://github.com/laisuk/OpenccNet/actions/workflows/release.yml/badge.svg)](https://github.com/laisuk/OpenccNet/actions/workflows/release.yml)

**OpenccNetLib** is a fast and efficient .NET library for converting Chinese text, offering support for Simplified ↔
Traditional, Taiwan, Hong Kong, and Japanese Kanji variants. Built with inspiration
from [OpenCC](https://github.com/BYVoid/OpenCC), this library is designed to integrate seamlessly into modern .NET
projects with a focus on performance and minimal memory usage.

## Table of Contents

- [Features](#features)
- [Installation](#installation)
- [Usage](#usage)
- [Office Document & EPUB Conversion](#-office-document--epub-conversion)
    - [Supported formats](#-supported-formats)
    - [I/O Model Comparison](#io-model-comparison)
    - [Example: Pure In-Memory Conversion from Bytes](#-example-pure-in-memory-conversion-from-bytes)
    - [Backward-Compatible String Overload](#-backward-compatible-string-overload)
    - [Async API](#-async-api)
    - [File-Based Streaming Conversion](#-file-based-streaming-conversion)
    - [Package Processing and Memory Model](#-package-processing-and-memory-model)
        - [Format-specific behavior](#format-specific-behavior)
    - [EPUB Packaging](#-epub-packaging)
    - [Validation and Error Handling](#-validation-and-error-handling)
    - [Unit Tested](#-unit-tested-mstest)
    - [Why This Matters](#-why-this-matters)
- [Performance](#performance)
- [API Reference](#api-reference)
- [Dictionary Data](#dictionary-data)
- [Add-On CLI Tools](#add-on-cli-tools-separated-from-openccnetlib)
- [Usage Notes - `OpenccNet pdf`](#usage-notes--openccnet-pdf)
- [Project That Use OpenccNetLib](#project-that-use-openccnetlib)
- [License](#license)

## Features

- Fast, multi-stage Chinese text conversion using prebuilt dictionary unions  
  (optimized with static caching and zero-allocation hot paths)
- Supports:
    - Simplified ↔ Traditional Chinese
    - Taiwan Traditional (T) ↔ Simplified / Traditional
    - Hong Kong Traditional (HK) ↔ Simplified / Traditional
    - Japanese Kanji Shinjitai ↔ Traditional Kyujitai
- Accurate handling of **Supplementary Plane CJK (U+20000+)** characters  
  (correct surrogate-pair detection and matching)
- Optional punctuation conversion

- Thread-safe conversion core with immutable shared dictionaries; suitable for high-throughput parallel processing when
  converters are not reconfigured concurrently.

- **Office document & EPUB conversion**:

    - `.docx` (Word), `.xlsx` (Excel), `.pptx` (PowerPoint), `.odt`, `.ods`, `.odp`, `.epub`
    - Pure in-memory `byte[] → byte[]` APIs for web, server, IPC, and memory-oriented workflows
    - Separate streaming file APIs for desktop, CLI, and large-document workflows
    - Converts only targeted XML/XHTML content while streaming non-target assets entry by entry
    - EPUB-compliant rebuild with `mimetype` first and uncompressed
    - Optional punctuation conversion and font preservation
    - Async wrappers plus validated, safe file-output publication

- **.NET Standard 2.0 compatible** (.NET Core 2.0+, .NET 5/6/7/8/9/10 and later), with an optimized .NET 9.0+
  implementation path  
  (cross-platform: Windows, Linux, macOS; usable from .NET implementations supporting .NET Standard 2.0)

## Installation

- Add the library to your project via NuGet or reference the source code directly.
- Add required dependencies of dictionary files to library root.
    - `dicts\dictionary_maxlength.zstd` Default dictionary file.
    - `dicts\*.*` Others dictionary files for different configurations.

Install via NuGet:

```bash
dotnet add package OpenccNetLib
```

Or, clone and include the source files in your project.

## Usage

### Basic Example

```csharp
using OpenccNetLib;

// Recommended: use the enum-based constructor
var opencc = new Opencc(OpenccConfig.S2T); // Simplified → Traditional

string traditional = opencc.Convert("汉字转换测试");
Console.WriteLine(traditional);
// Output: 漢字轉換測試
```

Or, using the legacy string-based configuration:

```csharp
using OpenccNetLib;
var opencc = new Opencc("s2t"); // Simplified to Traditional 
string traditional = opencc.Convert("汉字转换测试"); 
Console.WriteLine(traditional);
// Output: 漢字轉換測試
```

---

### Supported Configurations

| Config | Description                                     |
|--------|-------------------------------------------------|
| s2t    | Simplified → Traditional                        |
| t2s    | Traditional → Simplified                        |
| s2tw   | Simplified → Traditional (Taiwan)               |
| tw2s   | Traditional (Taiwan) → Simplified               |
| s2twp  | Simplified → Traditional (Taiwan, idioms)       |
| tw2sp  | Traditional (Taiwan, idioms) → Simplified       |
| s2hkp  | Simplified → Traditional (Hong Kong phrases)    |
| hk2sp  | Traditional (Hong Kong phrases) → Simplified    |
| t2hkp  | Traditional → Traditional (Hong Kong, phrases)  |
| hk2tp  | Traditional (Hong Kong, phrases) → Traditional  |
| s2hk   | Simplified → Traditional (Hong Kong)            |
| hk2s   | Traditional (Hong Kong) → Simplified            |
| t2tw   | Traditional → Traditional (Taiwan)              |
| tw2t   | Traditional (Taiwan) → Traditional              |
| t2twp  | Traditional → Traditional (Taiwan, idioms)      |
| tw2tp  | Traditional (Taiwan, idioms) → Traditional      |
| t2hk   | Traditional → Traditional (Hong Kong)           |
| hk2t   | Traditional (Hong Kong) → Traditional           |
| t2jp   | Traditional Kyujitai → Japanese Kanji Shinjitai |
| jp2t   | Japanese Kanji Shinjitai → Traditional Kyujitai |

### Example: Convert with Punctuation

```csharp
var opencc = new Opencc("s2t"); 
string result = opencc.Convert("“汉字”转换。", punctuation: true);
Console.WriteLine(result);
// Output: 「漢字」轉換。
```

### Example: Hong Kong Phrase Conversion

```csharp
var cc = new Opencc(OpenccConfig.S2Hkp);
Console.WriteLine(cc.Convert("别随便录影侵犯个人隐私权"));
// 別隨便錄影侵犯個人私隱權
```

### Example: Switching Config Dynamically

```csharp
using OpenccNetLib;

var opencc = new Opencc("s2t");  // Or: var opencc = new Opencc(OpenccConfig.S2T);

// Initial conversion
string result = opencc.Convert("动态切换转换方式");
Console.WriteLine(result);  // Output: 動態切換轉換方式

// Switch config using string
opencc.Config = "t2s";  // Also valid: opencc.SetConfig("t2s")
result = opencc.Convert("動態切換轉換方式");
Console.WriteLine(result);  // Output: 动态切换转换方式

// Switch config using enum (recommended for safety and autocomplete)
opencc.SetConfig(OpenccConfig.S2T);
result = opencc.Convert("动态切换转换方式");
Console.WriteLine(result);  // Output: 動態切換轉換方式

// Invalid config falls back to "s2t"
opencc.Config = "invalid_config";
Console.WriteLine(opencc.GetLastError());  // Output: Invalid config provided: invalid_config. Using default 's2t'.
```

> Thread-safety note: `Opencc` instances should not be reconfigured while they are being used by other threads. For
> parallel conversion, create one instance per configuration and treat it as immutable, or use direct conversion
> methods.
> `GetLastError()` is instance-level diagnostic state and should not be shared across threads.

#### 💡 Tips

- Use `OpenccConfig` enum for compile-time safety and IntelliSense support.
- Use `GetLastError()` to check if fallback occurred due to an invalid config.
- You can also validate config strings with `Opencc.IsValidConfig("t2tw")`.

---

### Direct API Methods

You can also use direct methods for specific conversions:

```csharp
using OpenccNetLib;
var opencc = new Opencc();
opencc.S2T("汉字");  // Simplified to Traditional    
opencc.T2S("漢字");  // Traditional to Simplified     
opencc.S2Tw("汉字"); // Simplified to Taiwan Traditional    
opencc.T2Jp("漢字"); // Traditional to Japanese Kanji   
// ...and more
```

### Preserve IDS expressions

IDS preservation is disabled by default. Enable it when working with Unicode Ideographic Description Sequences (IDS).
Complete IDS chunks are preserved, while surrounding normal text is still converted.

```csharp
using OpenccNetLib;

var cc = new Opencc(OpenccConfig.T2S);

Console.WriteLine(cc.Convert("⿰氵漢"));
// ⿰氵汉

cc.SetPreserveIds(true);

Console.WriteLine(cc.Convert("⿰氵漢"));
// ⿰氵漢

Console.WriteLine(cc.Convert("測試⿰氵漢文本"));
// 测试⿰氵漢文本
```

The same option can be set as a property:

```csharp
var cc = new Opencc("t2s")
{
    IsPreserveIds = true
};
```

### CJK Compatibility Ideograph and Unicode Normalization

OpenccNetLib provides two complementary Unicode normalization layers for Chinese/CJK text:

* `NormalizeCompat(...)` / `CompatIdeographs` normalizes **CJK Compatibility Ideographs** using the built-in Unicode
  decomposition mappings for the CJK Compatibility Ideograph ranges.
* `NormalizeUnicodeCompat(...)` applies the curated **extended Chinese Unicode compatibility table**. It targets
  additional Kangxi radicals, CJK radical variants, legacy Chinese glyph forms, compatibility punctuation, and known
  text-extraction artifacts outside the normal compatibility-ideograph pass.

The two tables serve different purposes. `NormalizeUnicodeCompat(...)` is **not a replacement or superset** of
`NormalizeCompat(...)`. Use `NormalizeCompat(...)` when compatibility ideographs are the concern, and enable its
extended mode when both normalization layers should be applied together.

These operations are Unicode/text normalization, not OpenCC linguistic conversion. They do not modify OpenCC
dictionaries, phrase matching, regional variant selection, script detection, or OpenCC punctuation conversion. They are
also not intended to be a general-purpose Unicode NFKC implementation.

Both normalization tables intentionally use a strict **one Unicode scalar → one Unicode scalar** mapping contract. A
normalization entry may substitute the scalar representation, but it never expands or contracts the scalar sequence.
This keeps character positions stable for diffing, offsets, selections, diagnostics, and other position-sensitive text
processing. Length-changing rewrites belong to a separate conversion or extraction-repair layer rather than these
normalization tables.

For the curated extended table, source keys are additionally required to be **non-ASCII**. ASCII source mappings are
rejected when the table is loaded. This prevents compatibility normalization from accidentally rewriting structural
syntax such as `<`, `>`, `&`, `"`, `'`, `/`, or `=`, protecting HTML/XML, OpenXML-based Office documents, subtitle
formats, and other structured text from malformed output.

Targets may still be ASCII when that is the correct-normalized representation; the restriction applies only to source
keys.

For converted text, the recommended order is:

1. Normalize the input with `NormalizeCompat(...)`, optionally enabling extended normalization.
2. Run normal OpenCC conversion with `Convert(...)`.
3. Optionally run DeTofu on the converted result for display fallback.

#### CJK Compatibility Ideographs

The default `NormalizeCompat(...)` pass handles CJK Compatibility Ideographs only:

```csharp
using OpenccNetLib;

var cc = new Opencc();

Console.WriteLine(cc.NormalizeCompat("天龍八部書裡的喬峰是契丹人"));
// Output: 天龍八部書裡的喬峰是契丹人

Console.WriteLine(cc.NormalizeCompat("abc天龍八部書裡的喬峰是契丹人123"));
// Output: abc天龍八部書裡的喬峰是契丹人123
```

This conservative pass is useful before OpenCC conversion because compatibility ideographs can otherwise prevent
dictionary keys from matching their ordinary unified-ideograph forms.

#### Extended Chinese Unicode normalization

For Chinese text that also needs the curated extended normalization table, enable the extended mode:

```csharp
using OpenccNetLib;

var cc = new Opencc();

Console.WriteLine(
    cc.NormalizeCompat("⾣〸ム敻耈‧︰﹐﹑﹔﹕﹖﹗", extended: true));
// Output: 酉十厶夐耇·：，、；：？！
```

The extended table includes carefully selected forms useful for Chinese text processing and text extraction, including
compatibility punctuation such as `‧ → ·`, `︰ → ：`, `﹐ → ，`, `﹑ → 、`, `﹔ → ；`, `﹕ → ：`,
`﹖ → ？`, and `﹗ → ！`, plus conservative extraction-artifact repairs such as `⸺ → —`. Normalizing these punctuation forms
can improve downstream CJK paragraph reflow and sentence splitting; normalizing the middle dot also helps preserve
translated Western personal names during splitting.

The extended table is scalar-preserving by design. For example, `⸺ → —` is allowed, while a visually similar
length-changing rewrite such as `⸺ → ——` is intentionally rejected. Built-in mapping data is validated when loaded, and
malformed rows fail fast instead of being silently skipped.

The extended table is intentionally curated rather than blindly applying every compatibility or historical glyph
mapping. Mappings where both source and target remain valid Chinese unified ideographs are excluded when normalization
would amount to choosing one legitimate Han form over another.

`NormalizeUnicodeCompat(...)` exposes the extended table directly when only that layer is wanted, for example when
cleaning text extracted from PDF or other document formats:

```csharp
using OpenccNetLib;

Console.WriteLine(
    Opencc.NormalizeUnicodeCompat("⾣〸ム敻耈‧︰﹐"));
// Output: 酉十厶夐耇·：，
```

The extended normalization table is curated for **Chinese/CJK-Chinese text**. Japanese- and Korean-specific
compatibility normalization is outside its scope. Characters not covered by the selected normalization table are
preserved unchanged.

Normalize before OpenCC conversion:

```csharp
using OpenccNetLib;

var cc = new Opencc(OpenccConfig.T2S);

string normalized =
    cc.NormalizeCompat("天龍八部書裡的喬峰是契丹人", extended: true);
string converted = cc.Convert(normalized);

Console.WriteLine(converted);
// Output: 天龙八部书里的乔峰是契丹人
```

#### Direct compatibility-ideograph normalizer

`CompatIdeographs` remains available for callers that need direct access to the reusable compatibility-ideograph
normalizer:

```csharp
using OpenccNetLib;

var compat = CompatIdeographs.Builtin();

Console.WriteLine(
    compat.Normalize("天龍八部書裡的喬峰是契丹人"));
// Output: 天龍八部書裡的喬峰是契丹人
```

`CompatIdeographs` also supports custom mapping text for advanced callers:

```csharp
using OpenccNetLib;

var compat = CompatIdeographs.FromText("金\\t金\\n");

Console.WriteLine(compat.Normalize("金"));
// Output: 金
```

#### Compatibility normalization APIs:

```text
Opencc.NormalizeCompat(...)
Opencc.NormalizeUnicodeCompat(...)

CompatIdeographs.Builtin()
CompatIdeographs.FromText(...)
CompatIdeographs.Normalize(...)
CompatIdeographs.NormalizeScalar(...)
CompatIdeographs.NormalizeChar(...)
CompatIdeographs.NormalizeInPlace(...)
CompatIdeographs.NormalizeCompatIdeographs(...)
```

`NormalizeCompat(...)` is the normal entry point for CJK Compatibility Ideograph normalization. Its extended mode adds
the curated Chinese Unicode compatibility table in the same preprocessing operation. `NormalizeUnicodeCompat(...)`
exposes that extended table independently. Unmapped characters are preserved unchanged.

> **Advanced users**: `dicts/Unicode_Compatibility.txt` may be customized to add project-specific extended Chinese
> Unicode normalization mappings used by `NormalizeUnicodeCompat (...)` and `NormalizeCompat (..., extended: true)`.
> This
> customization applies only to the extended Unicode compatibility table. The built-in CJK Compatibility Ideograph
> mappings are separate and are not intended to be customized through this file.
>
> Dictionary Format:
> ```
> # source<TAB>target
> ‧	·
> ︰	：
> ⸺	—
> ```
>
> Both `source` and `target` must contain **exactly one valid Unicode scalar value**. Entries that are malformed or that
> expand/contract the scalar sequence are rejected when the table is loaded.
>

### DeTofu Display Compatibility

DeTofu is an optional display-compatibility pass for rare non-BMP CJK extension characters. Some systems, browsers,
document viewers, e-book readers, and mobile platforms do not have complete font coverage for these characters, so they
may render as tofu boxes or missing glyphs.

DeTofu is **not** OpenCC linguistic conversion. It does not modify OpenCC dictionaries, phrase matching, regional
variant selection, script detection, or punctuation conversion. For converted text, the recommended order is:

1. Optionally normalize CJK Compatibility Ideographs before conversion.
2. Run normal OpenCC conversion with `Convert(...)`.
3. Run DeTofu on the converted result.

Normal OpenCC conversion:

```csharp
using OpenccNetLib;

var cc = new Opencc(OpenccConfig.S2T);
string converted = cc.Convert("汉字转换测试");
Console.WriteLine(converted);
// Output: 漢字轉換測試
```

OpenCC conversion followed by DeTofu:

```csharp
using OpenccNetLib;

var cc = new Opencc(OpenccConfig.T2S);
string converted = cc.Convert("驂𬴂");
string displaySafe = cc.DeTofu(converted, DeTofuLevel.ExtB);

Console.WriteLine(displaySafe);
```

Direct utility usage:

```csharp
using OpenccNetLib;

string displaySafe = DeTofu.Convert("驂𬴂", DeTofuLevel.ExtB);
Console.WriteLine(displaySafe);
```

DeTofu APIs:

```text
DeTofu.ParseLevel(...)
DeTofu.Convert(...)
DeTofuMap.Builtin(...)
DeTofuMap.WithCustomFile(...)
DeTofuMap.WithCustomPairs(...)
Opencc.DeTofu(...)
Opencc.DeTofuWithCustomFile(...)
Opencc.DeTofuWithCustomPairs(...)
```

Reusable map usage:

```csharp
using System.Collections.Generic;
using OpenccNetLib;

var map = DeTofuMap
    .Builtin(DeTofuLevel.ExtB)
    .WithCustomPairs(new[]
    {
        new KeyValuePair<string, string>("𣭲", "氄")
    });

string displaySafe = map.Convert("𣭲");
Console.WriteLine(displaySafe);
```

Custom in-memory pairs usage:

```csharp
using OpenccNetLib;

var cc = new Opencc();

var pairs = new Dictionary<string, string>
{
    ["𣭲"] = "氂",
    ["𬴂"] = "騑"
};

var output = cc.DeTofuWithCustomPairs(
    "𣭲毛 骖𬴂",
    DeTofuLevel.ExtB,
    pairs);

Console.WriteLine(output);
// 氂毛 骖騑
```

In-memory pairs are supplied as `IEnumerable<KeyValuePair<string, string>>`, where each key is a tofu-risk character and
each value is its display-compatible fallback character. Only the first Unicode scalar value from each key and value is
used, and null or empty keys and values are ignored. Pairs do not carry an extension column, so they are applied
directly to the selected map after the built-in mappings. Custom pairs override built-in mappings for the same tofu-risk
character. If duplicate keys are supplied, the later mapping wins according to enumeration order.

Custom fallback file usage:

```csharp
using OpenccNetLib;

var cc = new Opencc(OpenccConfig.T2S);
string converted = cc.Convert("驂𬴂");
string displaySafe = cc.DeTofuWithCustomFile(
    converted,
    DeTofuLevel.ExtB,
    "dicts/custom-tofu.txt");

Console.WriteLine(displaySafe);
```

Fallback files are UTF-8 text files with one mapping per line:

```text
# Format: tofu_char<TAB>fallback_char<TAB>extension
```

Example:

```text
# Custom DeTofu fallbacks
𣭲	氂	B
𬴂	騑	ExtC
```

Blank lines and lines beginning with `#` are ignored. The extension column accepts compact `B`-`I` values and legacy
`ExtB`-`ExtI` values.

Built-in mappings are loaded from `dicts/TSCharactersTofu.txt`. Custom files and custom pairs are applied after the
built-in mappings. File mappings override built-in mappings for the same tofu-risk character, and custom pairs do the
same. Later mappings override earlier mappings when the same tofu-risk character is provided.

Characters without built-in or custom fallback mappings are preserved unchanged, even if they belong to an enabled CJK
extension block. DeTofu is non-destructive: it never replaces unknown characters with `?`, `□`, `�`, or empty text.

`DeTofuLevel` is threshold-based:

| Level              | Replacement threshold |
|--------------------|-----------------------|
| `DeTofuLevel.ExtB` | ExtB and above        |
| `DeTofuLevel.ExtC` | ExtC and above        |
| `DeTofuLevel.ExtD` | ExtD and above        |
| `DeTofuLevel.ExtE` | ExtE and above        |
| `DeTofuLevel.ExtF` | ExtF and above        |
| `DeTofuLevel.ExtG` | ExtG and above        |
| `DeTofuLevel.ExtH` | ExtH and above        |
| `DeTofuLevel.ExtI` | ExtI only             |

### Error Handling

If an error occurs (e.g., invalid config), use:

```csharp
string error = opencc.GetLastError();
Console.WriteLine(error); // Output the last error message
```

### Language Detection

Detect if a string is Simplified, Traditional, or neither:

```csharp
using OpenccNetLib;
int result = Opencc.ZhoCheck("汉字"); // Returns 2 for Simplified, 1 for Traditional, 0 for neither
Console.WriteLine(result); // Output: 2 (for Simplified)
```

### User Custom Dictionaries

By default, OpenccNetLib uses the built-in Zstandard-compressed lexicon. For advanced custom dictionary workflows, build
or customize a `DictionaryMaxlength` instance, then activate it **before** creating `Opencc` instances.

```csharp
DictionaryMaxlength DictionaryLib.FromDicts(
    string relativeBaseDir = "dicts",
    IDictionary<DictSlot, string> overrides = null,
    IDictionary<DictSlot, string> appends = null)
```

OpenccNetLib follows the OpenCC lexicon structure. Custom dictionaries must attach to existing OpenCC dictionary slots
such as `DictSlot.STPhrases` or `DictSlot.TSPhrases`; dynamic generic slots such as `user_dict` are intentionally
rejected. Preserving the OpenCC dictionary topology keeps dictionary metadata, lookup acceleration structures, and
runtime plans deterministic and compatible.

Regional variant phrase slots are also customizable. `DictSlot.TWVariantsPhrases` is applied before
`DictSlot.TWVariants`, and `DictSlot.HKVariantsPhrases` is applied before `DictSlot.HKVariants`, so phrase exceptions
can protect a full term from later character-level regional variant mappings. These slots can be used with both append
and override custom dictionary APIs.

Direct Hong Kong phrase slots are customizable too. `DictSlot.HKPhrases` is used by `s2hkp` after
Simplified-to-Traditional conversion, and `DictSlot.HKPhrasesRev` is used by `hk2sp` before Traditional-to-Simplified
conversion.

#### Portable custom-dictionary specifications

A portable custom-dictionary token has this grammar:

```text
<slot>:<append|override>:<path>
```

Use `CustomDictSpec.Parse(...)` when a specification comes from configuration, a command line, or another portable
string source. Slot and mode matching is case-insensitive. Slot parsing is strict: numeric enum strings, unknown names,
and the obsolete `JPVariants` and `JPVariantsRev` slots are rejected. The parser splits the token into no more than
three fields, preserving Windows drive-letter paths and relative paths containing additional colons. It validates the
specification syntax but does not check whether the file exists; dictionary loading reports missing or unreadable files.

```csharp
using OpenccNetLib;

var parsed = CustomDictSpec.Parse(
    @"hkphrasesrev:append:data\my_hk_dict.txt");

var parsedDict = DictionaryLib.WithCustomDicts(
    DictionaryLib.New(),
    new[] { parsed });
```

Use `CustomDictSpec.FromFile(...)` when the slot and mode are already strongly typed in C#:

```csharp
var typed = CustomDictSpec.FromFile(
    DictSlot.HKPhrasesRev,
    @"data\my_hk_dict.txt",
    CustomDictMode.Append);

var typedDict = DictionaryLib.WithCustomDicts(
    DictionaryLib.New(),
    new[] { typed });
```

`Parse(...)` and `FromFile(...)` construct specifications; they do not load files or change the active dictionary. Apply
the resulting specifications with `DictionaryLib.WithCustomDicts(...)`. For custom files applied while loading a
complete OpenCC text dictionary directory, use `DictionaryLib.FromDicts(...)` with its strongly typed `appends` or
`overrides` dictionaries.

Canonical names and supported slots are discoverable without maintaining a separate list:

```csharp
foreach (var slot in DictSlotExtensions.ActiveSlots)
{
    Console.WriteLine(slot.ToCanonicalName());
}

var slotParsed = DictSlotExtensions.Parse("hkphrasesrev");
bool recognized = DictSlotExtensions.TryParse("HKPhrasesRev", out var slotTried);
```

The token representation is intentionally unified across the C#, Java, Rust, and Python OpenCC ecosystem. This release
provides the public C# `CustomDictSpec.Parse(...)` API and uses it for the C# CLI. A matching public library parser is
not claimed here for Java, Rust, or Python; those public API ports are separate work and should be treated as planned or
not yet ported unless the documentation for that language explicitly says otherwise. A CLI in another language may
already accept the shared token without exposing a public library parser.

#### File-level customization

Use `DictionaryLib.FromDicts()` when custom files should be applied while loading the OpenCC text dictionaries.

Use `appends` to load custom entries after the built-in dictionary in the selected slot. Appended entries use
"late-comer wins" behavior, so duplicate keys override earlier built-in mappings.

```csharp
using System.Collections.Generic;
using OpenccNetLib;

var dict = DictionaryLib.FromDicts(
    appends: new Dictionary<DictSlot, string>
    {
        [DictSlot.STPhrases] = "custom_st_phrases.txt",
        [DictSlot.TWVariantsPhrases] = "custom_tw_variant_phrases.txt"
    });

Opencc.UseCustomDictionary(dict);

var opencc = new Opencc("s2t");
Console.WriteLine(opencc.Convert("帕兰蒂尔是一家公司"));
```

Hong Kong phrase slots can be customized with the same API:

```csharp
var dict = DictionaryLib.FromDicts(
    appends: new Dictionary<DictSlot, string>
    {
        [DictSlot.HKPhrases] = "custom_hk_phrases.txt"
    });

Opencc.UseCustomDictionary(dict);

var cc = new Opencc(OpenccConfig.S2Hkp);
Console.WriteLine(cc.Convert("小女孩问：什么是个人隐私权？"));
// 妹丁問：什麽是個人私隱權？
```

For in-memory pairs, apply a post-load custom spec:

```csharp
var dict = DictionaryLib.New();

DictionaryLib.WithCustomDicts(
    dict,
    new[]
    {
        new CustomDictSpec
        {
            Slot = DictSlot.HKPhrases,
            Mode = CustomDictMode.Append,
            Pairs = new Dictionary<string, string>
            {
                ["小女孩"] = "妹丁",
                ["動畫片"] = "卡通片"
            }
        }
    });

Opencc.UseCustomDictionary(dict);

var cc = new Opencc(OpenccConfig.S2Hkp);
Console.WriteLine(cc.Convert("小女孩喜欢看动画片"));
// 妹丁喜歡看卡通片
```

#### Override an entire slot

Use `overrides` only when replacing the full content of an OpenCC dictionary slot with a complete custom dictionary.

```csharp
using OpenccNetLib;

var dict = DictionaryLib.FromDicts(
    overrides: new Dictionary<DictSlot, string>
    {
        [DictSlot.STPhrases] = "./company/STPhrases.txt"
    });

Opencc.UseCustomDictionary(dict);
```

#### Post-load customization

Use `DictionaryLib.WithCustomDicts()` when you already have a loaded `DictionaryMaxlength` provider and want to apply
additional slot-level changes.

```csharp
using System.Collections.Generic;
using OpenccNetLib;

var dict = DictionaryLib.New();

DictionaryLib.WithCustomDicts(
    dict,
    new CustomDictSpec[]
    {
        new CustomDictSpec
        {
            Slot = DictSlot.STPhrases,
            Mode = CustomDictMode.Append,
            Paths = new[] { "company_terms.txt", "product_terms.txt" },
            Pairs = new Dictionary<string, string>
            {
                ["帕兰蒂尔"] = "帕蘭蒂爾"
            }
        }
    });

Opencc.UseCustomDictionary(dict);
var opencc = new Opencc("s2t");
```

Post-load customization works with any already loaded provider, including `DictionaryLib.New()`, `FromDicts()`,
`FromJson()`, `FromCbor()`, or another customized `DictionaryMaxlength` instance.

Each `CustomDictSpec` targets one slot. `Paths` is optional and can contain multiple custom dictionary files. `Pairs` is
optional and contains in-memory entries. At least one of `Paths` or `Pairs` must be supplied. When both are supplied,
files are applied first in array order, then pairs are applied; later duplicate keys overwrite earlier entries, so pairs
win over file entries.

`CustomDictMode.Append` merges into the existing slot. `CustomDictMode.Override` replaces the whole target slot with the
merged result from that spec. Dictionary metadata and lookup acceleration structures are rebuilt automatically after
customization.

#### Exact in-memory fallback pairs

Use `CustomDictSpec.Pairs` for small, exact in-memory fallback pairs when an application needs project-local conversion
patches without restructuring the built-in OpenCC dictionary files.

This is especially useful for tofu-risk or CJK Extension Unicode cases where some target platforms may not render newer
characters correctly. Applications can provide temporary alternate mappings while keeping the built-in dictionary
topology unchanged.

```csharp
using System.Collections.Generic;
using OpenccNetLib;

var dict = DictionaryLib.New();

DictionaryLib.WithCustomDicts(
    dict,
    new CustomDictSpec[]
    {
        new CustomDictSpec
        {
            Slot = DictSlot.STPhrases,
            Mode = CustomDictMode.Append,
            Pairs = new Dictionary<string, string>
            {
                // Project-local fallback pairs for tofu-risk / Extension Unicode cases.
                // Keep these patches small, explicit, and easy to remove later.
                ["骖𬴂"] = "驂騑",
                ["𫜩合"] = "齧合",
                ["𫜩蘗吞针"] = "齧蘗吞針",

                // Normal custom phrase pairs may be mixed in as well.
                ["帕兰蒂尔"] = "帕蘭蒂爾"
            }
        }
    });

Opencc.UseCustomDictionary(dict);

var opencc = new Opencc("s2t");

Console.WriteLine(opencc.Convert("骖𬴂"));
Console.WriteLine(opencc.Convert("𫜩合"));
Console.WriteLine(opencc.Convert("帕兰蒂尔"));
```

This keeps the core dictionary structure unchanged while still allowing applications to patch specific high-risk entries
at load time.

| API                                               | Description                                    |
|---------------------------------------------------|------------------------------------------------|
| `DictSlot`                                        | Strongly typed OpenCC dictionary slot selector |
| `DictSlotExtensions.ActiveSlots`                  | Enumerate active supported slots               |
| `DictSlotExtensions.Parse(...)` / `TryParse(...)` | Strictly parse a canonical slot name           |
| `DictSlotExtensions.ToCanonicalName()`            | Format an active slot canonically              |
| `CustomDictSpec.Parse(...)`                       | Parse a portable custom-dictionary token       |
| `CustomDictSpec.FromFile(...)`                    | Construct a strongly typed single-file spec    |
| `CustomDictSpec.Slot`                             | Target slot                                    |
| `CustomDictSpec.Paths`                            | Custom dictionary files                        |
| `CustomDictSpec.Pairs`                            | In-memory dictionary entries                   |
| `CustomDictSpec.Mode`                             | `Append` or `Override`                         |
| `CustomDictMode.Append`                           | Merge into the existing slot                   |
| `CustomDictMode.Override`                         | Replace the whole slot                         |

#### Custom dictionary file format

Custom dictionary files are UTF-8 text files. Each entry is written as `phrase<TAB>translation`; blank lines are
ignored, comments are supported, and duplicate keys use late-comer wins behavior.

```text
# Company terminology
帕兰蒂尔	帕蘭蒂爾
人工智能	人工智慧
```

Short append example:

```csharp
var dictionary = DictionaryLib.FromDicts(
    appends: new Dictionary<DictSlot, string>
    {
        [DictSlot.STPhrases] = "custom-st-phrases.txt"
    });

Opencc.UseCustomDictionary(dictionary);
var opencc = new Opencc("s2t");
```

#### Supported dictionary slots

| DictSlot                        | Serialization Field       | Default File                   |
|---------------------------------|---------------------------|--------------------------------|
| `DictSlot.STCharacters`         | `st_characters`           | `STCharacters.txt`             |
| `DictSlot.STPhrases`            | `st_phrases`              | `STPhrases.txt`                |
| `DictSlot.STPunctuations`       | `st_punctuations`         | `STPunctuations.txt`           |
| `DictSlot.TSCharacters`         | `ts_characters`           | `TSCharacters.txt`             |
| `DictSlot.TSPhrases`            | `ts_phrases`              | `TSPhrases.txt`                |
| `DictSlot.TSPunctuations`       | `ts_punctuations`         | `TSPunctuations.txt`           |
| `DictSlot.TWPhrases`            | `tw_phrases`              | `TWPhrases.txt`                |
| `DictSlot.TWPhrasesRev`         | `tw_phrases_rev`          | `TWPhrasesRev.txt`             |
| `DictSlot.TWVariants`           | `tw_variants`             | `TWVariants.txt`               |
| `DictSlot.TWVariantsPhrases`    | `tw_variants_phrases`     | `TWVariantsPhrases.txt`        |
| `DictSlot.TWVariantsRev`        | `tw_variants_rev`         | `TWVariantsRev.txt`            |
| `DictSlot.TWVariantsRevPhrases` | `tw_variants_rev_phrases` | `TWVariantsRevPhrases.txt`     |
| `DictSlot.HKVariants`           | `hk_variants`             | `HKVariants.txt`               |
| `DictSlot.HKPhrases`            | `hk_phrases`              | `HKPhrases.txt`                |
| `DictSlot.HKVariantsPhrases`    | `hk_variants_phrases`     | `HKVariantsPhrases.txt`        |
| `DictSlot.HKVariantsRev`        | `hk_variants_rev`         | `HKVariantsRev.txt`            |
| `DictSlot.HKPhrasesRev`         | `hk_phrases_rev`          | `HKPhrasesRev.txt`             |
| `DictSlot.HKVariantsRevPhrases` | `hk_variants_rev_phrases` | `HKVariantsRevPhrases.txt`     |
| `DictSlot.JPSCharacters`        | `jps_characters`          | `JPShinjitaiCharacters.txt`    |
| `DictSlot.JPSCharactersRev`     | `jps_characters_rev`      | `JPShinjitaiCharactersRev.txt` |
| `DictSlot.JPSPhrases`           | `jps_phrases`             | `JPShinjitaiPhrases.txt`       |

Japanese Shinjitai dictionary layout follows upstream OpenCC commit `93ee7f7`: `JPShinjitaiCharacters.txt`
is the authoritative character mapping source, and `JPShinjitaiCharactersRev.txt` is the generated reverse dictionary
used by `t2jp`. `JPVariants.txt` and `JPVariantsRev.txt` are no longer part of the active dictionary schema. Users who
provide custom dictionary bundles, JSON, CBOR, or Zstd packs must regenerate those bundles or include the new non-empty
`JPShinjitaiCharactersRev.txt` / `jps_characters_rev` slot. The retired `DictSlot.JPVariants` and
`DictSlot.JPVariantsRev` enum members remain defined as obsolete compatibility sentinels with their original numeric
values. Custom dictionary APIs reject these inactive slots rather than silently redirecting their values to a different
dictionary.

#### Recommended usage

Use `appends` for company terms, product names, domain vocabulary, and temporary conversion fixes. Use `overrides` only
when maintaining a full proprietary replacement dictionary. Prefer following the upstream OpenCC lexicon structure
whenever possible.

Prefer activating a custom dictionary once during application startup, before conversions begin. The active provider is
process-wide and should be treated as the application's single source of truth. If it changes, conversions already in
progress may finish with the previous complete state; conversions started after the replacement is published use the new
provider, including calls made through existing `Opencc` instances. Avoid routine provider changes when concurrent calls
must all use the same dictionary snapshot.

This global provider design is intentional for performance: dictionary data, derived lookup metadata, and prepared
conversion state can be shared instead of duplicated per `Opencc` instance. Normal applications usually need only one
custom provider. Unit tests that mutate the global provider should not run in parallel with tests expecting the default
provider.

#### Why no `user_dict` slot?

OpenccNetLib intentionally preserves the OpenCC dictionary topology. Generic dynamic slots complicate conversion
ordering, derived dictionary metadata, and cached lookup state. Existing OpenCC slots already provide deterministic and
extensible customization points.

---

## 🆕 Office Document & EPUB Conversion

`OfficeDocConverter` supports two intentionally different I/O models for ZIP-based Office and EPUB containers.

`ConvertOfficeBytes(...)` accepts the complete package as `byte[]`. It opens and rebuilds the package in memory, then
returns a new `byte[]`. This is the pure in-memory API.

`ConvertOfficeFile(...)` uses a separate streaming filesystem path. It opens the input package with `FileStream` and
writes the rebuilt package to a sibling temporary file. The temporary package is validated before it is published to the
requested output path.

### ✔ Supported formats

| Format | Document type                          | Converted content                             |
|--------|----------------------------------------|-----------------------------------------------|
| `docx` | Microsoft Word (Office Open XML)       | `word/document.xml`                           |
| `xlsx` | Microsoft Excel (Office Open XML)      | Shared strings and worksheet inline strings   |
| `pptx` | Microsoft PowerPoint (Office Open XML) | Slides, notes, layouts, masters, and comments |
| `odt`  | OpenDocument Text                      | `content.xml`                                 |
| `ods`  | OpenDocument Spreadsheet               | `content.xml`                                 |
| `odp`  | OpenDocument Presentation              | `content.xml`                                 |
| `epub` | EPUB 2/3 e-book                        | XHTML, HTML, OPF, and NCX content             |

Optional punctuation conversion and font-name preservation are supported through the same APIs.

### I/O Model Comparison

| API                       | I/O and memory model                                                                                                                                    | Best suited for                                                |
|---------------------------|---------------------------------------------------------------------------------------------------------------------------------------------------------|----------------------------------------------------------------|
| `ConvertOfficeBytes(...)` | Caller supplies all package bytes. Source and rebuilt packages remain in managed memory. No temporary file or extraction directory is used.             | Web, server, IPC, database BLOB, and other in-memory pipelines |
| `ConvertOfficeFile(...)`  | Opens the source with `FileStream` and writes to a temporary sibling package. The complete package is not intentionally loaded into a managed `byte[]`. | Desktop, CLI, filesystem workflows, and large documents        |

Large packages can require significantly more managed memory with the byte API. This is especially relevant when they
contain large embedded fonts, images, or media. Prefer `ConvertOfficeFile(...)` when filesystem access is available and
the document may be large.

---

## 📦 Example: Pure In-Memory Conversion from Bytes

```csharp
using OpenccNetLib;

var opencc = new Opencc(OpenccConfig.S2T);

byte[] inputBytes = File.ReadAllBytes("sample.docx");

byte[] outputBytes = OfficeDocConverter.ConvertOfficeBytes(
    inputBytes,
    format: OfficeFormat.Docx,
    converter: opencc,
    punctuation: false,
    keepFont: true);

File.WriteAllBytes("output.docx", outputBytes);
```

`ConvertOfficeBytes(...)` itself is pure in-memory. The caller supplies the complete source package, and the method
returns a newly allocated rebuilt package. It does not create a temporary file or extraction directory.

`File.ReadAllBytes(...)` and `File.WriteAllBytes(...)` only demonstrate loading and saving. Callers may obtain or
consume the byte arrays from any source.

For example, the same API can be used with uploaded files, database blobs, network responses, embedded resources, or
other byte-stream workflows without creating intermediate document files.

---

## 🔁 Backward-Compatible String Overload

The original string-based format overload remains available:

```csharp
byte[] outputBytes = OfficeDocConverter.ConvertOfficeBytes(
    inputBytes,
    format: "docx",
    converter: opencc);
```

For new code, `OfficeFormat` is recommended for compile-time safety:

```csharp
byte[] outputBytes = OfficeDocConverter.ConvertOfficeBytes(
    inputBytes,
    OfficeFormat.Docx,
    opencc);
```

No public Office conversion API was removed when the byte-array pipeline became pure in-memory.

---

## ⚡ Async API

Async wrappers are available when synchronous conversion should not occupy the calling thread:

```csharp
var outputBytes = await OfficeDocConverter.ConvertOfficeBytesAsync(
    inputBytes,
    format: OfficeFormat.Docx,
    converter: opencc,
    punctuation: false,
    keepFont: true);
```

`ConvertOfficeBytesAsync(...)` uses `Task.Run(...)` around the synchronous byte path.
`ConvertOfficeFileAsync(...)` similarly uses `Task.Run(...)` around the synchronous streaming file path. The async APIs
do not merge the two I/O models or turn package processing into native asynchronous ZIP I/O.

Cancellation is honored before the background conversion task begins. Once synchronous package conversion is running, it
continues to completion.

String-format async overloads remain available for backward compatibility.

---

## 📁 File-Based Streaming Conversion

For file-to-file workflows, `OfficeDocConverter` provides a first-class streaming filesystem path:

```csharp
OfficeDocConverter.ConvertOfficeFile(
    "input.docx",
    "output.docx",
    format: OfficeFormat.Docx,
    converter: opencc);
```

And:

```csharp
await OfficeDocConverter.ConvertOfficeFileAsync(
    "input.docx",
    "output.docx",
    format: OfficeFormat.Docx,
    converter: opencc);
```

The file APIs are not wrappers around `ConvertOfficeBytes(...)`. They:

1. Open the input package directly with `FileStream`.
2. Process ZIP entries sequentially.
3. Materialize only selected text-bearing XML/XHTML entries as strings for OpenCC conversion.
4. Stream non-target assets from each input ZIP entry to its output ZIP entry.
5. Write the rebuilt package to a sibling temporary file.
6. Validate the completed temporary package.
7. Move or replace it into the requested output path only after validation succeeds.

The temporary file supports safe publication. It is not an extracted document tree, and no temporary extraction
directory is used. The temporary file is cleaned up if conversion fails.

`System.IO.Compression.ZipArchive` may decompress and recompress unchanged entries internally. The file path's memory
benefit is that it does not intentionally materialize the complete input or output package as a managed `byte[]`.

---

## 🔍 Package Processing and Memory Model

Both I/O models use the same entry-selection and text-conversion rules. Packages are processed entry by entry. Only a
selected XML/XHTML entry is materialized as a string for conversion.

The pure in-memory `ConvertOfficeBytes(...)` pipeline is:

```text
input byte[]
    ↓
MemoryStream
    ↓
ZipArchive (Read)
    ↓
process entries sequentially
    ├─ target XML/XHTML → read → OpenCC convert → write
    └─ other entries    → stream directly to output
    ↓
ZipArchive (Create)
    ↓
MemoryStream
    ↓
validate package
    ↓
output byte[]
```

The source package and rebuilt package are both memory-resident. The complete decompressed archive is not materialized
as one document tree, and no temporary file or extraction directory is used.

The streaming `ConvertOfficeFile(...)` pipeline is:

```text
input file → FileStream → ZipArchive (Read)
    ↓
process entries sequentially
    ├─ target XML/XHTML → read as string → OpenCC convert → write
    └─ other entries    → stream into rebuilt package
    ↓
sibling temporary ZIP → validate → move/replace output file
```

The complete input or output package is not intentionally held in a managed `byte[]`. Non-target assets include images,
embedded fonts, media, relationships, stylesheets, and metadata.

### Format-specific behavior

- **DOCX** — converts the main WordprocessingML document content.
- **XLSX** — converts shared strings and text inside worksheet `inlineStr` cells. Formulas and other worksheet
  structural data are left untouched.
- **PPTX** — converts text-bearing slide, notes, layout, master, and comment XML parts.
- **ODT / ODS / ODP** — converts the OpenDocument `content.xml` payload.
- **EPUB** — converts XHTML/HTML content together with OPF/NCX textual metadata.

When `keepFont` is enabled, relevant font declarations are temporarily protected with internal markers during text
conversion and restored before the XML/XHTML entry is written to the new package.

---

## 📚 EPUB Packaging

EPUB output follows the required ZIP packaging rule:

- `mimetype` is written as the **first ZIP entry**.
- `mimetype` is stored **without compression**.
- Remaining EPUB entries are written afterward using normal ZIP compression.
- Missing `mimetype` causes conversion to fail instead of producing a malformed EPUB.

These requirements are enforced by both the in-memory byte path and the streaming file path.

---

## 🛡 Validation and Error Handling

Generated Office/EPUB packages are reopened and validated as ZIP containers before conversion succeeds.

Invalid or corrupted input packages raise `InvalidOperationException`, preserving the underlying package/ZIP exception
as the inner exception where applicable.

For file APIs, output is written to a sibling temporary package. It is validated before being moved or replaced at the
requested destination. The temporary file is cleaned up on failure, so an existing valid output is not overwritten.

---

## 🧪 Unit Tested (MSTest)

The Office/EPUB conversion suite covers both real documents and synthetic package-level test cases, including:

- Real DOCX conversion and ZIP/package validation
- Strongly typed and legacy string format overloads
- XLSX shared-string conversion
- XLSX worksheet inline-string conversion
- Preservation of non-target binary ZIP entry payloads
- Pure `byte[] → byte[]` DOCX conversion
- EPUB XHTML conversion
- EPUB `mimetype` first-entry requirement
- EPUB uncompressed `mimetype` requirement
- Missing EPUB `mimetype` rejection
- Corrupted ZIP error propagation
- Failed conversion without overwriting an existing output file
- Atomic file-output cleanup

---

## 🚀 Why This Matters

- **Pure in-memory byte APIs** — `ConvertOfficeBytes(...)` requires no temporary directory or intermediate package file.
- **Streaming file APIs** — `ConvertOfficeFile(...)` avoids loading the complete input or output package into a managed
  `byte[]`.
- **Entry-by-entry processing** — only selected XML/XHTML entries are materialized as strings for conversion.
- **Preserves package assets** — non-target binary and structural entry payloads are streamed into the rebuilt archive.
- **Server and byte-stream friendly** — documents can enter and leave the converter entirely as `byte[]`.
- **EPUB compliant** — required `mimetype` ordering and storage rules are preserved.
- **Safe file publication** — file conversion validates a sibling temporary package before moving or replacing output.
- **Cross-platform** — built on .NET `System.IO.Compression` rather than Office automation or native Office
  applications.

---

## Performance

- Uses shared dictionary caching, precomputed candidate-length metadata, and thread-local buffers for high throughput.
- On .NET 9 and later, dictionary candidates are probed directly from `ReadOnlySpan<char>` without allocating temporary
  string keys. The .NET Standard 2.0 asset retains the compatible string-key fallback.
- Suitable for real-time, batch, and parallel processing.

### 🚀 Performance Benchmark for **OpenccNetLib 1.6.2**

#### `S2T` Conversion (.NET 9+ Span-Key Optimizations, Real-World Load)

> Benchmarked under **normal desktop usage** (IDE and background apps running) to reflect realistic performance.

---

### Environment

| Item                | Value                                    |
|---------------------|------------------------------------------|
| **BenchmarkDotNet** | v0.15.8                                  |
| **OS**              | Windows 11 (Build 26200.8875, 25H2)      |
| **CPU**             | Intel Core i5-13400 (10C/16T @ 2.50 GHz) |
| **.NET SDK**        | 10.0.302                                 |
| **Runtime**         | .NET 10.0.10 (X64 RyuJIT x86-64-v3)      |
| **Iterations**      | 10 (1 warm-up)                           |

---

### Results

| Method               |      Size |              Mean |       Error |     StdDev |           Min |           Max | Rank |     Gen0 |     Gen1 |     Gen2 |    Allocated |
|----------------------|----------:|------------------:|------------:|-----------:|--------------:|--------------:|-----:|---------:|---------:|---------:|-------------:|
| **BM_Convert_Sized** |       100 |      **1.776 µs** |   0.0156 µs |  0.0093 µs |      1.769 µs |      1.793 µs |    1 |   0.0305 |        – |        – |        328 B |
| **BM_Convert_Sized** |     1,000 |     **35.594 µs** |   0.2929 µs |  0.1743 µs |     35.364 µs |     35.876 µs |    2 |   0.1831 |        – |        – |      2,128 B |
| **BM_Convert_Sized** |    10,000 |    **199.543 µs** |  13.7299 µs |  9.0815 µs |    192.796 µs |    214.343 µs |    3 |  14.6484 |   2.4414 |        – |    146,651 B |
| **BM_Convert_Sized** |   100,000 |  **1,383.007 µs** |  36.8934 µs | 21.9547 µs |  1,352.110 µs |  1,422.130 µs |    4 | 156.2500 | 119.1406 | 109.3750 |  1,035,338 B |
| **BM_Convert_Sized** | 1,000,000 | **11,444.810 µs** | 144.7586 µs | 86.1435 µs | 11,278.808 µs | 11,562.277 µs |    5 | 968.7500 | 859.3750 | 531.2500 | 10,274,191 B |

---

### Summary

- **100 chars** → ~1.8 µs, 328 B allocated
- **1,000 chars** → ~35.6 µs, 2.1 KB allocated
- **10,000 chars** → ~0.20 ms, 143.2 KB allocated
- **100,000 chars** → ~1.38 ms, 0.99 MB allocated
- **1,000,000 chars (1M)** → ~11.45 ms, 9.8 MB allocated

On this system, the 1M-character result corresponds to approximately **87 million characters per second**.

---

### Comparison with v1.6.1

|      Size | v1.6.1 Mean | v1.6.2 Mean | Speedup | Allocation Reduction |
|----------:|------------:|------------:|--------:|---------------------:|
|       100 |    2.430 µs |    1.776 µs |   1.37× |               ~93.8% |
|     1,000 |   62.305 µs |   35.594 µs |   1.75× |               ~97.6% |
|    10,000 |  250.230 µs |  199.543 µs |   1.25× |               ~81.1% |
|   100,000 |    3.807 ms |    1.383 ms |   2.75× |               ~86.7% |
| 1,000,000 |   20.040 ms |   11.445 ms |   1.75× |               ~87.1% |

The large improvement is primarily due to **target-specific optimization introduced in v1.6.2**. Version 1.6.1 did not
ship a .NET 9+ optimized asset, so modern applications used the .NET Standard 2.0 implementation and materialized a
temporary `string` for each candidate dictionary key. Version 1.6.2 adds a `net9.0` asset that uses
`Dictionary.TryGetAlternateLookup<ReadOnlySpan<char>>()`, allowing the union conversion hot path to probe existing
`Dictionary<string, string>` data directly from input spans. This removes most temporary candidate-key allocations,
reduces GC pressure, and improves throughput without changing dictionaries or conversion results.

The package still includes its `netstandard2.0` asset for broad compatibility. Applications running on runtimes that do
not select the .NET 9+ asset continue to use the string-allocation fallback, so the gains above should not be assumed
for those targets.

---

### Notes

- Benchmarks include **real-world system noise** (IDE and background services), not isolated lab conditions.
- The benchmark measures warmed `S2T` conversion. Conversion prepares only the required dictionary groups. Precomputed
  starter metadata eliminates impossible candidate lengths, and prepared lookup structures are reused across
  conversions.
- Managed allocation now comes mainly from conversion output and buffer growth rather than temporary lookup keys.
- Time and memory remain approximately linear with input size; expected GC activity is visible at larger sizes.
- BenchmarkDotNet removed one outlier from the 100-, 1,000-, 100,000-, and 1,000,000-character measurements. For the
  1,000,000-character case, two outliers were detected and one was removed.
- Results are specific to the listed hardware, runtime, input distribution, and system load. Treat them as comparative
  measurements rather than universal latency guarantees.

---

### Conclusion

OpenccNetLib 1.6.2 delivers a clear performance step forward on .NET 9 and later: the measured workload is **1.25×–2.75×
faster than v1.6.1**, while managed allocation falls by approximately **81%–98%**. At one million characters, conversion
improves from ~20.0 ms to ~11.45 ms and allocation drops from ~75.7 MB to ~9.8 MB, with the same deterministic
conversion behavior and a preserved .NET Standard 2.0 compatibility path.

---

### ⏱ Relative Performance Chart

![Benchmark: Time vs Memory](https://raw.githubusercontent.com/laisuk/OpenccNet/master/OpenccNetLib/Images/benchmark_v162.png)

---

### 🟢 Highlights (OpenccNetLib v1.6.2)

- **🚀 High throughput:** processes 1M characters in ~11.45 ms, or roughly 87 million characters/second on the tested
  Intel i5-13400 system.
- **📉 Much lower allocation:** uses about 9.8 MB for the 1M-character conversion, down from about 75.7 MB in v1.6.1.
- **⚙️ Modern-runtime fast path:** .NET 9+ uses allocation-free span-key dictionary probes; .NET Standard 2.0 retains
  the compatible string-key fallback.
- **📌 Predictable scaling:** both elapsed time and memory remain approximately linear as input size grows.
- **📚 Same conversion semantics:** the optimization changes candidate lookup mechanics, not dictionary selection,
  longest-match behavior, or output.

---

## API Reference

### `Opencc` Class

#### 🔧 Constructors

- `Opencc(string config = null)`  
  Creates a new converter using a configuration name (e.g., `"s2t"`, `"t2s"`).  
  This overload is compatible with existing code but requires string-based config.

- `Opencc(OpenccConfig configEnum)`  
  Creates a new converter using the strongly-typed `OpenccConfig` enum  
  (e.g., `OpenccConfig.S2T`, `OpenccConfig.T2S`).  
  **Recommended for all new code** because it avoids magic strings.

#### 🔁 Conversion Methods

- `string Convert(string inputText, bool punctuation = false)`  
  Convert text according to the current config and punctuation mode.

- `string S2T(string inputText, bool punctuation = false)`
- `string T2S(string inputText, bool punctuation = false)`
- `string S2Tw(string inputText, bool punctuation = false)`
- `string Tw2S(string inputText, bool punctuation = false)`
- `string S2Twp(string inputText, bool punctuation = false)`
- `string Tw2Sp(string inputText, bool punctuation = false)`
- `string S2Hkp(string inputText, bool punctuation = false)`
- `string Hk2Sp(string inputText, bool punctuation = false)`
- `string T2Hkp(string inputText, bool punctuation = false)`
- `string Hk2Tp(string inputText, bool punctuation = false)`
- `string S2Hk(string inputText, bool punctuation = false)`
- `string Hk2S(string inputText, bool punctuation = false)`
- `string T2Tw(string inputText, bool punctuation = false)`
- `string T2Twp(string inputText, bool punctuation = false)`
- `string Tw2T(string inputText, bool punctuation = false)`
- `string Tw2Tp(string inputText, bool punctuation = false)`
- `string T2Hk(string inputText, bool punctuation = false)`
- `string Hk2T(string inputText, bool punctuation = false)`
- `string T2Jp(string inputText)`
- `string Jp2T(string inputText)`

#### ⚙️ Configuration

Opencc supports both **string-based** and **enum-based** configuration APIs.  
Internally, all configurations are stored as a strongly typed `OpenccConfig` identifier;  
string APIs are provided for backward compatibility and convenience.

> **Recommended:** Use the `OpenccConfig` enum–based APIs whenever possible.  
> String-based APIs are fully supported but are considered legacy-style convenience helpers.

---

##### Instance Configuration APIs

- `string Config { get; set; }`  
  Gets or sets the current conversion configuration using a canonical string  
  (for example, `"s2t"`, `"tw2sp"`).  
  Invalid values automatically fall back to `"s2t"` and update the internal error status.

- `void SetConfig(string config)`  
  Sets the conversion configuration using a string name.  
  Comparison is case-insensitive and ignores surrounding whitespace.  
  Falls back to `"s2t"` if the value is invalid.

- `void SetConfig(OpenccConfig configEnum)`  
  Sets the conversion configuration using a strongly typed `OpenccConfig` enum value.  
  **This is the preferred and recommended approach** for type safety, IDE support, and interop scenarios (P/Invoke, JNI,
  bindings).

- `string GetConfig()`  
  Returns the current configuration as a canonical lowercase string  
  (for example, `"s2tw"`).

- `OpenccConfig GetConfigId()`  
  Returns the current configuration as an `OpenccConfig` enum value.  
  This reflects the authoritative internal configuration state.

- `string GetLastError()`  
  Returns the most recent configuration-related error message, if any.  
  A `null` value indicates that no configuration error is currently recorded.

---

#### 📋 Validation and Helper APIs

The following static helpers are provided for validation, parsing, and discovery of supported configurations:

- `static bool TryParseConfig(string config, out OpenccConfig result)`  
  Attempts to parse a configuration string into the corresponding `OpenccConfig` enum value.  
  Comparison is case-insensitive and ignores leading or trailing whitespace.  
  Returns `false` if the input is `null`, empty, or not a recognized configuration.

- `static bool IsValidConfig(string config)`  
  Determines whether the specified string represents a supported OpenCC configuration.

- `static IReadOnlyCollection<string> GetSupportedConfigs()`  
  Returns a read-only collection of all supported configuration names  
  (canonical lowercase strings).  
  The returned collection is stable and does not allocate on each call.

- `static int ZhoCheck(string inputText)`  
  Detects whether the input text is likely:
    - `2` → Simplified Chinese
    - `1` → Traditional Chinese
    - `0` → Neither / unknown

- `string NormalizeCompat(string text)`
  Normalizes mapped CJK Compatibility Ideographs with the built-in Unicode compatibility table. Use this as an optional
  pre-processing step before `Convert(...)` when input may contain forms such as `金`. Unmapped text is preserved
  unchanged.

---

#### 📚 Dictionary Provider APIs

OpenccNetLib exposes dictionary provider APIs for advanced scenarios such as custom dictionaries, generated dictionary
artifacts, test fixtures, and tooling. Most applications can use the built-in dictionary without calling these APIs.

##### `Opencc` dictionary activation helpers

- `static void UseCustomDictionary(DictionaryMaxlength customDictionary)`
  Sets the process-wide provider to a custom `DictionaryMaxlength` instance and atomically refreshes internal conversion
  state. Prefer calling this once during application startup, before conversions begin. Calls already in progress may
  finish with the previous complete state; subsequent calls, including calls through existing `Opencc` instances, use
  the replacement provider.

- `static void UseDefaultDictionary()`
  Restores the active provider to the built-in dictionary and atomically refreshes internal conversion state.

- `static void UseDictionaryFromPath(string dictionaryRelativePath)`
  Loads OpenCC text dictionary files with `DictionaryLib.FromDicts(dictionaryRelativePath)` and activates the result.

- `static void UseDictionaryFromJsonString(string jsonString)`
  Deserializes a `DictionaryMaxlength` JSON payload and activates it as the custom dictionary provider.

##### `DictionaryLib` provider APIs

- `static DictionaryMaxlength Provider { get; }`
  Returns the shared built-in dictionary instance.

- `static DictionaryMaxlength GetActiveProvider()`
  Returns the dictionary instance currently supplied by the active provider delegate.

- `static DictionaryMaxlength New()`
  Returns the built-in dictionary and resets the active provider to the built-in dictionary.

- `static void SetDictionaryProvider(DictionaryMaxlength dictionary)`
  Sets the active dictionary provider to a fixed `DictionaryMaxlength` instance and atomically refreshes derived
  conversion state. Conversions already in progress may finish with the previous complete state; subsequent conversions
  use the replacement state.

- `static void ResetDictionaryProviderToDefault()`
  Restores the active dictionary provider to the built-in dictionary and atomically refreshes derived conversion state.

##### `DictionaryLib` loading APIs

-

`static DictionaryMaxlength FromDicts(string relativeBaseDir = "dicts", IDictionary<DictSlot, string> overrides = null, IDictionary<DictSlot, string> appends = null)`
Loads OpenCC text dictionary files, optionally replacing slots with `overrides` or extending slots with `appends`.

- `static DictionaryMaxlength WithCustomDicts(DictionaryMaxlength dict, IEnumerable<CustomDictSpec> specs)`
  Applies post-load customization to an already loaded dictionary provider. Each spec targets one `DictSlot`, reads
  optional `Paths` and/or `Pairs`, and applies them with `CustomDictMode.Append` or `CustomDictMode.Override`.

- `static DictionaryMaxlength FromJson(string relativePath = "dicts/dictionary_maxlength.json")`
  Loads and normalizes a JSON dictionary payload.

- `static DictionaryMaxlength DeserializedFromJson(string path)`
  Compatibility wrapper around `FromJson(path)`.

- `static DictionaryMaxlength FromCbor(string relativePath = "dicts/dictionary_maxlength.cbor")`
  Loads and normalizes a CBOR dictionary payload.

- `static DictionaryMaxlength LoadJsonCompressed(string path)`
  Loads and normalizes a Zstandard-compressed JSON dictionary payload.

##### `DictionaryLib` serialization APIs

The serialization helpers accept an optional `DictionaryMaxlength dictionary = null` parameter. When omitted, they load
from the default OpenCC text dictionary sources with `FromDicts()`.

- `static void SerializeToJson(string path, DictionaryMaxlength dictionary = null)`
  Writes a dictionary to indented JSON.

- `static void SerializeToJsonUnescaped(string path, DictionaryMaxlength dictionary = null)`
  Writes indented UTF-8 JSON without escaping non-ASCII characters.

- `static void SaveCbor(string path, DictionaryMaxlength dictionary = null)`
  Writes a dictionary as CBOR.

- `static byte[] ToCborBytes(DictionaryMaxlength dictionary = null)`
  Returns a CBOR-encoded dictionary payload.

- `static void SaveJsonCompressed(string path, DictionaryMaxlength dictionary = null)`
  Writes a dictionary as Zstandard-compressed JSON.

```csharp
var dict = DictionaryLib.FromDicts(
    appends: new Dictionary<DictSlot, string>
    {
        [DictSlot.STPhrases] = "./UserDict.txt"
    });

DictionaryLib.SerializeToJson("./custom-dictionary.json", dict);
DictionaryLib.SaveCbor("./custom-dictionary.cbor", dict);
DictionaryLib.SaveJsonCompressed("./custom-dictionary.zstd", dict);
```

---

##### Notes

- All configuration inputs ultimately resolve to a single internal
  `OpenccConfig` identifier.
- Invalid configuration values never throw; they safely fall back to `"s2t"`.
- Enum-based APIs are future-proof and align with the C API, Rust core, and other language bindings.

---

## Dictionary Data

- Dictionaries are loaded and cached on first use.
- Data files are expected in the `dicts/` directory (see `DictionaryLib` for details).

## Add-On CLI Tools (Separated from OpenccNetLib)

### `OpenccNet dictgen`

```
Description:
  Generate OpenccNetLib dictionary files.
  
  Examples:
    OpenccNet dictgen
      Generate default Zstd dictionary (dictionary_maxlength.zstd)
  
    OpenccNet dictgen -f cbor
      Generate CBOR dictionary for interop
  
    OpenccNet dictgen -f json --unescape
      Generate readable JSON dictionary without \uXXXX escapes
  

Usage:
  OpenccNet dictgen [options]

Options:
  -f, --format <format>            Dictionary format: zstd|cbor|json [default: zstd]
  -o, --output <output>            Output filename. Default: dictionary_maxlength.<ext>
  -b, --base-dir <base-dir>        Base directory containing OpenCC-style .txt dictionary sources (for dictgen) [default: dicts]
  -u, --unescape                   For JSON format only: write readable Unicode characters instead of \uXXXX escapes
  -D, --custom-dict <custom-dict>  Load custom dictionary: <slot>:<mode>:<path>.
                                   Example: HkPhrasesRev:append:my_hk_dict.txt
                                   Available slots: STCharacters, STPhrases, STPunctuations, TSCharacters, TSPhrases, TSPunctuations, TWPhrases, TWPhrasesRev, 
                                   TWVariants, TWVariantsRev, TWVariantsRevPhrases, HKVariants, HKVariantsRev, HKVariantsRevPhrases, JPSCharacters, JPSPhrases, 
                                   TWVariantsPhrases, HKVariantsPhrases, JPSCharactersRev, HKPhrases, HKPhrasesRev
  -?, -h, --help                   Show help and usage information
```

### `OpenccNet convert`

```
Description:
  Convert text using OpenccNetLib configurations.

Usage:
  OpenccNet convert [options]

Options:
  -i, --input <input>               Read original text from file <input>
  -o, --output <output>             Write original text to file <output>
  -c, --config <config> (REQUIRED)  Conversion configuration.
                                    Valid options: s2t, t2s, s2tw, tw2s, s2twp, tw2sp, s2hkp, hk2sp, t2hkp, hk2tp, s2hk, hk2s, t2tw, tw2t, t2twp, tw2tp, t2hk, 
                                    hk2t, t2jp, jp2t
  -p, --punct                       Punctuation conversion.
  --detofu <detofu>                 Apply tofu-safe fallback after conversion: all, ext-b, ext-c, ext-d, ext-e, ext-f, ext-g, ext-h, ext-i
  --detofu-file <detofu-file>       Load additional DeTofu fallback mappings from a UTF-8 text file. Custom mappings override built-in mappings (requires 
                                    --detofu)
  -I, --keep-ids                    Preserve Unicode IDS expressions during conversion.
  -n, --norm-compat                 Normalize CJK Compatibility Ideographs before conversion.
  -D, --custom-dict <custom-dict>   Load custom dictionary: <slot>:<mode>:<path>.
                                    Example: HkPhrasesRev:append:my_hk_dict.txt
                                    Available slots: STCharacters, STPhrases, STPunctuations, TSCharacters, TSPhrases, TSPunctuations, TWPhrases, TWPhrasesRev, 
                                    TWVariants, TWVariantsRev, TWVariantsRevPhrases, HKVariants, HKVariantsRev, HKVariantsRevPhrases, JPSCharacters, 
                                    JPSPhrases, TWVariantsPhrases, HKVariantsPhrases, JPSCharactersRev, HKPhrases, HKPhrasesRev
  --in-enc <in-enc>                 Encoding for input: UTF-8|UNICODE|GBK|GB2312|BIG5|Shift-JIS [default: UTF-8]
  --out-enc <out-enc>               Encoding for output: UTF-8|UNICODE|GBK|GB2312|BIG5|Shift-JIS [default: UTF-8]
  -?, -h, --help                    Show help and usage information
```

Example: append a custom Hong Kong phrase dictionary for `hk2sp` using the verified portable token.

`data\my_hk_dict.txt`:

```text
# Custom Dictionary

細路哥	小男孩
```

```powershell
"這個細路哥很靈活" | .\OpenccNet.exe convert -c hk2sp -D 'hkphrasesrev:append:data\my_hk_dict.txt'
这个小男孩很灵活
✅ Conversion (hk2sp): <stdin> → <stdout>
```

Repeat `-D` or `--custom-dict` to apply multiple specifications in command-line order:

```powershell
.\OpenccNet.exe convert -c hk2sp -i input.txt -o output.txt `
  -D 'hkphrasesrev:append:data\my_hk_dict.txt' `
  --custom-dict 'tsphrases:append:data\company_ts_phrases.txt'
```

### `OpenccNet office`

```
Description:
  Convert Office documents or EPUB using OpenccNetLib.

Usage:
  OpenccNet office [options]

Options:
  -i, --input <input>               Input Office document <input>
  -o, --output <output>             Output Office document <output>
  -c, --config <config> (REQUIRED)  Conversion configuration.
                                    Valid options: s2t, t2s, s2tw, tw2s, s2twp, tw2sp, s2hkp, hk2sp, t2hkp, hk2tp, s2hk, hk2s, t2tw, tw2t, t2twp, tw2tp, t2hk, 
                                    hk2t, t2jp, jp2t
  -p, --punct                       Enable punctuation conversion.
  -f, --format <format>             Force Office document format: docx | xlsx | pptx | odt | ods | odp | epub
  -k, --keep-font                   Preserve font names in Office documents [default: true]. Use --keep-font:false to disable.
  -q, --quiet                       Suppress status and progress output; only errors will be shown.
  -D, --custom-dict <custom-dict>   Load custom dictionary: <slot>:<mode>:<path>.
                                    Example: HkPhrasesRev:append:my_hk_dict.txt
                                    Available slots: STCharacters, STPhrases, STPunctuations, TSCharacters, TSPhrases, TSPunctuations, TWPhrases, TWPhrasesRev, 
                                    TWVariants, TWVariantsRev, TWVariantsRevPhrases, HKVariants, HKVariantsRev, HKVariantsRevPhrases, JPSCharacters, 
                                    JPSPhrases, TWVariantsPhrases, HKVariantsPhrases, JPSCharactersRev, HKPhrases, HKPhrasesRev
  -?, -h, --help                    Show help and usage information
```

### `OpenccNet pdf`

```
Description:
  Convert a PDF to UTF-8 text using PdfPig + OpenccNetLib, with optional CJK paragraph reflow.

Usage:
  OpenccNet pdf [options]

Options:
  -i, --input <input>              Input PDF file <input.pdf>
  -o, --output <output>            Output text file <output.txt>
  -c, --config <config>            Conversion configuration.
                                   Valid options: s2t, t2s, s2tw, tw2s, s2twp, tw2sp, s2hkp, hk2sp, t2hkp, hk2tp, s2hk, hk2s, t2tw, tw2t, t2twp, tw2tp, t2hk, 
                                   hk2t, t2jp, jp2t
  -p, --punct                      Enable punctuation conversion.
  -H, --header                     Add [Page x/y] headers to the extracted text.
  -r, --reflow                     Reflow CJK paragraphs into continuous lines.
  -C, --compact                    Use compact reflow (fewer blank lines between paragraphs). Only meaningful with --reflow.
  -q, --quiet                      Suppress status and progress output; only errors will be shown.
  -e, --extract                    Extract text from PDF only (no OpenCC conversion).
  -n, --norm-compat                Normalize CJK Compatibility Ideographs before conversion.
  -D, --custom-dict <custom-dict>  Load custom dictionary: <slot>:<mode>:<path>.
                                   Example: HkPhrasesRev:append:my_hk_dict.txt
                                   Available slots: STCharacters, STPhrases, STPunctuations, TSCharacters, TSPhrases, TSPunctuations, TWPhrases, TWPhrasesRev, 
                                   TWVariants, TWVariantsRev, TWVariantsRevPhrases, HKVariants, HKVariantsRev, HKVariantsRevPhrases, JPSCharacters, JPSPhrases, 
                                   TWVariantsPhrases, HKVariantsPhrases, JPSCharactersRev, HKPhrases, HKPhrasesRev
  -?, -h, --help                   Show help and usage information
```

## Usage Notes — `OpenccNet pdf`

### PDF extraction engine

`OpenccNet pdf` uses a **text-based PDF extraction engine** (PdfPig) and is intended for **digitally generated PDFs**
(e-books, research papers, reports).

- ✅ Works best with selectable text
- ❌ Does **not** perform OCR on scanned/image-only PDFs
- ❌ Visual layout (columns, tables, figures) is not preserved

---

### CJK paragraph reflow

The `--reflow` option applies a **CJK-aware paragraph reconstruction pipeline**, designed for Chinese novels, essays,
and academic text.

Reflow attempts to:

- Join artificially wrapped lines
- Repair cross-line splits (e.g. `面` + `容` → `面容`)
- Preserve headings, short titles, dialog markers, and metadata-like lines

⚠️ **Important limitations**

- Reflow is **heuristic-based**
- It is **not suitable** for:
    - Poetry
    - Comics / scripts
    - Highly informal or experimental layouts
- Web novels often use inconsistent formatting and may require tuning

---

### `--compact` mode

When used together with `--reflow`, `--compact`:

- Reduces excessive blank lines
- Produces denser, book-like paragraphs
- Is recommended for **long-form reading or further text processing**

> `--compact` has no effect unless `--reflow` is enabled.

---

### Page headers

Using `--header` inserts markers such as:

```
=== [Page 12/240] ===
```

This is useful for:

- Debugging extraction issues
- Locating original PDF pages
- Avoiding empty or ambiguous page boundaries

---

### Quiet mode

`--quiet` suppresses:

- Progress bars
- Status messages
- Informational logs

Only **errors** will be printed.  
Recommended for batch processing or script integration.

---

### Output encoding

- Output text is always written as **UTF-8**
- Line endings follow the host platform

If you need other encodings, convert the output text using standard tools after extraction.

---

### Recommended Workflows

**Simple PDF → Traditional Chinese text**

```
OpenccNet pdf -i input.pdf -o output.txt -c s2t -r
```

Compact novel conversion with **page markers**

```
OpenccNet pdf -i novel.pdf -o novel.txt -c s2tw -r --compact -H
```

Batch / automation use

```
OpenccNet pdf -i file.pdf -o out.txt -c t2s -r -q
```

---

## Project That Use OpenccNetLib

- [OpenccNetLibGui](https://github.com/laisuk/OpenccNetLibGui) : A GUI application for `OpenccNetLib`, providing a
  user-friendly interface for Traditional/Simplified Chinese text conversion.

## License

- This project is licensed under the MIT License. See
  the [LICENSE](https://raw.githubusercontent.com/laisuk/OpenccNet/master/OpenccNetLib/LICENSE) file for details.
-

See [THIRD_PARTY_NOTICES.md](https://raw.githubusercontent.com/laisuk/OpenccNet/master/OpenccNetLib/THIRD_PARTY_NOTICES.md)
for bundled OpenCC lexicons (_Apache License 2.0_).

---

**OpenccNet** is not affiliated with the original **OpenCC** project, but aims to provide a compatible and
high-performance solution for .NET developers.


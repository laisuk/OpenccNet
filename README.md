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

- [Installation](#installation)
- [Usage](#usage)
- [API Reference](#api-reference)
- [Office Document Conversion](#-office-document--epub-conversion-in-memory-no-temp-files-required)
- [Add-On CLI Tools](#add-on-cli-tools-separated-from-openccnetlib)
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
- Thread-safe and suitable for high-throughput parallel processing
- **Office document & EPUB conversion** (pure in-memory):
    - `.docx` (Word), `.xlsx` (Excel), `.pptx` (PowerPoint), `.epub`
    - `byte[] → byte[]` conversion with full XML patching
    - Async/await supported (`ConvertOfficeBytesAsync`)
    - Zero temp files required; safe for Web, Server, and WASM/Blazor hosts
- .NET Standard 2.0 compatible  
  (cross-platform: Windows, Linux, macOS; supported on .NET Core 2.0+, .NET 5+, .NET 6/7/8/9/10 LTS)

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
opencc.S2T("汉字");      
// Simplified to Traditional opencc.T2S("漢字");      
// Traditional to Simplified opencc.S2Tw("汉字");     
// Simplified to Taiwan Traditional opencc.T2Jp("漢字");     
// Traditional to Japanese Kanji
// ...and more
```

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
topology
unchanged.

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

| API                       | Description                                    |
|---------------------------|------------------------------------------------|
| `DictSlot`                | Strongly typed OpenCC dictionary slot selector |
| `CustomDictSpec.Slot`     | Target slot                                    |
| `CustomDictSpec.Paths`    | Custom dictionary files                        |
| `CustomDictSpec.Pairs`    | In-memory dictionary entries                   |
| `CustomDictSpec.Mode`     | `Append` or `Override`                         |
| `CustomDictMode.Append`   | Merge into the existing slot                   |
| `CustomDictMode.Override` | Replace the whole slot                         |

#### Custom dictionary file format

Custom dictionary files are UTF-8 text files. Each entry is written as `phrase<TAB>translation`; blank lines are
ignored,
comments are supported, and duplicate keys use late-comer wins behavior.

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

| DictSlot                        | Serialization Field       | Default File                |
|---------------------------------|---------------------------|-----------------------------|
| `DictSlot.STCharacters`         | `st_characters`           | `STCharacters.txt`          |
| `DictSlot.STPhrases`            | `st_phrases`              | `STPhrases.txt`             |
| `DictSlot.STPunctuations`       | `st_punctuations`         | `STPunctuations.txt`        |
| `DictSlot.TSCharacters`         | `ts_characters`           | `TSCharacters.txt`          |
| `DictSlot.TSPhrases`            | `ts_phrases`              | `TSPhrases.txt`             |
| `DictSlot.TSPunctuations`       | `ts_punctuations`         | `TSPunctuations.txt`        |
| `DictSlot.TWPhrases`            | `tw_phrases`              | `TWPhrases.txt`             |
| `DictSlot.TWPhrasesRev`         | `tw_phrases_rev`          | `TWPhrasesRev.txt`          |
| `DictSlot.TWVariants`           | `tw_variants`             | `TWVariants.txt`            |
| `DictSlot.TWVariantsPhrases`    | `tw_variants_phrases`     | `TWVariantsPhrases.txt`     |
| `DictSlot.TWVariantsRev`        | `tw_variants_rev`         | `TWVariantsRev.txt`         |
| `DictSlot.TWVariantsRevPhrases` | `tw_variants_rev_phrases` | `TWVariantsRevPhrases.txt`  |
| `DictSlot.HKVariants`           | `hk_variants`             | `HKVariants.txt`            |
| `DictSlot.HKVariantsPhrases`    | `hk_variants_phrases`     | `HKVariantsPhrases.txt`     |
| `DictSlot.HKVariantsRev`        | `hk_variants_rev`         | `HKVariantsRev.txt`         |
| `DictSlot.HKVariantsRevPhrases` | `hk_variants_rev_phrases` | `HKVariantsRevPhrases.txt`  |
| `DictSlot.JPSCharacters`        | `jps_characters`          | `JPShinjitaiCharacters.txt` |
| `DictSlot.JPSPhrases`           | `jps_phrases`             | `JPShinjitaiPhrases.txt`    |
| `DictSlot.JPVariants`           | `jp_variants`             | `JPVariants.txt`            |
| `DictSlot.JPVariantsRev`        | `jp_variants_rev`         | `JPVariantsRev.txt`         |

#### Recommended usage

Use `appends` for company terms, product names, domain vocabulary, and temporary conversion fixes. Use `overrides` only
when maintaining a full proprietary replacement dictionary. Prefer following the upstream OpenCC lexicon structure
whenever possible.

Call `Opencc.UseCustomDictionary(dict)` once during application startup, before constructing `Opencc` instances. The
chosen dictionary should be treated as the application's single source of truth. Do not hot-swap the global dictionary
provider while existing `Opencc` instances are still active; if the provider must change, set the new provider and then
discard and recreate existing `Opencc` instances.

This global provider design is intentional for performance: dictionary data, metadata, `StarterUnion` / `UnionCache`
acceleration structures, and runtime plans can be shared instead of duplicated per `Opencc` instance. Normal
applications
usually need only one custom provider. Unit tests that mutate the global provider should not run in parallel with tests
expecting the default provider.

#### Why no `user_dict` slot?

OpenccNetLib intentionally preserves the OpenCC dictionary topology. Generic dynamic slots complicate conversion
contracts, `DictRefs`, starter indexes, `StarterUnion`, and the conversion plan/union caches. Existing OpenCC slots
already provide deterministic and extensible customization points.

---

## 🆕 Office Document & EPUB Conversion (In-Memory, No Temp Files Required)

Starting from **OpenccNetLib v1.3.2**, the library now provides a **pure in-memory Office / EPUB conversion API**.  
This allows converting `.docx`, `.xlsx`, `.pptx`, and `.epub` **directly from byte[] to byte[]**, without touching the
filesystem.

This is ideal for:

- **Web servers** (ASP.NET Core)
- **Blazor / WebAssembly**
- **JavaScript interop**
- **Desktop apps that want to avoid temp paths**
- **Security-restricted environments**

### ✔ Supported formats

| Format | Description                                      |
|--------|--------------------------------------------------|
| `docx` | Word document (Office Open XML)                  |
| `xlsx` | Excel spreadsheet (Office Open XML)              |
| `pptx` | PowerPoint presentation (Office Open XML)        |
| `odt`  | OpenDocument Text (LibreOffice / OpenOffice)     |
| `ods`  | OpenDocument Spreadsheet                         |
| `odp`  | OpenDocument Presentation                        |
| `epub` | EPUB e-book (with correct uncompressed mimetype) |

---

## 📦 Example: Convert Office Document In-Memory

```csharp
using OpenccNetLib;

var opencc = new Opencc("s2t"); // Simplified → Traditional

byte[] inputBytes = File.ReadAllBytes("sample.docx");

// New strongly-typed OfficeFormat enum (recommended)
byte[] outputBytes = OfficeDocConverter.ConvertOfficeBytes(
    inputBytes,
    format: OfficeFormat.Docx,
    converter: opencc,
    punctuation: false,
    keepFont: true
);

File.WriteAllBytes("output.docx", outputBytes);
```

---

## 🔁 Backward-Compatible String Overload

Existing string-based API still works:

```csharp
byte[] outputBytes = OfficeDocConverter.ConvertOfficeBytes(
    inputBytes,
    format: "docx",   // legacy string format
    converter: opencc
);
```

No breaking changes — all existing code continues working.

---

## ⚡ Async API (Recommended for Server/Web)

```csharp
var outputBytes = await OfficeDocConverter.ConvertOfficeBytesAsync(
    inputBytes,
    format: OfficeFormat.Docx,
    converter: opencc,
    punctuation: false,
    keepFont: true
);
```

- Fully async
- No blocking
- Safe for ASP.NET Core, MAUI, Blazor WebAssembly

String format async overload also remains available.

---

## 📁 Convert Files (Convenience wrappers)

```csharp
OfficeDocConverter.ConvertOfficeFile(
    "input.docx",
    "output.docx",
    format: OfficeFormat.Docx,
    converter: opencc
);
```

Or async:

```csharp
await OfficeDocConverter.ConvertOfficeFileAsync(
    "input.docx",
    "output.docx",
    format: OfficeFormat.Docx,
    converter: opencc
);
```

String-based overload:

```csharp
OfficeDocConverter.ConvertOfficeFile(
    "input.docx",
    "output.docx",
    "docx",
    opencc
);
```

---

## 🔍 What does conversion do?

Inside the Office/EPUB container (ZIP), the library will:

- Extract only the relevant XML/XHTML parts
- Apply OpenCC text conversion (`s2t`, `t2s`, `t2tw`, `hk2s`, etc.)
- Preserve XML structure and formatting
- Optionally preserve fonts (`keepFont = true`)
- Rebuild the Office container as valid ZIP
- For EPUB: ensure `mimetype` is **first uncompressed entry** (EPUB spec)

---

## 🛡 Error Handling

If conversion fails (invalid format, corrupted ZIP, missing document.xml, etc.):

```csharp
throw new InvalidOperationException("Conversion failed: ...");
```

A companion “Try” API may be added in future versions.

---

## 🧪 Unit Tested (MSTest)

OpenccNetLib includes integration tests for:

- `.docx` (Word)
- ZIP structure validation
- XML extraction correctness
- Chinese text conversion inside `word/document.xml`
- Round-trip verification

Example (`OfficeDocConverterTests`):

```csharp
[TestMethod]
public void ConvertOfficeBytes_Docx_S2T_Succeeds()
{
    var opencc = new Opencc("s2t");
    var inputBytes = File.ReadAllBytes("滕王阁序.docx");

    var outputBytes = OfficeDocConverter.ConvertOfficeBytes(
        inputBytes, "docx", opencc);

    Assert.IsNotNull(outputBytes);

    using var ms = new MemoryStream(outputBytes);
    using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

    Assert.IsNotNull(zip.GetEntry("word/document.xml"));
}
```

---

## 🚀 Why This Matters

- **Zero temp files** → perfect for cloud environments
- **Memory-only pipeline** → safer, faster, cleaner
- **Cross-platform** (Windows / macOS / Linux / WASM)
- **Blazor and JavaScript-ready** (byte[] in/out)
- No external dependencies (only built-in System.IO.Compression)

---

## Performance

- Uses static dictionary caching, precomputed StarterUnion masks, and thread-local buffers for high throughput.
- Fully optimized for multi-stage conversion with zero-allocation hot paths.
- Suitable for real-time, batch, and parallel processing.

### 🚀 Performance Benchmark for **OpenccNetLib 1.5.0**

#### `S2T` Conversion (Union-based Optimizations, Real-World Load)

> Benchmarked under **normal desktop usage** (IDE, background apps running) to reflect realistic performance.

---

### Environment

| Item                | Value                                    |
|---------------------|------------------------------------------|
| **BenchmarkDotNet** | v0.15.8                                  |
| **OS**              | Windows 11 (Build 26200.8246, 25H2)      |
| **CPU**             | Intel Core i5-13400 (10C/16T @ 2.50 GHz) |
| **.NET SDK**        | 10.0.203                                 |
| **Runtime**         | .NET 10.0.7 (X64 RyuJIT x86-64-v3)       |
| **Iterations**      | 10 (1 warm-up)                           |

---

### Results

| Method               |      Size |          Mean |     Error |    StdDev |       Min |       Max | Rank |     Gen0 |     Gen1 |    Gen2 |       Allocated |
|----------------------|----------:|--------------:|----------:|----------:|----------:|----------:|-----:|---------:|---------:|--------:|----------------:|
| **BM_Convert_Sized** |       100 |   **2.49 µs** |   0.04 µs |   0.02 µs |   2.47 µs |   2.53 µs |    1 |    0.515 |        – |       – |          5.3 KB |
| **BM_Convert_Sized** |     1,000 |  **68.79 µs** |   2.71 µs |   1.79 µs |  66.73 µs |  72.50 µs |    2 |    8.789 |        – |       – |         90.3 KB |
| **BM_Convert_Sized** |    10,000 | **235.49 µs** |  11.01 µs |   7.28 µs | 226.53 µs | 245.62 µs |    3 |   75.684 |   16.113 |       – |        766.4 KB |
| **BM_Convert_Sized** |   100,000 |   **2.64 ms** | 605.47 µs | 360.31 µs |   2.30 ms |   3.38 ms |    4 |  832.031 |  347.656 | 132.813 |      7,695.8 KB |
| **BM_Convert_Sized** | 1,000,000 |  **20.56 ms** | 243.93 µs | 145.16 µs |  20.32 ms |  20.80 ms |    5 | 7,781.25 | 1,312.50 | 625.000 | **78,589.5 KB** |

---

### Summary

- **100 chars** → ~2.5 µs
- **1,000 chars** → ~69 µs
- **10,000 chars** → ~0.24 ms
- **100,000 chars** → ~2.6 ms
- **1,000,000 chars (1M)** → ~20.6 ms

---

### Notes

- Benchmarks include **real-world system noise** (IDE, background services), not isolated lab conditions.
- Despite this, performance remains **highly stable and near-linear scaling**.
- Minor variance at larger sizes is expected due to OS scheduling and GC activity.
- Allocation behavior remains consistent with previous versions, with **no regression in memory profile**.

---

### Conclusion

OpenccNetLib 1.5.0 maintains its position among the **high performance .NET-based CJK converters**,  
delivering **production-grade performance under realistic workloads**, while preserving deterministic conversion
results.

---

### ⏱ Relative Performance Chart

![Benchmark: Time vs Memory](https://raw.githubusercontent.com/laisuk/OpenccNet/master/OpenccNetLib/Images/benchmark_v150.png)

---

### 🟢 Highlights (OpenccNetLib v1.5.0)

- **🚀 High Performance (Real-World Tested)**  
  Processes **1M characters in ~20 ms** under normal desktop load (IDE, background apps).  
  Sustains **tens of millions of chars/sec** on a mid-range CPU (Intel i5-13400).

- **📌 Predictable, Linear Scaling**  
  Both **time** and **memory usage** scale *linearly* with input size:
    - consistent latency for small and large inputs
    - stable throughput for batch and streaming workloads
    - no unexpected slow paths

- **⚙️ Optimized Conversion Core**  
  Built on a highly efficient pipeline:
    - fast **Union-based lookup** for candidate filtering
    - minimal branching for non-matching paths
    - streamlined control flow for better CPU utilization
    - allocation-aware design for sustained performance

- **📈 Stable GC Behavior**
    - allocations mainly come from output buffers
    - low GC pressure in typical workloads
    - remains stable even for large inputs (≥1M chars)

- **🏁 Production-Ready Throughput**  
  Designed for real applications:
    - performs consistently outside benchmark isolation
    - suitable for CLI, GUI, and backend services
    - reliable under multitasking environments

- **💾 Memory Characteristics**
    - scales proportionally with input size
    - no abnormal spikes or hidden overhead
    - predictable usage for large document processing

> **Note:**  
> Internal caching and optimized data structures ensure consistently fast conversions  
> across repeated calls and multiple instances.

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
- `string S2Hk(string inputText, bool punctuation = false)`
- `string Hk2S(string inputText, bool punctuation = false)`
- `string T2Tw(string inputText)`
- `string T2Twp(string inputText)`
- `string Tw2T(string inputText)`
- `string Tw2Tp(string inputText)`
- `string T2Hk(string inputText)`
- `string Hk2T(string inputText)`
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
  **This is the preferred and recommended approach** for type safety, IDE support,
  and interop scenarios (P/Invoke, JNI, bindings).

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

The following static helpers are provided for validation, parsing, and discovery of
supported configurations:

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

---

#### 📚 Dictionary Provider APIs

OpenccNetLib exposes dictionary provider APIs for advanced scenarios such as custom dictionaries, generated dictionary
artifacts, test fixtures, and tooling. Most applications can use the built-in dictionary without calling these APIs.

##### `Opencc` dictionary activation helpers

- `static void UseCustomDictionary(DictionaryMaxlength customDictionary)`
  Sets the active conversion dictionary provider to a custom `DictionaryMaxlength` instance and clears cached conversion
  plans. Call this once during application startup, before creating converters that should use the custom dictionary.
  Treat the chosen dictionary as the shared application provider; if it must change, recreate existing `Opencc`
  instances after setting the new provider.

- `static void UseDefaultDictionary()`
  Restores the active provider to the built-in dictionary and clears cached conversion plans.

- `static void UseDictionaryFromPath(string dictionaryRelativePath)`
  Loads OpenCC text dictionary files with `DictionaryLib.FromDicts(dictionaryRelativePath)` and activates the result.

- `static void UseDictionaryFromJsonString(string jsonString)`
  Deserializes a `DictionaryMaxlength` JSON payload and activates it as the custom dictionary provider.

##### `DictionaryLib` provider and cache APIs

- `static DictionaryMaxlength Provider { get; }`
  Returns the shared built-in dictionary instance.

- `static ConversionPlanCache PlanCache { get; }`
  Returns the active global conversion plan cache.

- `static DictionaryMaxlength GetActiveProvider()`
  Returns the dictionary instance currently supplied by the active provider delegate.

- `static DictionaryMaxlength New()`
  Returns the built-in dictionary and resets the active provider to the built-in dictionary.

- `static void SetDictionaryProvider(DictionaryMaxlength dictionary)`
  Sets the active dictionary provider to a fixed `DictionaryMaxlength` instance and publishes a fresh plan cache.

- `static void ResetDictionaryProviderToDefault()`
  Restores the active dictionary provider to the built-in dictionary and publishes a fresh plan cache.

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
- Enum-based APIs are future-proof and align with the C API, Rust core,
  and other language bindings.

---

## Dictionary Data

- Dictionaries are loaded and cached on first use.
- Data files are expected in the `dicts/` directory (see `DictionaryLib` for details).

## Add-On CLI Tools (Separated from OpenccNetLib)

### `OpenccNet dictgen`

```
Description:
  Generate OpenccNetLib dictionary files.

Usage:
  OpenccNet dictgen [options]

Options:
  -f, --format <format>      Dictionary format: zstd|cbor|json [default: zstd]
  -o, --output <output>      Output filename. Default: dictionary_maxlength.<ext>
  -b, --base-dir <base-dir>  Base directory containing source dictionary files [default: dicts]
  -u, --unescape             For JSON format only: write readable Unicode characters instead of \uXXXX escapes
  -?, -h, --help             Show help and usage information
```

### `OpenccNet convert`

```
Description:
  Convert text using OpenccNetLib configurations.

Usage:
  OpenccNet convert [options]

Options:
  -i, --input              Read original text from file <input>
  -o, --output             Write original text to file <output>
  -c, --config (REQUIRED)  Conversion configuration: s2t|s2tw|s2twp|s2hk|t2s|tw2s|tw2sp|hk2s|jp2t|t2jp
  -p, --punct              Punctuation conversion. [default: False]
  --in-enc                 Encoding for input: UTF-8|UNICODE|GBK|GB2312|BIG5|Shift-JIS [default: UTF-8]
  --out-enc                Encoding for output: UTF-8|UNICODE|GBK|GB2312|BIG5|Shift-JIS [default: UTF-8]
  -?, -h, --help           Show help and usage information
```

### `OpenccNet office`

```
Description:
  Convert Office documents or Epub using OpenccNetLib.

Usage:
  OpenccNet office [options]

Options:
  -i, --input              Input Office document <input>
  -o, --output             Output Office document <output>
  -c, --config (REQUIRED)  Conversion configuration: s2t|s2tw|s2twp|s2hk|t2s|tw2s|tw2sp|hk2s|jp2t|t2jp
  -p, --punct              Enable punctuation conversion. [default: False]
  -f, --format             Force Office document format: docx | xlsx | pptx | odt | ods | odp | epub
  --keep-font              Preserve font names in Office documents [default: true]. Use --keep-font:false to disable. [default: True]
  --auto-ext               Auto append correct extension to Office output files [default: true]. Use --auto-ext:false to disable. [default: True]
  -?, -h, --help           Show help and usage information
```

### `OpenccNet pdf`

```
Description:
  Convert a PDF to UTF-8 text using PdfPig + OpenccNetLib, with optional CJK paragraph reflow.

Usage:
  OpenccNet pdf [options]

Options:
  -i, --input <input>    Input PDF file <input.pdf>
  -o, --output <output>  Output text file <output.txt>
  -c, --config <config>  Conversion configuration.
                         Valid options: s2t, t2s, s2tw, tw2s, s2twp, tw2sp, s2hk, hk2s, t2tw, tw2t, t2twp, tw2tp, t2hk, hk2t, t2jp, jp2t
  -p, --punct            Enable punctuation conversion.
  -H, --header           Add [Page x/y] headers to the extracted text.
  -r, --reflow           Reflow CJK paragraphs into continuous lines.
  --compact              Use compact reflow (fewer blank lines between paragraphs). Only meaningful with --reflow.
  -q, --quiet            Suppress status and progress output; only errors will be shown.
  -e, --extract          Extract text from PDF only (no OpenCC conversion).
  -?, -h, --help         Show help and usage information

```

## Usage Notes — `OpenccNet pdf`

### PDF extraction engine

`OpenccNet pdf` uses a **text-based PDF extraction engine** (PdfPig) and is intended for **digitally generated PDFs** (
e-books, research papers, reports).

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


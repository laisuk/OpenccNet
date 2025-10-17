# OpenccNet

[![NuGet](https://img.shields.io/nuget/v/OpenccNetLib.svg)](https://www.nuget.org/packages/OpenccNetLib/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/OpenccNetLib.svg?label=downloads&color=blue)](https://www.nuget.org/packages/OpenccNetLib/)
[![License](https://img.shields.io/github/license/laisuk/OpenccNet.svg)](https://github.com/laisuk/OpenccNet/blob/main/LICENSE)
[![Release](https://github.com/laisuk/OpenccNet/actions/workflows/release.yml/badge.svg)](https://github.com/laisuk/OpenccNet/actions/workflows/release.yml)

**OpenccNetLib** is a fast and efficient .NET library for converting Chinese text, offering support for Simplified ↔
Traditional, Taiwan, Hong Kong, and Japanese Kanji variants. Built with inspiration
from [OpenCC](https://github.com/BYVoid/OpenCC), this library is designed to integrate seamlessly into modern .NET
projects with a focus on performance and minimal memory usage.

## Table of Contents

- [Installation](#installation)
- [Usage](#usage)
- [API Reference](#api-reference)
- [Add-On CLI Tools](#add-on-cli-tools-separated-from-openccnetlib)
- [License](#license)

## Features

- Fast, multi-stage conversion using static dictionary caching
- Supports:
    - Simplified ↔ Traditional Chinese
    - Traditional (Taiwan) ↔ Simplified/Traditional
    - Traditional (Hong Kong) ↔ Simplified/Traditional
    - Japanese Kanji Shinjitai ↔ Traditional Kyujitai
- Accurate handling of **non-BMP (U+20000+) Chinese characters** for better conversion fidelity
- Optional punctuation conversion
- Thread-safe and suitable for parallel processing
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
var opencc = new Opencc("s2t"); // Simplified to Traditional 
string traditional = opencc.Convert("汉字转换测试"); 
Console.WriteLine(traditional);
// Output: 漢字轉換測試
```

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

var opencc = new Opencc("s2t");

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

### Using Custom Dictionary

Library default is zstd compressed dictionary Lexicon.
It can be changed to custom dictionary (`JSON`, `CBOR` or `"baseDir/*.txt"`) prior to instantiate `Opencc()`:

```csharp
using OpenccNetLib;
Opencc.UseCustomDictionary(DictionaryLib.FromDicts()) // Init only onece, dicts from baseDir "./dicts/"
var opencc = new Opencc("s2t"); // Simplified to Traditional 
string traditional = opencc.Convert("汉字转换测试"); 
Console.WriteLine(traditional); // Output: 漢字轉換測試
```

## Performance

- Uses static dictionary caching and thread-local buffers for high throughput.
- Suitable for batch and parallel processing scenarios.

### 📊 Benchmark Results – OpenccNetLib 1.2.0

> BenchmarkDotNet v0.15.4 · .NET 9.0.9 · Windows 11 · RyuJIT AVX2  
> Test: `BM_Convert_Sized` · Warmup + 10 Iterations
> Config: `s2t`

| Input Size | Mean Time | Gen0 (per 1k ops) |     Gen1 |    Gen2 | Allocated Memory |
|-----------:|----------:|------------------:|---------:|--------:|-----------------:|
|        100 |   2.57 µs |             0.523 |        – |       – |          5.36 KB |
|      1,000 |  65.27 µs |             8.789 |        - |       – |         90.37 KB |
|     10,000 | 221.82 µs |            81.543 |   22.461 |       – |        827.37 KB |
|    100,000 |   6.23 ms |           914.863 |  406.250 |  187.50 |      8,213.83 KB |
|  1,000,000 |  46.15 ms |          8,090.91 | 2,363.64 | 727.273 |     83,288.56 KB |

### ⏱ Relative Performance Chart

![Benchmark: Time vs Memory](https://raw.githubusercontent.com/laisuk/OpenccNet/master/OpenccNetLib/Images/Benchmarks110.png)

### ✅ Highlights

- ✅ **Preallocated `StringBuilder`** delivers consistent performance across all input sizes, minimizing reallocations.
- 🚀 **Inclusive splitting** reduces `ConvertBy()` calls, boosting throughput for segmented processing.
- 🔁 **Parallel processing** automatically engages for large workloads (≥16 segments, ≥2000 chars), taking full advantage
  of multicore CPUs.
- 📉 **Memory usage scales linearly** with input size — from ~5 KB to ~85 MB — with no unpredictable spikes.
- 🧠 **GC pressure remains stable** and predictable, even at 1M characters:
    - Gen0: ~7.7K collections
    - Gen1: ~2.25K collections
    - Gen2: ~625 collections  
      All within expected and manageable ranges.
- ⚡ **Fast warm startup**, suitable for both CLI batch conversion and responsive GUI usage.
- ✨ **OpenccNetLib 1.1.0** is fully production-ready for high-performance, large-scale Chinese text conversion.

---

## API Reference

### `Opencc` Class

#### 🔧 Constructor

- `Opencc(string config = null)`  
  Create a new converter with the specified configuration.

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

- `string Config { get; set; }`  
  Gets or sets the current config string. Invalid configs fallback to "`s2t`" and update error status.
- `void SetConfig(string config)`  
  Set the config using a string (e.g., "`tw2sp`"). Falls back to "`s2t`" if invalid.
- `void SetConfig(OpenccConfig configEnum)`  
  Set the config using a strongly typed OpenccConfig enum. Recommended for safety and IDE support.
- `string GetConfig()`  
  Returns the current config string (e.g., "`s2tw`").
- `string GetLastError()`  
  Returns the most recent error message, if any, from config setting.

#### 📋 Validation and Helpers

- `static bool IsValidConfig(string config)`  
  Checks whether the given string is a valid config name.
- `static IReadOnlyCollection<string> GetSupportedConfigs()`  
  Returns the list of all supported config names as strings.
- `static bool TryParseConfig(string config, out OpenccConfig result)`  
  Converts a valid config string to the corresponding `OpenccConfig` enum. Returns `false` if invalid.
- `static int ZhoCheck(string inputText)`  
  Detects whether the input is likely Simplified Chinese (`2`), Traditional Chinese (`1`), or neither (`0`).

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
  -f, --format <cbor|json|zstd>  Dictionary format: [zstd|cbor|json] [default: zstd]
  -o, --output <output>          Output filename. Default: dictionary_maxlength.<ext>
  -b, --base-dir <base-dir>      Base directory containing source dictionary files [default: dicts]
  -?, -h, --help                 Show help and usage information
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

## Project That Use OpenccNetLib

- [OpenccNetLibGui](https://github.com/laisuk/OpenccNetLibGui) : A GUI application for `OpenccNetLib`, providing a
  user-friendly interface for Chinese text conversion.

## License

- This project is licensed under the MIT License. See the [LICENSE](https://raw.githubusercontent.com/laisuk/OpenccNet/master/OpenccNetLib/LICENSE) file for details.
- See [THIRD_PARTY_NOTICES.md](https://raw.githubusercontent.com/laisuk/OpenccNet/master/OpenccNetLib/THIRD_PARTY_NOTICES.md) for bundled OpenCC lexicons (_Apache License 2.0_).

---

**OpenccNet** is not affiliated with the original **OpenCC** project, but aims to provide a compatible and
high-performance solution for .NET developers.


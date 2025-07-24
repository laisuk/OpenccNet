# OpenccNet

[![NuGet](https://img.shields.io/nuget/v/OpenccNetLib.svg)](https://www.nuget.org/packages/OpenccNetLib/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/OpenccNetLib.svg?label=downloads&color=blue)](https://www.nuget.org/packages/OpenccNetLib/)
[![License](https://img.shields.io/github/license/laisuk/OpenccNet.svg)](https://github.com/laisuk/OpenccNet/blob/main/LICENSE)
[![Release](https://github.com/laisuk/OpenccNet/actions/workflows/release.yml/badge.svg)](https://github.com/laisuk/OpenccNet/actions/workflows/release.yml)

**OpenccNetLib** is a fast and efficient .NET library for converting Chinese text, offering support for Simplified ↔ Traditional, Taiwan, Hong Kong, and Japanese Kanji variants. Built with inspiration from [OpenCC](https://github.com/BYVoid/OpenCC), this library is designed to integrate seamlessly into modern .NET projects with a focus on performance and minimal memory usage.

## Table of Contents
- [Installation](#installation)
- [Usage](#usage)
- [API Reference](#api-reference)
- [Dictionary Files](#dictionary-files)
- [Add-On Tools](#add-on-tools-separated-from-openccnetlib)
- [License](#license)

## Features

- Fast, multi-stage conversion using static dictionary caching
- Supports:
  - Simplified ↔ Traditional Chinese
  - Traditional (Taiwan) ↔ Simplified/Traditional
  - Traditional (Hong Kong) ↔ Simplified/Traditional
  - Japanese Kanji Shinjitai ↔ Traditional Kyojitai
- Optional punctuation conversion
- Thread-safe and suitable for parallel processing
- .NET Standard 2.0 compatible

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

| Config   | Description                                     |
|----------|-------------------------------------------------|
| s2t      | Simplified → Traditional                        |
| t2s      | Traditional → Simplified                        |
| s2tw     | Simplified → Traditional (Taiwan)               |
| tw2s     | Traditional (Taiwan) → Simplified               |
| s2twp    | Simplified → Traditional (Taiwan, phrases)      |
| tw2sp    | Traditional (Taiwan, phrases) → Simplified      |
| s2hk     | Simplified → Traditional (Hong Kong)            |
| hk2s     | Traditional (Hong Kong) → Simplified            |
| t2tw     | Traditional → Traditional (Taiwan)              |
| tw2t     | Traditional (Taiwan) → Traditional              |
| t2twp    | Traditional → Traditional (Taiwan, phrases)     |
| tw2tp    | Traditional (Taiwan, phrases) → Traditional     |
| t2hk     | Traditional → Traditional (Hong Kong)           |
| hk2t     | Traditional (Hong Kong) → Traditional           |
| t2jp     | Traditional Kyojitao → Japanese Kanji Shinjitai |
| jp2t     | Japanese Kanji Shinjitai → Traditional Kyojitai |

### Example: Convert with Punctuation

```csharp
var opencc = new Opencc("s2t"); 
string result = opencc.Convert("“汉字”转换。", punctuation: true);
Console.WriteLine(result);
// Output: 「漢字」轉換。
```

### Example: Switching Config Dynamically

```csharp
var opencc = new Opencc("s2t"); 
string result = opencc.Convert("动态切换转换方式");
Console.WriteLine(result);  // Output: 動態切換轉換方式
opencc.Config = "t2s";  // or opencc.SetConfig("t2s");
result = opencc.Convert("動態切換轉換方式");
Console.WriteLine(result);  // Output: 动态切换转换方式
```

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

## Dictionary Files

Ensure the necessary dictionary files are included in your project. Add the following to your `.csproj`.  
In most case, it is auto-set when package added from `Nuget`:

```xml
<ItemGroup>
  <None Update="dicts\dictionary_maxlength.zstd">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    <Pack>true</Pack>
    <PackagePath>contentFiles\any\any\dicts\dictionary_maxlength.zstd</PackagePath>
  </None>
  <!-- Repeat for other dictionary files -->
</ItemGroup>
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

### 📊 Benchmark Results – OpenccNetLib 1.0.3

> BenchmarkDotNet v0.15.2 · .NET 9.0.7 · Windows 11 · RyuJIT AVX2  
> Test: `BM_Convert_Sized` · Warmup + 10 Iterations

| Input Size | Mean Time | Gen0 (per 1k ops) |     Gen1 |     Gen2 | Allocated Memory |
|------------|----------:|------------------:|---------:|---------:|-----------------:|
| 100        |  10.79 µs |              2.08 |        – |        – |         21.20 KB |
| 1,000      | 169.58 µs |             22.46 |     0.98 |        – |        229.43 KB |
| 10,000     | 472.20 µs |            195.80 |    57.13 |        – |          1.99 MB |
| 100,000    |   8.57 ms |           2218.75 |   531.25 |   218.75 |         21.72 MB |
| 1,000,000  |  88.61 ms |         21,833.33 | 5,666.67 | 1,000.00 |        225.30 MB |

### ⏱ Relative Performance Chart

![Benchmark: Time vs Memory](https://raw.githubusercontent.com/laisuk/OpenccNet/master/OpenccNetLib/Images/benchmark_combined_chart.png)

### ✅ Highlights

- ✅ Preallocated StringBuilder delivers consistent performance across all input sizes.
- 🚀 Inclusive splitting ensures fewer ConvertBy() calls, improving throughput.
- 🔁 Parallel processing kicks in for large workloads (≥16 segments, ≥2000 chars) to utilize multi-core efficiency.
- 📉 Memory usage scales linearly with input size — from 21 KB to 225 MB — no spikes.
- 🧠 GC pressure remains stable and predictable, even at 1M characters:
  - Gen0: ~21K collections,
  - Gen1: ~5.6K,
  - Gen2: ~1K — all expected and manageable.
- ⚡ Warm startup is fast, ideal for both CLI batch conversion and interactive GUI usage.
- ✨ OpenccNetLib 1.0.3 is now production-ready for large-scale Chinese text conversion.

---

## API Reference

### `Opencc` Class

- `Opencc(string config = null)`  
  Create a new converter with the specified configuration.

- `string Convert(string inputText, bool punctuation = false)`  
  Convert text according to the current config.

- `string S2T(string inputText, bool punctuation = false)`  
  Simplified → Traditional

- `string T2S(string inputText, bool punctuation = false)`  
  Traditional → Simplified

- ... (see source for all conversion methods)

- `string GetLastError()`  
  Get the last error message.

- `string Config { get; set; }`  
  Gets or sets the current OpenCC config. Invalid configs fall back to "s2t" and update the error message.

- `void SetConfig(string config)`  
  Sets the current config. Falls back to "s2t" if invalid.

- `string GetConfig()`  
  Returns the current config value.

- `static bool IsValidConfig(string config)`  
  Checks whether the given config string is supported.

- `static IReadOnlyCollection<string> GetSupportedConfigs()`  
  Returns the list of valid OpenCC config names.

- `static int ZhoCheck(string inputText)`  
  Detect if text is Simplified, Traditional, or neither.

## Dictionary Data

- Dictionaries are loaded and cached on first use.
- Data files are expected in the `dicts/` directory (see `DictionaryLib` for details).

## Add-On Tools (Separated from OpenccNetLib)

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
  -i, --input <input>               Read original text from file <input>.
  -o, --output <output>             Write converted text to file <output>.
  -c, --config <config> (REQUIRED)  Conversion configuration: [s2t|s2tw|s2twp|s2hk|t2s|tw2s|tw2sp|hk2s|jp2t|t2jp]
  -p, --punct                       Punctuation conversion: True|False [default: False]
  --in-enc <in-enc>                 Encoding for input: [UTF-8|UNICODE|GBK|GB2312|BIG5|Shift-JIS] [default: UTF-8]
  --out-enc <out-enc>               Encoding for output: [UTF-8|UNICODE|GBK|GB2312|BIG5|Shift-JIS] [default: UTF-8]
  --office                          Convert Office documents (.docx | .xlsx | .pptx | .odt | .ods | .odp | .epub) [default: False]
  --format <format>                 Force Office document format: docx | xlsx | pptx | odt | ods | odp | epub
  --keep-font                       Preserve original font names in Office documents during conversion.
                                    Default: true. To disable, use: --keep-font:false [default: True]
  --auto-ext                        Automatically append correct Office document extension to output file if missing (e.g., .docx, .xlsx).
                                    Default: true. To disable, use: --auto-ext:false [default: True]
  -?, -h, --help                    Show help and usage information
```

## Project That Use OpenccNetLib

- [OpenccNetLibGui](https://github.com/laisuk/OpenccNetLibGui) : A GUI application for `OpenccNetLib`, providing a user-friendly interface for Chinese text conversion.

## License

This project is licensed under the MIT License. See the [LICENSE](LICENSE.txt) file for details.

---

**OpenccNet** is not affiliated with the original **OpenCC** project, but aims to provide a compatible and high-performance solution for .NET developers.


# OpenccNet

**OpenccNetLib** is a high-performance .NET Standard 2.0 library for Chinese text conversion, supporting Simplified ↔ Traditional, Taiwan, Hong Kong, and Japanese Kanji variants. It is inspired by [OpenCC](https://github.com/BYVoid/OpenCC) and optimized for speed and memory efficiency in .NET environments.

## Features

- Fast, multi-stage conversion using static dictionary caching
- Supports:
  - Simplified ↔ Traditional Chinese
  - Taiwan ↔ Simplified/Traditional
  - Hong Kong ↔ Simplified/Traditional
  - Japanese Kanji ↔ Traditional
- Optional punctuation conversion
- Thread-safe and suitable for parallel processing
- .NET Standard 2.0 compatible

## Installation

- Add the library to your project via NuGet (if available) or reference the source code directly.
- Add required dependencies of dictionary files to library root.
	- `dicts\dictionary_maxlength.zstd` Default dictionary file.
	- `dicts\*.*` Others dictionary files for different configurations.
 
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

| Config   | Description                                 |
|----------|---------------------------------------------|
| s2t      | Simplified → Traditional                    |
| t2s      | Traditional → Simplified                    |
| s2tw     | Simplified → Traditional (Taiwan)           |
| tw2s     | Traditional (Taiwan) → Simplified           |
| s2twp    | Simplified → Traditional (Taiwan, phrases)  |
| tw2sp    | Traditional (Taiwan, phrases) → Simplified  |
| s2hk     | Simplified → Traditional (Hong Kong)        |
| hk2s     | Traditional (Hong Kong) → Simplified        |
| t2tw     | Traditional → Traditional (Taiwan)          |
| tw2t     | Traditional (Taiwan) → Traditional          |
| t2twp    | Traditional → Traditional (Taiwan, phrases) |
| tw2tp    | Traditional (Taiwan, phrases) → Traditional |
| t2hk     | Traditional → Traditional (Hong Kong)       |
| hk2t     | Traditional (Hong Kong) → Traditional       |
| t2jp     | Traditional → Japanese Kanji                |
| jp2t     | Japanese Kanji → Traditional                |

### Example: Convert with Punctuation

```csharp
var opencc = new Opencc("s2t"); 
string result = opencc.Convert("“汉字”转换。", punctuation: true);
Console.WriteLine(result);
// Output: 「漢字」轉換。
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

### Using Custom Dictionary

Library default to use zstd compressed dictionary Lexicon, this can be changed to custom dictionary (JSON, CBOR or "baseDir/*.txt")before instantiate Opencc() :

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

- `static int ZhoCheck(string inputText)`  
  Detect if text is Simplified, Traditional, or neither.

## Dictionary Data

- Dictionaries are loaded and cached on first use.
- Data files are expected in the `dicts/` directory (see `DictionaryLib` for details).

## Add-On Tools

### OpenccConvert

```
Description:
  OpenCC Converter for command-line text conversion.

Usage:
  OpenccConvert [options]

Options:
  -i, --input <input>               Read original text from file <input>.
  -o, --output <output>             Write converted text to file <output>.
  -c, --config <config> (REQUIRED)  Conversion configuration: [s2t|s2tw|s2twp|s2hk|t2s|tw2s|tw2sp|hk2s|jp2t|t2jp]
  -p, --punct                       Punctuation conversion: True|False [default: False]
  --in-enc <in-enc>                 Encoding for input: [UTF-8|UNICODE|GBK|GB2312|BIG5|Shift-JIS] [default: UTF-8]
  --out-enc <out-enc>               Encoding for output: [UTF-8|UNICODE|GBK|GB2312|BIG5|Shift-JIS] [default: UTF-8]
  --version                         Show version information
  -?, -h, --help                    Show help and usage information
  
```

### DictGenerate

```
Description:
  Dictionary Generator CLI Tool

Usage:
  DictGenerate [options]

Options:
  -f, --format <cbor|json|zstd>  Dictionary format: [zstd|cbor|json] [default: zstd]
  -o, --output <output>          Output filename. Default: dictionary_maxlength.<ext>
  -b, --base-dir <base-dir>      Base directory containing source dictionary files [default: dicts]
  --version                      Show version information
  -?, -h, --help                 Show help and usage information
  
```

## License

[MIT](LICENSE.txt)

---

**OpenccNet** is not affiliated with the original OpenCC project, but aims to provide a compatible and high-performance solution for .NET developers.


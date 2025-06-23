# OpenccNet CLI

**OpenccNet** is a fast, Unicode-aware, OpenCC-powered document converter. It supports conversion of plain text and Office-style structured formats (e.g., `.docx`, `.xlsx`, `.pptx`, `.odt`, `.epub`) from Simplified to Traditional Chinese and vice versa.

## 🚀 Features

- ✅ OpenCC config-based conversion (`s2t`, `t2s`, etc.)
- 📁 Batch convert `.docx`, `.xlsx`, `.pptx`, `.odt`, `.ods`, `.odp`, `.epub`
- 🧠 Font name preservation (optional)
- 🧼 Converts only target content (not binary or irrelevant metadata)
- 🖥️ CLI-friendly output and stdin/stdout support

---

## 📦 Installation

> Coming soon via NuGet

```bash
# Planned:
dotnet tool install --global OpenccNet.Cli
```

## 🔧 Usage

```bash
OpenccNet convert -c <config> [options]
```

### Examples

#### Convert plain text

```bash
OpenccNet convert -c s2t -i input.txt -o output.txt
```

#### Convert Office document (docx → Traditional)

```bash
OpenccNet convert -c s2t -i file.docx --office --format docx -p
```

#### Convert EPUB file with font preservation

```bash
OpenccNet convert -c s2t -i book.epub --office --format epub --keep-font:false
```

#### Use stdin/stdout

```bash
echo "汉字简化" | OpenccNet convert -c s2t
```

---

## ⚙️ Options

| Option           | Description                                                       |
|------------------|-------------------------------------------------------------------|
| `-c`, `--config` | OpenCC config: `s2t`, `t2s`, `s2tw`, etc. (required)              |
| `-i`, `--input`  | Input file path (or use stdin)                                    |
| `-o`, `--output` | Output file path (or use stdout)                                  |
| `--office`       | Enable Office/EPUB document conversion                            |
| `--format`       | Format for Office document: `docx`, `xlsx`, `pptx`, `odt`, `epub` |
| `--punct`, `-p`  | Enable punctuation conversion                                     |
| `--keep-font`    | Preserve original font names (default: true)                      |
| `--auto-ext`     | Automatically append correct file extension (default: true)       |

---

## 🔤 Supported Formats

| Format | Extension | Converted Part(s)                |
|--------|:----------|:---------------------------------|
| DOCX   | `.docx`   | `word/document.xml`              |
| XLSX   | `.xlsx`   | `xl/sharedStrings.xml`           |
| PPTX   | `.pptx`   | All `ppt/*.xml` slides and notes |
| ODT    | `.odt`    | `content.xml`                    |
| ODS    | `.ods`    | `content.xml`                    |
| ODP    | `.odp`    | `content.xml`                    |
| EPUB   | `.epub`   | All `.xhtml`, `.opf`, `.ncx`     |

---

## ❓ FAQ

**Q: Will this overwrite fonts or layouts?**\
**A:** No. Conversion happens only in readable XML/text. Font names are preserved by default.

**Q: Is this meant for document editing?**\
**A:** No. OpenccNet is a text conversion tool, not an Office editor.

---

## 🛠️ Development

- Target framework: `.NET 8.0`
- Uses: [`OpenccNetLib`](https://github.com/laisuk/OpenccNetLib)
- CLI: `System.CommandLine`

---

## 📄 License

MIT License. See [LICENSE](LICENSE) for details.

```bash
OpenccNet --help
Description:
  OpenccNet: A CLI tool for OpenccNetLib dictionary generation and Open Chinese text conversion.

Usage:
  OpenccNet [command] [options]

Options:
  --version       Show version information
  -?, -h, --help  Show help and usage information

Commands:
  dictgen  Generate OpenccNetLib dictionary files.
  convert  Convert text using OpenccNetLib configurations.
```

```bash
OpenccNet dictgen --help
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

```bash
OpenccNet convert --help
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
  --office                          Convert Office documents (.docx | .xlsx | .pptx | .odt | .ods | .odp | .epub)
                                    [default: False]
  --format <format>                 Force Office document format: docx | xlsx | pptx | odt | ods | odp | epub
  --keep-font                       Preserve original font names in Office documents during conversion.
                                    Default: true. To disable, use: --keep-font:false [default: True]
  --auto-ext                        Automatically append correct Office document extension to output file if missing
                                    (e.g., .docx, .xlsx).
                                    Default: true. To disable, use: --auto-ext:false [default: True]
  -?, -h, --help                    Show help and usage information
```
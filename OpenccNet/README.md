# OpenccNet CLI

**OpenccNet** is a fast, Unicode-aware, OpenCC-powered document converter. It supports conversion of plain text and Office-style structured formats (e.g., `.docx`, `.xlsx`, `.pptx`, `.odt`, `.epub`) from Simplified to Traditional Chinese and vice versa.

## 🚀 Features

- ✅ OpenCC config-based conversion (`s2t`, `t2s`, etc.)
- 📁 Batch convert `text-based` files, `.docx`, `.xlsx`, `.pptx`, `.odt`, `.ods`, `.odp`, `.epub` and `.pdf`
- 🧠 Font name preservation (optional)
- 🧼 Converts only target content (not binary or irrelevant metadata)
- 🖥️ CLI-friendly output and stdin/stdout support

---

## 📦 Installation

Extract and run OpenccNet executable file.

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
OpenccNet office -c s2t -i file.docx --format docx -p
```

#### Convert EPUB file with font preservation

```bash
OpenccNet office -c s2t -i book.epub --format epub --keep-font:false
```

#### Convert PDF document (Text embedded or searchable PDF document)

```bash
OpenccNet pdf -c s2t -p -i sample.pdf -o sample.txt --reflow
```

#### Use stdin/stdout

```bash
echo "汉字简化" | OpenccNet convert -c s2t
```

---

## ⚙️ Office Document Conversion Options

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
  OpenccNet – Convert Chinese text or Office documents using OpenccNetLib configurations and generate dictionaries.

Usage:
  OpenccNet [command] [options]

Options:
  -?, -h, --help  Show help and usage information
  --version       Show version information

Commands:
  dictgen  Generate OpenccNetLib dictionary files.
  convert  Convert text using OpenccNetLib configurations.
  office   Convert Office documents or Epub using OpenccNetLib.
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
  -i, --input              Read original text from file <input>
  -o, --output             Write original text to file <output>
  -c, --config (REQUIRED)  Conversion configuration: s2t|s2tw|s2twp|s2hk|t2s|tw2s|tw2sp|hk2s|jp2t|t2jp
  -p, --punct              Punctuation conversion. [default: False]
  --in-enc                 Encoding for input: UTF-8|UNICODE|GBK|GB2312|BIG5|Shift-JIS [default: UTF-8]
  --out-enc                Encoding for output: UTF-8|UNICODE|GBK|GB2312|BIG5|Shift-JIS [default: UTF-8]
  -?, -h, --help           Show help and usage information
```

```bash
OpenccNet office --help  
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

```bash
OpenccNet pdf --help
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
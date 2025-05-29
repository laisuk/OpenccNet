# OpenccConvert

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
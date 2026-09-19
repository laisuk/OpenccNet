# Dictionary persistence validation

Validated on Windows x64, 2026-09-19, using the existing Rust implementation at
`D:/codes/Rust/opencc-fmmseg` as a design reference.

## Implementation

- `DictionaryLib.cs`: one O (1) slot storage predicate, source-generated JSON write-contract modifier,
  and conditional CBOR emission with definite map count 5 instead of 6.
- Exact predicate: `LengthMask > 0UL && LengthMask <= 3UL &&
  (LongLengths == null || LongLengths.Count == 0)`.
- Readable JSON, unescaped JSON, compact JSON inside Zstd, and CBOR share that predicate.
  The JSON source-generation context itself remains unchanged; no reflection fallback was added.
- Existing `EnsureDerivedMetadata` / `EnsureDictionaryMetadata` / `BuildStarterLenMask` already
  restore omitted starter maps once. No new reader, conversion-path scan, runtime representation,
  public API, plan, or cache change is needed. Supplied nonempty maps remain untouched.
- Tests added in `DictionaryPersistenceSlimmingTests.cs`; README, XML comments, changelog, and
  package release notes updated. Regenerated `data/dictionary_maxlength.json`,
  `data/dictionary_maxlength.cbor`, and embedded `dicts/dictionary_maxlength.zstd`.

## Compatibility and design caveats

UTF-16 lengths 1 and 2 do not prove single-scalar keys: two-BMP-character phrases also qualify.
The requested metadata-only predicate is used without a key scan. The general starter builder
correctly reconstructs such phrases under the first BMP character and supplementary scalars under
their complete surrogate pair, with mask `2UL`. Tables with lengths 3..64 or nonempty `LongLengths`
retain their stored starter metadata. All built-in phrase slots retain their maps.

Absent and explicit-null starter fields both decode to null. Explicit-empty maps decode as empty.
Existing normalization repairs all three states for nonempty dictionaries; nonempty supplied maps
are preserved, including deliberately non-derived fixture values. Empty dictionaries retain their
existing null starter-map behavior. CBOR casing aliases and unknown-field handling are unchanged.
The new reader accepts old artifacts. Older readers accepting new slim artifacts is not guaranteed.
Application-owned direct JsonSerializer calls are outside the library persistence helpers.

## Size comparison

Both sides were regenerated from the same current `OpenccNetLib/dicts` text sources using the
pre-change and post-change library helpers. Dictionary mappings match the original checked-in JSON.
Zstd uses the existing level 19. Compact JSON was obtained by decompressing the Zstd artifact.
The checked-in JSON keeps its existing unescaped-readable style. Sizes below are bytes on this
Windows checkout; line-ending changes can affect readable exports.

| Format                  |    Before |     After | Removed | Reduction |
|-------------------------|----------:|----------:|--------:|----------:|
| Readable JSON           | 3,485,820 | 3,290,492 | 195,328 |     5.60% |
| Unescaped readable JSON | 2,333,169 | 2,173,234 | 159,935 |     6.85% |
| Compact JSON            | 2,760,046 | 2,648,557 | 111,489 |     4.04% |
| CBOR                    | 1,299,436 | 1,251,261 |  48,175 |     3.71% |
| Zstd JSON               |   450,467 |   435,952 |  14,515 |     3.22% |

## Omitted slots

| Slot                 | LengthMask | Starter entries removed |
|----------------------|-----------:|------------------------:|
| `st_characters`      |          3 |                   4,014 |
| `ts_characters`      |          3 |                   4,148 |
| `tw_variants`        |          1 |                      67 |
| `tw_variants_rev`    |          1 |                      44 |
| `hk_variants`        |          1 |                      79 |
| `hk_variants_rev`    |          1 |                      78 |
| `jps_characters`     |          1 |                     403 |
| `jps_characters_rev` |          1 |                     460 |
| `st_punctuations`    |          1 |                       4 |
| `ts_punctuations`    |          1 |                       4 |

Total: 10 slots and 9,301 redundant starter entries.

## Validation results

- `dotnet build OpenccNet.sln -c Release --no-restore`: passed, 0 warnings, 0 errors.
- `dotnet test OpenccNetTests/OpenccNetTests.csproj -c Release --no-build`: passed on both
  net8.0 (netstandard2.0 library) and net10.0 (net9.0 library), 237 tests each, no failures or skips.
  Ran before and after installing the regenerated assets.
- Ten new parameterized cases exercise every writer, physical omission, CBOR definite counts and
  complete consumption, runtime non-mutation, BMP/supplementary/mixed keys, length-3 phrases,
  two-BMP phrases, long-length exclusion, and absent/null/empty/supplied legacy maps in all formats.
  Existing PeterO golden fixture, unknown-field, casing, conversion and custom dictionary tests pass.
- `dotnet pack OpenccNetLib/OpenccNetLib.csproj -c Release --no-build`: passed; nupkg and snupkg created.
- NativeAOT win-x64 smoke executable (net9.0), with `PublishAot=true` and
  `JsonSerializerIsReflectionEnabledByDefault=false` on the executable project: published and ran
  successfully without warnings. Saved all formats, loaded JSON/CBOR/Zstd and the embedded provider,
  verified ST starter masks and S2T conversion. Native artifacts have identical measured sizes.
  An initial global PublishAot flag was rejected for the library netstandard2.0 target (NETSDK1207);
  scoping the flag to the smoke executable resolved the invocation issue.
- `git diff --check`: passed. No browser/WASM runtime test was performed.

Local baseline/after artifacts, smoke project and native executable are retained under the ignored
`artifacts/persistence-check` directory. Full test result files are under `OpenccNetTests/TestResults`.

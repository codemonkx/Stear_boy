# Reference artifacts

The original Windows build is **not** committed. Place it here when you need it:

```
reference/Gyropad-PC-Setup.exe
```

## Expected hashes

Verified against the build analysed on 2026-11-14.

| Artifact | Size | SHA-256 |
|---|---|---|
| `Gyropad-PC-Setup.exe` (Inno Setup installer) | 16,426,243 | `12b58dd3ac3fb8862cf17ace55b8ccaae2112f713e8b75d5f63f3881ac901caf` |
| `GyropadPC.exe` (app, extracted from installer) | 18,033,664 | `c23a4194149a53bb822c9b71ba4f75470af2c36a49f268838ebf4103bd28612b` |

Confirm before analysis:

```powershell
Get-FileHash .\reference\Gyropad-PC-Setup.exe -Algorithm SHA256
```

## Extraction

The installer is Inno Setup 6.7.0. `innoextract` 1.9 cannot read 6.7.0;
`innounp` 2.70.1 can:

```powershell
innounp -x -d .\extracted .\reference\Gyropad-PC-Setup.exe
# -> extracted\{app}\GyropadPC.exe
```

Do not commit `extracted/` — it is gitignored.

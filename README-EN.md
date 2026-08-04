# Zotero Duplicate PDF Cleaner — Get Your Disk Space Back

Your Downloads folder is full of PDFs you "already have". The same paper is stored in Zotero, and another copy is still sitting in your Downloads folder — you can't tell, because it was renamed.

This tool helps you **find those duplicate files and safely send them to the Recycle Bin**.

> If you prefer the command line, check out [zotero-cleaner-cli](https://github.com/Tenktau/zotero-cleaner-cli), the CLI version I developed in parallel for the same need.

[![Generic badge](https://img.shields.io/badge/README-CN-red.svg)](https://github.com/Tenktau/zotero-cleaner-gui/blob/master/README.md)
[![Latest release](https://badgen.net/github/release/Naereen/Strapdown.js)](https://github.com/Tenktau/zotero-cleaner-gui/releases)
![GitHub Downloads](https://img.shields.io/github/downloads/Tenktau/zotero-cleaner-gui/total?color=green)

## What it does

- **Only deletes duplicates**: identifies files by content, not by filename. Renames and `(1)`, `(2)` suffixes — none of them can fool it.
- **Only touches one place**: only cleans the folder you specify; attachments in your Zotero library stay untouched.
- **Safe to delete**: files go to the Recycle Bin first, so you can recover anything deleted by mistake.
- **Ready to use**: auto-detects your Zotero attachment folder and Downloads folder; you can also pick them manually with the "Browse…" button.

## Get started in a minute

Open the program and do three things:

1. It auto-finds your Zotero attachment folder and Downloads folder (if not found, pick them via "Browse…").
2. Click "Start Scan".
3. Check the files to clean (all selected by default) and click "Move to Recycle Bin".

> Windows only for now (deletion relies on the system Recycle Bin API). Portable and green — no installation, no registry writes.

## Download and use

Download the latest **`ZoteroPdfCleaner-windows.zip`** from the [Releases](https://github.com/Tenktau/zotero-cleaner-gui/releases) page, extract it, and double-click to run.

> **The "warning" your browser shows after downloading is normal**: Windows flags "unsigned programs downloaded from the internet" as "Unsafe / Unknown publisher". It is just a routine check on unknown programs — **it does not mean the file is bad**.

### Build it yourself (optional)

Windows 10/11 ships with .NET Framework, so **no SDK is required**:

1. Download or clone the [source code](https://github.com/Tenktau/zotero-cleaner-gui/archive/refs/heads/master.zip).
2. Double-click `build.cmd` after decompress.
3. `ZoteroPdfCleaner.exe` is generated in the repository directory.

## How it works

1. Calculate a "content fingerprint" (SHA-256) for every file in the Zotero attachment folder and build an index.
2. Scan the target folder: first filter by **file size** — if the sizes differ, the content must differ — then compare the fingerprints of the remaining candidates.
3. Fingerprints match → duplicate found → confirm by checking → **move to Recycle Bin** (recoverable, not permanently deleted).

## Customization (optional)

By default, the program auto-detects the Zotero attachment folder and the system Downloads folder. To specify paths manually, just click "Browse…" in the two input boxes at the top.

## Changelog

### 0.2.0 (2026-08-03)

- Added an author / repository / license notice at the bottom of the window; the repository link is clickable
- Added the MIT license
- Added "Build it yourself" instructions and a note that the download warning is normal

### 0.1.0 (2026-08-03)

The first usable version of the core flow, in sync with the CLI version.

- Deduplication by **SHA-256 content fingerprint** — renames and `(1)` suffixes can't fool it
- Single-file portable app: double-click to run, no command line required
- Auto-detects the Zotero attachment folder and Downloads folder (supports custom data directories and OneDrive redirection)
- Visual workflow: progress bar + checkable result list + select-all / deselect-all
- Double safety: deletion only happens after you confirm the list; files go to the Recycle Bin and are recoverable
- Built-in guard: refuses to clean the Zotero attachment folder itself or its subfolders
- GitHub Actions automation: pushing a `v*` tag automatically compiles, packages, and publishes a Release

## TODO

Ideas are welcome — file an Issue or a PR any time (or just come and pester me).

- [ ] **Export lists**: write each cleanup to CSV / a log for later review
- [ ] **Detect "similar" PDFs**: different versions or re-compressed copies of the same paper (fuzzy matching is harder — and more useful — than exact hashing)
- [ ] **Cross-platform**: support macOS / Linux via .NET MAUI or Avalonia (the Recycle Bin logic needs adaptation)
- [ ] **App icon**: give the portable version a proper icon

## Credits

I asked, DeepSeek built, DeepSeek is great — say it with me: "Thank you, DeepSeek!"…

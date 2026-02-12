# FileDeduplicator

A command-line tool that finds and removes duplicate files recursively within a directory, keeping the copy with the shortest filename.

## How It Works

1. Recursively scans all files in the specified directory
2. Computes SHA256 hashes in parallel to identify duplicates by content
3. Groups files with identical hashes
4. For each group of duplicates, keeps the file with the shortest filename and removes the rest

## Installation

```sh
dotnet build
```

## Usage

### Interactive Menu

Run without arguments to launch the interactive menu:

```sh
FileDeduplicator
```

### Commands

```sh
# Scan a directory and display duplicate groups
FileDeduplicator Scan -p "C:\path\to\directory"

# Preview what would be deleted (no files are modified)
FileDeduplicator DryRun -p "C:\path\to\directory"

# Remove duplicates (prompts for confirmation before deleting)
FileDeduplicator Deduplicate -p "C:\path\to\directory"

# Show file and duplication statistics
FileDeduplicator Stats -p "C:\path\to\directory"
```

### Options

| Option | Description |
|--------|-------------|
| `-p`, `--path` | The root path to scan for files |
| `--help` | Display help for a specific command |
| `--version` | Display version information |

## Which File Is Kept?

When duplicates are found, the file with the **shortest filename** is kept. If two files have filenames of equal length, the one with the lexicographically first full path is kept.

For example, given these duplicates:
- `C:\photos\vacation\IMG_20240101_123456.jpg`
- `C:\photos\beach.jpg`

`beach.jpg` is kept because its filename is shorter.

## Commands

### Scan

Discovers duplicate files and displays them grouped by content hash. Shows which file would be kept and which would be deleted, along with wasted disk space.

### DryRun

Identical to Scan but formatted as a deletion preview with a summary of how many files would be removed and how much space would be reclaimed. No files are modified.

### Deduplicate

Performs the actual deduplication. After scanning and displaying results, prompts for confirmation (`y/N`) before deleting any files. Reports the number of files deleted and disk space reclaimed.

### Stats

Displays statistics including total files, unique files, duplicate count, wasted space, duplicates broken down by file extension, and the largest duplicate groups by wasted space.

## Requirements

- .NET 10.0 SDK or later

# Pst2Mbox

A .NET command-line tool that converts an Outlook Personal Store `.pst` file into an Mail box `.mbox` file, so its contents can be imported into mail clients that support mbox (Thunderbird, Apple Mail, etc.).

## Requirements

- .NET 10

## Building

```
dotnet build
```

This produces `pst2mbox.exe` in `bin/Debug/net10.0/`.

## Usage

```
pst2mbox <input.pst> <output.mbox> [options]
```

Or, without building a standalone exe:

```
dotnet run -- <input.pst> <output.mbox> [options]
```

### Options

| Option | Description |
|---|---|
| `-f, --folder <text>` | Only convert messages from folders whose path contains `<text>` (case-insensitive). Default: all folders. |
| `-a, --include-all` | Include all item types (calendar, contacts, tasks, notes), not just regular mail (`IPM.Note`). Default: mail only. |
| `-s, --split-folders` | Write one `.mbox` file per PST folder instead of a single combined file. `<output.mbox>` is treated as an output directory (created if needed); each folder that contains qualifying messages is written to `<folder name>.mbox` inside it. Folders with no qualifying messages are skipped. If two folders share the same name, later ones get a ` (2)`, ` (3)`, ... suffix. |
| `--overwrite` | Overwrite the output file if it already exists (or reuse a non-empty output directory in `--split-folders` mode). |
| `-v, --verbose` | Print each converted message as it is written. |
| `-h, --help` | Show usage help. |

### Examples

Convert an entire PST file:

```
pst2mbox "C:\Mail\Outlook.pst" "C:\Mail\outlook.mbox" --overwrite
```

Convert only messages from folders whose path contains "Inbox":

```
pst2mbox "C:\Mail\Outlook.pst" "C:\Mail\inbox.mbox" -f Inbox --overwrite
```

Convert everything, including calendar/contacts/tasks/notes, with verbose output:

```
pst2mbox "C:\Mail\Outlook.pst" "C:\Mail\full.mbox" -a -v --overwrite
```

Export each PST folder to its own mbox file in a directory:

```
pst2mbox "C:\Mail\Outlook.pst" "C:\Mail\ByFolder" --split-folders --overwrite
```

This creates `C:\Mail\ByFolder\` (if it doesn't exist) with one `<folder name>.mbox` file per folder that contains messages, e.g. `Inbox.mbox`, `Sent Items.mbox`, `Archive.mbox`.

## Output

After conversion, a summary is printed to the console:

- Folders visited
- Mbox files written (only shown with `--split-folders`)
- Messages converted
- Messages skipped by filter
- Attachments included
- Elapsed time
- Any errors encountered during conversion (also printed to stderr)

## Notes

- The input `.pst` file must exist; the tool exits with an error otherwise.
- By default, the output mbox file is not overwritten — pass `--overwrite` to replace an existing file. With `--split-folders`, the same applies to a non-empty output directory.

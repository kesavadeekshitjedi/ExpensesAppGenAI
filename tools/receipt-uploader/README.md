# Receipt uploader (local util)

A standalone PowerShell watcher that files fixed-named receipt PDFs into the Costco receipts folder with a
date stamp taken from the receipt. It is **not part of the web app or API** — nothing here is under `src/`,
so CI/Deploy never build or ship it. It runs on the machine where OneDrive syncs.

## What it does

A phone/scanner workflow usually saves every receipt to the **same name** (e.g. `Gas_Station_Receipt.pdf`).
This watcher:

1. Watches the OneDrive root (`D:\OneDrive`) for `In_Warehouse_Receipt.pdf` and `Gas_Station_Receipt.pdf`.
2. When one appears and is fully written, reads the **date printed on the receipt** (Costco `Date: MM/DD/YY`,
   plus the time when it's cleanly readable; Amazon `Order placed …` as a fallback).
3. **Moves** it into `D:\OneDrive\Costo Receipts` renamed `"<base>_<yyyy-MM-dd_HHmm>.pdf"`
   (e.g. `Gas_Station_Receipt_2026-10-07_1518.pdf`). Moving frees the fixed name for the next receipt;
   same-date collisions get a `_1`, `_2`, … suffix. If no date can be read, the file's last-write time is used.

From there you import them in the web app under **Expenses → Import invoice** (they're Costco PDFs).

## Run it

```powershell
# default paths and 30s polling
pwsh -File D:\temp\receit_uploader\Watch-Receipts.ps1

# test against the current files once and exit
pwsh -File D:\temp\receit_uploader\Watch-Receipts.ps1 -RunOnce

# keep originals instead of moving (dedupes by content hash), faster polling
pwsh -File D:\temp\receit_uploader\Watch-Receipts.ps1 -Mode Copy -IntervalSeconds 15
```

Stop it with `Ctrl+C`. To run it automatically at logon, create a Task Scheduler task that runs
`pwsh -WindowStyle Hidden -File D:\temp\receit_uploader\Watch-Receipts.ps1` at logon.

Parameters: `-WatchFolder`, `-DestFolder`, `-FileNames`, `-IntervalSeconds`, `-Mode Move|Copy`, `-RunOnce`.

## Why polling (every 30s) instead of a file-creation event

`FileSystemWatcher` gives real "file created" events, but OneDrive's sync and **cloud/placeholder**
behavior make those events unreliable for files that arrive via sync (the file can appear as a placeholder
first, or be created/renamed in a way that doesn't raise a usable `Created` event). A short poll is simpler
and dependable, and the script waits for the file to stop changing (size stable + exclusively openable)
before filing it, so it never grabs a half-synced file. 30s is the default; lower it with `-IntervalSeconds`.

## Notes

- PDF text is read with no external dependencies: the script inflates the PDF's FlateDecode streams with
  `System.IO.Compression` and pulls the text. This is best-effort — the **date** is reliable on these Costco
  receipts; the **time** is included only when the receipt doesn't kern the digits.
- The canonical copy lives here in the repo (`tools/receipt-uploader/`); a runnable copy is kept at
  `D:\temp\receit_uploader\` for day-to-day use. Keep them in sync if you edit one.

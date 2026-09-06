# FindUpTo Printer Agent

The Windows printer agent runs on the Windows POS workstation and bridges the POS application to printers installed in Windows.

## Current implementation

- Enumerates Windows local/connection print queues through `System.Printing`.
- Detects likely USB/cable, Bluetooth and network printers when Windows exposes them as print queues.
- Classifies queues as `Thermal80mm` or `A4` using printer/port/driver metadata.
- Sends raw ESC/POS bytes through the Windows spooler for thermal printers.
- Binds to `127.0.0.1:18991` only.
- Requires `X-FindUpTo-Agent-Key` on printer discovery and print endpoints.
- Uses a fixed-time comparison for the authentication key.

## Start

Set a random key of at least 32 characters in the Windows service/process environment:

```bat
set FINDUPTO_PRINTER_AGENT_KEY=replace-with-a-random-local-agent-secret
```

Then run the published agent executable. `/health` is intentionally unauthenticated so service monitoring can check availability. `/printers` and `/print/raw` require the header.

## Routing contract

- Sale/receipt -> choose a connected `Thermal80mm` printer.
- Report/document -> choose a connected `A4` printer.
- If no matching printer exists, the caller should report printer-unavailable while keeping the underlying business transaction successful.

## Important limitation

Printer classification is based on Windows spooler metadata; it is not a hardware capability certificate. Always test the actual printer model, driver and paper width during deployment. A4 document rendering and end-to-end server-to-agent routing should be treated as a separate integration step from the raw ESC/POS path.

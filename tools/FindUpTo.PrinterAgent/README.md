# FindUpTo Printer Agent

The Windows printer agent is intentionally documented here before platform-specific spooler integration is added.

## Purpose

The agent will run on the Windows POS workstation and bridge the server/Flutter POS to printers installed in Windows. It is responsible for:

- discovering Windows-installed USB/cable, Bluetooth, and network printers;
- identifying likely 80mm thermal printers and A4/document printers;
- accepting authenticated loopback print requests;
- sending raw ESC/POS to selected thermal printers;
- sending report documents to an A4 printer through the Windows print spooler.

## Security

The agent must bind to loopback only and require a locally provisioned authentication key for every print request. It must not expose a LAN-wide unauthenticated print endpoint.

## Routing

- Receipt/sale -> connected 80mm thermal printer.
- Report/document -> connected A4 printer.
- If no matching printer exists, return a clear error so the POS can keep the sale/report operation successful.

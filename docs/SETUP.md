# FindUpTo POS — installation and connection guide

This project uses a Windows PC as the POS server/database host and Flutter clients (Android/Windows) as clients. Clients never open the SQLite file directly; they communicate with the server over HTTP and SignalR.

## 1. Requirements

### Windows POS computer
- Windows 10/11 x64
- .NET 8 SDK for development/building
- Flutter SDK + Android SDK for mobile builds
- Visual Studio 2022 with Desktop development with C++ for Flutter Windows builds
- A stable local network/Wi-Fi for phones, tablets, kitchen screens and printers

### Android device
- Android 8+ recommended
- Same LAN/Wi-Fi as the POS computer
- USB debugging only if you are developing; production users install the APK

## 2. First-time server configuration

Open a new **Command Prompt** in the repository root and set the required secrets for the current shell:

```bat
set POS_JWT_KEY=replace-with-a-random-secret-at-least-32-characters
set INITIAL_OWNER_PASSWORD=replace-with-a-strong-owner-password
set INITIAL_ADMIN_PASSWORD=replace-with-a-strong-admin-password
set INITIAL_WAITER_PASSWORD=replace-with-a-strong-waiter-password
set INITIAL_COUNTER_PASSWORD=replace-with-a-strong-counter-password
```

Optional business configuration before the first server start:

```bat
set POS_BUSINESS_NAME=Your Restaurant Name
set POS_BUSINESS_PHONE=03001234567
set POS_BUSINESS_ADDRESS=Your address
set POS_TAX_PERCENT=0
set POS_CURRENCY_CODE=PKR
set POS_CURRENCY_SYMBOL=Rs.
```

The initial business settings are only seeded when the database is first created. Change them later through the authenticated settings API/UI.

Do not commit these values to Git. For a permanent Windows deployment, use protected machine/user environment variables or Windows service configuration rather than putting secrets in source files.

## 3. Build everything

From the repository root:

```bat
BUILD.bat
```

The script restores, builds and tests the .NET solution, publishes the self-contained Windows server, runs Flutter analysis/tests, and builds Android release APK + Windows release output.

Outputs:

```text
artifacts/windows-server/
mobile/flutter_app/build/app/outputs/flutter-apk/app-release.apk
mobile/flutter_app/build/windows/x64/runner/Release/
```

If you only need the server:

```bat
dotnet publish src\FindUpTo.Pos.Server\FindUpTo.Pos.Server.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts\windows-server
```

## 4. Start the server during development

From the same shell containing `POS_JWT_KEY` and the initial passwords:

```bat
dotnet run --project src\FindUpTo.Pos.Server
```

The default local URL is normally `http://localhost:5000` unless ASP.NET Core selects another configured port.

For another device on the LAN, use the Windows PC's LAN IPv4 address, for example:

```text
http://192.168.1.50:5000
```

Do not expose the server directly to the public internet. Use a properly authenticated relay/reverse proxy/VPN for remote access.

## 5. Find the Windows server IP

On the POS PC:

```bat
ipconfig
```

Find the active Wi-Fi/Ethernet adapter's **IPv4 Address**. Example: `192.168.1.50`.

From an Android phone on the same Wi-Fi, the API base URL becomes:

```text
http://192.168.1.50:5000
```

Windows Firewall must allow inbound TCP traffic on the server's configured HTTP port for the local/private network profile.

## 6. Connect the Flutter app

The Flutter app accepts `POS_SERVER_URL` at build/run time.

Android emulator:

```bat
flutter run --dart-define=POS_SERVER_URL=http://10.0.2.2:5000
```

Physical Android phone:

```bat
flutter run --dart-define=POS_SERVER_URL=http://192.168.1.50:5000
```

Windows desktop client:

```bat
flutter run -d windows --dart-define=POS_SERVER_URL=http://127.0.0.1:5000
```

For a second Windows PC on the same LAN, replace `127.0.0.1` with the POS server's LAN IP.

## 7. Recommended deployment layout

```text
                    Local Wi-Fi / Ethernet
                             |
                 +-----------+-----------+
                 |                       |
          Windows POS Server        Printers / scanners
          ASP.NET Core + SQLite     USB / Bluetooth / LAN
                 |
       +---------+---------+
       |         |         |
   Counter     Kitchen    Waiter/Rider
   Windows     Android   Android/Windows
       |
    Customer Android app
```

The Windows POS server is the source of truth. Mobile clients use the API, SignalR realtime events, and the offline queue. SQLite must remain on the server; never copy the database into the mobile app as a shared database.

## 8. Roles and first login

The first seeded staff accounts are:

| Username | Role |
|---|---|
| Malik | Owner |
| MK | Admin |
| WR | Waiter |
| CP | Counter |

Their passwords come from the four `INITIAL_*_PASSWORD` environment variables. Change passwords through the user-management functionality after installation.

## 9. Printing

The POS supports network printer discovery and 80mm thermal receipt printing. The repository also contains a Windows printer agent for OS-managed printers.

Recommended topology:

- 80mm receipt: USB/Bluetooth/Windows-connected thermal printer on the POS PC, or a LAN ESC/POS printer.
- A4 report/invoice: Windows-installed A4 printer.
- Network ESC/POS printer: reachable from the POS server on TCP 9100.

Printer availability must not be treated as payment success/failure. A sale remains recorded even when a receipt printer is temporarily unavailable; the receipt can be reprinted.

## 10. Offline operation

The staff POS can queue sales/payment intent locally and retry synchronization when the server becomes available. The server uses idempotent client operation IDs to prevent duplicate order creation during retries.

Offline mode is not permission to bypass server validation. When synchronization occurs, the server validates products, customers, tables, payment data and order contents again and records conflicts for administrator review.

## 11. Backup and restore

Keep automatic backups enabled on the POS server. Store periodic copies outside the application directory and test restores on a separate machine.

A restore is an administrative operation and should be performed when active POS transactions are stopped. Always create a safety backup before replacing the live database.

## 12. Troubleshooting connection problems

### Phone cannot connect
1. Confirm the phone and POS PC are on the same Wi-Fi/LAN.
2. Run `ipconfig` on the POS PC and verify the IPv4 address.
3. Open `http://SERVER_IP:PORT/health` from the phone browser.
4. Check Windows Firewall for the configured server port.
5. Check that the server process is running.
6. Verify the Flutter `POS_SERVER_URL` value.

### Login fails
- Confirm the initial password environment variables were set before the first database creation.
- If users already exist, changing environment variables does not reset their stored password hashes.
- Use the user-management/password-reset workflow instead of deleting the database.

### SignalR realtime events fail
- Verify the same server URL is reachable from the client.
- Check that WebSocket traffic is permitted by any reverse proxy/firewall.
- Re-login after changing the server URL so the client has a valid JWT.

### Printer is not found
- For a LAN printer, verify its IP and TCP 9100 reachability.
- For USB/Bluetooth printers, install/connect the printer in Windows first and run the Windows printer agent.
- Check the printer's Windows queue/driver and print a Windows test page.

## 13. Production checklist

- [ ] Set a unique random `POS_JWT_KEY` (32+ characters; longer is better).
- [ ] Set strong initial passwords and change them after first login.
- [ ] Configure the real business settings.
- [ ] Use a fixed Windows server IP or DHCP reservation.
- [ ] Restrict the Windows Firewall rule to the private LAN where possible.
- [ ] Keep the SQLite database and backups on reliable storage.
- [ ] Test backup restore before opening the restaurant.
- [ ] Install/configure printers and test both receipt and A4 output.
- [ ] Test offline sale → reconnect → synchronization.
- [ ] Test cash drawer opening/closing and variance reporting.
- [ ] Test all role permissions with real accounts.
- [ ] Do not expose the POS API or SQLite file directly to the public internet.

AdGuardTray Installer project

Expected repository layout:

  AdGuardTray.sln
  AdGuardTray\
  AdGuardTray.Installer\
  publish\win-x64\AdGuardTray.exe

Before building the MSI, publish the application from the solution directory:

  dotnet publish .\AdGuardTray\AdGuardTray.csproj -c Release -r win-x64 --self-contained true -o .\publish\win-x64

Then rebuild AdGuardTray.Installer in Visual Studio.

Expected MSI output name:

  AdGuardTray-1.3.0-x64.msi

Do not commit bin, obj, or publish output folders.

---
description: Kako ispravno pokrenuti aplikaciju i API (bez timeout grešaka)
---

Slijedi ove korake kako bi izbjegao "HttpClient.Timeout" i greške s Android bildanjem:

### 1. Pokreni API (Backend)
API mora raditi u pozadini. Otvori novi terminal i pokreni:
```powershell
dotnet run --project GetThereAPI/GetThereAPI.csproj --launch-profile https
```

### 2. Ako koristiš fizički Android uređaj preko USB-a
Prije pokretanja aplikacije proslijedi API port na uređaj:
```powershell
adb reverse tcp:7230 tcp:7230
```

Provjeri da ADB vidi uređaj:
```powershell
adb devices -l
```

### 3. Pokreni Mobilnu Aplikaciju
U drugom terminalu pokreni aplikaciju. Obavezno navedi put do `.csproj` datoteke kako ne bi pokušavao bildati API za Android:
```powershell
dotnet build GetThere/GetThere.csproj -t:Run -f net10.0-android
```

### Zašto je ovo važno?
- **Timeout:** Ako vidiš "request was canceled", provjeri vrti li se prvi terminal (API).
- **10.0.2.2:** Ovo je adresa koju koristi samo Android emulator da bi vidio tvoj kompjuter.
- **localhost na fizičkom uređaju:** Radi samo kada je aktivan `adb reverse tcp:7230 tcp:7230`.
- **Build Errors:** Korištenje punog puta do projekta (`GetThere/GetThere.csproj`) sprječava build sustav da pokušava bildati krive stvari.

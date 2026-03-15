---
description: Kako ispravno pokrenuti aplikaciju i API (bez timeout grešaka)
---

Slijedi ove korake kako bi izbjegao "HttpClient.Timeout" i greške s Android bildanjem:

### 1. Pokreni API (Backend)
API mora raditi u pozadini. Otvori novi terminal i pokreni:
```powershell
dotnet run --project GetThereAPI/GetThereAPI.csproj --launch-profile https
```

### 2. Pokreni Mobilnu Aplikaciju
U drugom terminalu pokreni aplikaciju. Obavezno navedi put do `.csproj` datoteke kako ne bi pokušavao bildati API za Android:
```powershell
dotnet build GetThere/GetThere.csproj -t:Run -f net10.0-android
```

### Zašto je ovo važno?
- **Timeout:** Ako vidiš "request was canceled", provjeri vrti li se prvi terminal (API).
- **10.0.2.2:** Ovo je adresa koju emulator koristi da bi vidio tvoj kompjuter.
- **Build Errors:** Korištenje punog puta do projekta (`GetThere/GetThere.csproj`) sprječava build sustav da pokušava bildati krive stvari.

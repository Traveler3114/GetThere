# EF Core Migration Guide

## First Time Setup
Only run this once to install EF Core tools globally:
```bash
dotnet tool install --global dotnet-ef
```

---

## Every Time You Add or Change a Table

### Step 1 — Stop the API in Visual Studio
Press `Shift + F5` or click the red Stop button

### Step 2 — Open Terminal in Visual Studio
`View → Terminal`

### Step 3 — Navigate to the API project
```bash
cd GetThereAPI
```

### Step 4 — Create the migration
```bash
dotnet ef migrations add YourMigrationName
```

Use a descriptive name every time, for example:
- `InitialCreate` — first time ever
- `AddedWalletTable` — when you add a Wallet table
- `AddedTripTable` — when you add a Trip table
- `AddedCityToUser` — when you add a field to AppUser

### Step 5 — Apply migration to SQL Server
```bash
dotnet ef database update
```

### Step 6 — Start the API again
Press `F5`

---

## Quick Reference
```bash
cd GetThereAPI
dotnet ef migrations add YourMigrationName
dotnet ef database update
```

---

## Rules to Remember
- Always **stop the API** before running migrations
- Always run commands from inside the **GetThereAPI folder**
- Migration names must be **unique** every time
- Always run **both commands** — `migrations add` creates the file, `database update` applies it to SQL Server

---

## Adding a New Table — Full Process

1. Create a new model class in `GetThereAPI/Models/`
2. Add `DbSet<YourModel>` to `AppDbContext.cs`
3. Stop the API (`Shift + F5`)
4. Run migrations (`dotnet ef migrations add` + `dotnet ef database update`)
5. Start the API (`F5`)

---

## Migrations Folder Over Time
Each migration is tracked in your code and Git:
```
Migrations/
├── 20260301_InitialCreate.cs
├── 20260302_AddedWalletTable.cs
├── 20260303_AddedTripTable.cs
```

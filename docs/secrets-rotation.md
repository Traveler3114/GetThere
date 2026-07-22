# Secrets Management & Rotation

## Where secrets live

| Secret | Project | Storage |
|--------|---------|---------|
| `Jwt:Key` (signing key) | Both APIs | `dotnet user-secrets` (dev), env vars (prod) |
| `TransitInfoApi:ClientSecret` | GetThereAPI | `dotnet user-secrets` (dev), env vars (prod) |
| `AdminCredentials:Password` | GetThereAPI | `dotnet user-secrets` (dev), env vars (prod) |
| `ConnectionStrings:DefaultConnection` | Both APIs | `dotnet user-secrets` (dev), env vars (prod) |

**Never** commit real secrets to `appsettings.json`. The checked-in files contain `"CHANGE-ME"` placeholders only.

## Accessing secrets

```powershell
# List all secrets for a project
dotnet user-secrets list --project GetThereAPI/GetThereAPI.csproj
dotnet user-secrets list --project TransitInfoAPI/TransitInfoAPI.csproj

# Set a secret
dotnet user-secrets set "Jwt:Key" "<value>" --project GetThereAPI/GetThereAPI.csproj
```

## Rotation policy

| Secret | Rotation cadence | Impact of rotation |
|--------|------------------|-------------------|
| JWT signing keys | Every 90 days | All existing tokens invalidated — users must re-login |
| Service account password | Every 90 days | GetThereAPI→TransitInfoAPI auth breaks until updated in both projects |
| Admin passwords | Every 90 days | Manual login required |

## Rotation procedure (JWT keys)

1. Generate a new 64+ character random key
2. Set it in both projects: `dotnet user-secrets set "Jwt:Key" "<new-key>" --project <project>`
3. **Both APIs use separate JWT keys** — they are not shared
4. Update production env vars if applicable
5. Deploy — all sessions invalidate, users re-authenticate

## Recovery: compromised key

1. Immediately rotate the key (see above)
2. If the service account password was compromised, rotate it in TransitInfoAPI's seed data (drop DB or manually update `AspNetUsers` password hash)
3. Update `TransitInfoApi:ClientSecret` in GetThereAPI to match

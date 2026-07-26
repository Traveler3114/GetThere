# Secrets Rotation

Credentials are generated on first run via startup seeding:
- Admin account credentials written to `.admin-credentials`
- Service account credentials written to `.service-account-credentials`

These files appear in `AppContext.BaseDirectory` and are gitignored.

## Rotation

1. Generate a new password (min 24 chars, mixed case + digits + symbols)
2. Update the user's password via the Identity API
3. Update any config files or environment variables that reference the old credential
4. Delete the old `.admin-credentials` / `.service-account-credentials` file (the new password will be regenerated on next seeding only if the user record doesn't exist — so pre-create it if rotating without deleting)

For JWT signing keys, use `dotnet user-secrets set "Jwt:Key" "<key>"` — never store in `appsettings.json`.

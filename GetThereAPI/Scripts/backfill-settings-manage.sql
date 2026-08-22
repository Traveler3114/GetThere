-- Grants settings.manage to the User role on deployments that run with Seed:Enabled=false,
-- where Program.cs never adds it. Safe to run repeatedly.
INSERT INTO AspNetRoleClaims (RoleId, ClaimType, ClaimValue)
SELECT r.Id, 'permission', 'settings.manage'
FROM AspNetRoles r
WHERE r.Name = 'User'
  AND NOT EXISTS (
      SELECT 1 FROM AspNetRoleClaims c
      WHERE c.RoleId = r.Id AND c.ClaimType = 'permission' AND c.ClaimValue = 'settings.manage');

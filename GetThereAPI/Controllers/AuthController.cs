using GetThereAPI.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GetThereAPI.Entities;
using GetThereShared.Common;
using GetThereShared.Dtos;
using GetThereAPI.Managers; // WalletManager and TokenManager live here
using Microsoft.EntityFrameworkCore;

namespace GetThereAPI.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly UserManager<AppUser> _userManager;
        private readonly SignInManager<AppUser> _signInManager;
        private readonly WalletManager _walletManager;
        private readonly TokenManager _tokenManager;
        private readonly AppDbContext _dbContext;

        public AuthController(
            UserManager<AppUser> userManager,
            SignInManager<AppUser> signInManager,
            WalletManager walletManager,
            TokenManager tokenManager,
            AppDbContext dbContext)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _walletManager = walletManager;
            _tokenManager = tokenManager;
            _dbContext = dbContext;
        }

        // POST /auth/register
        [HttpPost("register")]
        public async Task<ActionResult<OperationResult>> Register(RegisterDto request)
        {
            var existingUser = await _userManager.FindByEmailAsync(request.Email);
            if (existingUser != null)
                return BadRequest(OperationResult.Fail("Email already in use"));

            var user = new AppUser { Email = request.Email, UserName = request.Email, FullName = request.FullName };
            var result = await _userManager.CreateAsync(user, request.Password);

            if (!result.Succeeded)
                return BadRequest(OperationResult.Fail(string.Join(", ", result.Errors.Select(e => e.Description))));

            await _walletManager.CreateWalletForUserAsync(user.Id);

            return Ok(OperationResult.Ok("User registered successfully"));
        }

        // POST /auth/login
        [HttpPost("login")]
        public async Task<ActionResult<LoginResponseDto>> Login(LoginDto request, [FromQuery] bool rememberMe = false)
        {
            var user = await _userManager.FindByEmailAsync(request.Email);
            if (user == null)
                return Unauthorized();

            var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, false);
            if (!result.Succeeded)
                return Unauthorized();

            var accessToken = _tokenManager.CreateToken(user);
            var rawRefreshToken = _tokenManager.GenerateRefreshToken();
            var refreshTokenHash = _tokenManager.HashToken(rawRefreshToken);
            var refreshTokenExpiry = _tokenManager.GetRefreshTokenExpiry(rememberMe);

            var refreshToken = new RefreshToken
            {
                Token = refreshTokenHash,
                UserId = user.Id,
                ExpiresAt = refreshTokenExpiry,
                DeviceInfo = Request.Headers["User-Agent"].ToString()
            };

            _dbContext.RefreshTokens.Add(refreshToken);
            await _dbContext.SaveChangesAsync();

            var userDto = new UserDto
            {
                Id = user.Id,
                Email = user.Email!,
                FullName = user.FullName,
                Token = accessToken
            };

            return Ok(new LoginResponseDto
            {
                User = userDto,
                AccessToken = accessToken,
                RefreshToken = rawRefreshToken
            });
        }

        [HttpPost("refresh")]
        public async Task<ActionResult<RefreshTokenResponseDto>> Refresh(RefreshTokenRequestDto request)
        {
            if (string.IsNullOrWhiteSpace(request.RefreshToken))
                return Unauthorized();

            var incomingTokenHash = _tokenManager.HashToken(request.RefreshToken);
            var existingRefreshToken = await _dbContext.RefreshTokens
                .Include(rt => rt.User)
                .FirstOrDefaultAsync(rt => rt.Token == incomingTokenHash);

            if (existingRefreshToken == null || !existingRefreshToken.IsActive)
                return Unauthorized();

            existingRefreshToken.RevokedAt = DateTime.UtcNow;

            var newRawRefreshToken = _tokenManager.GenerateRefreshToken();
            var newHashedRefreshToken = _tokenManager.HashToken(newRawRefreshToken);
            var wasRememberMeToken = (existingRefreshToken.ExpiresAt - existingRefreshToken.CreatedAt) > TimeSpan.FromDays(1);

            var newRefreshTokenEntity = new RefreshToken
            {
                Token = newHashedRefreshToken,
                UserId = existingRefreshToken.UserId,
                ExpiresAt = _tokenManager.GetRefreshTokenExpiry(wasRememberMeToken),
                DeviceInfo = Request.Headers["User-Agent"].ToString()
            };

            existingRefreshToken.ReplacedByToken = newHashedRefreshToken;

            _dbContext.RefreshTokens.Add(newRefreshTokenEntity);
            await _dbContext.SaveChangesAsync();

            var newAccessToken = _tokenManager.CreateToken(existingRefreshToken.User);

            return Ok(new RefreshTokenResponseDto
            {
                AccessToken = newAccessToken,
                RefreshToken = newRawRefreshToken
            });
        }

        [Authorize]
        [HttpPost("logout")]
        public async Task<ActionResult<OperationResult>> Logout(RefreshTokenRequestDto request)
        {
            if (!string.IsNullOrWhiteSpace(request.RefreshToken))
            {
                var tokenHash = _tokenManager.HashToken(request.RefreshToken);
                var existingRefreshToken = await _dbContext.RefreshTokens
                    .FirstOrDefaultAsync(rt => rt.Token == tokenHash);

                if (existingRefreshToken != null && !existingRefreshToken.RevokedAt.HasValue)
                {
                    existingRefreshToken.RevokedAt = DateTime.UtcNow;
                    await _dbContext.SaveChangesAsync();
                }
            }

            return Ok(OperationResult.Ok("Logged out"));
        }
    }
}

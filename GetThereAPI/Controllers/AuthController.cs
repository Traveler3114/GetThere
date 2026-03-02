using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using GetThereAPI.Data;
using GetThereAPI.Models;
using GetThereShared.Models;

namespace GetThereAPI.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly UserManager<AppUser> _userManager;
        private readonly SignInManager<AppUser> _signInManager;
        private readonly AppDbContext _context;

        public AuthController(UserManager<AppUser> userManager, SignInManager<AppUser> signInManager, AppDbContext context)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _context = context;
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

            var wallet = new Wallet { UserId = user.Id, Balance = 0, LastUpdated = DateTime.UtcNow };
            _context.Wallets.Add(wallet);
            await _context.SaveChangesAsync();

            return Ok(OperationResult.Ok("User registered successfully"));
        }

        // POST /auth/login
        [HttpPost("login")]
        public async Task<ActionResult<OperationResult<UserDto>>> Login(LoginDto request)
        {
            var user = await _userManager.FindByEmailAsync(request.Email);
            if (user == null)
                return Unauthorized(OperationResult<UserDto>.Fail("Invalid credentials"));

            var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, false);
            if (!result.Succeeded)
                return Unauthorized(OperationResult<UserDto>.Fail("Invalid credentials"));

            var userDto = new UserDto
            {
                Id = user.Id,
                Email = user.Email!,
                FullName = user.FullName
            };

            return Ok(OperationResult<UserDto>.Ok(userDto, "Login successful"));
        }
    }
}
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
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

        public AuthController(UserManager<AppUser> userManager, SignInManager<AppUser> signInManager)
        {
            _userManager = userManager;
            _signInManager = signInManager;
        }

        // POST /auth/register
        [HttpPost("register")]
        public async Task<ActionResult> Register(RegisterDto request)
        {
            var existingUser = await _userManager.FindByEmailAsync(request.Email);
            if (existingUser != null)
                return BadRequest(new OperationResult(false, "Email already in use"));

            var user = new AppUser { Email = request.Email, FullName = request.FullName };
            var result = await _userManager.CreateAsync(user, request.Password);

            if (!result.Succeeded)
                return BadRequest(new OperationResult(false, string.Join(", ", result.Errors.Select(e => e.Description))));

            return Ok(new OperationResult(true, "User registered successfully"));
        }

        // POST /auth/login
        [HttpPost("login")]
        public async Task<ActionResult> Login(LoginDto request)
        {
            var user = await _userManager.FindByEmailAsync(request.Email);
            if (user == null)
                return Unauthorized(new OperationResult(false, "Invalid credentials"));

            var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, false);
            if (!result.Succeeded)
                return Unauthorized(new OperationResult(false, "Invalid credentials"));

            return Ok(new OperationResult(true, "Login successful"));
        }
    }
}
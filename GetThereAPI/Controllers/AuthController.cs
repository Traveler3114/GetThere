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
            // Check if email already exists
            var existingUser = await _userManager.FindByEmailAsync(request.Email);
            if (existingUser != null)
                return BadRequest(new { message = "Email already in use" });

            var user = new AppUser
            {
                UserName = request.Username,
                Email = request.Email,
                FullName = request.FullName,
                City = request.City
            };

            var result = await _userManager.CreateAsync(user, request.Password);

            if (!result.Succeeded)
                return BadRequest(result.Errors);

            return Ok(new { message = "User registered successfully" });
        }

        // POST /auth/login
        [HttpPost("login")]
        public async Task<ActionResult> Login(LoginDto request)
        {
            var user = await _userManager.FindByEmailAsync(request.Email);

            if (user == null)
                return Unauthorized(new { message = "Invalid credentials" });

            var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, false);

            if (!result.Succeeded)
                return Unauthorized(new { message = "Invalid credentials" });

            // map AppUser to UserDto before sending to MAUI
            var userDto = new UserDto
            {
                Id = user.Id,
                Username = user.UserName!,
                Email = user.Email!,
                FullName = user.FullName,
                City = user.City
            };

            return Ok(userDto);
        }
    }
}
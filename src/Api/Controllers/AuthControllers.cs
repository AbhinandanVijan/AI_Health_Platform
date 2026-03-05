using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Api.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.Globalization;

namespace Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly UserManager<AppUser> _userManager;
    private readonly SignInManager<AppUser> _signInManager;
    private readonly IConfiguration _config;

    public AuthController(UserManager<AppUser> userManager, SignInManager<AppUser> signInManager, IConfiguration config)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _config = config;
    }

    public record RegisterRequest(string Email, string Password, string? Role);
    public record LoginRequest(string Email, string Password);

    private static readonly HashSet<string> AllowedRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "User",
        "Clinician",
        "Admin"
    };

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest req)
    {
        var selectedRole = string.IsNullOrWhiteSpace(req.Role) ? "User" : NormalizeRole(req.Role);
        if (!AllowedRoles.Contains(selectedRole))
        {
            return BadRequest(new { message = "Role must be one of: User, Clinician, Admin." });
        }

        var user = new AppUser { UserName = req.Email, Email = req.Email };
        var result = await _userManager.CreateAsync(user, req.Password);
        if (!result.Succeeded) return BadRequest(result.Errors);

        await _userManager.AddToRoleAsync(user, selectedRole);

        return Ok(new { message = "Registered", role = selectedRole });
    }

    private static string NormalizeRole(string role)
    {
        var trimmed = role.Trim();
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(trimmed.ToLowerInvariant());
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest req)
    {
        var user = await _userManager.FindByEmailAsync(req.Email);
        if (user == null) return Unauthorized();

        var result = await _signInManager.CheckPasswordSignInAsync(user, req.Password, false);
        if (!result.Succeeded) return Unauthorized();

        var roles = await _userManager.GetRolesAsync(user);
        var token = CreateJwt(user, roles);

        return Ok(new { token });
    }

    private string CreateJwt(AppUser user, IList<string> roles)
    {
        var key = _config["Jwt:Key"]
            ?? _config["JWT_KEY"]
            ?? throw new InvalidOperationException("Missing JWT key. Set Jwt:Key or JWT_KEY.");
        var issuer = _config["Jwt:Issuer"]
            ?? _config["JWT_ISSUER"]
            ?? "aihealth.local";
        var audience = _config["Jwt:Audience"]
            ?? _config["JWT_AUDIENCE"]
            ?? "aihealth.local";

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Email, user.Email ?? ""),
            new(ClaimTypes.Email, user.Email ?? ""),
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.Email ?? "")
        };

        foreach (var r in roles)
            claims.Add(new Claim(ClaimTypes.Role, r));

        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(8),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
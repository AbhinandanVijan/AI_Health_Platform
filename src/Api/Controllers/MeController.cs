using Api.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Api.Controllers;

[ApiController]
[Route("api/me")]
public class MeController : ControllerBase
{
    private readonly UserManager<AppUser> _userManager;

    public MeController(UserManager<AppUser> userManager)
    {
        _userManager = userManager;
    }

    [Authorize]
    [HttpGet]
    public IActionResult Get()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = User.FindFirstValue(ClaimTypes.Email);
        var roles = User.Claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value).ToList();
        return Ok(new { userId, email, roles });
    }

    [Authorize]
    [HttpGet("profile")]
    public async Task<IActionResult> GetProfile()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null) return NotFound();

        return Ok(new UserProfileDto(
            DateOfBirth: user.DateOfBirth,
            BiologicalSex: user.BiologicalSex,
            IsSmoker: user.IsSmoker,
            IsDiabetic: user.IsDiabetic,
            IsHypertensive: user.IsHypertensive,
            Bmi: user.Bmi,
            ActivityLevel: user.ActivityLevel
        ));
    }

    [Authorize]
    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile([FromBody] UserProfileDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null) return NotFound();

        user.DateOfBirth = dto.DateOfBirth;
        user.BiologicalSex = dto.BiologicalSex;
        user.IsSmoker = dto.IsSmoker;
        user.IsDiabetic = dto.IsDiabetic;
        user.IsHypertensive = dto.IsHypertensive;
        user.Bmi = dto.Bmi;
        user.ActivityLevel = dto.ActivityLevel;

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
            return BadRequest(result.Errors.Select(e => e.Description));

        return Ok(dto);
    }
}

public record UserProfileDto(
    DateTime? DateOfBirth,
    string? BiologicalSex,
    bool? IsSmoker,
    bool? IsDiabetic,
    bool? IsHypertensive,
    decimal? Bmi,
    string? ActivityLevel
);
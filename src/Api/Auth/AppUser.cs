using Microsoft.AspNetCore.Identity;

namespace Api.Auth;

public class AppUser : IdentityUser
{
    public DateTime? DateOfBirth { get; set; }
    // "Male" | "Female" | "Other"
    public string? BiologicalSex { get; set; }
    public bool? IsSmoker { get; set; }
    public bool? IsDiabetic { get; set; }
    public bool? IsHypertensive { get; set; }
    public decimal? Bmi { get; set; }
    // "Sedentary" | "Moderate" | "Active"
    public string? ActivityLevel { get; set; }
}
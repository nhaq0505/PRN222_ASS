using CourtBook.BusinessObjects.Enums;

namespace CourtBook.Services.Models;

public class AuthUserDto
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public UserRole Role { get; set; }
}

public class RegisterRequestDto
{
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool RegisterAsOwner { get; set; }
}

public class DemoAccountDto
{
    public string Label { get; set; } = string.Empty;
    public string RoleBadge { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

using System.ComponentModel.DataAnnotations;

namespace ProductAssetManager.Api.DTOs;

public record RegisterRequest
{
    [Required]
    public string Name { get; init; } = string.Empty;

    [Required]
    [EmailAddress]
    public string Email { get; init; } = string.Empty;

    [Required]
    public string Password { get; init; } = string.Empty;
}

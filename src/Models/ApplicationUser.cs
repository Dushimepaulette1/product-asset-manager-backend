using Microsoft.AspNetCore.Identity;

namespace ProductAssetManager.Api.Models;

public class ApplicationUser : IdentityUser
{
    public ICollection<Order> Orders { get; set; } = new List<Order>();
}

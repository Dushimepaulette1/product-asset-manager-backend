using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProductAssetManager.Api.DTOs;
using ProductAssetManager.Api.Services;

namespace ProductAssetManager.Api.Controllers;

[ApiController]
[Route("api/products")]
public class ProductsController : ControllerBase
{
    private readonly IProductService _productService;

    public ProductsController(IProductService productService)
    {
        _productService = productService;
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create(CreateProductRequest request)
    {
        var result = await _productService.CreateAsync(request);

        if (result.CategoryNotFound)
        {
            return NotFound(new { message = $"Category '{request.CategoryId}' was not found." });
        }

        if (result.ValidationError is not null)
        {
            return BadRequest(new { message = result.ValidationError });
        }

        return StatusCode(201, result.Product);
    }
}

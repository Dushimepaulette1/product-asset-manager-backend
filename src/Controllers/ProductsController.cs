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

        return CreatedAtAction(nameof(GetById), new { id = result.Product!.Id }, result.Product);
    }

    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] string? keyword, [FromQuery] decimal? maxPrice)
    {
        var products = await _productService.SearchAsync(keyword, maxPrice);
        return Ok(products);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var product = await _productService.GetByIdAsync(id);

        if (product is null)
        {
            return NotFound(new { message = $"Product '{id}' was not found." });
        }

        return Ok(product);
    }

    [HttpPost("{productId:guid}/variants")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> AddVariant(Guid productId, CreateVariantRequest request)
    {
        var result = await _productService.AddVariantAsync(productId, request);

        if (result.ProductNotFound)
        {
            return NotFound(new { message = $"Product '{productId}' was not found." });
        }

        if (result.ValidationError is not null)
        {
            return BadRequest(new { message = result.ValidationError });
        }

        return StatusCode(201, result.Variant);
    }
}

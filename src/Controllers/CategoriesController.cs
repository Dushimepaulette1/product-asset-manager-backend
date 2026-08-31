using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProductAssetManager.Api.DTOs;
using ProductAssetManager.Api.Services;

namespace ProductAssetManager.Api.Controllers;

[ApiController]
[Route("api/categories")]
public class CategoriesController : ControllerBase
{
    private readonly ICategoryService _categoryService;

    public CategoriesController(ICategoryService categoryService)
    {
        _categoryService = categoryService;
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create(CreateCategoryRequest request)
    {
        var result = await _categoryService.CreateAsync(request);

        if (result.ParentNotFound)
        {
            return NotFound(new { message = $"Parent category '{request.ParentCategoryId}' was not found." });
        }

        return CreatedAtAction(nameof(GetById), new { id = result.Category!.Id }, result.Category);
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var categories = await _categoryService.GetAllAsync();
        return Ok(categories);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var category = await _categoryService.GetByIdAsync(id);

        if (category is null)
        {
            return NotFound(new { message = $"Category '{id}' was not found." });
        }

        return Ok(category);
    }
}
